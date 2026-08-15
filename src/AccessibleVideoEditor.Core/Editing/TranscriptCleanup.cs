using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Editing;

/// <summary>
/// The cleanup passes that are only cheap because the edit is text.
///
/// Removing every "um" from an hour of footage is a day's work with a
/// waveform and a few seconds with word-level timings. That asymmetry is the
/// whole argument for a transcript-driven editor, and these are the operations
/// that cash it in.
/// </summary>
public static class TranscriptCleanup
{
    /// <summary>
    /// The words worth removing. Deliberately short: an aggressive list starts
    /// eating "so" and "right" at the beginnings of real sentences, and a cut
    /// that removes meaning is far worse than an "um" left in.
    /// </summary>
    public static IReadOnlyList<string> DefaultFillers { get; } =
        ["um", "uh", "erm", "ah", "hmm", "mm"];

    /// <summary>
    /// Splits filler words out of their segments and disables them.
    ///
    /// Disabled rather than deleted, so a pass that took out something it should
    /// not have is one keystroke from being put back - and so you can hear what
    /// it did before committing to it.
    /// </summary>
    public static EditResult RemoveFillers(
        Project project,
        IReadOnlyList<string>? fillers = null)
    {
        var words = (fillers ?? DefaultFillers)
            .Select(Normalise)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removed = 0;
        var seconds = 0.0;

        // Work backwards: splitting inserts elements, and going forwards would
        // walk into the halves just created.
        for (var i = project.Spine.Count - 1; i >= 0; i--)
        {
            if (project.Spine[i] is not SpanElement span || !span.Enabled) continue;
            if (span.Words.Count == 0) continue;

            foreach (var word in span.Words.Where(w => words.Contains(Normalise(w.Text))).Reverse())
            {
                var isolated = Isolate(project, span, word);
                if (isolated is null) continue;

                isolated.Enabled = false;
                removed++;
                seconds += isolated.Duration;
            }
        }

        return removed == 0
            ? EditResult.NoChange("no filler words found")
            : EditResult.Ok(
                $"cut {removed} filler word{(removed == 1 ? "" : "s")}, {Timecode.Speak(seconds)}. " +
                "They are marked cut, not deleted");
    }

    /// <summary>
    /// Disables gaps between words longer than the threshold.
    ///
    /// Long pauses read as dead air; short ones are the rhythm of speech. The
    /// default leaves anything under a second alone, which is roughly where a
    /// pause stops sounding deliberate.
    /// </summary>
    public static EditResult RemoveSilences(Project project, double longerThan = 1.0)
    {
        var removed = 0;
        var seconds = 0.0;

        for (var i = project.Spine.Count - 1; i >= 0; i--)
        {
            if (project.Spine[i] is not SpanElement span || !span.Enabled) continue;
            if (span.Words.Count < 2) continue;

            var gaps = new List<(double Start, double End)>();

            for (var w = 1; w < span.Words.Count; w++)
            {
                var gap = span.Words[w].Start - span.Words[w - 1].End;
                if (gap > longerThan) gaps.Add((span.Words[w - 1].End, span.Words[w].Start));
            }

            foreach (var gap in Enumerable.Reverse(gaps))
            {
                var isolated = Isolate(project, span, new Word(string.Empty, gap.Start, gap.End));
                if (isolated is null) continue;

                isolated.Enabled = false;
                removed++;
                seconds += isolated.Duration;
            }
        }

        return removed == 0
            ? EditResult.NoChange($"no gaps longer than {Timecode.Speak(longerThan)}")
            : EditResult.Ok($"cut {removed} gap{(removed == 1 ? "" : "s")}, {Timecode.Speak(seconds)}");
    }

    /// <summary>
    /// How fast you are talking, overall and where it varies.
    ///
    /// Pace drift across a twenty minute edit is invisible and inaudible in the
    /// moment - you cannot hear your own tempo change - but it is what makes a
    /// video feel rushed at the end.
    /// </summary>
    public static string PaceReport(Project project, TimelineMap map)
    {
        var spans = map.Elements
            .Where(p => p.Element is SpanElement { Words.Count: > 0 })
            .Select(p => (Placed: p, Span: (SpanElement)p.Element))
            .ToList();

        if (spans.Count == 0) return "nothing transcribed to measure";

        var totalWords = spans.Sum(s => s.Span.Words.Count);
        var totalSeconds = spans.Sum(s => s.Placed.Duration);

        if (totalSeconds <= 0) return "nothing to measure";

        var overall = totalWords / totalSeconds * 60;

        var rates = spans
            .Where(s => s.Placed.Duration > 0.5)
            .Select(s => (s.Span, Rate: s.Span.Words.Count / s.Placed.Duration * 60))
            .ToList();

        if (rates.Count < 2) return $"{overall:0} words per minute";

        var fastest = rates.MaxBy(r => r.Rate);
        var slowest = rates.MinBy(r => r.Rate);

        return $"{overall:0} words per minute overall. "
               + $"Fastest {fastest.Rate:0} at \"{Shorten(fastest.Span.Text)}\", "
               + $"slowest {slowest.Rate:0} at \"{Shorten(slowest.Span.Text)}\"";
    }

    /// <summary>
    /// Splits a span so the given word range is a segment of its own, and
    /// returns it. Returns null when the range is not inside the span.
    /// </summary>
    private static SpineElement? Isolate(Project project, SpanElement span, Word word)
    {
        if (word.Start <= span.SourceIn && word.End >= span.SourceOut) return span;
        if (word.Start < span.SourceIn || word.End > span.SourceOut) return null;

        var map = TimelineMap.Build(project);
        var placed = map.Find(span.Id);
        if (placed is null || placed.Media is not { } media) return null;

        var startAt = placed.ProgrammeStart + (word.Start - media.In) / placed.Speed;
        var endAt = placed.ProgrammeStart + (word.End - media.In) / placed.Speed;

        if (endAt - startAt < 0.02) return null;

        EditOperations.SplitAt(project, startAt);
        EditOperations.SplitAt(project, endAt);

        return TimelineMap.Build(project)
            .Elements
            .FirstOrDefault(p => p.ProgrammeStart >= startAt - 0.01 && p.ProgrammeEnd <= endAt + 0.01)
            ?.Element;
    }

    private static string Normalise(string word) =>
        new(word.Where(c => char.IsLetter(c)).ToArray());

    private static string Shorten(string text) =>
        text.Length <= 40 ? text : text[..37] + "...";
}
