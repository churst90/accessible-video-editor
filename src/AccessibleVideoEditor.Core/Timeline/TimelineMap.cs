using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Timeline;

/// <summary>
/// Where every spine element lands in the finished video.
///
/// This is the one function the whole application leans on. The transcript
/// works in <b>source time</b> (11.360 seconds into take1.mkv); the scrubber
/// works in <b>programme time</b> (where that lands in the output). F6 between
/// the two panes with the cursor intact is exactly this map applied in one
/// direction or the other.
/// </summary>
public sealed class TimelineMap
{
    private readonly List<PlacedElement> _placed;

    private TimelineMap(List<PlacedElement> placed, double duration)
    {
        _placed = placed;
        Duration = duration;
    }

    public IReadOnlyList<PlacedElement> Elements => _placed;

    public double Duration { get; }

    public static TimelineMap Build(Project project)
    {
        var settings = project.Settings;
        var placed = new List<PlacedElement>();
        var cursor = 0.0;

        PlacedElement? previous = null;

        foreach (var element in project.Spine.Where(e => e.Enabled))
        {
            var media = MediaRange(element, settings);

            // Retiming changes how long the same media occupies in the
            // programme without changing which part of the file it plays.
            var speed = element.Speed > 0 ? element.Speed : 1.0;
            var duration = (media?.Duration ?? element.Duration) / speed;

            // Transitions overlap the outgoing element, so a transition shortens
            // total programme time. Shorten it if it would swallow either side.
            var transition = TransitionInto(element, previous, settings);
            if (previous is not null && transition > 0)
            {
                transition = Math.Min(transition, previous.Duration * 0.5);
                transition = Math.Min(transition, duration * 0.5);
                cursor -= transition;
            }
            else
            {
                transition = 0;
            }

            var current = new PlacedElement(element, cursor, cursor + duration, transition, media, speed);
            placed.Add(current);

            cursor += duration;
            previous = current;
        }

        return new TimelineMap(placed, Math.Max(0, cursor));
    }

    /// <summary>
    /// The range of the media file this element actually plays, with span
    /// padding already folded in. Padding is applied here rather than at render
    /// time so that programme time, source time and split points all agree -
    /// otherwise a split lands a frame or two off exactly where the pad is.
    /// </summary>
    private static MediaRange? MediaRange(SpineElement element, ProjectSettings settings)
    {
        // A recorded segment plays its active take. The segment keeps its
        // identity and its place; only the media it points at changes, which is
        // what lets you audition takes without disturbing the edit around them.
        if (element.ActiveTake is { } take)
        {
            var pad = element is SpanElement;

            return new MediaRange(
                take.Source,
                Math.Max(0, take.SourceIn - (pad ? settings.SpanPadIn : 0)),
                take.SourceOut + (pad ? settings.SpanPadOut : 0));
        }

        return element switch
        {
            SpanElement span => new MediaRange(
                span.Source,
                Math.Max(0, span.SourceIn - settings.SpanPadIn),
                span.SourceOut + settings.SpanPadOut),
            ClipElement clip => new MediaRange(clip.Source, clip.SourceIn, clip.SourceOut),
            _ => null,
        };
    }

    private static double TransitionInto(SpineElement element, PlacedElement? previous, ProjectSettings settings)
    {
        if (previous is null) return 0;
        if (element.TransitionIn is { } explicitTransition)
        {
            return explicitTransition.Type == TransitionType.Cut ? 0 : explicitTransition.Duration;
        }

        // Same shot, back to back speech: a short dissolve hides the jump cut.
        var sameShot = element is SpanElement a
                       && previous.Element is SpanElement b
                       && a.Source == b.Source;

        return sameShot ? settings.JumpCutDuration : settings.SceneTransitionDuration;
    }

    /// <summary>Which element is on screen at this programme time.</summary>
    public PlacedElement? Locate(double programmeTime)
    {
        if (_placed.Count == 0) return null;

        // Binary search on start time; ranges are contiguous and ordered.
        var low = 0;
        var high = _placed.Count - 1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            var candidate = _placed[mid];
            if (programmeTime < candidate.ProgrammeStart) high = mid - 1;
            else if (programmeTime >= candidate.ProgrammeEnd) low = mid + 1;
            else return candidate;
        }

        return programmeTime >= Duration ? _placed[^1] : _placed[Math.Clamp(low, 0, _placed.Count - 1)];
    }

    public PlacedElement? Find(ElementId id) => _placed.FirstOrDefault(p => p.Element.Id == id);

    /// <summary>
    /// Anchor to programme time. Returns null when the anchor names a disabled
    /// element - a cut line has no programme position, and the UI must say so
    /// rather than silently snapping somewhere.
    /// </summary>
    public double? ResolveAnchor(TimeAnchor anchor) =>
        Find(anchor.Element) is { } placed
            ? Math.Clamp(placed.ProgrammeStart + anchor.Offset, 0, Duration)
            : null;

    /// <summary>Programme time to the anchor form everything else is stored in.</summary>
    public TimeAnchor? ToAnchor(double programmeTime) =>
        Locate(programmeTime) is { } placed
            ? new TimeAnchor(placed.Element.Id, programmeTime - placed.ProgrammeStart)
            : null;

    /// <summary>Programme time to a point inside the original media file.</summary>
    public SourcePoint? ToSource(double programmeTime)
    {
        if (Locate(programmeTime) is not { Media: { } media } placed) return null;
        return new SourcePoint(media.Source, placed.SourceTimeAt(programmeTime - placed.ProgrammeStart));
    }

    /// <summary>
    /// The transcript-to-timeline direction: a point in a take, to where it
    /// lands in the cut. Null when that moment was cut out - which the UI must
    /// announce ("cut, not in programme") rather than silently snapping.
    /// </summary>
    public double? FromSource(SourceId source, double sourceTime)
    {
        foreach (var placed in _placed)
        {
            if (placed.Media is not { } media || media.Source != source) continue;
            if (sourceTime < media.In || sourceTime > media.Out) continue;

            return placed.ProgrammeStart + (sourceTime - media.In) / placed.Speed;
        }

        return null;
    }
}

public sealed record PlacedElement(
    SpineElement Element,
    double ProgrammeStart,
    double ProgrammeEnd,
    double TransitionIn,
    MediaRange? Media,
    double Speed = 1.0)
{
    public double Duration => ProgrammeEnd - ProgrammeStart;

    public bool Contains(double programmeTime) =>
        programmeTime >= ProgrammeStart && programmeTime < ProgrammeEnd;

    /// <summary>
    /// Where a point this far into the element sits inside the media file.
    /// Retiming makes this a scaling rather than an addition, and every split
    /// and trim has to go through it or they land in the wrong place.
    /// </summary>
    public double SourceTimeAt(double programmeOffset) =>
        (Media?.In ?? 0) + programmeOffset * Speed;
}

/// <summary>The slice of a media file an element plays, padding included.</summary>
public readonly record struct MediaRange(SourceId Source, double In, double Out)
{
    public double Duration => Math.Max(0, Out - In);
}

public readonly record struct SourcePoint(SourceId Source, double Time);
