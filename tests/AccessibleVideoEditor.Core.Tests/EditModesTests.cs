using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// Ripple mode, snapping, and selecting a range by naming it.
///
/// All four had a documented key and no implementation, which is worse than
/// having neither: the keymap said the key worked, so a press that did nothing
/// read as the application having missed it rather than as the feature being
/// absent.
/// </summary>
public class EditModesTests
{
    /// <summary>Three five-second segments back to back: 0-5, 5-10, 10-15.</summary>
    private static Project ThreeSegments()
    {
        var project = Project.CreateDefault("modes");
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

    private static DocumentCursor CursorAt(Project project, double time)
    {
        var cursor = new DocumentCursor { FocusedTrack = project.ProgrammeTrack.Id };
        cursor.MoveTo(time);
        return cursor;
    }

    // ---- ripple mode -----------------------------------------------------

    [Fact]
    public void Ripple_mode_cycles_through_all_three_and_back()
    {
        var settings = new ProjectSettings { RippleMode = RippleMode.Off };

        EditModes.CycleRipple(settings);
        Assert.Equal(RippleMode.FocusedTrack, settings.RippleMode);

        EditModes.CycleRipple(settings);
        Assert.Equal(RippleMode.AllTracks, settings.RippleMode);

        EditModes.CycleRipple(settings);
        Assert.Equal(RippleMode.Off, settings.RippleMode);
    }

    [Fact]
    public void Every_ripple_mode_says_what_it_does_not_only_what_it_is_called()
    {
        // "Ripple off" alone leaves you to remember which way round it is, and
        // remembering is what this application exists to remove.
        Assert.Contains("leaves everything after it where it is", EditModes.Describe(RippleMode.Off));
        Assert.Contains("this track only", EditModes.Describe(RippleMode.FocusedTrack));
        Assert.Contains("every track together", EditModes.Describe(RippleMode.AllTracks));
    }

    [Fact]
    public void Cycling_announces_the_mode_it_arrived_at()
    {
        var settings = new ProjectSettings { RippleMode = RippleMode.Off };

        Assert.Equal(EditModes.Describe(RippleMode.FocusedTrack), EditModes.CycleRipple(settings));
    }

    // ---- snapping --------------------------------------------------------

    [Fact]
    public void Snapping_toggles_and_says_which_way_it_went()
    {
        var settings = new ProjectSettings { Snap = true };

        Assert.Contains("snapping off", EditModes.ToggleSnap(settings));
        Assert.False(settings.Snap);

        Assert.Contains("snapping on", EditModes.ToggleSnap(settings));
        Assert.True(settings.Snap);
    }

    // ---- select the segment ----------------------------------------------

    [Fact]
    public void Selecting_the_segment_takes_the_one_under_the_cursor()
    {
        var project = ThreeSegments();
        var result = Selections.Segment(project, TimelineMap.Build(project), CursorAt(project, 7.5));

        Assert.True(result.Selected);
        Assert.Equal(5, result.Range!.Value.From, 3);
        Assert.Equal(10, result.Range!.Value.To, 3);
    }

    [Fact]
    public void On_a_boundary_it_selects_the_segment_that_starts_there()
    {
        // Resting exactly on a cut is the normal position after navigating to
        // it, so it has to mean the segment you just arrived at rather than the
        // one you left.
        var project = ThreeSegments();
        var result = Selections.Segment(project, TimelineMap.Build(project), CursorAt(project, 5));

        Assert.Equal(5, result.Range!.Value.From, 3);
        Assert.Equal(10, result.Range!.Value.To, 3);
    }

    [Fact]
    public void Past_the_last_segment_says_so_rather_than_selecting_nothing()
    {
        // The empty cases must name the specific thing. "Nothing to select" and
        // "you have run off the end" need different reactions from you.
        var project = ThreeSegments();
        var result = Selections.Segment(project, TimelineMap.Build(project), CursorAt(project, 99));

        Assert.False(result.Selected);
        Assert.Contains("past the last segment", result.Announce);
    }

    [Fact]
    public void An_empty_track_says_it_has_no_segments()
    {
        var project = Project.CreateDefault("empty");
        var result = Selections.Segment(project, TimelineMap.Build(project), CursorAt(project, 0));

        Assert.False(result.Selected);
        Assert.Contains("no segments", result.Announce);
    }

    // ---- select the track ------------------------------------------------

    [Fact]
    public void Selecting_the_track_spans_first_start_to_last_end()
    {
        var project = ThreeSegments();
        var result = Selections.Track(project, TimelineMap.Build(project), CursorAt(project, 0));

        Assert.True(result.Selected);
        Assert.Equal(0, result.Range!.Value.From, 3);
        Assert.Equal(15, result.Range!.Value.To, 3);
    }

    [Fact]
    public void Selecting_the_track_counts_the_segments_out_loud()
    {
        // The count is the part you cannot otherwise get: a range of fifteen
        // seconds could be one segment or twenty.
        var project = ThreeSegments();
        var result = Selections.Track(project, TimelineMap.Build(project), CursorAt(project, 0));

        Assert.Contains("3 segments", result.Announce);
    }

    [Fact]
    public void One_segment_is_not_announced_as_one_segments()
    {
        var project = ThreeSegments();
        project.Spine.RemoveRange(1, 2);

        var result = Selections.Track(project, TimelineMap.Build(project), CursorAt(project, 0));

        Assert.Contains("1 segment,", result.Announce);
    }

    [Fact]
    public void With_no_track_focused_both_refuse_rather_than_guessing()
    {
        var project = ThreeSegments();
        var map = TimelineMap.Build(project);
        var cursor = new DocumentCursor();

        Assert.False(Selections.Segment(project, map, cursor).Selected);
        Assert.False(Selections.Track(project, map, cursor).Selected);
        Assert.Contains("no track focused", Selections.Track(project, map, cursor).Announce);
    }
}
