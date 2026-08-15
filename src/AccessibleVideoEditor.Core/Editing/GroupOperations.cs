using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Editing;

/// <summary>
/// Grouping a run of segments into one named thing, and everything that follows
/// from having done so.
///
/// The rule underneath all of it: <b>a group must never be a way to lose track
/// of what you have.</b> So grouping says how many segments and how long,
/// ungrouping says the same, every verb that acts on a whole group says how many
/// it touched, and a group is refused rather than silently repaired whenever it
/// would not mean what it says.
/// </summary>
public static class GroupOperations
{
    /// <summary>
    /// Groups everything the selection covers. Members must be consecutive: a
    /// group with a gap in it would move segments past each other when you
    /// dragged it, which is the one thing a group is supposed to make safe.
    /// </summary>
    public static EditResult Group(Project project, TimeSelection selection, string name)
    {
        name = name.Trim();
        if (name.Length == 0) return EditResult.NoChange("a group needs a name");

        if (project.Groups.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return EditResult.NoChange($"there is already a group called {name}");
        }

        var map = TimelineMap.Build(project);

        var covered = map.Elements
            .Where(p => p.ProgrammeEnd > selection.From + Epsilon
                        && p.ProgrammeStart < selection.To - Epsilon)
            .ToList();

        if (covered.Count == 0) return EditResult.NoChange("nothing in that range to group");

        if (covered.Count == 1)
        {
            // Allowed by nothing in the model, refused here on purpose: a group
            // of one is a rename with extra steps, and it would read as a group
            // in every list for no benefit.
            return EditResult.NoChange("a group needs more than one segment");
        }

        var already = covered
            .Select(p => project.GroupContaining(p.Element.Id))
            .FirstOrDefault(g => g is not null);

        if (already is not null)
        {
            return EditResult.NoChange($"some of that is already in {already.Name}");
        }

        var members = covered.Select(p => p.Element.Id).ToList();
        var duration = covered[^1].ProgrammeEnd - covered[0].ProgrammeStart;

        project.Groups.Add(new SegmentGroup
        {
            Id = Ids.NewGroup(),
            Name = name,
            Members = members,
        });

        return EditResult.Ok($"grouped {members.Count} segments as {name}, {Timecode.Speak(duration)}");
    }

    public static EditResult Ungroup(Project project, GroupId id)
    {
        var group = project.GroupOf(id);
        if (group is null) return EditResult.NoChange("no such group");

        var count = group.Members.Count;
        project.Groups.Remove(group);

        // Says what survived. Ungrouping sounds destructive and is not.
        return EditResult.Ok($"ungrouped {group.Name}. {count} segments, all still there");
    }

    public static EditResult Rename(Project project, GroupId id, string name)
    {
        var group = project.GroupOf(id);
        if (group is null) return EditResult.NoChange("no such group");

        name = name.Trim();
        if (name.Length == 0) return EditResult.NoChange("a group needs a name");

        if (project.Groups.Any(g => g.Id != id
                                    && string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return EditResult.NoChange($"there is already a group called {name}");
        }

        var was = group.Name;
        group.Name = name;

        return EditResult.Ok($"renamed {was} to {name}");
    }

    /// <summary>
    /// Collapsed or expanded. This is the only thing that decides whether the
    /// group behaves as one object or as its members, so it is always announced
    /// with what it now means rather than with the word alone.
    /// </summary>
    public static EditResult ToggleCollapsed(Project project, GroupId id)
    {
        var group = project.GroupOf(id);
        if (group is null) return EditResult.NoChange("no such group");

        group.Collapsed = !group.Collapsed;

        return EditResult.Ok(group.Collapsed
            ? $"{group.Name} collapsed, {group.Members.Count} segments move and delete as one"
            : $"{group.Name} expanded, its {group.Members.Count} segments behave separately again");
    }

    /// <summary>
    /// Moves a whole group earlier or later by one position, taking its members
    /// with it in order. Delta is in segments, matching <c>MoveSegment</c>.
    /// </summary>
    public static EditResult Move(Project project, GroupId id, int delta)
    {
        var group = project.GroupOf(id);
        if (group is null) return EditResult.NoChange("no such group");
        if (delta == 0) return EditResult.NoChange($"{group.Name} did not move");

        var members = Ordered(project, group);
        if (members.Count == 0) return EditResult.NoChange($"{group.Name} has no segments left in it");

        var first = project.Spine.IndexOf(members[0]);
        var last = project.Spine.IndexOf(members[^1]);

        if (first < 0 || last < 0) return EditResult.NoChange($"{group.Name} is not on the timeline");

        if (last - first + 1 != members.Count)
        {
            // Something was inserted into the middle of the group. Moving it now
            // would carry that stranger along or leave it behind, and either is
            // a surprise you cannot see.
            return EditResult.NoChange(
                $"{group.Name} is no longer a single run - something was inserted into it");
        }

        var target = delta < 0 ? first + delta : last + delta;
        if (target < 0 || target >= project.Spine.Count)
        {
            return EditResult.NoChange(delta < 0
                ? $"{group.Name} is already first"
                : $"{group.Name} is already last");
        }

        var block = project.Spine.GetRange(first, members.Count);
        project.Spine.RemoveRange(first, members.Count);

        var insertAt = delta < 0 ? first + delta : first + delta;
        project.Spine.InsertRange(Math.Clamp(insertAt, 0, project.Spine.Count), block);

        return EditResult.Ok(
            $"moved {group.Name}, {members.Count} segments, {(delta < 0 ? "earlier" : "later")}");
    }

    /// <summary>Deletes every segment in the group, and the group with it.</summary>
    public static EditResult Delete(Project project, GroupId id)
    {
        var group = project.GroupOf(id);
        if (group is null) return EditResult.NoChange("no such group");

        var members = Ordered(project, group);
        if (members.Count == 0) return EditResult.NoChange($"{group.Name} has no segments left in it");

        var duration = members.Sum(m => m.Duration);

        foreach (var element in members) project.Spine.Remove(element);
        project.Groups.Remove(group);

        // The count and the length both, because this is the most destructive
        // thing a group can do and "deleted the intro" does not tell you how
        // much of the video just went.
        return EditResult.Ok(
            $"deleted {group.Name}, {members.Count} segments, {Timecode.Speak(duration)}");
    }

    /// <summary>Disables or restores every segment in the group at once.</summary>
    public static EditResult ToggleDisable(Project project, GroupId id)
    {
        var group = project.GroupOf(id);
        if (group is null) return EditResult.NoChange("no such group");

        var members = Ordered(project, group);
        if (members.Count == 0) return EditResult.NoChange($"{group.Name} has no segments left in it");

        // Any enabled member means "cut the group"; only when all are already
        // cut does it restore. Mixed states resolve towards cutting, so pressing
        // twice is always cut-then-restore rather than something unpredictable.
        var cutting = members.Any(m => m.Enabled);
        foreach (var element in members) element.Enabled = !cutting;

        return EditResult.Ok(cutting
            ? $"cut {group.Name}, {members.Count} segments"
            : $"restored {group.Name}, {members.Count} segments");
    }

    /// <summary>
    /// What the cursor should say about a group when it lands inside one, or
    /// null when it has not. Collapsed groups lead with the name because that is
    /// then the identity of the thing you are on.
    /// </summary>
    public static string? Announce(Project project, TimelineMap map, double programmeTime)
    {
        if (map.Locate(programmeTime)?.Element.Id is not { } elementId) return null;
        if (project.GroupContaining(elementId) is not { } group) return null;

        var members = Ordered(project, group);
        if (members.Count == 0) return null;

        var index = members.FindIndex(m => m.Id == elementId);
        var duration = members.Sum(m => m.Duration);

        return group.Collapsed
            ? group.Describe(members.Count, duration)
            : $"{group.Name}, {index + 1} of {members.Count}";
    }

    /// <summary>
    /// The range a collapsed group occupies, for the verbs that act on all of
    /// it. Null when the group is expanded, has gone, or is no longer a single
    /// run - callers fall back to the segment under the cursor.
    /// </summary>
    public static TimeSelection? RangeOf(Project project, TimelineMap map, GroupId id)
    {
        var group = project.GroupOf(id);
        if (group is null) return null;

        var placed = group.Members
            .Select(map.Find)
            .Where(p => p is not null)
            .Select(p => p!)
            .OrderBy(p => p.ProgrammeStart)
            .ToList();

        return placed.Count == 0
            ? null
            : new TimeSelection(placed[0].ProgrammeStart, placed[^1].ProgrammeEnd);
    }

    public static string Describe(Project project)
    {
        if (project.Groups.Count == 0) return "no groups yet";

        var lines = project.Groups.Select(g =>
        {
            var members = Ordered(project, g);
            var duration = members.Sum(m => m.Duration);
            return g.Describe(members.Count, duration);
        });

        return $"{project.Groups.Count} group{(project.Groups.Count == 1 ? string.Empty : "s")}. "
               + string.Join(". ", lines);
    }

    /// <summary>
    /// Members in spine order, skipping any that have been deleted from under
    /// the group. Held by ID, so a stale member is a normal state rather than a
    /// corruption - it is dropped here rather than being repaired behind your
    /// back.
    /// </summary>
    private static List<SpineElement> Ordered(Project project, SegmentGroup group) =>
        project.Spine.Where(e => group.Members.Contains(e.Id)).ToList();

    private const double Epsilon = 1e-6;
}
