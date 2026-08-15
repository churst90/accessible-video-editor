namespace AccessibleVideoEditor.Core.Model;

/// <summary>
/// A value that changes over the length of a segment - what every other editor
/// draws as a curve with draggable points.
///
/// A curve is a picture of a decision, and the decision has a name: fade in,
/// duck under the voice, ease into it. So this stores <b>the shape</b> rather
/// than a list of coordinates, which makes it describable ("ducks to -18
/// decibels while the narration runs"), adjustable by keystroke, and impossible
/// to end up with a stray point in that you cannot find.
///
/// Arbitrary point editing is deliberately not offered. It is the least
/// accessible control in any editor, and every use of it that matters for
/// talking-head video is one of these shapes.
/// </summary>
public sealed class Automation
{
    public required AutomationTarget Target { get; init; }

    public AutomationShape Shape { get; set; } = AutomationShape.Steady;

    /// <summary>Where the value starts, in the target's own unit.</summary>
    public double From { get; set; }

    /// <summary>Where it ends up.</summary>
    public double To { get; set; }

    /// <summary>
    /// How long the move takes, in seconds. For a dip, how long it spends at
    /// the far end. Zero means "the whole segment", which is what a ramp usually
    /// wants and a fade never does.
    /// </summary>
    public double Length { get; set; }

    /// <summary>Seconds from the start of the segment before anything happens.</summary>
    public double Delay { get; set; }

    public string Unit => Target switch
    {
        AutomationTarget.Volume => "decibels",
        AutomationTarget.Opacity => "percent",
        _ => "percent",
    };

    /// <summary>
    /// Reads back as the sentence that would create it. The shape is named
    /// first, because that is the decision; the numbers follow, because they are
    /// the adjustment.
    /// </summary>
    public string Describe()
    {
        var what = Target switch
        {
            AutomationTarget.Volume => "volume",
            AutomationTarget.Opacity => "opacity",
            AutomationTarget.PositionX => "horizontal position",
            _ => "vertical position",
        };

        var span = Length > 0 ? $" over {Length:0.##} seconds" : " over the whole segment";
        var after = Delay > 0 ? $", starting {Delay:0.##} seconds in" : string.Empty;

        return Shape switch
        {
            AutomationShape.Ramp =>
                $"{what} moves from {From:0.#} to {To:0.#} {Unit}{span}{after}",

            AutomationShape.Dip =>
                $"{what} dips to {To:0.#} {Unit}{span}, then comes back{after}",

            AutomationShape.Rise =>
                $"{what} rises to {To:0.#} {Unit}{span}, and stays{after}",

            AutomationShape.EaseIn =>
                $"{what} eases in to {To:0.#} {Unit}{span}{after}",

            AutomationShape.EaseOut =>
                $"{what} eases out to {To:0.#} {Unit}{span}{after}",

            _ => $"{what} holds at {To:0.#} {Unit}",
        };
    }

    /// <summary>
    /// The value at a point, for auditioning and for the meter. Time is seconds
    /// from the start of the segment.
    /// </summary>
    public double At(double time, double segmentDuration)
    {
        var span = Length > 0 ? Length : Math.Max(0.001, segmentDuration - Delay);
        var t = Math.Clamp((time - Delay) / span, 0, 1);

        return Shape switch
        {
            AutomationShape.Steady => To,
            AutomationShape.Ramp => From + (To - From) * t,
            AutomationShape.Rise => From + (To - From) * Smooth(t),
            AutomationShape.EaseIn => From + (To - From) * (t * t),
            AutomationShape.EaseOut => From + (To - From) * (1 - (1 - t) * (1 - t)),

            // A dip goes down and comes back inside its own length, so the value
            // at the very start and the very end is the one you began with.
            AutomationShape.Dip => From + (To - From) * Math.Sin(t * Math.PI),

            _ => To,
        };
    }

    private static double Smooth(double t) => t * t * (3 - 2 * t);

    /// <summary>Volume ducking under narration - the common case, ready made.</summary>
    public static Automation Duck(double restingDb, double duckedDb, double length) => new()
    {
        Target = AutomationTarget.Volume,
        Shape = AutomationShape.Dip,
        From = restingDb,
        To = duckedDb,
        Length = length,
    };
}

public enum AutomationTarget
{
    Volume,
    Opacity,
    PositionX,
    PositionY,
}

/// <summary>
/// Named shapes, not keyframes. Each is a decision somebody actually makes; a
/// list of arbitrary points is not.
/// </summary>
public enum AutomationShape
{
    /// <summary>One value throughout. The way to say "quieter, all of it".</summary>
    Steady,

    /// <summary>Straight from one value to another.</summary>
    Ramp,

    /// <summary>Down and back. Ducking music under a voice.</summary>
    Dip,

    /// <summary>Up and stay. Music swelling at the end.</summary>
    Rise,

    /// <summary>Slow to start, then quick.</summary>
    EaseIn,

    /// <summary>Quick to start, then slow.</summary>
    EaseOut,
}
