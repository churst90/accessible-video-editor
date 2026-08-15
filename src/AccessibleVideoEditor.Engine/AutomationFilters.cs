using System.Globalization;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// Turns a named automation shape into an ffmpeg expression.
///
/// Both filters this targets - <c>volume</c> for sound and <c>drawtext</c> for
/// titles - evaluate their parameters per frame with <c>t</c> as the time in
/// seconds, so a shape becomes arithmetic rather than a list of points. That is
/// the whole reason the model stores shapes: a curve would have to be flattened
/// into keyframes and interpolated by hand, and this does not.
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
    /// The alpha expression for an overlay, or null when its opacity is not
    /// automated. <paramref name="startsAt"/> is the item's programme start,
    /// because an overlay is drawn onto the finished timeline while a segment is
    /// rendered as its own file beginning at zero.
    /// </summary>
    public static string? Opacity(IReadOnlyList<Automation> automation, double duration, double startsAt)
    {
        var shape = automation.FirstOrDefault(a => a.Target == AutomationTarget.Opacity);

        // Percent to a 0-1 alpha, then clamped: drawtext silently misbehaves
        // outside that range rather than complaining.
        return shape is null
            ? null
            : $"clip({Expression(shape, duration, startsAt, Percent)},0,1)";
    }

    /// <summary>
    /// The horizontal or vertical placement expression, as a fraction of the
    /// frame, or null when that axis is not automated.
    /// </summary>
    public static string? Position(
        IReadOnlyList<Automation> automation,
        bool horizontal,
        double duration,
        double startsAt)
    {
        var target = horizontal ? AutomationTarget.PositionX : AutomationTarget.PositionY;
        var shape = automation.FirstOrDefault(a => a.Target == target);

        return shape is null ? null : Expression(shape, duration, startsAt, Percent);
    }

    /// <summary>
    /// The shape as an expression in <c>t</c>.
    ///
    /// <paramref name="convert"/> maps the stored value into whatever unit the
    /// filter multiplies by - decibels become a linear gain for
    /// <c>volume</c>, percentages become fractions for <c>drawtext</c>. Getting
    /// that wrong is inaudibly small at one end and catastrophic at the other,
    /// so it is a parameter rather than an assumption.
    /// </summary>
    public static string Expression(
        Automation shape,
        double duration,
        double startsAt = 0,
        Func<double, double>? convert = null)
    {
        convert ??= Gain;

        var span = shape.Length > 0 ? shape.Length : Math.Max(0.001, duration - shape.Delay);
        var begins = startsAt + shape.Delay;

        // Progress through the shape, clamped, so the value holds at each end
        // rather than running off.
        var t = $"clip((t-{N(begins)})/{N(span)},0,1)";

        var from = convert(shape.From);
        var to = convert(shape.To);

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

    /// <summary>Percent to a fraction, which is what a placement wants.</summary>
    private static double Percent(double percent) => percent / 100;

    private static string N(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);
}
