using System.Globalization;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// Turns a named automation shape into an ffmpeg expression.
///
/// ffmpeg's <c>volume</c> filter takes an expression evaluated per sample, with
/// <c>t</c> as the time in seconds, so a shape becomes arithmetic rather than a
/// list of points. That is the whole reason the model stores shapes: a curve
/// would have to be flattened into keyframes and interpolated by hand, and this
/// does not.
/// </summary>
public static class AutomationFilters
{
    /// <summary>
    /// The volume automation for a segment, or empty when there is none. Only
    /// one volume shape applies - a second would be two people turning the same
    /// knob, and the result would depend on order rather than on intent.
    /// </summary>
    public static string Volume(IReadOnlyList<Automation> automation, double duration)
    {
        var shape = automation.FirstOrDefault(a => a.Target == AutomationTarget.Volume);
        if (shape is null) return string.Empty;

        // eval=frame rather than the default: the default evaluates once and
        // the whole point here is that it changes.
        return $"volume=eval=frame:volume='{Expression(shape, duration)}'";
    }

    /// <summary>
    /// The shape as an expression in <c>t</c>, returning a linear gain rather
    /// than decibels because that is what the volume filter multiplies by.
    /// </summary>
    public static string Expression(Automation shape, double duration)
    {
        var span = shape.Length > 0 ? shape.Length : Math.Max(0.001, duration - shape.Delay);
        var delay = shape.Delay;

        // Progress through the shape, clamped, so the value holds at each end
        // rather than running off.
        var t = $"clip((t-{N(delay)})/{N(span)},0,1)";

        var from = Gain(shape.From);
        var to = Gain(shape.To);

        return shape.Shape switch
        {
            AutomationShape.Steady => N(to),

            AutomationShape.Ramp => Lerp(from, to, t),

            AutomationShape.EaseIn => Lerp(from, to, $"pow({t},2)"),

            AutomationShape.EaseOut => Lerp(from, to, $"(1-pow(1-{t},2))"),

            AutomationShape.Rise => Lerp(from, to, $"({t}*{t}*(3-2*{t}))"),

            // sin gives 0 at both ends and 1 in the middle, which is exactly a
            // dip that returns to where it started.
            AutomationShape.Dip => Lerp(from, to, $"sin({t}*3.14159265)"),

            _ => N(to),
        };
    }

    private static string Lerp(double from, double to, string t) =>
        $"({N(from)}+({N(to - from)})*{t})";

    /// <summary>Decibels to a linear multiplier, which is what volume wants.</summary>
    private static double Gain(double decibels) => Math.Pow(10, decibels / 20);

    private static string N(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);
}
