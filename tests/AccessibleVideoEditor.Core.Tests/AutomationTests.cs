using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;
using AccessibleVideoEditor.Engine;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// Values that change over a segment, stored as named shapes rather than as
/// keyframes. A curve is a picture of a decision; the decision has a name, and
/// the name is what can be spoken and adjusted by keystroke.
/// </summary>
public class AutomationTests
{
    [Fact]
    public void A_duck_returns_to_where_it_started()
    {
        // The defining property: music ducks under narration and comes back. If
        // it did not, it would be a ramp and you would need a second one to
        // undo it.
        var duck = Automation.Duck(restingDb: 0, duckedDb: -18, length: 4);

        Assert.Equal(0, duck.At(0, 10), 1);
        Assert.Equal(-18, duck.At(2, 10), 1);
        Assert.Equal(0, duck.At(4, 10), 1);
    }

    [Fact]
    public void A_ramp_goes_from_one_value_to_the_other_and_holds()
    {
        var ramp = new Automation
        {
            Target = AutomationTarget.Volume,
            Shape = AutomationShape.Ramp,
            From = -20,
            To = 0,
            Length = 5,
        };

        Assert.Equal(-20, ramp.At(0, 10), 1);
        Assert.Equal(-10, ramp.At(2.5, 10), 1);
        Assert.Equal(0, ramp.At(5, 10), 1);
        Assert.Equal(0, ramp.At(9, 10), 1);
    }

    [Fact]
    public void A_delay_holds_the_starting_value_until_it_begins()
    {
        var ramp = new Automation
        {
            Target = AutomationTarget.Volume,
            Shape = AutomationShape.Ramp,
            From = -20, To = 0, Length = 2, Delay = 3,
        };

        Assert.Equal(-20, ramp.At(1, 10), 1);
        Assert.Equal(0, ramp.At(5, 10), 1);
    }

    [Fact]
    public void A_zero_length_shape_spans_the_whole_segment()
    {
        var ramp = new Automation
        {
            Target = AutomationTarget.Volume, Shape = AutomationShape.Ramp, From = 0, To = -12,
        };

        Assert.Equal(-6, ramp.At(5, 10), 1);
    }

    [Fact]
    public void Easing_in_and_out_are_slower_and_faster_than_a_straight_ramp()
    {
        var easeIn = new Automation
        {
            Target = AutomationTarget.Volume, Shape = AutomationShape.EaseIn, From = 0, To = 10, Length = 10,
        };

        var easeOut = new Automation
        {
            Target = AutomationTarget.Volume, Shape = AutomationShape.EaseOut, From = 0, To = 10, Length = 10,
        };

        Assert.True(easeIn.At(2.5, 10) < 2.5);
        Assert.True(easeOut.At(2.5, 10) > 2.5);
    }

    [Fact]
    public void Every_shape_reads_back_as_a_sentence_naming_the_decision()
    {
        var duck = Automation.Duck(0, -18, 4);

        var described = duck.Describe();

        Assert.Contains("volume", described);
        Assert.Contains("dips", described);
        Assert.Contains("comes back", described);
        Assert.Contains("decibels", described);
    }

    [Fact]
    public void A_shape_over_the_whole_segment_says_so_rather_than_giving_a_length()
    {
        var steady = new Automation
        {
            Target = AutomationTarget.Volume, Shape = AutomationShape.Ramp, From = 0, To = -6,
        };

        Assert.Contains("whole segment", steady.Describe());
    }

    [Fact]
    public void Position_automation_describes_itself_in_its_own_words()
    {
        var move = new Automation
        {
            Target = AutomationTarget.PositionX, Shape = AutomationShape.Ramp, From = 0, To = 50, Length = 2,
        };

        Assert.Contains("horizontal position", move.Describe());
        Assert.Contains("percent", move.Describe());
    }

    // ---- the ffmpeg expression -------------------------------------------

    [Fact]
    public void Volume_automation_evaluates_per_frame_not_once()
    {
        // Without eval=frame the expression is evaluated a single time and the
        // whole point - that it changes - is lost silently.
        var filter = AutomationFilters.Volume([Automation.Duck(0, -18, 4)], 10);

        Assert.Contains("eval=frame", filter);
    }

    [Fact]
    public void A_segment_with_no_automation_produces_no_filter()
    {
        Assert.Empty(AutomationFilters.Volume([], 10));
        Assert.Empty(AutomationFilters.Volume([new Automation { Target = AutomationTarget.Opacity }], 10));
    }

    [Fact]
    public void The_expression_clamps_so_the_value_holds_at_both_ends()
    {
        var expression = AutomationFilters.Expression(
            new Automation
            {
                Target = AutomationTarget.Volume, Shape = AutomationShape.Ramp,
                From = -20, To = 0, Length = 2,
            },
            10);

        Assert.Contains("clip(", expression);
    }

    [Fact]
    public void The_expression_uses_invariant_numbers()
    {
        var expression = AutomationFilters.Expression(Automation.Duck(0, -6, 2.5), 10);

        Assert.Contains("2.5", expression);
        Assert.DoesNotContain("2,5", expression);
    }

    // ---- position and opacity on an overlay ------------------------------

    [Fact]
    public void Opacity_is_a_fraction_because_that_is_what_drawtext_wants()
    {
        // Stored as a percentage, because that is what a person says. drawtext
        // wants 0 to 1, and passing 50 straight through would be silently
        // fully opaque rather than half.
        var alpha = AutomationFilters.Opacity(
            [new Automation
            {
                Target = AutomationTarget.Opacity,
                Shape = AutomationShape.Steady, To = 50,
            }],
            duration: 4,
            startsAt: 0)!;

        Assert.Contains("0.5", alpha);
    }

    [Fact]
    public void Opacity_is_clamped_because_drawtext_misbehaves_outside_zero_to_one()
    {
        var alpha = AutomationFilters.Opacity(
            [new Automation
            {
                Target = AutomationTarget.Opacity,
                Shape = AutomationShape.Ramp, From = 0, To = 100, Length = 1,
            }],
            duration: 4,
            startsAt: 0)!;

        Assert.StartsWith("clip(", alpha);
    }

    [Fact]
    public void An_overlays_time_is_offset_by_where_it_starts_on_the_timeline()
    {
        // A segment renders as its own file beginning at zero; an overlay is
        // drawn onto the finished timeline. Ignoring that would make every
        // title animate at the top of the video instead of at its own start.
        var shape = new Automation
        {
            Target = AutomationTarget.Opacity,
            Shape = AutomationShape.Ramp, From = 0, To = 100, Length = 1,
        };

        var atZero = AutomationFilters.Opacity([shape], 4, startsAt: 0)!;
        var later = AutomationFilters.Opacity([shape], 4, startsAt: 12)!;

        Assert.Contains("t-0", atZero);
        Assert.Contains("t-12", later);
    }

    [Fact]
    public void A_delay_adds_to_where_the_overlay_starts_rather_than_replacing_it()
    {
        var shape = new Automation
        {
            Target = AutomationTarget.Opacity,
            Shape = AutomationShape.Ramp, From = 100, To = 0, Length = 0.5, Delay = 2,
        };

        Assert.Contains("t-14", AutomationFilters.Opacity([shape], 4, startsAt: 12)!);
    }

    [Fact]
    public void Each_axis_is_asked_for_separately_and_is_null_when_unset()
    {
        var horizontalOnly = new[]
        {
            new Automation
            {
                Target = AutomationTarget.PositionX,
                Shape = AutomationShape.EaseOut, From = -20, To = 10, Length = 0.6,
            },
        };

        Assert.NotNull(AutomationFilters.Position(horizontalOnly, horizontal: true, 3, 0));
        Assert.Null(AutomationFilters.Position(horizontalOnly, horizontal: false, 3, 0));
    }

    [Fact]
    public void An_overlay_with_no_automation_asks_for_nothing()
    {
        Assert.Null(AutomationFilters.Opacity([], 3, 0));
        Assert.Null(AutomationFilters.Position([], horizontal: true, 3, 0));
    }

    [Fact]
    public void An_automated_title_puts_its_expressions_into_the_drawtext()
    {
        var project = Project.CreateDefault("titles");
        project.Settings.SpanPadIn = 0;
        project.Settings.SpanPadOut = 0;
        project.Settings.JumpCutDuration = 0;
        project.Settings.SceneTransitionDuration = 0;

        var source = new Source { Id = Ids.NewSource(), Path = "take.mkv", Duration = 60 };
        project.Sources.Add(source);

        project.Spine.Add(new SpanElement
        {
            Id = Ids.NewElement(), Source = source.Id, SourceIn = 0, SourceOut = 10, Text = "hello",
        });

        var title = new TitleItem
        {
            Id = Ids.NewItem(),
            Track = project.Tracks.First(t => t.Kind == TrackKind.Graphics).Id,
            Start = new TimeAnchor(project.Spine[0].Id, 0),
            Length = 4,
            Text = "Cody Hurst",
        };

        title.Automation.Add(new Automation
        {
            Target = AutomationTarget.Opacity,
            Shape = AutomationShape.Ramp, From = 0, To = 100, Length = 0.5,
        });

        title.Automation.Add(new Automation
        {
            Target = AutomationTarget.PositionX,
            Shape = AutomationShape.EaseOut, From = -20, To = 50, Length = 0.5,
        });

        project.Overlays.Add(title);

        var filter = OverlayFilters.Video(project, TimelineMap.Build(project), 1920, 1080, "/tmp/f.ttf")!;

        Assert.Contains("alpha=", filter);
        Assert.Contains("clip(", filter);

        // The x expression must still centre the text on the moving point.
        Assert.Contains("(text_w/2)", filter);
    }

    [Fact]
    public void A_title_with_no_automation_keeps_a_plain_placement()
    {
        // No expression machinery on the overwhelmingly common case.
        var project = Project.CreateDefault("plain");
        var source = new Source { Id = Ids.NewSource(), Path = "take.mkv", Duration = 60 };
        project.Sources.Add(source);

        project.Spine.Add(new SpanElement
        {
            Id = Ids.NewElement(), Source = source.Id, SourceIn = 0, SourceOut = 10, Text = "hello",
        });

        project.Overlays.Add(new TitleItem
        {
            Id = Ids.NewItem(),
            Track = project.Tracks.First(t => t.Kind == TrackKind.Graphics).Id,
            Start = new TimeAnchor(project.Spine[0].Id, 0),
            Length = 4,
            Text = "Cody Hurst",
        });

        var filter = OverlayFilters.Video(project, TimelineMap.Build(project), 1920, 1080, "/tmp/f.ttf")!;

        Assert.DoesNotContain("alpha=", filter);
        Assert.DoesNotContain("clip(", filter);
    }

    [Fact]
    public void A_slide_ends_exactly_where_the_placement_said_it_would_sit()
    {
        // The point of sliding in: it stops at the position the layer was
        // given, rather than somewhere near it.
        var slide = new Automation
        {
            Target = AutomationTarget.PositionX,
            Shape = AutomationShape.EaseOut, From = -20, To = 50, Length = 0.6,
        };

        Assert.Equal(50, slide.At(0.6, 3), 3);
        Assert.Equal(50, slide.At(2.9, 3), 3);
    }

    [Fact]
    public void Decibels_become_a_linear_gain_because_that_is_what_volume_multiplies_by()
    {
        // -6 dB is about half; the filter takes a multiplier, not decibels, and
        // passing decibels straight through would be inaudibly wrong at small
        // values and catastrophic at large ones.
        var expression = AutomationFilters.Expression(
            new Automation
            {
                Target = AutomationTarget.Volume, Shape = AutomationShape.Steady, To = -6,
            },
            10);

        Assert.Contains("0.5", expression);
    }
}
