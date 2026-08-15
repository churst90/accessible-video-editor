namespace AccessibleVideoEditor.Core.Images;

/// <summary>
/// A small colour copy of the picture, for the questions grey cannot answer.
///
/// A colour cast is invisible to a brightness histogram - a photograph that is
/// too blue and one that is correct have the same shape in grey. So anything
/// about <i>colour</i> works on this: what the cast is, and what would remove
/// it.
/// </summary>
public sealed class ColourRaster(int width, int height, byte[] pixels)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    /// <summary>Three bytes per pixel, red green blue, row major.</summary>
    public byte[] Pixels { get; } = pixels;

    public (byte R, byte G, byte B) At(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return (0, 0, 0);

        var i = (y * Width + x) * 3;

        return (Pixels[i], Pixels[i + 1], Pixels[i + 2]);
    }

    public static ColourRaster Blank(int width, int height, (byte R, byte G, byte B) colour)
    {
        var pixels = new byte[width * height * 3];

        for (var i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = colour.R;
            pixels[i + 1] = colour.G;
            pixels[i + 2] = colour.B;
        }

        return new ColourRaster(width, height, pixels);
    }

    public ColourRaster Fill(int x, int y, int width, int height, (byte R, byte G, byte B) colour)
    {
        for (var row = Math.Max(0, y); row < Math.Min(Height, y + height); row++)
        {
            for (var column = Math.Max(0, x); column < Math.Min(Width, x + width); column++)
            {
                var i = (row * Width + column) * 3;

                Pixels[i] = colour.R;
                Pixels[i + 1] = colour.G;
                Pixels[i + 2] = colour.B;
            }
        }

        return this;
    }

    public (double R, double G, double B) Means()
    {
        if (Pixels.Length == 0) return (0, 0, 0);

        double r = 0, g = 0, b = 0;

        for (var i = 0; i < Pixels.Length; i += 3)
        {
            r += Pixels[i];
            g += Pixels[i + 1];
            b += Pixels[i + 2];
        }

        var count = Pixels.Length / 3.0;

        return (r / count, g / count, b / count);
    }

    /// <summary>One channel's histogram, for finding where that channel starts and stops.</summary>
    public int[] Histogram(int channel)
    {
        var histogram = new int[256];

        for (var i = channel; i < Pixels.Length; i += 3) histogram[Pixels[i]]++;

        return histogram;
    }

    public int Count => Pixels.Length / 3;

    /// <summary>
    /// The average colour of a small patch. Used when a point is named as
    /// neutral: one pixel is noise, a patch is a measurement.
    /// </summary>
    public (double R, double G, double B) PatchAt(int x, int y, int radius = 2)
    {
        double r = 0, g = 0, b = 0;
        var count = 0;

        for (var row = y - radius; row <= y + radius; row++)
        {
            for (var column = x - radius; column <= x + radius; column++)
            {
                if (row < 0 || column < 0 || row >= Height || column >= Width) continue;

                var (pr, pg, pb) = At(column, row);

                r += pr;
                g += pg;
                b += pb;
                count++;
            }
        }

        return count == 0 ? (0, 0, 0) : (r / count, g / count, b / count);
    }
}

/// <summary>
/// Which way the colour is pulling, and by how much.
///
/// Said as a direction rather than as three numbers: "a warm cast" is something
/// you can act on, and "red 138, green 130, blue 121" is arithmetic you would
/// have to do first. The numbers follow for anyone who wants them.
/// </summary>
public readonly record struct ColourCast(double Red, double Green, double Blue)
{
    public static ColourCast Of(ColourRaster raster)
    {
        var (r, g, b) = raster.Means();

        return new ColourCast(r, g, b);
    }

    public double Average => (Red + Green + Blue) / 3;

    /// <summary>How far the furthest channel is from the average, as a percentage.</summary>
    public double Strength
    {
        get
        {
            var average = Average;

            if (average < 1) return 0;

            var spread = Math.Max(Math.Max(Red, Green), Blue) - Math.Min(Math.Min(Red, Green), Blue);

            return spread * 100 / average;
        }
    }

    /// <summary>Below this the difference is noise rather than a cast.</summary>
    public const double Neutral = 4;

    public bool IsNeutral => Strength < Neutral;

    public string Name =>
        IsNeutral ? "neutral"
        : Red > Green && Red > Blue ? Green > Blue ? "warm" : "red"
        : Blue > Red && Blue > Green ? Green > Red ? "cool" : "blue"
        : Green > Red && Green > Blue ? "green"
        : "mixed";

    public string Describe() =>
        IsNeutral
            ? $"the colour is neutral, red {Red:0}, green {Green:0}, blue {Blue:0}"
            : $"a {Name} cast, {Strength:0} percent. Red {Red:0}, green {Green:0}, blue {Blue:0}. "
              + $"Neutralise it with the pointer on something that should be grey";
}
