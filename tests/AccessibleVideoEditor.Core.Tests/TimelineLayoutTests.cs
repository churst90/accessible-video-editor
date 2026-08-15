using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// The drawn timeline is computed in Core precisely so it can be asserted on
/// without a window. If the picture ever disagrees with the speech, one of
/// these is what should have caught it.
/// </summary>
public class TimelineLayoutTests
{
    private static Project ThreeSpans()
    {
        var project = Project.CreateDefault("layout");
        project.Settings.SpanPadIn = 0;
        project.Settings.SpanPadOut = 0;
        project.Settings.JumpCutDuration = 0;
        project.Settings.SceneTransitionDuration = 0;

        var source = new Source { Id = Ids.NewSource(), Path = "take.mkv", Duration = 60 };
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

    private static TimelineView Layout(
        Project project,
        DocumentCursor cursor,
        double width = 900,
        double pixelsPerSecond = 60,
        double viewStart = 0) =>
        TimelineLayout.Build(
            project,
            TimelineMap.Build(project),
            cursor,
            new TimelineViewport(width, pixelsPerSecond, viewStart));

    // ---- geometry --------------------------------------------------------

    [Fact]
    public void A_segment_is_drawn_where_it_plays_and_as_wide_as_it_lasts()
    {
        var project = ThreeSpans();
        var view = Layout(project, new DocumentCursor(), pixelsPerSecond: 60);

        var blocks = view.Lanes[0].Blocks;

        Assert.Equal(3, blocks.Count);
        Assert.Equal(0, blocks[0].X, 3);
        Assert.Equal(300, blocks[0].Width, 3);
        Assert.Equal(300, blocks[1].X, 3);
    }

    [Fact]
    public void A_segment_too_short_to_see_is_still_given_a_visible_width()
    {
        // A two-frame segment that draws as nothing looks like a missing
        // segment, which is worse than a slightly-too-wide one.
        var project = ThreeSpans();
        EditOperations.SplitAt(project, 0.02);

        var view = Layout(project, new DocumentCursor(), pixelsPerSecond: 4);

        Assert.All(view.Lanes[0].Blocks, b => Assert.True(b.Width >= TimelineLayout.MinimumBlockWidth));
    }

    [Fact]
    public void Only_what_is_on_screen_is_laid_out()
    {
        var project = ThreeSpans();
        var view = Layout(project, new DocumentCursor(), width: 120, pixelsPerSecond: 60);

        // 120 pixels at 60 per second shows the first two seconds, so only the
        // first five-second segment can be in it.
        Assert.Single(view.Lanes[0].Blocks);
    }

    [Fact]
    public void Scrolling_the_view_shifts_the_blocks_left_by_the_same_amount()
    {
        var project = ThreeSpans();

        var atZero = Layout(project, new DocumentCursor(), pixelsPerSecond: 60);
        var scrolled = Layout(project, new DocumentCursor(), pixelsPerSecond: 60, viewStart: 5);

        // Compared segment by segment rather than by position in the list: a
        // segment that ends exactly on the left edge is still partly on screen,
        // so scrolling does not simply drop the first block.
        var second = project.Spine[1].Id;

        Assert.Equal(
            atZero.Lanes[0].Blocks.First(b => b.Element == second).X - 300,
            scrolled.Lanes[0].Blocks.First(b => b.Element == second).X,
            3);
    }

    // ---- the cursor and the selection ------------------------------------

    [Fact]
    public void The_playhead_lands_on_the_cursor_and_the_segment_under_it_is_marked()
    {
        var project = ThreeSpans();
        var cursor = new DocumentCursor();
        cursor.MoveTo(7);

        var view = Layout(project, cursor, pixelsPerSecond: 60);

        Assert.Equal(420, view.PlayheadX!.Value, 3);
        Assert.True(view.Lanes[0].Blocks[1].UnderCursor);
        Assert.False(view.Lanes[0].Blocks[0].UnderCursor);
    }

    [Fact]
    public void A_playhead_off_the_left_of_the_view_is_not_drawn_at_the_edge()
    {
        // Clamping it to zero would draw a playhead that is not where the
        // cursor is, which is worse than not drawing one.
        var project = ThreeSpans();
        var cursor = new DocumentCursor();
        cursor.MoveTo(1);

        var view = Layout(project, cursor, pixelsPerSecond: 60, viewStart: 10);

        Assert.Null(view.PlayheadX);
    }

    [Fact]
    public void A_marked_range_becomes_a_highlighted_band()
    {
        var project = ThreeSpans();
        var cursor = new DocumentCursor();
        cursor.SelectRange(2, 6);

        var view = Layout(project, cursor, pixelsPerSecond: 60);

        Assert.NotNull(view.Selection);
        Assert.Equal(120, view.Selection!.Value.X, 3);
        Assert.Equal(240, view.Selection.Value.Width, 3);
    }

    [Fact]
    public void A_half_made_selection_draws_nothing_rather_than_a_zero_width_band()
    {
        var project = ThreeSpans();
        var cursor = new DocumentCursor();
        cursor.SetSelectionStart(3);

        Assert.Null(Layout(project, cursor).Selection);
    }

    [Fact]
    public void A_selection_running_off_the_screen_is_clipped_to_the_view()
    {
        var project = ThreeSpans();
        var cursor = new DocumentCursor();
        cursor.SelectRange(0, 100);

        var view = Layout(project, cursor, width: 600, pixelsPerSecond: 60);

        Assert.Equal(0, view.Selection!.Value.X, 3);
        Assert.Equal(600, view.Selection.Value.Width, 3);
    }

    // ---- lanes -----------------------------------------------------------

    [Fact]
    public void Every_track_gets_a_lane_in_the_order_the_arrow_keys_use()
    {
        var project = ThreeSpans();
        var view = Layout(project, new DocumentCursor());

        Assert.Equal(
            project.InOrder.Select(t => t.Name).ToList(),
            view.Lanes.Select(l => l.Name).ToList());
    }

    [Fact]
    public void The_focused_track_is_the_focused_lane()
    {
        var project = ThreeSpans();
        var graphics = project.Tracks.First(t => t.Kind == TrackKind.Graphics);

        var cursor = new DocumentCursor { FocusedTrack = graphics.Id };
        var view = Layout(project, cursor);

        Assert.Single(view.Lanes, l => l.IsFocused);
        Assert.Equal(graphics.Id, view.Lanes.First(l => l.IsFocused).Track);
    }

    [Fact]
    public void Lanes_stack_without_overlapping_and_start_below_the_ruler()
    {
        var project = ThreeSpans();
        var view = Layout(project, new DocumentCursor());

        Assert.Equal(view.RulerHeight, view.Lanes[0].Top, 3);

        for (var i = 1; i < view.Lanes.Count; i++)
        {
            Assert.True(view.Lanes[i].Top >= view.Lanes[i - 1].Top + view.Lanes[i - 1].Height);
        }
    }

    [Fact]
    public void Lanes_go_exactly_where_the_front_end_says_they_do()
    {
        // The front end knows where the real track headers ended up; the layout
        // does not. When it is told, it must not second-guess it, or the
        // picture drifts away from the list beside it row by row.
        var project = ThreeSpans();

        var slots = new List<LaneSlot>
        {
            new(30, 71), new(101, 71), new(172, 71), new(243, 71),
        };

        var view = TimelineLayout.Build(
            project,
            TimelineMap.Build(project),
            new DocumentCursor(),
            new TimelineViewport(900, 60, 0),
            slots);

        Assert.Equal(30, view.Lanes[0].Top, 3);
        Assert.Equal(71, view.Lanes[0].Height, 3);
        Assert.Equal(101, view.Lanes[1].Top, 3);
        Assert.Equal(243, view.Lanes[^1].Top, 3);
    }

    [Fact]
    public void Fewer_slots_than_tracks_falls_back_rather_than_dropping_a_lane()
    {
        var project = ThreeSpans();

        var view = TimelineLayout.Build(
            project,
            TimelineMap.Build(project),
            new DocumentCursor(),
            new TimelineViewport(900, 60, 0),
            [new LaneSlot(30, 71)]);

        Assert.Equal(project.InOrder.Count(), view.Lanes.Count);
        Assert.Equal(101, view.Lanes[1].Top, 3);
    }

    // ---- state carried onto the picture ----------------------------------

    [Fact]
    public void Mute_hide_and_disable_are_three_different_things_on_screen_too()
    {
        var project = ThreeSpans();
        EditOperations.ToggleMute(project, 1);
        project.Spine[1].Hidden = true;
        project.Spine[2].Enabled = false;

        var view = Layout(project, new DocumentCursor());
        var blocks = view.Lanes[0].Blocks;

        Assert.True(blocks[0].Muted);
        Assert.False(blocks[0].Hidden);
        Assert.True(blocks[1].Hidden);

        // A disabled segment is still drawn - it is restorable, not gone - but
        // it is only ever two blocks wide because the third left the programme.
        Assert.Equal(2, blocks.Count);
    }

    [Fact]
    public void A_transition_is_drawn_across_the_join_it_actually_covers()
    {
        var project = ThreeSpans();
        EditOperations.SetTransition(project, 5, new Transition
        {
            Type = TransitionType.WipeLeft,
            Duration = 1,
        });

        var view = Layout(project, new DocumentCursor(), pixelsPerSecond: 60);
        var second = view.Lanes[0].Blocks[1];

        Assert.True(second.HasTransitionIn);
        Assert.Equal(60, second.TransitionWidth, 3);
    }

    [Fact]
    public void A_card_is_labelled_with_its_words_and_coloured_as_a_card()
    {
        var project = ThreeSpans();
        project.Spine.Insert(1, new CardElement
        {
            Id = Ids.NewElement(),
            Length = 3,
            Composition = CardTemplates.TitleCard("Chapter one"),
        });

        var block = Layout(project, new DocumentCursor()).Lanes[0].Blocks[1];

        Assert.Equal(BlockKind.Card, block.Kind);
        Assert.Contains("Chapter one", block.Label);
    }

    [Fact]
    public void An_empty_project_says_so_rather_than_drawing_an_empty_grid()
    {
        var project = Project.CreateDefault("nothing");
        var view = Layout(project, new DocumentCursor());

        Assert.Equal("no project loaded", view.EmptyMessage);
    }

    // ---- the ruler -------------------------------------------------------

    [Theory]
    [InlineData(480, 0.25)]
    [InlineData(90, 1)]
    [InlineData(42, 2)]
    [InlineData(9, 10)]
    [InlineData(4, 30)]
    public void The_ruler_picks_an_interval_that_keeps_labels_apart(double pps, double expected)
    {
        Assert.Equal(expected, TimelineLayout.TickInterval(pps), 3);
    }

    [Fact]
    public void Labelled_ticks_are_never_closer_than_the_minimum_spacing()
    {
        foreach (var pps in new double[] { 4, 9, 18, 42, 90, 220, 480 })
        {
            var view = Layout(ThreeSpans(), new DocumentCursor(), width: 1200, pixelsPerSecond: pps);
            var labelled = view.Ticks.Where(t => t.Labelled).Select(t => t.X).ToList();

            for (var i = 1; i < labelled.Count; i++)
            {
                Assert.True(
                    labelled[i] - labelled[i - 1] >= TimelineLayout.MinimumTickSpacing - 0.001,
                    $"labels {labelled[i] - labelled[i - 1]} apart at {pps} pixels per second");
            }
        }
    }

    [Fact]
    public void The_ruler_never_marks_negative_time()
    {
        var view = Layout(ThreeSpans(), new DocumentCursor(), pixelsPerSecond: 60);

        Assert.All(view.Ticks, t => Assert.True(t.Time >= 0));
    }

    // ---- zoom is the step size -------------------------------------------

    [Fact]
    public void Finer_steps_mean_a_closer_zoom_at_every_level()
    {
        Granularity[] fineToCoarse =
        [
            Granularity.Frame, Granularity.Tenth, Granularity.Second,
            Granularity.Word, Granularity.Element, Granularity.Boundary, Granularity.Marker,
        ];

        for (var i = 1; i < fineToCoarse.Length; i++)
        {
            Assert.True(
                TimelineZoom.PixelsPerSecondFor(fineToCoarse[i - 1])
                > TimelineZoom.PixelsPerSecondFor(fineToCoarse[i]),
                $"{fineToCoarse[i - 1]} should be more zoomed in than {fineToCoarse[i]}");
        }
    }

    // ---- following the playhead ------------------------------------------

    [Fact]
    public void The_view_holds_still_while_the_playhead_is_comfortably_inside_it()
    {
        Assert.Equal(10, TimelineLayout.Follow(10, 15, 20), 3);
    }

    [Fact]
    public void The_view_moves_on_when_the_playhead_reaches_the_edge()
    {
        var moved = TimelineLayout.Follow(10, 29, 20);

        Assert.True(moved > 10);
        Assert.True(29 >= moved && 29 <= moved + 20);
    }

    [Fact]
    public void Jumping_backwards_brings_the_playhead_back_into_view()
    {
        var moved = TimelineLayout.Follow(100, 12, 20);

        Assert.True(12 >= moved && 12 <= moved + 20);
    }

    [Fact]
    public void The_view_never_scrolls_before_the_start_of_the_programme()
    {
        Assert.Equal(0, TimelineLayout.Follow(5, 0, 20), 3);
    }
}

/// <summary>
/// Peaks belong to the media, not to the edit, so the same extraction has to
/// serve any slice of it.
/// </summary>
public class WaveformTests
{
    private static WaveformData Ramp(int seconds = 10)
    {
        // One peak per tenth of a second, rising steadily, so a slice's maximum
        // is a value the test can predict exactly.
        var peaks = new float[seconds * 10];
        for (var i = 0; i < peaks.Length; i++) peaks[i] = (float)i / peaks.Length;

        return new WaveformData(Ids.NewSource(), seconds, peaks);
    }

    [Fact]
    public void A_slice_covers_the_part_of_the_source_the_segment_plays()
    {
        var data = Ramp();
        var slice = data.Slice(5, 6, 10);

        Assert.Equal(10, slice.Length);
        Assert.True(slice[0] >= 0.5f && slice[0] < 0.55f);
        Assert.True(slice[^1] > slice[0]);
    }

    [Fact]
    public void Buckets_take_the_loudest_peak_they_cover_rather_than_the_average()
    {
        // Averaging flattens transients: a single loud frame in a quiet second
        // has to survive being drawn 20 pixels wide.
        var peaks = new float[100];
        peaks[50] = 1f;

        var data = new WaveformData(Ids.NewSource(), 1, peaks);

        Assert.Equal(1f, data.Slice(0, 1, 10).Max());
    }

    [Fact]
    public void An_empty_or_impossible_slice_returns_silence_rather_than_throwing()
    {
        var data = Ramp();

        Assert.Empty(data.Slice(0, 1, 0));
        Assert.All(data.Slice(5, 5, 8), value => Assert.Equal(0, value));
        Assert.Equal(8, data.Slice(-4, 400, 8).Length);
    }

    [Fact]
    public void Peaks_come_out_of_raw_samples_at_the_resolution_asked_for()
    {
        var samples = new short[8000];
        for (var i = 0; i < samples.Length; i++) samples[i] = (short)(i < 4000 ? 16384 : 0);

        var data = WaveformData.FromSamples(Ids.NewSource(), samples, 8000, peaksPerSecond: 100);

        Assert.Equal(1, data.Duration, 3);
        Assert.Equal(100, data.Peaks.Count);
        Assert.Equal(0.5f, data.Peaks[0], 2);
        Assert.Equal(0f, data.Peaks[^1], 3);
    }

    [Fact]
    public void No_samples_means_no_waveform_rather_than_a_division_by_zero()
    {
        var data = WaveformData.FromSamples(Ids.NewSource(), [], 8000);

        Assert.Empty(data.Peaks);
        Assert.Equal(0, data.SecondsPerPeak);
    }
}
