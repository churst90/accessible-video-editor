using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Tests;

public class TimelineMapTests
{
    private static Project ThreeSpans()
    {
        var project = Project.CreateDefault("test");
        project.Settings.SpanPadIn = 0;
        project.Settings.SpanPadOut = 0;
        project.Settings.JumpCutDuration = 0;
        project.Settings.SceneTransitionDuration = 0;

        var source = new Source { Id = Ids.NewSource(), Path = "take1.mkv", Duration = 60 };
        project.Sources.Add(source);

        for (var i = 0; i < 3; i++)
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

    [Fact]
    public void Programme_time_is_the_concatenation_of_enabled_elements()
    {
        var map = TimelineMap.Build(ThreeSpans());

        Assert.Equal(15, map.Duration, 3);
        Assert.Equal([0, 5, 10], map.Elements.Select(e => e.ProgrammeStart).ToArray());
    }

    [Fact]
    public void Disabled_elements_contribute_no_programme_time()
    {
        var project = ThreeSpans();
        project.Spine[1].Enabled = false;

        var map = TimelineMap.Build(project);

        Assert.Equal(10, map.Duration, 3);
        Assert.Null(map.ResolveAnchor(new TimeAnchor(project.Spine[1].Id)));
    }

    [Fact]
    public void Source_time_round_trips_through_programme_time()
    {
        var project = ThreeSpans();
        var map = TimelineMap.Build(project);
        var source = project.Sources[0].Id;

        // 12 seconds into take1 sits inside the second span, which starts at 10.
        var programme = map.FromSource(source, 12);

        Assert.NotNull(programme);
        Assert.Equal(7, programme!.Value, 3);

        var back = map.ToSource(programme.Value);
        Assert.Equal(12, back!.Value.Time, 3);
    }

    [Fact]
    public void A_moment_that_was_cut_has_no_programme_time()
    {
        // This is what the transcript pane must announce rather than silently
        // snapping the cursor somewhere plausible.
        var map = TimelineMap.Build(ThreeSpans());

        Assert.Null(map.FromSource(map.Elements[0].Media!.Value.Source, 7.5));
    }

    [Fact]
    public void Transitions_shorten_the_programme()
    {
        var project = ThreeSpans();
        project.Settings.SceneTransitionDuration = 0.4;
        project.Spine[1].TransitionIn = new Transition { Type = TransitionType.Fade, Duration = 1 };

        var map = TimelineMap.Build(project);

        // The one second dissolve overlaps, so total drops by one second.
        Assert.Equal(14, map.Duration, 3);
    }

    [Fact]
    public void Split_keeps_the_original_id_on_the_first_half()
    {
        var project = ThreeSpans();
        var originalId = project.Spine[0].Id;

        var result = EditOperations.SplitAt(project, 2);

        Assert.True(result.Changed);
        Assert.Equal(4, project.Spine.Count);
        Assert.Equal(originalId, project.Spine[0].Id);
        Assert.Equal(15, TimelineMap.Build(project).Duration, 3);
    }

    [Fact]
    public void Ripple_delete_closes_the_gap()
    {
        var project = ThreeSpans();

        var result = EditOperations.RippleDelete(project, new TimeSelection(2, 7));

        Assert.True(result.Changed);
        Assert.Equal(10, TimelineMap.Build(project).Duration, 3);
    }

    [Fact]
    public void Lift_preserves_downstream_timing()
    {
        var project = ThreeSpans();

        var result = EditOperations.Lift(project, new TimeSelection(2, 7));

        Assert.True(result.Changed);
        Assert.Equal(15, TimelineMap.Build(project).Duration, 3);
    }

    [Fact]
    public void Overlays_ride_along_when_the_spine_ripples()
    {
        var project = ThreeSpans();
        var third = project.Spine[2];

        project.Overlays.Add(new TitleItem
        {
            Id = Ids.NewItem(),
            Track = project.Tracks.First(t => t.Kind == TrackKind.Graphics).Id,
            Text = "hello",
            Start = new TimeAnchor(third.Id),
            Length = 2,
        });

        var before = TimelineMap.Build(project).ResolveAnchor(project.Overlays[0].Start);
        Assert.Equal(10, before!.Value, 3);

        EditOperations.RippleDelete(project, new TimeSelection(0, 5));

        // The title still sits on the same sentence, five seconds earlier.
        var after = TimelineMap.Build(project).ResolveAnchor(project.Overlays[0].Start);
        Assert.Equal(5, after!.Value, 3);
    }
}
