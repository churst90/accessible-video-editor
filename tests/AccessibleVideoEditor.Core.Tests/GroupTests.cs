using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// Compound segments - a run of segments treated as one named thing.
///
/// A grouping rather than a container, so programme time is unchanged and the
/// members stay individually reachable. What is tested here is mostly the
/// refusals and the counting: a group must never be a way to lose track of what
/// you have.
/// </summary>
public class GroupTests
{
    /// <summary>Four five-second segments: 0-5, 5-10, 10-15, 15-20.</summary>
    private static Project FourSegments()
    {
        var project = Project.CreateDefault("groups");
        project.Settings.SpanPadIn = 0;
        project.Settings.SpanPadOut = 0;
        project.Settings.JumpCutDuration = 0;
        project.Settings.SceneTransitionDuration = 0;

        var source = new Source { Id = Ids.NewSource(), Path = "take1.mkv", Duration = 120 };
        project.Sources.Add(source);

        for (var i = 0; i < 4; i++)
        {
            project.Spine.Add(new SpanElement
            {
                Id = Ids.NewElement(),
                Source = source.Id,
                SourceIn = i * 10,
                SourceOut = i * 10 + 5,
                Text = $"sentence {i}",
            });
        }

        return project;
    }

    private static EditResult GroupFirstTwo(Project project, string name = "the intro") =>
        GroupOperations.Group(project, new TimeSelection(0, 10), name);

    [Fact]
    public void Grouping_says_how_many_and_how_long()
    {
        var project = FourSegments();

        var result = GroupFirstTwo(project);

        Assert.True(result.Changed);
        Assert.Contains("2 segments", result.Description);
        Assert.Contains("the intro", result.Description);
    }

    [Fact]
    public void A_group_of_one_is_refused_because_that_is_a_rename()
    {
        var project = FourSegments();

        var result = GroupOperations.Group(project, new TimeSelection(0, 5), "just one");

        Assert.False(result.Changed);
        Assert.Contains("more than one", result.Description);
    }

    [Fact]
    public void Grouping_an_empty_range_says_there_was_nothing_there()
    {
        var project = FourSegments();

        var result = GroupOperations.Group(project, new TimeSelection(50, 60), "nothing");

        Assert.False(result.Changed);
        Assert.Contains("nothing in that range", result.Description);
    }

    [Fact]
    public void A_segment_cannot_be_in_two_groups_and_the_refusal_names_the_first()
    {
        var project = FourSegments();
        GroupFirstTwo(project);

        var result = GroupOperations.Group(project, new TimeSelection(5, 15), "overlapping");

        Assert.False(result.Changed);
        Assert.Contains("the intro", result.Description);
    }

    [Fact]
    public void Grouping_does_not_change_programme_time_at_all()
    {
        // The whole reason this is a grouping rather than nesting.
        var project = FourSegments();
        var before = TimelineMap.Build(project).Duration;

        GroupFirstTwo(project);

        Assert.Equal(before, TimelineMap.Build(project).Duration, 3);
    }

    [Fact]
    public void Ungrouping_says_the_segments_survived()
    {
        var project = FourSegments();
        GroupFirstTwo(project);

        var result = GroupOperations.Ungroup(project, project.Groups[0].Id);

        Assert.Contains("all still there", result.Description);
        Assert.Equal(4, project.Spine.Count);
        Assert.Empty(project.Groups);
    }

    [Fact]
    public void Collapsing_says_what_it_now_means_not_just_the_word()
    {
        var project = FourSegments();
        GroupFirstTwo(project);
        var id = project.Groups[0].Id;

        var expanded = GroupOperations.ToggleCollapsed(project, id);
        Assert.Contains("behave separately", expanded.Description);

        var collapsed = GroupOperations.ToggleCollapsed(project, id);
        Assert.Contains("as one", collapsed.Description);
    }

    [Fact]
    public void Moving_a_group_carries_every_member_in_order()
    {
        var project = FourSegments();
        var firstText = ((SpanElement)project.Spine[0]).Text;
        var secondText = ((SpanElement)project.Spine[1]).Text;

        GroupFirstTwo(project);
        var result = GroupOperations.Move(project, project.Groups[0].Id, 1);

        Assert.True(result.Changed);
        Assert.Equal(firstText, ((SpanElement)project.Spine[1]).Text);
        Assert.Equal(secondText, ((SpanElement)project.Spine[2]).Text);
    }

    [Fact]
    public void Moving_a_group_that_is_already_first_says_so()
    {
        var project = FourSegments();
        GroupFirstTwo(project);

        var result = GroupOperations.Move(project, project.Groups[0].Id, -1);

        Assert.False(result.Changed);
        Assert.Contains("already first", result.Description);
    }

    [Fact]
    public void A_group_broken_apart_by_an_insert_refuses_to_move()
    {
        // Moving it would carry the stranger along or leave it behind, and
        // either is a surprise you cannot see.
        var project = FourSegments();
        GroupFirstTwo(project);

        project.Spine.Insert(1, new PauseElement { Id = Ids.NewElement(), Length = 2 });

        var result = GroupOperations.Move(project, project.Groups[0].Id, 1);

        Assert.False(result.Changed);
        Assert.Contains("no longer a single run", result.Description);
    }

    [Fact]
    public void Deleting_a_group_says_the_count_and_the_length()
    {
        // The most destructive thing a group can do, so "deleted the intro" is
        // not enough - you need to know how much of the video just went.
        var project = FourSegments();
        GroupFirstTwo(project);

        var result = GroupOperations.Delete(project, project.Groups[0].Id);

        Assert.Contains("2 segments", result.Description);
        Assert.Equal(2, project.Spine.Count);
        Assert.Empty(project.Groups);
    }

    [Fact]
    public void Cutting_a_group_disables_all_of_it_and_pressing_again_restores()
    {
        var project = FourSegments();
        GroupFirstTwo(project);
        var id = project.Groups[0].Id;

        var cut = GroupOperations.ToggleDisable(project, id);
        Assert.Contains("cut", cut.Description);
        Assert.All(project.Spine.Take(2), e => Assert.False(e.Enabled));

        var restored = GroupOperations.ToggleDisable(project, id);
        Assert.Contains("restored", restored.Description);
        Assert.All(project.Spine.Take(2), e => Assert.True(e.Enabled));
    }

    [Fact]
    public void A_half_cut_group_resolves_towards_cutting_so_two_presses_are_predictable()
    {
        var project = FourSegments();
        GroupFirstTwo(project);
        project.Spine[0].Enabled = false;

        GroupOperations.ToggleDisable(project, project.Groups[0].Id);

        Assert.All(project.Spine.Take(2), e => Assert.False(e.Enabled));
    }

    [Fact]
    public void A_collapsed_group_announces_itself_as_one_thing()
    {
        var project = FourSegments();
        GroupFirstTwo(project);

        var said = GroupOperations.Announce(project, TimelineMap.Build(project), 7);

        Assert.NotNull(said);
        Assert.Contains("the intro", said);
        Assert.Contains("2 segments", said);
    }

    [Fact]
    public void An_expanded_group_announces_where_you_are_inside_it()
    {
        var project = FourSegments();
        GroupFirstTwo(project);
        GroupOperations.ToggleCollapsed(project, project.Groups[0].Id);

        var said = GroupOperations.Announce(project, TimelineMap.Build(project), 7);

        Assert.Equal("the intro, 2 of 2", said);
    }

    [Fact]
    public void Outside_any_group_it_says_nothing_rather_than_saying_none()
    {
        // The cursor readout is terse by design; "not in a group" on every move
        // would be noise.
        var project = FourSegments();
        GroupFirstTwo(project);

        Assert.Null(GroupOperations.Announce(project, TimelineMap.Build(project), 17));
    }

    [Fact]
    public void A_member_deleted_from_under_the_group_is_dropped_rather_than_breaking_it()
    {
        var project = FourSegments();
        GroupFirstTwo(project);

        project.Spine.RemoveAt(0);

        var said = GroupOperations.Announce(project, TimelineMap.Build(project), 2);
        Assert.NotNull(said);
        Assert.Contains("1 segment,", said);
    }
}
