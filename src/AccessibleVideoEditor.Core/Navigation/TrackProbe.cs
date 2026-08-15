using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Navigation;

/// <summary>
/// What is under the cursor on a given track.
///
/// On a visual timeline you can see at a glance that a track is empty here and
/// has a clip there. That glance is what this replaces: every Left/Right and
/// every Up/Down announces whether the focused track has content at the cursor,
/// so moving between tracks tells you the shape of the edit.
/// </summary>
public static class TrackProbe
{
    public static TrackContent At(Project project, TimelineMap map, TrackId trackId, double programmeTime)
    {
        var track = project.TrackOf(trackId);
        if (track is null) return TrackContent.Blank;

        if (track.Kind == TrackKind.Programme)
        {
            if (map.Locate(programmeTime) is not { } placed) return TrackContent.Blank;

            // A transition occupies the head of the element it leads into, so
            // landing inside one is reported as being in the transition. That
            // is also the answer to "where does this transition apply" - to the
            // boundary entering this element, on this track.
            var inTransition = placed.TransitionIn > 0
                               && programmeTime < placed.ProgrammeStart + placed.TransitionIn;

            var transition = placed.Element.TransitionIn?.Describe()
                             ?? (placed.TransitionIn > 0 ? $"{placed.TransitionIn:0.##} second dissolve" : null);

            return new TrackContent(
                Kind: inTransition
                    ? ContentKind.Transition
                    : placed.Element switch
                    {
                        SpanElement => ContentKind.Video,
                        ClipElement => ContentKind.Clip,
                        HoleElement => ContentKind.Hole,
                        PauseElement => ContentKind.Pause,
                        CardElement => ContentKind.Card,
                        _ => ContentKind.Video,
                    },
                Label: inTransition ? transition : placed.Element.Describe(),
                Start: placed.ProgrammeStart,
                End: placed.ProgrammeEnd,
                Muted: placed.Element.Muted,
                Speed: placed.Speed,
                Transition: transition,
                Hidden: placed.Element.Hidden,
                Identity: placed.Element is CardElement card ? card.Composition.PlainText() : null);
        }

        foreach (var item in project.ItemsOn(trackId).Where(i => i.Enabled))
        {
            var start = map.ResolveAnchor(item.Start);
            if (start is null) continue;

            var end = item.End is { } endAnchor
                ? map.ResolveAnchor(endAnchor)
                : start + (item.Length ?? 0);

            if (end is null || programmeTime < start || programmeTime >= end) continue;

            return new TrackContent(
                Kind: item switch
                {
                    BrollItem => ContentKind.Broll,
                    TitleItem => ContentKind.Title,
                    GraphicItem => ContentKind.Image,
                    MusicItem => ContentKind.Audio,
                    AudioItem => ContentKind.Audio,
                    CardItem => ContentKind.Card,
                    _ => ContentKind.Video,
                },
                Label: item.Describe(),
                Start: start,
                End: end,
                Muted: item.Muted,
                Hidden: item.Hidden,
                Identity: item switch
                {
                    CardItem overlayCard => overlayCard.Composition.PlainText(),
                    TitleItem title => title.Text,
                    _ => null,
                });
        }

        return TrackContent.Blank;
    }

    private const double Epsilon = 1e-3;

    /// <summary>
    /// Shift+comma: the start of the segment under the cursor. If already
    /// there, the start of the previous segment - so repeated presses walk
    /// backwards through the track rather than sticking.
    /// </summary>
    public static double? SegmentStart(Project project, TimelineMap map, TrackId trackId, double programmeTime)
    {
        var segments = Segments(project, map, trackId);

        var current = segments.FirstOrDefault(s =>
            programmeTime >= s.Start - Epsilon && programmeTime < s.End);

        if (current != default && programmeTime > current.Start + Epsilon) return current.Start;

        return segments
            .Where(s => s.Start < programmeTime - Epsilon)
            .Select(s => (double?)s.Start)
            .LastOrDefault();
    }

    /// <summary>Shift+period: the end of the segment under the cursor, then the next end.</summary>
    public static double? SegmentEnd(Project project, TimelineMap map, TrackId trackId, double programmeTime)
    {
        var segments = Segments(project, map, trackId);

        var current = segments.FirstOrDefault(s =>
            programmeTime >= s.Start && programmeTime < s.End - Epsilon);

        if (current != default) return current.End;

        return segments
            .Where(s => s.End > programmeTime + Epsilon)
            .Select(s => (double?)s.End)
            .FirstOrDefault();
    }

    /// <summary>
    /// Ctrl+left and Ctrl+right: the start of the previous or next segment on
    /// this track. Starts only - stepping onto the end of a segment and then
    /// its start again would make one press feel like two.
    /// </summary>
    public static double? AdjacentSegmentStart(
        Project project,
        TimelineMap map,
        TrackId trackId,
        double programmeTime,
        bool forward)
    {
        var starts = Segments(project, map, trackId).Select(s => s.Start).ToList();

        return forward
            ? starts.Where(t => t > programmeTime + Epsilon).Select(t => (double?)t).FirstOrDefault()
            : starts.Where(t => t < programmeTime - Epsilon).Select(t => (double?)t).LastOrDefault();
    }

    /// <summary>
    /// The segments on a track, ascending. On the programme track these are the
    /// spine elements; elsewhere they are the overlay segments.
    /// </summary>
    public static IReadOnlyList<(double Start, double End)> Segments(
        Project project,
        TimelineMap map,
        TrackId trackId)
    {
        var track = project.TrackOf(trackId);
        var segments = new List<(double Start, double End)>();

        if (track is null) return segments;

        if (track.Kind == TrackKind.Programme)
        {
            segments.AddRange(map.Elements.Select(p => (p.ProgrammeStart, p.ProgrammeEnd)));
        }
        else
        {
            foreach (var item in project.ItemsOn(trackId).Where(i => i.Enabled))
            {
                var start = map.ResolveAnchor(item.Start);
                if (start is null) continue;

                var end = item.End is { } anchor
                    ? map.ResolveAnchor(anchor)
                    : start + (item.Length ?? 0);

                if (end is not null) segments.Add((start.Value, end.Value));
            }
        }

        return segments.OrderBy(s => s.Start).ToList();
    }

    /// <summary>
    /// The utterance for a cursor move. Terse by default because at navigation
    /// speed every syllable is latency - "12.4, blank" is readable while
    /// holding an arrow key down, and a full sentence is not.
    /// </summary>
    public static string Announce(TrackContent content, double programmeTime, Verbosity verbosity) =>
        verbosity switch
        {
            Verbosity.Terse => $"{Timecode.FormatShort(programmeTime)}, {content.Word}",

            Verbosity.Normal => content.HasContent && content.Label is { Length: > 0 } label
                ? $"{Timecode.FormatShort(programmeTime)}, {content.Word}, {label}"
                : $"{Timecode.FormatShort(programmeTime)}, {content.Word}",

            _ => content.HasContent
                ? $"{Timecode.FormatShort(programmeTime)}, {content.Word}, {content.Label}, " +
                  $"{Timecode.Speak(content.Remaining(programmeTime))} remaining"
                : $"{Timecode.FormatShort(programmeTime)}, {content.Word}",
        };
}

public sealed record TrackContent(
    ContentKind Kind,
    string? Label = null,
    double? Start = null,
    double? End = null,
    bool Muted = false,
    double Speed = 1.0,
    string? Transition = null,
    bool Hidden = false,
    string? Identity = null)
{
    public static TrackContent Blank { get; } = new(ContentKind.Blank);

    public bool HasContent => Kind != ContentKind.Blank;

    /// <summary>
    /// What the cursor readout says. One word for anything whose identity is
    /// obvious from its kind - a span of video is a span of video - but cards
    /// and titles carry their text, because "card" alone is useless in a video
    /// with six of them, and the text is the only way to tell them apart.
    /// State that is off by default is appended only when it is on.
    /// </summary>
    public string Word
    {
        get
        {
            if (Kind is ContentKind.Card or ContentKind.Title && Identity is { Length: > 0 } identity)
            {
                return Decorate($"{(Kind == ContentKind.Card ? "card" : "title")} \"{identity}\"");
            }

            var word = Kind switch
            {
                ContentKind.Blank => "blank",
                ContentKind.Video => "video",
                ContentKind.Audio => "audio",
                ContentKind.Image => "image",
                ContentKind.Title => "title",
                ContentKind.Broll => "b-roll",
                ContentKind.Clip => "clip",
                ContentKind.Hole => "hole",
                ContentKind.Pause => "pause",
                ContentKind.Card => "card",
                ContentKind.Transition => "transition",
                _ => "content",
            };

            return Decorate(word);
        }
    }

    private string Decorate(string word)
    {
        if (Muted) word += ", audio muted";
        if (Hidden) word += ", picture hidden";
        if (Math.Abs(Speed - 1.0) > 0.001) word += $", {Speed:0.##} times speed";

        return word;
    }

    public double Remaining(double programmeTime) => Math.Max(0, (End ?? programmeTime) - programmeTime);
}

public enum ContentKind
{
    Blank,
    Video,
    Audio,
    Image,
    Title,
    Broll,
    Clip,
    Hole,
    Pause,

    /// <summary>A composed screen, full frame or as an overlay.</summary>
    Card,

    /// <summary>Inside the dissolve leading into an element.</summary>
    Transition,
}
