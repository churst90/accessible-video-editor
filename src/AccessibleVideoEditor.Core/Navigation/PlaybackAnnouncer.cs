using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Navigation;

/// <summary>
/// What to say while the video is playing.
///
/// The rule is <b>boundary crossings only</b>. Announcing the position
/// continuously would bury the audio you are trying to listen to; announcing
/// nothing leaves you unable to tell whether an edit actually took. So it
/// speaks when something changes - a new segment, a transition, b-roll coming
/// in - and is silent in between.
///
/// Never timecodes. During playback you can hear the time passing; what you
/// cannot hear is whether the wipe you inserted is really there.
/// </summary>
public sealed class PlaybackAnnouncer
{
    private ElementId _lastSegment;
    private bool _wasInTransition;
    private readonly HashSet<ItemId> _activeOverlays = [];

    public PlaybackVerbosity Verbosity { get; set; } = PlaybackVerbosity.Boundaries;

    /// <summary>Call when playback starts, so the first segment is announced rather than assumed.</summary>
    public void Reset()
    {
        _lastSegment = default;
        _wasInTransition = false;
        _activeOverlays.Clear();
    }

    /// <summary>
    /// Returns what should be spoken at this instant, or null for silence -
    /// which is most of the time.
    /// </summary>
    public string? Tick(Project project, TimelineMap map, double programmeTime)
    {
        if (Verbosity == PlaybackVerbosity.Off) return null;

        var placed = map.Locate(programmeTime);
        if (placed is null) return null;

        var announcements = new List<string>();

        // A transition is announced as it begins, so you hear it land rather
        // than inferring it from the picture changing.
        var inTransition = placed.TransitionIn > 0
                           && programmeTime < placed.ProgrammeStart + placed.TransitionIn;

        if (inTransition && !_wasInTransition)
        {
            announcements.Add(placed.Element.TransitionIn is { } transition
                ? $"transition, {transition.Describe()}"
                : "transition");
        }

        _wasInTransition = inTransition;

        if (placed.Element.Id != _lastSegment)
        {
            _lastSegment = placed.Element.Id;
            announcements.Add(DescribeSegment(placed.Element));
        }

        if (Verbosity == PlaybackVerbosity.Everything)
        {
            announcements.AddRange(OverlayChanges(project, map, programmeTime));
        }
        else
        {
            TrackOverlays(project, map, programmeTime, announcements);
        }

        return announcements.Count == 0 ? null : string.Join(". ", announcements);
    }

    /// <summary>
    /// Overlays are announced on entry only, even at Boundaries verbosity -
    /// b-roll starting is exactly the kind of thing you cannot hear.
    /// </summary>
    private void TrackOverlays(Project project, TimelineMap map, double time, List<string> into)
    {
        foreach (var item in Active(project, map, time))
        {
            if (_activeOverlays.Add(item.Id)) into.Add(item.Describe());
        }

        _activeOverlays.RemoveWhere(id => Active(project, map, time).All(i => i.Id != id));
    }

    private IEnumerable<string> OverlayChanges(Project project, TimelineMap map, double time)
    {
        var current = Active(project, map, time).ToList();

        foreach (var item in current.Where(i => _activeOverlays.Add(i.Id)))
        {
            yield return item.Describe();
        }

        foreach (var gone in _activeOverlays.Where(id => current.All(i => i.Id != id)).ToList())
        {
            _activeOverlays.Remove(gone);
            yield return "overlay ends";
        }
    }

    private static IEnumerable<OverlayItem> Active(Project project, TimelineMap map, double time)
    {
        foreach (var item in project.Overlays.Where(o => o.Enabled))
        {
            var start = map.ResolveAnchor(item.Start);
            if (start is null) continue;

            var end = item.End is { } anchor ? map.ResolveAnchor(anchor) : start + (item.Length ?? 0);
            if (end is not null && time >= start && time < end) yield return item;
        }
    }

    private static string DescribeSegment(SpineElement element) => element switch
    {
        SpanElement span when span.Text.Length > 0 => span.Text,
        SpanElement => "speech",
        ClipElement => "clip",
        CardElement card => $"card, {card.Composition.PlainText()}",
        HoleElement hole => $"hole, {hole.Note}",
        PauseElement => "pause",
        _ => "segment",
    };
}

public enum PlaybackVerbosity
{
    /// <summary>Say nothing; just play.</summary>
    Off,

    /// <summary>Segments, transitions and overlays as they begin. The default.</summary>
    Boundaries,

    /// <summary>Also announce when overlays end.</summary>
    Everything,
}
