using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// Named ranges of a source. The value is the name: without one, re-finding
/// "the good intro" means listening through the take again.
/// </summary>
public class SubclipTests
{
    private static Project WithSource(out SourceId source)
    {
        var project = Project.CreateDefault("subclips");
        project.Settings.SpanPadIn = 0;
        project.Settings.SpanPadOut = 0;
        project.Settings.JumpCutDuration = 0;
        project.Settings.SceneTransitionDuration = 0;

        var take = new Source { Id = Ids.NewSource(), Path = "take1.mkv", Duration = 60 };
        project.Sources.Add(take);
        source = take.Id;

        return project;
    }

    [Fact]
    public void A_named_range_becomes_something_you_can_insert()
    {
        var project = WithSource(out var source);

        var made = SubclipOperations.Create(project, source, 10, 18, "the good intro");
        Assert.True(made.Changed);
        Assert.Contains("the good intro", made.Description);

        var subclip = project.Subclips.Single();
        Assert.Equal(8, subclip.Duration, 3);
    }

    [Fact]
    public void Marks_given_backwards_are_taken_as_a_range_not_refused()
    {
        // Marking out before in is normal when you are marking by ear and
        // realise you wanted the bit you just passed.
        var project = WithSource(out var source);

        SubclipOperations.Create(project, source, 20, 12, "backwards");

        var subclip = project.Subclips.Single();
        Assert.Equal(12, subclip.In, 3);
        Assert.Equal(20, subclip.Out, 3);
    }

    [Fact]
    public void Marking_past_the_end_of_a_take_clamps_rather_than_refusing()
    {
        var project = WithSource(out var source);

        SubclipOperations.Create(project, source, 55, 90, "the end");

        Assert.Equal(60, project.Subclips.Single().Out, 3);
    }

    [Fact]
    public void A_range_too_short_to_hear_is_refused_and_says_how_long_it_was()
    {
        var project = WithSource(out var source);

        var result = SubclipOperations.Create(project, source, 10, 10.01, "nothing");

        Assert.False(result.Changed);
        Assert.Contains("too short", result.Description);
        Assert.Empty(project.Subclips);
    }

    [Fact]
    public void An_unnamed_subclip_is_refused_because_the_name_is_the_point()
    {
        var project = WithSource(out var source);

        Assert.False(SubclipOperations.Create(project, source, 1, 5, "   ").Changed);
    }

    [Fact]
    public void A_duplicate_name_is_refused_rather_than_numbered()
    {
        // "good intro" and "good intro 2" are indistinguishable in a list read
        // aloud, which defeats the point of naming them.
        var project = WithSource(out var source);
        SubclipOperations.Create(project, source, 1, 5, "intro");

        var second = SubclipOperations.Create(project, source, 10, 15, "INTRO");

        Assert.False(second.Changed);
        Assert.Contains("already a subclip", second.Description);
    }

    [Fact]
    public void Inserting_a_subclip_puts_its_range_on_the_timeline()
    {
        var project = WithSource(out var source);
        SubclipOperations.Create(project, source, 10, 18, "intro");

        var result = SubclipOperations.Insert(project, project.Subclips[0].Id, 0);

        Assert.True(result.Changed);
        var clip = Assert.IsType<ClipElement>(project.Spine.Single());
        Assert.Equal(10, clip.SourceIn, 3);
        Assert.Equal(18, clip.SourceOut, 3);
    }

    [Fact]
    public void Inserting_says_the_name_not_the_file()
    {
        var project = WithSource(out var source);
        SubclipOperations.Create(project, source, 10, 18, "the good intro");

        var result = SubclipOperations.Insert(project, project.Subclips[0].Id, 0);

        Assert.Contains("the good intro", result.Description);
        Assert.DoesNotContain("take1.mkv", result.Description);
    }

    [Fact]
    public void Overwriting_with_a_subclip_leaves_the_total_length_alone()
    {
        var project = WithSource(out var source);
        SubclipOperations.Create(project, source, 0, 20, "whole");
        SubclipOperations.Insert(project, project.Subclips[0].Id, 0);

        var before = TimelineMap.Build(project).Duration;
        SubclipOperations.Create(project, source, 30, 35, "middle");

        SubclipOperations.Overwrite(project, project.Subclips[1].Id, 5);

        Assert.Equal(before, TimelineMap.Build(project).Duration, 3);
    }

    [Fact]
    public void Removing_a_subclip_says_what_it_did_not_do()
    {
        // A subclip is a reference, so deleting one after using it looks
        // destructive and is not. Being told so is what makes the command safe
        // to use.
        var project = WithSource(out var source);
        SubclipOperations.Create(project, source, 10, 18, "intro");
        SubclipOperations.Insert(project, project.Subclips[0].Id, 0);

        var result = SubclipOperations.Remove(project, project.Subclips[0].Id);

        Assert.Contains("still", result.Description);
        Assert.Single(project.Spine);
    }

    [Fact]
    public void Retrimming_says_what_it_was_as_well_as_what_it_is()
    {
        var project = WithSource(out var source);
        SubclipOperations.Create(project, source, 10, 20, "intro");

        var result = SubclipOperations.Retrim(project, project.Subclips[0].Id, 10, 15);

        Assert.True(result.Changed);
        Assert.Contains("was", result.Description);
    }

    [Fact]
    public void The_list_is_grouped_by_which_take_each_came_from()
    {
        var project = WithSource(out var source);
        SubclipOperations.Create(project, source, 1, 5, "one");
        SubclipOperations.Create(project, source, 10, 15, "two");

        var described = SubclipOperations.Describe(project);

        Assert.Contains("2 subclips", described);
        Assert.Contains("take1.mkv", described);
    }

    [Fact]
    public void With_none_it_says_so_specifically()
    {
        Assert.Equal("no subclips yet", SubclipOperations.Describe(Project.CreateDefault("empty")));
    }
}
