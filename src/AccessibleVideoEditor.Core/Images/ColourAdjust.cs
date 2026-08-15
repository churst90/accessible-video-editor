using AccessibleVideoEditor.Core.Editing;

namespace AccessibleVideoEditor.Core.Images;

/// <summary>
/// Colour correction, by description.
///
/// Every grading tool is a curve or a wheel, and both are pointing at a
/// picture. The controls underneath them are not - they are numbers with names
/// photographers already use: <b>stops</b> of exposure, <b>kelvin</b> of white
/// balance, percentages of contrast and saturation. So those are the controls,
/// and each change says the new value in the unit that means something.
///
/// Nothing here is a preview you have to look at. The application measures the
/// picture, says what is wrong with it, and suggests the correction - the same
/// pattern the video's quality analysis already uses.
/// </summary>
public sealed record ColourAdjust
{
    /// <summary>Stops. Plus one doubles the light, as on any camera.</summary>
    public double Exposure { get; init; }

    /// <summary>Percent above or below normal.</summary>
    public double Contrast { get; init; }

    public double Saturation { get; init; }

    /// <summary>
    /// White balance in kelvin. Lower is warmer, which is backwards from what
    /// it sounds like and is the convention everywhere, so it is always spoken
    /// as "warmer" or "cooler" as well as as a number.
    /// </summary>
    public double TemperatureK { get; init; } = 6500;

    /// <summary>Green to magenta, percent. The other half of white balance.</summary>
    public double Tint { get; init; }

    /// <summary>Lifts the shadows without touching the highlights.</summary>
    public double Shadows { get; init; }

    public double Highlights { get; init; }

    public bool Monochrome { get; init; }

    public static ColourAdjust None => new();

    public bool IsAnything =>
        Math.Abs(Exposure) > 0.001 || Math.Abs(Contrast) > 0.001 || Math.Abs(Saturation) > 0.001
        || Math.Abs(TemperatureK - 6500) > 1 || Math.Abs(Tint) > 0.001
        || Math.Abs(Shadows) > 0.001 || Math.Abs(Highlights) > 0.001 || Monochrome;

    /// <summary>
    /// What has been done, in the units a photographer would say. Reads as a
    /// list of decisions rather than as a set of slider positions.
    /// </summary>
    public string Describe()
    {
        if (!IsAnything) return "no colour changes";

        var parts = new List<string>();

        if (Math.Abs(Exposure) > 0.001) parts.Add($"exposure {Stops(Exposure)}");
        if (Math.Abs(Contrast) > 0.001) parts.Add($"contrast {Signed(Contrast)} percent");
        if (Math.Abs(Saturation) > 0.001) parts.Add($"saturation {Signed(Saturation)} percent");

        if (Math.Abs(TemperatureK - 6500) > 1)
        {
            parts.Add($"{(TemperatureK < 6500 ? "warmer" : "cooler")}, {TemperatureK:0} kelvin");
        }

        if (Math.Abs(Tint) > 0.001)
        {
            parts.Add($"{(Tint > 0 ? "magenta" : "green")} {Math.Abs(Tint):0} percent");
        }

        if (Math.Abs(Shadows) > 0.001) parts.Add($"shadows {Signed(Shadows)} percent");
        if (Math.Abs(Highlights) > 0.001) parts.Add($"highlights {Signed(Highlights)} percent");
        if (Monochrome) parts.Add("black and white");

        return string.Join(", ", parts);
    }

    private static string Stops(double value)
    {
        var text = Math.Abs(value) switch
        {
            < 0.4 => "a third of a stop",
            < 0.6 => "half a stop",
            < 0.8 => "two thirds of a stop",
            < 1.2 => "one stop",
            _ => $"{Math.Abs(value):0.#} stops",
        };

        return $"{(value > 0 ? "up" : "down")} {text}";
    }

    private static string Signed(double value) => value > 0 ? $"plus {value:0}" : $"minus {Math.Abs(value):0}";
}

/// <summary>
/// The adjustments as commands, each announcing where it landed.
/// </summary>
public static class ColourEdits
{
    /// <summary>
    /// Named corrections rather than numbers. These are the sentences people
    /// actually say about a photograph, and each one is a nudge rather than a
    /// jump so it can be applied twice when once was not enough.
    /// </summary>
    public static readonly string[] Presets =
    [
        "brighter", "darker",
        "warmer", "cooler",
        "punchier", "flatter",
        "richer", "muted",
        "lift the shadows", "recover the highlights",
        "black and white",
        "reset",
    ];

    public static EditResult Apply(ImageDocument document, string preset)
    {
        var before = document.Colour;

        document.Colour = preset switch
        {
            "brighter" => before with { Exposure = Clamp(before.Exposure + 0.33, -3, 3) },
            "darker" => before with { Exposure = Clamp(before.Exposure - 0.33, -3, 3) },

            // Kelvin runs backwards: a lower number is a warmer picture.
            "warmer" => before with { TemperatureK = Clamp(before.TemperatureK - 400, 2000, 12000) },
            "cooler" => before with { TemperatureK = Clamp(before.TemperatureK + 400, 2000, 12000) },

            "punchier" => before with { Contrast = Clamp(before.Contrast + 10, -80, 100) },
            "flatter" => before with { Contrast = Clamp(before.Contrast - 10, -80, 100) },

            "richer" => before with { Saturation = Clamp(before.Saturation + 12, -100, 100) },
            "muted" => before with { Saturation = Clamp(before.Saturation - 12, -100, 100) },

            "lift the shadows" => before with { Shadows = Clamp(before.Shadows + 10, -50, 60) },
            "recover the highlights" => before with { Highlights = Clamp(before.Highlights - 10, -60, 50) },

            "black and white" => before with { Monochrome = !before.Monochrome },

            "reset" => ColourAdjust.None,

            _ => before,
        };

        if (document.Colour == before)
        {
            return EditResult.NoChange(
                Presets.Contains(preset) ? $"{preset} is as far as it goes" : $"there is no correction called {preset}");
        }

        return EditResult.Ok(document.Colour.Describe());
    }

    private static double Clamp(double value, double low, double high) => Math.Clamp(value, low, high);

    /// <summary>
    /// What is wrong with the picture, and what would fix it.
    ///
    /// This is the half of colour correction that normally happens by looking:
    /// the brightness and the spread are measured, and the correction is
    /// suggested in the same words the commands use, so the advice can be acted
    /// on by pressing the thing it just named.
    /// </summary>
    public static string Advise(Raster raster)
    {
        if (raster.Pixels.Length == 0) return "nothing to measure";

        var mean = raster.Mean();

        var histogram = new int[256];
        foreach (var pixel in raster.Pixels) histogram[pixel]++;

        var total = raster.Pixels.Length;

        var shadowClipped = histogram[..4].Sum() * 100.0 / total;
        var highlightClipped = histogram[252..].Sum() * 100.0 / total;

        // The spread between the fifth and ninety-fifth percentile: a flat
        // picture uses a narrow band of the range and looks washed out.
        var low = Percentile(histogram, total, 0.05);
        var high = Percentile(histogram, total, 0.95);
        var spread = high - low;

        var notes = new List<string>
        {
            $"average brightness {mean * 100 / 255:0} percent",
            $"using {spread * 100 / 255:0} percent of the range",
        };

        var advice = new List<string>();

        if (mean < 70) advice.Add("brighter");
        else if (mean > 190) advice.Add("darker");

        if (spread < 110) advice.Add("punchier");
        else if (spread > 240 && (shadowClipped > 2 || highlightClipped > 2)) advice.Add("flatter");

        if (shadowClipped > 3) notes.Add($"{shadowClipped:0} percent is crushed to black");
        if (highlightClipped > 3) notes.Add($"{highlightClipped:0} percent is blown out");

        return advice.Count == 0
            ? $"{string.Join(", ", notes)}. Nothing obvious to correct"
            : $"{string.Join(", ", notes)}. Try {string.Join(", then ", advice)}";
    }

    private static int Percentile(int[] histogram, int total, double fraction)
    {
        var target = total * fraction;
        var running = 0;

        for (var i = 0; i < histogram.Length; i++)
        {
            running += histogram[i];

            if (running >= target) return i;
        }

        return 255;
    }
}
