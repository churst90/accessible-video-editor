using AccessibleVideoEditor.Core.Editing;

namespace AccessibleVideoEditor.Core.Images;

/// <summary>
/// The curve, made of things that can be said.
///
/// A levels curve is a graph you drag, which is the least accessible control in
/// any image editor. But the graph is only a picture of five numbers, and the
/// numbers have names photographers already use: the <b>black point</b>, the
/// <b>white point</b>, and how the <b>shadows</b>, <b>midtones</b> and
/// <b>highlights</b> sit between them. So those are the control, each one
/// nudged and read back, and the histogram underneath is read out by zone -
/// which is what the graph was showing all along.
///
/// The named corrections in <see cref="ColourEdits"/> are the fast path. This
/// is the one for when they are not precise enough.
/// </summary>
public sealed record Levels
{
    /// <summary>Whichever channel a nudge is aimed at.</summary>
    public Levels WithChannel(int channel, ChannelLevels levels) => channel switch
    {
        0 => this with { Red = levels },
        1 => this with { Green = levels },
        _ => this with { Blue = levels },
    };

    public ChannelLevels Channel(int channel) => channel switch
    {
        0 => Red,
        1 => Green,
        _ => Blue,
    };

    /// <summary>Everything at or below this becomes black. 0 leaves it alone.</summary>
    public int BlackPoint { get; init; }

    /// <summary>Everything at or above this becomes white. 255 leaves it alone.</summary>
    public int WhitePoint { get; init; } = 255;

    /// <summary>
    /// The middle of the range, as a percentage above or below where it would
    /// naturally fall. Positive is lighter.
    /// </summary>
    public double Midtones { get; init; }

    public double Shadows { get; init; }

    public double Highlights { get; init; }

    /// <summary>
    /// The same points, per channel, for a cast the temperature control cannot
    /// reach.
    ///
    /// Temperature moves the whole picture along one axis, from orange to blue.
    /// A scan with a yellowed page, a photograph taken under a mixed light, or
    /// a print that has faded unevenly is off in a direction that axis does not
    /// pass through - and no amount of "warmer" will fix it. This will, because
    /// each channel is stretched to its own range.
    /// </summary>
    public ChannelLevels Red { get; init; } = ChannelLevels.None;

    public ChannelLevels Green { get; init; } = ChannelLevels.None;

    public ChannelLevels Blue { get; init; } = ChannelLevels.None;

    public bool HasChannels => Red.IsAnything || Green.IsAnything || Blue.IsAnything;

    public static Levels None => new();

    public bool IsAnything =>
        BlackPoint > 0 || WhitePoint < 255
        || Math.Abs(Midtones) > 0.001 || Math.Abs(Shadows) > 0.001 || Math.Abs(Highlights) > 0.001
        || HasChannels;

    /// <summary>
    /// How much of the range is left after the points are set. Below about 60
    /// percent the picture is being stretched hard enough to band, which is
    /// worth saying before it is seen.
    /// </summary>
    public double RangeUsed => Math.Max(0, WhitePoint - BlackPoint) * 100.0 / 255;

    public string Describe()
    {
        if (!IsAnything) return "levels untouched";

        var parts = new List<string>();

        if (BlackPoint > 0) parts.Add($"black point {BlackPoint}");
        if (WhitePoint < 255) parts.Add($"white point {WhitePoint}");
        if (Math.Abs(Midtones) > 0.001) parts.Add($"midtones {Signed(Midtones)}");
        if (Math.Abs(Shadows) > 0.001) parts.Add($"shadows {Signed(Shadows)}");
        if (Math.Abs(Highlights) > 0.001) parts.Add($"highlights {Signed(Highlights)}");

        if (HasChannels)
        {
            // Named per channel rather than as six numbers: which channel moved
            // and which way is the thing you need, and it is what tells you
            // whether the correction went the right way.
            var channels = new List<string>();

            if (Red.IsAnything) channels.Add($"red {Red.Describe()}");
            if (Green.IsAnything) channels.Add($"green {Green.Describe()}");
            if (Blue.IsAnything) channels.Add($"blue {Blue.Describe()}");

            parts.Add(string.Join(", ", channels));
        }

        var note = RangeUsed < 60 ? $", stretching {RangeUsed:0} percent of the range" : string.Empty;

        return string.Join(", ", parts) + note;
    }

    private static string Signed(double value) =>
        value > 0 ? $"up {value:0}" : $"down {Math.Abs(value):0}";
}

/// <summary>
/// One channel's black and white points.
/// </summary>
public readonly record struct ChannelLevels(int BlackPoint, int WhitePoint)
{
    public static ChannelLevels None => new(0, 255);

    public bool IsAnything => BlackPoint > 0 || WhitePoint < 255;

    public string Describe() =>
        !IsAnything ? "untouched"
        : BlackPoint > 0 && WhitePoint < 255 ? $"{BlackPoint} to {WhitePoint}"
        : BlackPoint > 0 ? $"from {BlackPoint}"
        : $"to {WhitePoint}";
}

/// <summary>
/// The histogram, read out.
///
/// Five numbers rather than two hundred and fifty six: this is the shape of the
/// picture, and it is the thing a curve is drawn on top of. "Nothing in the
/// whites and a third of it in the shadows" is a picture you can act on.
/// </summary>
public readonly record struct ToneZones(
    double Blacks,
    double Shadows,
    double Midtones,
    double Highlights,
    double Whites)
{
    public static ToneZones Of(Raster raster)
    {
        if (raster.Pixels.Length == 0) return default;

        double blacks = 0, shadows = 0, mids = 0, highlights = 0, whites = 0;

        foreach (var pixel in raster.Pixels)
        {
            if (pixel < 26) blacks++;
            else if (pixel < 90) shadows++;
            else if (pixel < 170) mids++;
            else if (pixel < 235) highlights++;
            else whites++;
        }

        var total = (double)raster.Pixels.Length;

        return new ToneZones(
            blacks * 100 / total,
            shadows * 100 / total,
            mids * 100 / total,
            highlights * 100 / total,
            whites * 100 / total);
    }

    public string Describe() =>
        $"blacks {Blacks:0}, shadows {Shadows:0}, midtones {Midtones:0}, "
        + $"highlights {Highlights:0}, whites {Whites:0} percent";

    /// <summary>
    /// Which end the picture is bunched at, said as a sentence rather than as
    /// five numbers - the numbers are there when you want them.
    /// </summary>
    public string Summarise()
    {
        var dark = Blacks + Shadows;
        var light = Highlights + Whites;

        if (Blacks > 15) return "a lot of it is solid black";
        if (Whites > 15) return "a lot of it is solid white";
        if (dark > 65) return "bunched in the shadows";
        if (light > 65) return "bunched in the highlights";
        if (Midtones > 70) return "almost all midtones, so it will look flat";

        return "spread across the range";
    }
}

public static class LevelEdits
{
    /// <summary>
    /// Sets the black and white points from the picture itself.
    ///
    /// This is the one command that makes levels worth having without a graph:
    /// it finds where the picture actually starts and stops - ignoring the
    /// half percent at each end that is noise - and pulls those to black and
    /// white. It says the numbers it chose, so the automatic answer can be
    /// adjusted rather than merely accepted.
    /// </summary>
    public static EditResult Auto(ImageDocument document, Raster raster)
    {
        if (raster.Pixels.Length == 0) return EditResult.NoChange("nothing to measure");

        var histogram = new int[256];
        foreach (var pixel in raster.Pixels) histogram[pixel]++;

        var total = raster.Pixels.Length;

        var black = Percentile(histogram, total, 0.005);
        var white = Percentile(histogram, total, 0.995);

        if (white - black < 16)
        {
            return EditResult.NoChange("this is almost all one tone; levels cannot help it");
        }

        var before = document.Levels;

        document.Levels = before with
        {
            BlackPoint = Math.Clamp(black, 0, 240),
            WhitePoint = Math.Clamp(white, 15, 255),
        };

        if (document.Levels == before) return EditResult.NoChange("the levels are already set there");

        var gained = 255.0 / Math.Max(1, white - black);

        return EditResult.Ok(
            $"{document.Levels.Describe()}, opening it up by {gained:0.0} times",
            gained > 3
                ? ["that is a big stretch; it may show banding"]
                : []);
    }

    /// <summary>The nudges. Each says where it landed, in the units the control uses.</summary>
    public static readonly string[] Presets =
    [
        "auto levels",
        "raise the black point", "lower the black point",
        "lower the white point", "raise the white point",
        "midtones up", "midtones down",
        "shadows up", "shadows down",
        "highlights up", "highlights down",
        "auto colour levels",
        "reset levels",
    ];

    /// <summary>Nudges aimed at one channel, for anything the automatic answers miss.</summary>
    public static readonly string[] ChannelPresets =
    [
        "less red", "more red",
        "less green", "more green",
        "less blue", "more blue",
    ];

    public static EditResult Channel(ImageDocument document, string preset)
    {
        var channel = preset.Contains("red") ? 0 : preset.Contains("green") ? 1 : 2;
        var less = preset.StartsWith("less", StringComparison.Ordinal);

        var before = document.Levels;
        var current = before.Channel(channel);

        // Less of a channel is that channel's white point coming down, which is
        // the same operation the automatic balance performs.
        var updated = less
            ? current with { WhitePoint = Math.Clamp(current.WhitePoint - 8, 32, 255) }
            : current with { WhitePoint = Math.Clamp(current.WhitePoint + 8, 32, 255) };

        document.Levels = before.WithChannel(channel, updated);

        if (document.Levels == before) return EditResult.NoChange($"{preset} is as far as it goes");

        var name = channel == 0 ? "red" : channel == 1 ? "green" : "blue";

        return EditResult.Ok($"{name} {updated.Describe()}");
    }

    public static EditResult Apply(ImageDocument document, string preset)
    {
        var before = document.Levels;

        document.Levels = preset switch
        {
            "raise the black point" => before with { BlackPoint = Math.Clamp(before.BlackPoint + 8, 0, before.WhitePoint - 16) },
            "lower the black point" => before with { BlackPoint = Math.Clamp(before.BlackPoint - 8, 0, 240) },
            "lower the white point" => before with { WhitePoint = Math.Clamp(before.WhitePoint - 8, before.BlackPoint + 16, 255) },
            "raise the white point" => before with { WhitePoint = Math.Clamp(before.WhitePoint + 8, 15, 255) },

            "midtones up" => before with { Midtones = Math.Clamp(before.Midtones + 8, -60, 60) },
            "midtones down" => before with { Midtones = Math.Clamp(before.Midtones - 8, -60, 60) },

            "shadows up" => before with { Shadows = Math.Clamp(before.Shadows + 8, -50, 50) },
            "shadows down" => before with { Shadows = Math.Clamp(before.Shadows - 8, -50, 50) },

            "highlights up" => before with { Highlights = Math.Clamp(before.Highlights + 8, -50, 50) },
            "highlights down" => before with { Highlights = Math.Clamp(before.Highlights - 8, -50, 50) },

            "reset levels" => Levels.None,

            _ => before,
        };

        if (document.Levels == before)
        {
            return EditResult.NoChange(
                Presets.Contains(preset)
                    ? $"{preset} is as far as it goes"
                    : $"there is no level called {preset}");
        }

        return EditResult.Ok(document.Levels.Describe());
    }

    /// <summary>
    /// Stretches each channel to its own range, which is what removes a cast.
    ///
    /// The reasoning is the grey-world assumption: over a whole photograph the
    /// colours average out to something neutral, so a channel that starts
    /// higher or stops lower than the others is the cast rather than the
    /// subject. It is wrong for a picture that really is mostly one colour - a
    /// forest, a sunset - which is why it says what it did and can be undone.
    /// </summary>
    public static EditResult AutoColour(ImageDocument document, ColourRaster raster)
    {
        if (raster.Count == 0) return EditResult.NoChange("nothing to measure");

        var cast = ColourCast.Of(raster);
        var before = document.Levels;
        var levels = before;

        for (var channel = 0; channel < 3; channel++)
        {
            var histogram = raster.Histogram(channel);

            var black = Percentile(histogram, raster.Count, 0.005);
            var white = Percentile(histogram, raster.Count, 0.995);

            if (white - black < 16) continue;

            levels = levels.WithChannel(channel, new ChannelLevels(
                Math.Clamp(black, 0, 240),
                Math.Clamp(white, 15, 255)));
        }

        if (levels == before) return EditResult.NoChange("the channels are already where they should be");

        document.Levels = levels;

        return EditResult.Ok(
            $"{(cast.IsNeutral ? "colour levels set" : $"{cast.Name} cast removed")}. {levels.Describe()}",
            cast.IsNeutral
                ? ["it was already close to neutral, so this changed little"]
                : []);
    }

    /// <summary>
    /// White balance from a point you say is neutral.
    ///
    /// This is the eyedropper, done without pointing: sweep to something that
    /// ought to be grey or white - a wall, a shirt, the paper a photograph is
    /// printed on - and the correction that makes it neutral is worked out from
    /// there. It is the most reliable white balance there is, because it uses a
    /// fact about the scene rather than an assumption about the average.
    /// </summary>
    public static EditResult NeutraliseAt(
        ImageDocument document,
        ColourRaster raster,
        double x,
        double y)
    {
        var px = (int)Math.Round(Math.Clamp(x, 0, 1) * (raster.Width - 1));
        var py = (int)Math.Round(Math.Clamp(y, 0, 1) * (raster.Height - 1));

        var (r, g, b) = raster.PatchAt(px, py);

        var brightest = Math.Max(Math.Max(r, g), b);

        if (brightest < 24)
        {
            return EditResult.NoChange("that spot is too dark to balance from; find something lighter");
        }

        if (brightest > 250 && Math.Min(Math.Min(r, g), b) > 250)
        {
            return EditResult.NoChange("that spot is blown out; nothing can be read from it");
        }

        var cast = new ColourCast(r, g, b);
        var before = document.Levels;

        // Each channel's white point is pulled down to where that channel
        // actually is at this spot, which lifts it to match the brightest one -
        // so the patch comes out neutral and everything else follows it.
        document.Levels = before
            .WithChannel(0, new ChannelLevels(before.Red.BlackPoint, WhitePointFor(r, brightest)))
            .WithChannel(1, new ChannelLevels(before.Green.BlackPoint, WhitePointFor(g, brightest)))
            .WithChannel(2, new ChannelLevels(before.Blue.BlackPoint, WhitePointFor(b, brightest)));

        if (document.Levels == before) return EditResult.NoChange("that spot is already neutral");

        return EditResult.Ok(
            $"balanced on that spot, which was {(cast.IsNeutral ? "close to neutral" : $"{cast.Strength:0} percent {cast.Name}")}. "
            + document.Levels.Describe());
    }

    private static int WhitePointFor(double channel, double brightest)
    {
        if (channel >= brightest - 0.5) return 255;

        // The channel is short of the brightest one, so its white point comes
        // down by the same proportion and the two end up level.
        var scaled = 255 * channel / brightest;

        return (int)Math.Clamp(Math.Round(scaled), 15, 255);
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
