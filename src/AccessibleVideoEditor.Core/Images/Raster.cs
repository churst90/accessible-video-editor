namespace AccessibleVideoEditor.Core.Images;

/// <summary>
/// A small grey picture, in memory, as plain numbers.
///
/// Everything that has to <i>look</i> at an image goes through this. It lives
/// in Core rather than in the engine for one reason: a scanner bed with a photo
/// dropped on it sideways can then be built by a test in four lines, and the
/// detection can be asserted on rather than eyeballed. Analysis that can only
/// be checked by looking at it is analysis a blind person cannot trust.
///
/// Deliberately tiny - a few hundred pixels across. Every question asked here
/// is about <b>where things are</b>, and none of them need the full resolution.
/// </summary>
public sealed class Raster(int width, int height, byte[] pixels)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    /// <summary>One byte per pixel, row major. 0 is black, 255 is white.</summary>
    public byte[] Pixels { get; } = pixels;

    public byte At(int x, int y) =>
        x < 0 || y < 0 || x >= Width || y >= Height ? (byte)0 : Pixels[y * Width + x];

    public static Raster Blank(int width, int height, byte value = 0)
    {
        var pixels = new byte[width * height];

        if (value != 0) Array.Fill(pixels, value);

        return new Raster(width, height, pixels);
    }

    /// <summary>Paints a rectangle. Used by tests to build a bed with a photo on it.</summary>
    public Raster Fill(int x, int y, int width, int height, byte value)
    {
        for (var row = Math.Max(0, y); row < Math.Min(Height, y + height); row++)
        {
            for (var column = Math.Max(0, x); column < Math.Min(Width, x + width); column++)
            {
                Pixels[row * Width + column] = value;
            }
        }

        return this;
    }

    public double Mean()
    {
        if (Pixels.Length == 0) return 0;

        long total = 0;
        foreach (var pixel in Pixels) total += pixel;

        return (double)total / Pixels.Length;
    }

    /// <summary>
    /// The average brightness of the outermost ring. This is what "the paper
    /// the photograph is lying on" looks like numerically, and it is far more
    /// reliable than assuming a scanner bed is white - lids differ, and some
    /// are black on purpose.
    /// </summary>
    public double BorderMean(int thickness = 2)
    {
        long total = 0;
        var count = 0;

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (x >= thickness && x < Width - thickness && y >= thickness && y < Height - thickness)
                {
                    continue;
                }

                total += At(x, y);
                count++;
            }
        }

        return count == 0 ? 0 : (double)total / count;
    }
}

/// <summary>A rectangle in pixels, in the coordinate space of whatever produced it.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public int Area => Width * Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public double AspectRatio => Height == 0 ? 0 : (double)Width / Height;

    public bool IsLandscape => Width > Height;

    /// <summary>Scales a rectangle found on a small raster back to the real image.</summary>
    public PixelRect ScaleTo(int fromWidth, int fromHeight, int toWidth, int toHeight)
    {
        if (fromWidth <= 0 || fromHeight <= 0) return this;

        var x = (double)toWidth / fromWidth;
        var y = (double)toHeight / fromHeight;

        return new PixelRect(
            (int)Math.Round(X * x),
            (int)Math.Round(Y * y),
            (int)Math.Round(Width * x),
            (int)Math.Round(Height * y));
    }

    /// <summary>
    /// Where this sits, in the 3 by 3 language the rest of the application
    /// already uses, so "top left" means the same thing here as on a card.
    /// </summary>
    public string DescribePosition(int inWidth, int inHeight)
    {
        if (inWidth <= 0 || inHeight <= 0) return "somewhere";

        var centreX = X + Width / 2.0;
        var centreY = Y + Height / 2.0;

        var column = centreX < inWidth / 3.0 ? "left" : centreX > inWidth * 2 / 3.0 ? "right" : "centre";
        var row = centreY < inHeight / 3.0 ? "top" : centreY > inHeight * 2 / 3.0 ? "bottom" : "middle";

        return row == "middle" && column == "centre" ? "in the middle" : $"{row} {column}";
    }
}
