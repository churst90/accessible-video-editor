using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;

namespace AccessibleVideoEditor.Core.Timeline;

/// <summary>
/// Where everything sits on a drawn timeline.
///
/// <b>This lives in Core on purpose.</b> The accessible model came first and
/// the picture is computed from it, never the other way round - so the drawing
/// cannot quietly become the source of truth about what the timeline contains.
/// It also means the layout is testable without a window: the blocks, the
/// ruler, the playhead and the highlighted selection are all plain numbers that
/// an assertion can check.
///
/// Nothing here knows a colour or a font. Blocks carry a <see cref="BlockKind"/>
/// and a set of flags; the front end decides what those look like.
/// </summary>
public static class TimelineLayout
{
    /// <summary>Minimum drawn width, so a two-frame segment is still visible.</summary>
    public const double MinimumBlockWidth = 3;

    /// <summary>
    /// <paramref name="slots"/> is where the front end says each lane actually
    /// is. It is supplied when the lanes have to line up with something the
    /// layout does not control - the real track headers beside them - because
    /// a lane drawn one pixel per row away from its own header is the one
    /// drawing fault that would make the picture actively misleading. With no
    /// slots the lanes stack uniformly from the viewport.
    /// </summary>
    public static TimelineView Build(
        Project project,
        TimelineMap map,
        DocumentCursor cursor,
        TimelineViewport viewport,
        IReadOnlyList<LaneSlot>? slots = null)
    {
        var pps = viewport.PixelsPerSecond;
        var viewStart = Math.Max(0, viewport.ViewStart);
        var viewDuration = pps > 0 ? viewport.Width / pps : 0;
        var viewEnd = viewStart + viewDuration;

        double X(double t) => (t - viewStart) * pps;

        var lanes = new List<TimelineLane>();
        var top = viewport.RulerHeight;

        foreach (var track in project.InOrder)
        {
            var blocks = track.Kind == TrackKind.Programme
                ? ProgrammeBlocks(map, cursor, X, pps, viewStart, viewEnd)
                : ItemBlocks(project, map, track, cursor, X, pps, viewStart, viewEnd);

            var slot = slots is not null && lanes.Count < slots.Count ? slots[lanes.Count] : (LaneSlot?)null;

            lanes.Add(new TimelineLane(
                track.Id,
                track.Name,
                track.Kind,
                lanes.Count,
                slot?.Top ?? top,
                slot?.Height ?? viewport.LaneHeight,
                track.Id == cursor.FocusedTrack,
                track.Muted,
                track.Armed,
                track.Locked,
                blocks));

            top = slot is { } placed
                ? placed.Top + placed.Height
                : top + viewport.LaneHeight + viewport.LaneGap;
        }

        var playhead = cursor.ProgrammeTime;
        var playheadX = playhead >= viewStart && playhead <= viewEnd ? X(playhead) : (double?)null;

        TimelineBand? selection = null;
        if (cursor.Selection is { IsEmpty: false } range)
        {
            var from = Math.Max(range.From, viewStart);
            var to = Math.Min(range.To, viewEnd);
            if (to > from) selection = new TimelineBand(X(from), (to - from) * pps);
        }

        return new TimelineView(
            pps,
            viewStart,
            viewDuration,
            viewport.Width,
            top,
            viewport.RulerHeight,
            lanes,
            Ticks(viewStart, viewEnd, pps, X),
            playheadX,
            selection,
            project.Spine.Count == 0 && project.Overlays.Count == 0
                ? "no project loaded"
                : null);
    }

    private static List<TimelineBlock> ProgrammeBlocks(
        TimelineMap map,
        DocumentCursor cursor,
        Func<double, double> x,
        double pps,
        double viewStart,
        double viewEnd)
    {
        var blocks = new List<TimelineBlock>();

        foreach (var placed in map.Elements)
        {
            if (placed.ProgrammeEnd < viewStart || placed.ProgrammeStart > viewEnd) continue;

            var element = placed.Element;

            blocks.Add(new TimelineBlock(
                x(placed.ProgrammeStart),
                Math.Max(MinimumBlockWidth, placed.Duration * pps),
                Label(element),
                KindOf(element),
                element.Muted,
                element.Hidden,
                !element.Enabled,
                placed.Contains(cursor.ProgrammeTime),
                placed.TransitionIn > 0,
                placed.TransitionIn * pps,
                element.FadeIn * pps,
                element.FadeOut * pps,
                element.Id,
                null,
                placed.Media?.Source,
                placed.Media?.In ?? 0,
                placed.Media?.Out ?? 0));
        }

        return blocks;
    }

    private static List<TimelineBlock> ItemBlocks(
        Project project,
        TimelineMap map,
        Track track,
        DocumentCursor cursor,
        Func<double, double> x,
        double pps,
        double viewStart,
        double viewEnd)
    {
        var blocks = new List<TimelineBlock>();

        foreach (var item in project.ItemsOn(track.Id))
        {
            if (map.ResolveAnchor(item.Start) is not { } start) continue;

            var end = item.End is { } anchor
                ? map.ResolveAnchor(anchor) ?? start + (item.Length ?? 0)
                : start + (item.Length ?? 0);

            if (end < viewStart || start > viewEnd) continue;

            blocks.Add(new TimelineBlock(
                x(start),
                Math.Max(MinimumBlockWidth, (end - start) * pps),
                item.Describe(),
                KindOf(item),
                false,
                false,
                !item.Enabled,
                cursor.ProgrammeTime >= start && cursor.ProgrammeTime < end,
                false,
                0,
                0,
                0,
                null,
                item.Id,
                SourceOf(item),
                0,
                0));
        }

        return blocks;
    }

    private static SourceId? SourceOf(OverlayItem item) => item switch
    {
        BrollItem broll => broll.Source,
        AudioItem audio => audio.Source,
        MusicItem music => music.Source,
        _ => null,
    };

    private static string Label(SpineElement element) => element switch
    {
        SpanElement span => span.Text,
        CardElement card => card.Composition.PlainText(),
        HoleElement => "gap",
        PauseElement => "pause",
        _ => element.Describe(),
    };

    private static BlockKind KindOf(SpineElement element) => element switch
    {
        CardElement => BlockKind.Card,
        HoleElement => BlockKind.Hole,
        PauseElement => BlockKind.Pause,
        _ => BlockKind.Clip,
    };

    private static BlockKind KindOf(OverlayItem item) => item switch
    {
        BrollItem => BlockKind.Broll,
        TitleItem => BlockKind.Title,
        GraphicItem => BlockKind.Graphic,
        CardItem => BlockKind.Card,
        MusicItem => BlockKind.Music,
        AudioItem => BlockKind.Audio,
        _ => BlockKind.Clip,
    };

    /// <summary>
    /// Ruler marks at a round interval. The interval is chosen so labels stay
    /// roughly <see cref="MinimumTickSpacing"/> apart at any zoom - a ruler with
    /// overlapping numbers is worse than no ruler.
    /// </summary>
    public const double MinimumTickSpacing = 78;

    private static readonly double[] Ladder =
        [0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600];

    public static double TickInterval(double pixelsPerSecond)
    {
        if (pixelsPerSecond <= 0) return Ladder[^1];

        foreach (var step in Ladder)
        {
            if (step * pixelsPerSecond >= MinimumTickSpacing) return step;
        }

        return Ladder[^1];
    }

    private static List<TimelineTick> Ticks(
        double viewStart,
        double viewEnd,
        double pixelsPerSecond,
        Func<double, double> x)
    {
        var ticks = new List<TimelineTick>();
        var interval = TickInterval(pixelsPerSecond);
        if (interval <= 0) return ticks;

        // Minor marks subdivide the labelled ones; four is enough to read
        // against without turning the ruler into a comb.
        var minor = interval / 4;

        var first = Math.Floor(viewStart / minor) * minor;

        for (var t = first; t <= viewEnd + minor; t += minor)
        {
            if (t < -1e-6) continue;

            var labelled = Math.Abs(t / interval - Math.Round(t / interval)) < 1e-6;

            ticks.Add(new TimelineTick(
                x(t),
                t,
                labelled,
                labelled ? Timecode.FormatShort(t) : null));
        }

        return ticks;
    }

    /// <summary>
    /// How far the view has to scroll to keep the playhead on screen.
    ///
    /// It only moves when the playhead reaches the edge, and then it jumps by a
    /// good fraction of a screen rather than creeping - a view that scrolls on
    /// every frame is unreadable, and one that never scrolls loses the playhead.
    /// </summary>
    public static double Follow(double viewStart, double playhead, double viewDuration, double margin = 0.12)
    {
        if (viewDuration <= 0) return Math.Max(0, viewStart);

        var pad = viewDuration * margin;
        var left = viewStart + pad;
        var right = viewStart + viewDuration - pad;

        if (playhead < left) return Math.Max(0, playhead - viewDuration * 0.75);
        if (playhead > right) return Math.Max(0, playhead - viewDuration * 0.25);

        return Math.Max(0, viewStart);
    }
}

/// <summary>
/// The size of the drawing and how much time it shows. Supplied by the front
/// end because only it knows how big the window is.
/// </summary>
public readonly record struct TimelineViewport(
    double Width,
    double PixelsPerSecond,
    double ViewStart,
    double LaneHeight = 56,
    double LaneGap = 4,
    double RulerHeight = 26);

public sealed record TimelineView(
    double PixelsPerSecond,
    double ViewStart,
    double ViewDuration,
    double Width,
    double Height,
    double RulerHeight,
    IReadOnlyList<TimelineLane> Lanes,
    IReadOnlyList<TimelineTick> Ticks,
    double? PlayheadX,
    TimelineBand? Selection,
    string? EmptyMessage)
{
    public double TimeAt(double x) => ViewStart + (PixelsPerSecond > 0 ? x / PixelsPerSecond : 0);

    public TimelineLane? LaneAt(double y) =>
        Lanes.FirstOrDefault(l => y >= l.Top && y < l.Top + l.Height);
}

public sealed record TimelineLane(
    TrackId Track,
    string Name,
    TrackKind Kind,
    int Index,
    double Top,
    double Height,
    bool IsFocused,
    bool Muted,
    bool Armed,
    bool Locked,
    IReadOnlyList<TimelineBlock> Blocks)
{
    /// <summary>True when this lane draws a waveform rather than a solid block.</summary>
    public bool ShowsWaveform => Kind is TrackKind.Audio or TrackKind.Programme;
}

public sealed record TimelineBlock(
    double X,
    double Width,
    string Label,
    BlockKind Kind,
    bool Muted,
    bool Hidden,
    bool Disabled,
    bool UnderCursor,
    bool HasTransitionIn,
    double TransitionWidth,
    double FadeInWidth,
    double FadeOutWidth,
    ElementId? Element,
    ItemId? Item,
    SourceId? Source,
    double SourceIn,
    double SourceOut)
{
    public double Right => X + Width;
}

/// <summary>Where one lane sits, when something outside the layout decides that.</summary>
public readonly record struct LaneSlot(double Top, double Height);

public readonly record struct TimelineTick(double X, double Time, bool Labelled, string? Label);

public readonly record struct TimelineBand(double X, double Width);

/// <summary>
/// What a block is, so the front end can colour it. Deliberately semantic - the
/// layout never names a colour.
/// </summary>
public enum BlockKind
{
    Clip,
    Card,
    Hole,
    Pause,
    Broll,
    Title,
    Graphic,
    Music,
    Audio,
}
