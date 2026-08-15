using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Tests;

public class EditingModelTests
{
    private static Project ThreeSpans()
    {
        var project = Project.CreateDefault("editing");
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
                Words = [new Word($"sentence", i * 10, i * 10 + 2), new Word($"{i}", i * 10 + 2.5, i * 10 + 4)],
            });
        }

        return project;
    }

    // ---- heal ------------------------------------------------------------

    [Fact]
    public void Split_then_heal_returns_the_timeline_to_where_it_started()
    {
        var project = ThreeSpans();
        var before = TimelineMap.Build(project).Duration;

        EditOperations.SplitAt(project, 2);
        Assert.Equal(4, project.Spine.Count);

        var healed = EditOperations.Heal(project, 2);

        Assert.True(healed.Changed);
        Assert.Equal(3, project.Spine.Count);
        Assert.Equal(before, TimelineMap.Build(project).Duration, 3);
    }

    [Fact]
    public void Healing_across_a_real_edit_is_refused_with_a_reason()
    {
        // Two spans that are not contiguous in the source are an edit, not a
        // split, and joining them would silently reinstate cut material.
        var project = ThreeSpans();
        var result = EditOperations.Heal(project, 6);

        Assert.False(result.Changed);
        Assert.Contains("not two halves of one shot", result.Description);
    }

    [Fact]
    public void Healing_moves_overlays_from_the_absorbed_half_onto_the_survivor()
    {
        var project = ThreeSpans();
        EditOperations.SplitAt(project, 2);

        var secondHalf = project.Spine[1];
        project.Overlays.Add(new TitleItem
        {
            Id = Ids.NewItem(),
            Track = project.Tracks.First(t => t.Kind == TrackKind.Graphics).Id,
            Text = "hello",
            Start = new TimeAnchor(secondHalf.Id, 0.5),
            Length = 1,
        });

        var at = TimelineMap.Build(project).ResolveAnchor(project.Overlays[0].Start);
        EditOperations.Heal(project, 2);

        Assert.Equal(at!.Value, TimelineMap.Build(project).ResolveAnchor(project.Overlays[0].Start)!.Value, 3);
    }

    // ---- trim and roll ---------------------------------------------------

    [Fact]
    public void Trimming_the_head_shortens_the_element_and_ripples()
    {
        var project = ThreeSpans();
        var result = EditOperations.TrimHead(project, 2);

        Assert.True(result.Changed);
        Assert.Equal(13, TimelineMap.Build(project).Duration, 3);
    }

    [Fact]
    public void Rolling_a_boundary_leaves_total_length_unchanged()
    {
        var project = ThreeSpans();
        var before = TimelineMap.Build(project).Duration;

        var result = EditOperations.Roll(project, 5, 1);

        Assert.True(result.Changed);
        Assert.Equal(before, TimelineMap.Build(project).Duration, 3);
    }

    // ---- retiming --------------------------------------------------------

    [Fact]
    public void Doubling_the_speed_halves_the_time_it_occupies()
    {
        var project = ThreeSpans();
        EditOperations.SetSpeed(project, 1, 2.0);

        var map = TimelineMap.Build(project);

        Assert.Equal(2.5, map.Elements[0].Duration, 3);
        Assert.Equal(12.5, map.Duration, 3);
    }

    [Fact]
    public void A_retimed_element_still_maps_to_the_right_source_frame()
    {
        // Splitting or trimming a retimed clip has to scale, not just offset,
        // or the cut lands somewhere other than where it was heard.
        var project = ThreeSpans();
        EditOperations.SetSpeed(project, 1, 2.0);

        var source = TimelineMap.Build(project).ToSource(1.0);

        Assert.NotNull(source);
        Assert.Equal(2.0, source!.Value.Time, 3);
    }

    // ---- mute ------------------------------------------------------------

    [Fact]
    public void Muting_an_element_keeps_it_on_the_timeline()
    {
        var project = ThreeSpans();
        var before = TimelineMap.Build(project).Duration;

        EditOperations.ToggleMute(project, 1);

        Assert.True(project.Spine[0].Muted);
        Assert.Equal(before, TimelineMap.Build(project).Duration, 3);
    }

    [Fact]
    public void A_muted_element_says_so_when_the_cursor_lands_on_it()
    {
        var project = ThreeSpans();
        EditOperations.ToggleMute(project, 1);

        var content = TrackProbe.At(project, TimelineMap.Build(project), project.ProgrammeTrack.Id, 1);

        Assert.Contains("muted", content.Word);
    }

    // ---- transitions -----------------------------------------------------

    [Fact]
    public void A_transition_is_reported_where_it_actually_sits()
    {
        var project = ThreeSpans();
        EditOperations.SetTransition(project, 5, new Transition
        {
            Type = TransitionType.WipeLeft,
            Duration = 1,
        });

        var map = TimelineMap.Build(project);
        var inside = TrackProbe.At(project, map, project.ProgrammeTrack.Id, 4.5);
        var after = TrackProbe.At(project, map, project.ProgrammeTrack.Id, 8);

        Assert.Equal(ContentKind.Transition, inside.Kind);
        Assert.Contains("wipeleft", inside.Label);
        Assert.NotEqual(ContentKind.Transition, after.Kind);
    }

    [Fact]
    public void The_first_element_cannot_take_an_incoming_transition()
    {
        var project = ThreeSpans();
        var result = EditOperations.SetTransition(project, 0, new Transition());

        Assert.False(result.Changed);
        Assert.Contains("no incoming boundary", result.Description);
    }

    // ---- context-aware delete --------------------------------------------

    [Fact]
    public void A_time_selection_wins_over_whatever_is_under_the_cursor()
    {
        var project = ThreeSpans();
        var cursor = new DocumentCursor { FocusedTrack = project.ProgrammeTrack.Id };
        cursor.MoveTo(2);
        cursor.SelectRange(0, 7);

        var target = EditTarget.Resolve(project, TimelineMap.Build(project), cursor);

        Assert.Equal(EditTargetKind.Selection, target.Kind);
    }

    [Fact]
    public void With_no_selection_the_target_is_the_element_under_the_cursor()
    {
        var project = ThreeSpans();
        var cursor = new DocumentCursor { FocusedTrack = project.ProgrammeTrack.Id };
        cursor.MoveTo(7);

        var target = EditTarget.Resolve(project, TimelineMap.Build(project), cursor);

        Assert.Equal(EditTargetKind.Element, target.Kind);
        Assert.Equal(project.Spine[1].Id, target.Element);
    }

    [Fact]
    public void A_locked_track_yields_no_target_at_all()
    {
        var project = ThreeSpans();
        project.ProgrammeTrack.Locked = true;

        var cursor = new DocumentCursor { FocusedTrack = project.ProgrammeTrack.Id };
        cursor.MoveTo(2);

        var target = EditTarget.Resolve(project, TimelineMap.Build(project), cursor);

        Assert.False(target.IsActionable);
        Assert.Contains("locked", target.Describe);
    }

    [Fact]
    public void An_empty_spot_on_a_track_yields_nothing_rather_than_guessing()
    {
        var project = ThreeSpans();
        var graphics = project.Tracks.First(t => t.Kind == TrackKind.Graphics);

        var cursor = new DocumentCursor { FocusedTrack = graphics.Id };
        cursor.MoveTo(2);

        var target = EditTarget.Resolve(project, TimelineMap.Build(project), cursor);

        Assert.False(target.IsActionable);
        Assert.Equal("nothing under the cursor", target.Describe);
    }
}

/// <summary>
/// Which of the two possible things a destructive key acts on. Asking "did you
/// mean the range or the segment?" every time would be safer and unusable, so
/// the last thing you were working with wins.
/// </summary>
public class EditIntentTests
{
    private static Project ThreeSegments()
    {
        var project = Project.CreateDefault("intent");
        project.Settings.SpanPadIn = 0;
        project.Settings.SpanPadOut = 0;
        project.Settings.JumpCutDuration = 0;
        project.Settings.SceneTransitionDuration = 0;

        var source = new Source { Id = Ids.NewSource(), Path = "a.mkv", Duration = 60 };
        project.Sources.Add(source);

        for (var i = 0; i < 3; i++)
        {
            project.Spine.Add(new SpanElement
            {
                Id = Ids.NewElement(), Source = source.Id,
                SourceIn = i * 10, SourceOut = i * 10 + 5, Text = $"line {i}",
            });
        }

        return project;
    }

    [Fact]
    public void Marking_a_range_means_you_mean_the_range()
    {
        var project = ThreeSegments();
        var cursor = new DocumentCursor { FocusedTrack = project.ProgrammeTrack.Id };
        cursor.MoveTo(7);
        cursor.SetSelectionStart(2);
        cursor.SetSelectionEnd(8);

        var target = EditTarget.Resolve(project, TimelineMap.Build(project), cursor);

        Assert.Equal(EditTargetKind.Selection, target.Kind);
    }

    [Fact]
    public void Stepping_by_segment_afterwards_means_you_mean_the_segment()
    {
        // The complaint this answers: a range marked earlier quietly capturing
        // a delete meant for the segment you have since moved to.
        var project = ThreeSegments();
        var cursor = new DocumentCursor { FocusedTrack = project.ProgrammeTrack.Id };
        cursor.SetSelectionStart(2);
        cursor.SetSelectionEnd(4);

        cursor.Intend(EditIntent.Segment);
        cursor.MoveTo(7);

        var target = EditTarget.Resolve(project, TimelineMap.Build(project), cursor);

        Assert.Equal(EditTargetKind.Element, target.Kind);
        Assert.Equal(project.Spine[1].Id, target.Element);
    }

    [Fact]
    public void Clearing_the_selection_returns_to_meaning_the_segment()
    {
        var project = ThreeSegments();
        var cursor = new DocumentCursor { FocusedTrack = project.ProgrammeTrack.Id };
        cursor.MoveTo(7);
        cursor.SelectRange(0, 3);
        cursor.ClearSelection();

        Assert.Equal(EditIntent.Segment, cursor.Intent);
        Assert.Equal(EditTargetKind.Element,
            EditTarget.Resolve(project, TimelineMap.Build(project), cursor).Kind);
    }

    [Fact]
    public void A_half_made_selection_reports_its_in_point_rather_than_nothing()
    {
        // Setting the in point and not yet the out is a legitimate state;
        // calling it "no selection" makes the key look broken.
        var cursor = new DocumentCursor();
        cursor.MoveTo(0);
        cursor.SetSelectionStart(0);

        var spoken = cursor.Selection!.Value.DescribeMark(isStart: true);

        Assert.Contains("in point", spoken);
        Assert.DoesNotContain("no selection", spoken);
    }

    [Fact]
    public void A_completed_selection_describes_its_length()
    {
        var cursor = new DocumentCursor();
        cursor.SetSelectionStart(2);
        cursor.SetSelectionEnd(6);

        Assert.Contains("4 seconds", cursor.Selection!.Value.DescribeMark(isStart: false));
    }
}
