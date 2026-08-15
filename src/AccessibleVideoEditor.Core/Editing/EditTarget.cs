using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Editing;

/// <summary>
/// What Delete would delete. With nothing to look at, one key with three
/// possible meanings is a trap - so the target is resolved by an explicit rule
/// (a marked range, else what is under the cursor, else nothing) and is always
/// spoken. Deleting a track is never on Delete; it is its own command and it
/// confirms.
/// </summary>
public static class EditTarget
{
    public static EditTargetInfo Resolve(Project project, TimelineMap map, DocumentCursor cursor)
    {
        // A marked range only wins while it is what you were last working with.
        // Otherwise a selection made ten minutes ago would quietly capture a
        // delete you meant for the segment under the cursor.
        if (cursor.Intent == EditIntent.Selection
            && cursor.Selection is { IsEmpty: false } selection)
        {
            return new EditTargetInfo(
                EditTargetKind.Selection,
                selection,
                null,
                null,
                selection.Describe());
        }

        var trackId = cursor.FocusedTrack;
        if (trackId is null)
        {
            return EditTargetInfo.None;
        }

        var track = project.TrackOf(trackId.Value);
        if (track is null) return EditTargetInfo.None;

        if (track.Locked)
        {
            return EditTargetInfo.None with { Describe = $"{track.Name} is locked" };
        }

        var content = TrackProbe.At(project, map, trackId.Value, cursor.ProgrammeTime);
        if (!content.HasContent || content.Start is not { } start || content.End is not { } end)
        {
            return EditTargetInfo.None;
        }

        if (track.Kind == TrackKind.Programme)
        {
            var placed = map.Locate(cursor.ProgrammeTime);

            return new EditTargetInfo(
                EditTargetKind.Element,
                new TimeSelection(start, end),
                placed?.Element.Id,
                null,
                $"{content.Word}, {Timecode.Speak(end - start)}");
        }

        var item = project.ItemsOn(trackId.Value).FirstOrDefault(i =>
        {
            var itemStart = map.ResolveAnchor(i.Start);
            if (itemStart is null) return false;

            var itemEnd = i.End is { } anchor ? map.ResolveAnchor(anchor) : itemStart + (i.Length ?? 0);
            return itemEnd is not null
                   && cursor.ProgrammeTime >= itemStart
                   && cursor.ProgrammeTime < itemEnd;
        });

        return new EditTargetInfo(
            EditTargetKind.Item,
            new TimeSelection(start, end),
            null,
            item?.Id,
            $"{content.Word}, {Timecode.Speak(end - start)}");
    }
}

public sealed record EditTargetInfo(
    EditTargetKind Kind,
    TimeSelection Range,
    ElementId? Element,
    ItemId? Item,
    string Describe)
{
    public static EditTargetInfo None { get; } =
        new(EditTargetKind.None, default, null, null, "nothing under the cursor");

    public bool IsActionable => Kind != EditTargetKind.None;

    /// <summary>Spoken before a destructive action so the target is never a surprise.</summary>
    public string Announce(string verb) =>
        IsActionable ? $"{verb} {Describe}" : Describe;
}

public enum EditTargetKind
{
    None,

    /// <summary>An explicit in/out range. Wins over everything.</summary>
    Selection,

    /// <summary>A spine element on the programme track.</summary>
    Element,

    /// <summary>An overlay item on any other track.</summary>
    Item,
}
