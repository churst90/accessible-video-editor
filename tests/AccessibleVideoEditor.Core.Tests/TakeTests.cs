using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Serialization;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// Takes: recording into a segment again gives take 2, not a second segment.
/// The structure of the video does not change while you are still getting the
/// words right.
/// </summary>
public class TakeTests
{
    private static Project OneSentence(out SpanElement span)
    {
        var project = Project.CreateDefault("takes");
        project.Settings.SpanPadIn = 0;
        project.Settings.SpanPadOut = 0;
        project.Settings.JumpCutDuration = 0;
        project.Settings.SceneTransitionDuration = 0;

        var source = new Source { Id = Ids.NewSource(), Path = "take1.mkv", Duration = 60 };
        project.Sources.Add(source);

        span = new SpanElement
        {
            Id = Ids.NewElement(),
            Source = source.Id,
            SourceIn = 0,
            SourceOut = 5,
            Text = "hello there",
        };

        project.Spine.Add(span);
        return project;
    }

    private static Take Recorded(Project project, double length, string? label = null) => new()
    {
        Id = Ids.NewTake(),
        Source = project.Sources[0].Id,
        SourceIn = 20,
        SourceOut = 20 + length,
        Label = label,
    };

    [Fact]
    public void Recording_into_a_segment_gives_a_take_not_a_second_segment()
    {
        var project = OneSentence(out var span);

        EditOperations.AddTake(project, span.Id, Recorded(project, 4));

        Assert.Single(project.Spine);
        Assert.Equal(2, span.Takes.Count);
    }

    [Fact]
    public void The_original_media_becomes_take_one_so_there_is_a_way_back()
    {
        // Otherwise choosing take 2 would silently discard what was there.
        var project = OneSentence(out var span);

        EditOperations.AddTake(project, span.Id, Recorded(project, 4));

        Assert.Equal("original", span.Takes[0].Label);
        Assert.Equal(0, span.Takes[0].SourceIn, 3);
        Assert.Equal(5, span.Takes[0].SourceOut, 3);
    }

    [Fact]
    public void A_new_take_becomes_the_active_one()
    {
        var project = OneSentence(out var span);

        EditOperations.AddTake(project, span.Id, Recorded(project, 4, "the good one"));

        Assert.Equal(1, span.TakeIndex);
        Assert.Equal("the good one", span.ActiveTake!.Label);
    }

    [Fact]
    public void The_active_take_supplies_the_media_the_timeline_plays()
    {
        var project = OneSentence(out var span);
        Assert.Equal(5, TimelineMap.Build(project).Duration, 3);

        EditOperations.AddTake(project, span.Id, Recorded(project, 9));

        Assert.Equal(9, TimelineMap.Build(project).Duration, 3);
        Assert.Equal(20, TimelineMap.Build(project).Elements[0].Media!.Value.In, 3);
    }

    [Fact]
    public void Cycling_wraps_and_announces_which_take_and_how_long()
    {
        var project = OneSentence(out var span);
        EditOperations.AddTake(project, span.Id, Recorded(project, 4));
        EditOperations.AddTake(project, span.Id, Recorded(project, 7));

        Assert.Equal(3, span.Takes.Count);
        Assert.Equal(2, span.TakeIndex);

        var wrapped = EditOperations.CycleTake(project, span.Id, 1);

        Assert.Equal(0, span.TakeIndex);
        Assert.Contains("take 1 of 3", wrapped.Description);
        Assert.Contains("5 seconds", wrapped.Description);
    }

    [Fact]
    public void Cycling_backwards_works_too()
    {
        var project = OneSentence(out var span);
        EditOperations.AddTake(project, span.Id, Recorded(project, 4));

        span.TakeIndex = 0;
        EditOperations.CycleTake(project, span.Id, -1);

        Assert.Equal(1, span.TakeIndex);
    }

    [Fact]
    public void A_segment_with_no_takes_or_only_one_says_so_rather_than_doing_nothing()
    {
        var project = OneSentence(out var span);

        Assert.Contains("no takes", EditOperations.CycleTake(project, span.Id, 1).Description);

        EditOperations.AddTake(project, span.Id, Recorded(project, 4));
        span.Takes.RemoveAt(1);

        Assert.Contains("only one take", EditOperations.CycleTake(project, span.Id, 1).Description);
    }

    [Fact]
    public void Choosing_a_take_does_not_disturb_anything_anchored_to_the_segment()
    {
        // The point of takes over separate segments: overlays, markers and the
        // edit around it all stay attached.
        var project = OneSentence(out var span);

        project.Overlays.Add(new TitleItem
        {
            Id = Ids.NewItem(),
            Track = project.Tracks.First(t => t.Kind == TrackKind.Graphics).Id,
            Text = "stays put",
            Start = new TimeAnchor(span.Id, 1),
            Length = 2,
        });

        EditOperations.AddTake(project, span.Id, Recorded(project, 9));

        Assert.Equal(1, TimelineMap.Build(project).ResolveAnchor(project.Overlays[0].Start)!.Value, 3);
        Assert.Equal(span.Id, project.Overlays[0].Start.Element);
    }

    [Fact]
    public void Deleting_a_take_leaves_the_rest_and_refuses_to_delete_the_last()
    {
        var project = OneSentence(out var span);
        EditOperations.AddTake(project, span.Id, Recorded(project, 4));

        var deleted = EditOperations.DeleteTake(project, span.Id);
        Assert.True(deleted.Changed);
        Assert.Single(span.Takes);

        var refused = EditOperations.DeleteTake(project, span.Id);
        Assert.False(refused.Changed);
        Assert.Contains("only take", refused.Description);
    }

    [Fact]
    public void Capture_issues_travel_with_the_take_that_has_them()
    {
        // A take that drifted out of frame is exactly the one you need to know
        // about, and it is the thing you cannot hear when auditioning.
        var project = OneSentence(out var span);

        var flawed = Recorded(project, 4);
        flawed.Issues.Add(new CaptureIssue
        {
            Start = 1,
            End = 3,
            Kind = CaptureIssueKind.OutOfFrame,
        });

        EditOperations.AddTake(project, span.Id, flawed);

        Assert.Contains("1 capture issue", span.ActiveTake!.Describe(1, 2));
    }

    [Fact]
    public void Takes_survive_a_project_round_trip()
    {
        var project = OneSentence(out var span);
        EditOperations.AddTake(project, span.Id, Recorded(project, 4, "second attempt"));

        var restored = ProjectJson.Deserialise(ProjectJson.Serialise(project));
        var element = restored.Spine[0];

        Assert.Equal(2, element.Takes.Count);
        Assert.Equal(1, element.TakeIndex);
        Assert.Equal("second attempt", element.ActiveTake!.Label);
    }

    [Fact]
    public void The_cursor_readout_is_unchanged_by_which_take_is_active()
    {
        // Takes are an attribute of a segment, not a different kind of thing.
        var project = OneSentence(out var span);
        EditOperations.AddTake(project, span.Id, Recorded(project, 9));

        var map = TimelineMap.Build(project);
        var content = TrackProbe.At(project, map, project.ProgrammeTrack.Id, 1);

        Assert.Equal(ContentKind.Video, content.Kind);
    }
}
