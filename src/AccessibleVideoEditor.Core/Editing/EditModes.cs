using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Editing;

/// <summary>
/// The two modes that change what every other editing key does, and the two
/// ways of selecting a range without marking in and out by hand.
///
/// Ripple mode and snapping are settings rather than edits, so neither goes on
/// the undo stack: undoing "snapping off" would be indistinguishable from
/// undoing the cut you made after it, and an undo you cannot predict is worse
/// than none. Both are always announced, because a mode you cannot see is a
/// mode you have to be told about - a silent ripple mode is how an edit gets
/// destroyed by a key that used to do something else.
/// </summary>
public static class EditModes
{
    /// <summary>Off, this track, all tracks - then round again.</summary>
    public static string CycleRipple(ProjectSettings settings)
    {
        settings.RippleMode = settings.RippleMode switch
        {
            RippleMode.Off => RippleMode.FocusedTrack,
            RippleMode.FocusedTrack => RippleMode.AllTracks,
            _ => RippleMode.Off,
        };

        return Describe(settings.RippleMode);
    }

    /// <summary>
    /// Says what the mode does rather than only naming it. "Ripple off" alone
    /// leaves you to remember which way round it is, and remembering is the
    /// thing this application is meant to remove.
    /// </summary>
    public static string Describe(RippleMode mode) => mode switch
    {
        RippleMode.Off => "ripple off, an edit leaves everything after it where it is",
        RippleMode.FocusedTrack => "ripple this track, edits shift what follows on this track only",
        _ => "ripple all tracks, edits shift every track together",
    };

    public static string ToggleSnap(ProjectSettings settings)
    {
        settings.Snap = !settings.Snap;

        return settings.Snap
            ? "snapping on, the cursor lands on boundaries, word starts and markers"
            : "snapping off, the cursor lands where you put it";
    }
}

/// <summary>
/// Selecting a range by naming what you want rather than by marking each end.
///
/// Marking in and out is the primary idiom and stays. These two exist because
/// "this segment" and "this whole track" are the two ranges you ask for often
/// enough that setting both ends by hand is wasted work - and because a
/// selection you built in one keystroke is one you can trust you built
/// correctly, which a pair of marks is not.
/// </summary>
public static class Selections
{
    /// <summary>The segment under the cursor on the focused track.</summary>
    public static SelectionResult Segment(Project project, TimelineMap map, DocumentCursor cursor)
    {
        if (cursor.FocusedTrack is not { } trackId) return SelectionResult.Refused("no track focused");

        var track = project.TrackOf(trackId);
        if (track is null) return SelectionResult.Refused("no track focused");

        var segments = TrackProbe.Segments(project, map, trackId);
        if (segments.Count == 0) return SelectionResult.Refused($"no segments on {track.Name}");

        // Strictly inside, so a cursor resting on a boundary selects the
        // segment it is at the start of rather than the one that just ended.
        var here = segments.FirstOrDefault(s =>
            cursor.ProgrammeTime >= s.Start && cursor.ProgrammeTime < s.End);

        if (here == default && cursor.ProgrammeTime >= segments[^1].End)
        {
            return SelectionResult.Refused($"past the last segment on {track.Name}");
        }

        if (here == default) return SelectionResult.Refused($"nothing under the cursor on {track.Name}");

        var range = new TimeSelection(here.Start, here.End);

        return new SelectionResult(range, $"selected {Timecode.Speak(range.Length)} on {track.Name}");
    }

    /// <summary>Everything on the focused track, from its first segment to its last.</summary>
    public static SelectionResult Track(Project project, TimelineMap map, DocumentCursor cursor)
    {
        if (cursor.FocusedTrack is not { } trackId) return SelectionResult.Refused("no track focused");

        var track = project.TrackOf(trackId);
        if (track is null) return SelectionResult.Refused("no track focused");

        var segments = TrackProbe.Segments(project, map, trackId);
        if (segments.Count == 0) return SelectionResult.Refused($"no segments on {track.Name}");

        var range = new TimeSelection(segments[0].Start, segments[^1].End);

        return new SelectionResult(
            range,
            $"selected all of {track.Name}, {segments.Count} "
            + $"segment{(segments.Count == 1 ? string.Empty : "s")}, {Timecode.Speak(range.Length)}");
    }
}

/// <summary>
/// A selection that was made, or a refusal that says which of the several
/// possible reasons it was - "no segments on this track" and "past the last
/// segment" are different problems with different fixes, and collapsing them
/// into "nothing to select" leaves you guessing.
/// </summary>
public readonly record struct SelectionResult(TimeSelection? Range, string Announce)
{
    public static SelectionResult Refused(string why) => new(null, why);

    public bool Selected => Range is not null;
}
