using System.Text;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Navigation;

/// <summary>
/// The transcript as a single body of text, with a character-offset to
/// programme-time map in both directions.
///
/// This is what makes moving between the two panes work: the cursor never
/// moves, the lens changes. Tab into the transcript and the caret is already
/// on the word that was under the timeline cursor; Tab back and the timeline
/// is where the caret was.
///
/// Cut spans are included, marked. They have no programme time - you cannot be
/// "at" a moment that is not in the video - so they map to the boundary where
/// they would sit, and the UI says so rather than snapping silently.
/// </summary>
public sealed class TranscriptDocument
{
    private readonly List<TranscriptSegment> _segments;

    private TranscriptDocument(string text, List<TranscriptSegment> segments)
    {
        Text = text;
        _segments = segments;
    }

    public string Text { get; }

    public IReadOnlyList<TranscriptSegment> Segments => _segments;

    public const string CutMarker = "[cut] ";

    public static TranscriptDocument Build(Project project, TimelineMap map)
    {
        var builder = new StringBuilder();
        var segments = new List<TranscriptSegment>();

        foreach (var element in project.Spine)
        {
            // Every element gets a line, including the ones with no speech.
            // A blank line in the transcript reads as a bug, and a segment you
            // cannot see in the transcript is a segment you cannot reorder or
            // delete from it.
            var line = element switch
            {
                SpanElement span => span.Text.Length > 0 ? span.Text : "[silence]",
                ClipElement => "[clip]",
                CardElement card => $"[card: {card.Composition.PlainText()}]",
                HoleElement hole => $"[hole: {hole.Note}]",
                PauseElement pause => $"[pause {pause.Length:0.#}s]",
                _ => "[segment]",
            };

            if (!element.Enabled) line = CutMarker + line;

            var start = builder.Length;
            builder.Append(line).Append('\n');

            var placed = map.Find(element.Id);

            segments.Add(new TranscriptSegment(
                element.Id,
                start,
                start + line.Length,
                placed?.ProgrammeStart,
                placed?.ProgrammeEnd,
                element.Enabled,
                element is SpanElement s ? s : null,
                // Where in the media file this line begins, padding included.
                // Word timings are absolute source times, so they have to be
                // measured against this rather than against SourceIn.
                placed?.Media?.In,
                placed?.Speed ?? 1.0));
        }

        return new TranscriptDocument(builder.ToString(), segments);
    }

    /// <summary>Where the caret should go for a given moment in the programme.</summary>
    public int OffsetAt(double programmeTime)
    {
        var segment = _segments.FirstOrDefault(s =>
            s.ProgrammeStart is { } start && s.ProgrammeEnd is { } end
            && programmeTime >= start && programmeTime < end);

        segment ??= _segments.LastOrDefault(s => s.ProgrammeStart is not null);
        if (segment is null) return 0;

        // Land on the word, not just the line. At word granularity the caret
        // should be exactly where the ear was - which means the start of the
        // word being spoken, not the end of it.
        if (segment.Span is { Words.Count: > 0 } span
            && segment.ProgrammeStart is { } segmentStart
            && segment.SourceStart is { } sourceStart)
        {
            var elapsed = (programmeTime - segmentStart) * segment.Speed;

            var offsetInLine = 0;
            var wordStart = 0;

            foreach (var word in span.Words)
            {
                if (word.Start - sourceStart > elapsed) break;

                wordStart = offsetInLine;
                offsetInLine += word.Text.Length + 1;
            }

            return Math.Clamp(segment.CharStart + wordStart, segment.CharStart, segment.CharEnd);
        }

        return segment.CharStart;
    }

    /// <summary>Which line a caret offset is on, or -1.</summary>
    public int LineAt(int offset) =>
        _segments.FindIndex(s => offset >= s.CharStart && offset <= s.CharEnd);

    /// <summary>
    /// What moving the caret onto a new line should announce: position, times,
    /// then the line itself.
    /// </summary>
    public string AnnounceLine(int offset)
    {
        var index = LineAt(offset);
        if (index < 0) return "end of transcript";

        var segment = _segments[index];
        return $"{segment.DescribeLine(index, _segments.Count)}. {segment.Describe()}";
    }

    /// <summary>Where the timeline cursor should go for a given caret position.</summary>
    public TranscriptLocation LocationAt(int offset)
    {
        var segment = _segments.FirstOrDefault(s => offset >= s.CharStart && offset <= s.CharEnd)
                      ?? _segments.LastOrDefault();

        if (segment is null) return new TranscriptLocation(null, null, false, "empty transcript");

        if (!segment.Enabled)
        {
            return new TranscriptLocation(
                segment.Element, null, false,
                "cut, not in the programme");
        }

        if (segment.ProgrammeStart is not { } start)
        {
            return new TranscriptLocation(segment.Element, null, false, "not in the programme");
        }

        var into = offset - segment.CharStart;

        if (segment.Span is { Words.Count: > 0 } span && segment.SourceStart is { } sourceStart)
        {
            var consumed = 0;

            foreach (var word in span.Words)
            {
                var next = consumed + word.Text.Length + 1;
                if (into < next)
                {
                    return new TranscriptLocation(
                        segment.Element,
                        start + (word.Start - sourceStart) / segment.Speed,
                        true,
                        word.Text);
                }

                consumed = next;
            }
        }

        return new TranscriptLocation(segment.Element, start, true, segment.Describe());
    }
}

public sealed record TranscriptSegment(
    ElementId Element,
    int CharStart,
    int CharEnd,
    double? ProgrammeStart,
    double? ProgrammeEnd,
    bool Enabled,
    SpanElement? Span,
    double? SourceStart = null,
    double Speed = 1.0)
{
    public string Describe() => Span?.Text ?? "segment";

    /// <summary>
    /// What moving onto this line announces: which line, when it starts and
    /// ends, and how long it runs. Timecodes belong here rather than in the
    /// text itself - reading them inline would make the transcript unreadable
    /// as prose, but not having them at all leaves you unable to say where a
    /// sentence sits.
    /// </summary>
    public string DescribeLine(int index, int total)
    {
        var position = $"line {index + 1} of {total}";

        if (!Enabled) return $"{position}, cut, not in the programme";

        if (ProgrammeStart is not { } start || ProgrammeEnd is not { } end)
        {
            return $"{position}, not in the programme";
        }

        return $"{position}, {Timecode.FormatShort(start)} to {Timecode.FormatShort(end)}, "
               + Timecode.Speak(end - start);
    }
}

/// <summary>
/// Where a caret position lands on the timeline. <see cref="ProgrammeTime"/> is
/// null when the caret is on a cut line, which the UI must announce rather than
/// silently resolving.
/// </summary>
public sealed record TranscriptLocation(
    ElementId? Element,
    double? ProgrammeTime,
    bool InProgramme,
    string Describe);
