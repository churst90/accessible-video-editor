using AccessibleVideoEditor.Core.Model;
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
