using AccessibleVideoEditor.Core.Commands;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Navigation;

/// <summary>
/// The views and the rules for moving between them.
///
/// One view is on screen at a time, selected with Ctrl and its number. The
/// order is the direction work flows: media comes in, becomes tracks, becomes a
/// timeline, and reads out as a transcript.
///
/// Tab is deliberately <i>not</i> a view switcher - it moves between controls
/// within a view, as it does in every other application. Views are a bigger
/// operation than focus movement and get their own keys.
///
/// This lives in Core rather than in the UI so the order, the names, the status
/// line and above all the <b>empty states</b> are one definition and can be
/// tested. An empty view that says nothing is indistinguishable from a broken
/// one.
/// </summary>
public sealed class Workspace
{
    // Ordered by how often you are in them, not by how the data flows. The
    // timeline is where the work happens, so it is view 1.
    // No record view. Recording is per track, so it belongs in the track editor
    // and the timeline - leaving the view you are editing in to record into a
    // hole is exactly when you least want to move.
    private static readonly Pane[] Order =
        [Pane.Timeline, Pane.Tracks, Pane.Transcript, Pane.MediaBin, Pane.Stream, Pane.Images];

    public Pane Focused { get; private set; } = Pane.Timeline;

    public static IReadOnlyList<Pane> Panes => Order;

    public Pane Next() => Focused = Order[(IndexOf(Focused) + 1) % Order.Length];

    public Pane Previous() => Focused = Order[(IndexOf(Focused) + Order.Length - 1) % Order.Length];

    public Pane FocusOn(Pane pane) => Focused = pane;

    private static int IndexOf(Pane pane) => Array.IndexOf(Order, pane);

    /// <summary>
    /// Views are announced by name, never by number. "View 3" tells you nothing
    /// about where you are; "transcript editor" tells you everything.
    /// </summary>
    public static string Name(Pane pane) => pane switch
    {
        Pane.Timeline => "timeline editor",
        Pane.Tracks => "track editor",
        Pane.Transcript => "transcript editor",
        Pane.MediaBin => "media bin",
        Pane.Stream => "streamer view",
        Pane.Images => "image editor",
        _ => pane.ToString().ToLowerInvariant(),
    };

    /// <summary>The digit that selects this view. Ctrl plus this.</summary>
    public static int Number(Pane pane) => Array.IndexOf(Order, pane) + 1;

    public static Pane? ByNumber(int number) =>
        number >= 1 && number <= Order.Length ? Order[number - 1] : null;

    /// <summary>
    /// The line that is on screen no matter which view is showing: where the
    /// cursor is, how long the programme is, how far each arrow press moves,
    /// and which track is focused. Everything else can be a view away; this
    /// cannot.
    /// </summary>
    public static string StatusLine(
        double programmeTime,
        double duration,
        string stepSize,
        string? trackName) =>
        $"{Timecode.Format(programmeTime)} of {Timecode.Format(duration)}   "
        + $"step: {stepSize}   "
        + $"track: {trackName ?? "none"}";

    /// <summary>Which commands F1 should list for this pane.</summary>
    public static CommandContext ContextOf(Pane pane) => pane switch
    {
        Pane.MediaBin => CommandContext.MediaBin,
        Pane.Tracks => CommandContext.Tracks,
        Pane.Timeline => CommandContext.Timeline,
        Pane.Transcript => CommandContext.Transcript,
        Pane.Stream => CommandContext.Stream,
        Pane.Images => CommandContext.Images,
        _ => CommandContext.Global,
    };

    /// <summary>
    /// What a pane says when there is nothing in it. Returns null when the pane
    /// has content and should describe that instead.
    ///
    /// A null project is its own case: with nothing open, every pane says so,
    /// because "empty timeline" would imply a project exists.
    /// </summary>
    public static string? EmptyState(Pane pane, Project? project)
    {
        if (project is null) return "no project loaded. Control N for a new project, Control O to open one";

        return pane switch
        {
            Pane.MediaBin when project.Sources.Count == 0 =>
                "media bin empty. Control I to import video or audio",

            Pane.Tracks when project.Tracks.Count == 0 =>
                "no tracks",

            Pane.Timeline when project.Spine.Count(e => e.Enabled) == 0 =>
                "timeline empty. Insert media from the media bin, or Control I to import",

            Pane.Transcript when project.Spine.Count == 0 =>
                "transcript empty. Nothing has been transcribed yet",

            Pane.Stream => "streaming is not built yet",

            _ => null,
        };
    }

    /// <summary>
    /// The utterance when focus lands on a pane: its name, then either its
    /// empty state or a one-line summary of what is in it.
    /// </summary>
    public static string Announce(Pane pane, Project? project, TimelineMap? map = null)
    {
        var name = Name(pane);

        if (EmptyState(pane, project) is { } empty) return $"{name}. {empty}";

        return $"{name}. {Summarise(pane, project!, map)}";
    }

    private static string Summarise(Pane pane, Project project, TimelineMap? map) => pane switch
    {
        Pane.MediaBin =>
            $"{project.Sources.Count} source{Plural(project.Sources.Count)}",

        Pane.Tracks =>
            $"{project.Tracks.Count} track{Plural(project.Tracks.Count)}",

        Pane.Timeline =>
            $"{project.Spine.Count(e => e.Enabled)} segment{Plural(project.Spine.Count(e => e.Enabled))}, " +
            $"{Timecode.Speak((map ?? TimelineMap.Build(project)).Duration)}"
            + HoleWarning(project),

        Pane.Transcript =>
            $"{project.Spine.OfType<SpanElement>().Count()} line{Plural(project.Spine.OfType<SpanElement>().Count())}",

        _ => string.Empty,
    };

    /// <summary>Outstanding holes are the one thing worth interrupting a summary for.</summary>
    private static string HoleWarning(Project project)
    {
        var holes = project.Holes.Count();
        return holes == 0 ? string.Empty : $", {holes} hole{Plural(holes)} outstanding";
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}

public enum Pane
{
    Timeline,
    Tracks,
    Transcript,
    MediaBin,

    /// <summary>Live output: scenes, sources, chat and preview.</summary>
    Stream,

    /// <summary>
    /// Pictures. Appended rather than slotted next to the media bin on purpose:
    /// renumbering a view somebody has already learned costs more than the
    /// tidier order is worth.
    /// </summary>
    Images,
}
