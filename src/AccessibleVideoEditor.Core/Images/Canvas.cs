using System.IO.Compression;

namespace AccessibleVideoEditor.Core.Images;

/// <summary>
/// A picture being drawn on. Written from scratch so every operation can report
/// what it did - a flood fill that says how much it covered is one you can use
/// without seeing it, and no drawing library offers that.
/// </summary>
public sealed class Canvas
{
    public int Width { get; }

    public int Height { get; }
    public byte[] Pixels { get; }

    public Canvas(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Pixels = new byte[Width * Height * 4];
    }

    public (byte R, byte G, byte B, byte A) At(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return (0, 0, 0, 0);

        var i = (y * Width + x) * 4;

        return (Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
    }

    public void Set(int x, int y, (byte R, byte G, byte B) colour, byte alpha = 255)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;

        var i = (y * Width + x) * 4;

        // Painted over what is already there, so overlapping shapes behave the
        // way anyone would expect rather than replacing each other.
        if (alpha == 255)
        {
            Pixels[i] = colour.R;
            Pixels[i + 1] = colour.G;
            Pixels[i + 2] = colour.B;
            Pixels[i + 3] = 255;

            return;
        }

        var a = alpha / 255.0;

        Pixels[i] = (byte)Math.Round(colour.R * a + Pixels[i] * (1 - a));
        Pixels[i + 1] = (byte)Math.Round(colour.G * a + Pixels[i + 1] * (1 - a));
        Pixels[i + 2] = (byte)Math.Round(colour.B * a + Pixels[i + 2] * (1 - a));
        Pixels[i + 3] = (byte)Math.Max(Pixels[i + 3], alpha);
    }

    /// <summary>Returns how many pixels it covered, which is what gets spoken.</summary>
    public int FillRect(int x, int y, int width, int height, (byte R, byte G, byte B) colour, byte alpha = 255)
    {
        var painted = 0;

        for (var row = Math.Max(0, y); row < Math.Min(Height, y + height); row++)
        {
            for (var column = Math.Max(0, x); column < Math.Min(Width, x + width); column++)
            {
                Set(column, row, colour, alpha);
                painted++;
            }
        }

        return painted;
    }

    public int FillEllipse(int centreX, int centreY, int radiusX, int radiusY, (byte R, byte G, byte B) colour, byte alpha = 255)
    {
        if (radiusX <= 0 || radiusY <= 0) return 0;

        var painted = 0;

        for (var y = centreY - radiusY; y <= centreY + radiusY; y++)
        {
            for (var x = centreX - radiusX; x <= centreX + radiusX; x++)
            {
                var dx = (x - centreX) / (double)radiusX;
                var dy = (y - centreY) / (double)radiusY;

                if (dx * dx + dy * dy > 1) continue;
                if (x < 0 || y < 0 || x >= Width || y >= Height) continue;

                Set(x, y, colour, alpha);
                painted++;
            }
        }

        return painted;
    }

    /// <summary>Bresenham, thickened by drawing a square at each step.</summary>
    public int DrawLine(int x0, int y0, int x1, int y1, (byte R, byte G, byte B) colour, int thickness = 1)
    {
        var painted = 0;

        var dx = Math.Abs(x1 - x0);
        var dy = -Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;

        var half = Math.Max(0, thickness / 2);

        while (true)
        {
            painted += FillRect(x0 - half, y0 - half, Math.Max(1, thickness), Math.Max(1, thickness), colour);

            if (x0 == x1 && y0 == y1) break;

            var doubled = error * 2;

            if (doubled >= dy)
            {
                error += dy;
                x0 += sx;
            }

            if (doubled <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }

        return painted;
    }

    /// <summary>
    /// Flood fill, and the report that makes it usable.
    ///
    /// The surprise in a fill is always <b>how far it went</b> - through a gap
    /// you did not know was there, or stopped by an edge you did not know about.
    /// So it comes back with the area it covered and the box it stayed inside,
    /// and the caller says both.
    /// </summary>
    public FloodResult FloodFill(int x, int y, (byte R, byte G, byte B) colour, int tolerance = 16)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return new FloodResult(0, new PixelRect(0, 0, 0, 0), Width * Height);
        }

        var target = At(x, y);
        var visited = new bool[Width * Height];
        var queue = new Queue<(int X, int Y)>();

        queue.Enqueue((x, y));
        visited[y * Width + x] = true;

        var painted = 0;
        int minX = x, minY = y, maxX = x, maxY = y;

        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();

            Set(cx, cy, colour);
            painted++;

            if (cx < minX) minX = cx;
            if (cy < minY) minY = cy;
            if (cx > maxX) maxX = cx;
            if (cy > maxY) maxY = cy;

            foreach (var (dx, dy) in Steps)
            {
                var nx = cx + dx;
                var ny = cy + dy;

                if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) continue;

                var index = ny * Width + nx;
                if (visited[index]) continue;

                var here = At(nx, ny);

                if (Math.Abs(here.R - target.R) > tolerance
                    || Math.Abs(here.G - target.G) > tolerance
                    || Math.Abs(here.B - target.B) > tolerance)
                {
                    continue;
                }

                visited[index] = true;
                queue.Enqueue((nx, ny));
            }
        }

        return new FloodResult(
            painted,
            new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1),
            Width * Height);
    }

    private static readonly (int X, int Y)[] Steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>
    /// A vertical or horizontal gradient. The one drawing operation that is
    /// genuinely easier to describe than to make by hand.
    /// </summary>
    public void Gradient((byte R, byte G, byte B) from, (byte R, byte G, byte B) to, bool vertical = true)
    {
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var t = vertical
                    ? (Height <= 1 ? 0 : (double)y / (Height - 1))
                    : (Width <= 1 ? 0 : (double)x / (Width - 1));

                Set(x, y, (
                    (byte)Math.Round(from.R + (to.R - from.R) * t),
                    (byte)Math.Round(from.G + (to.G - from.G) * t),
                    (byte)Math.Round(from.B + (to.B - from.B) * t)));
            }
        }
    }

    /// <summary>
    /// The colours actually present, largest share first. This is how a canvas
    /// gets described without looking at it - "mostly navy, a fifth of it
    /// white" is a picture you can hold in your head.
    /// </summary>
    public IReadOnlyList<(string Name, double Share)> DominantColours(int most = 4)
    {
        var counts = new Dictionary<string, int>();
        var total = 0;

        for (var i = 0; i < Pixels.Length; i += 4)
        {
            if (Pixels[i + 3] < 8) continue;

            var name = Colours.NameOf(Pixels[i], Pixels[i + 1], Pixels[i + 2]);

            counts[name] = counts.GetValueOrDefault(name) + 1;
            total++;
        }

        if (total == 0) return [];

        return counts
            .OrderByDescending(pair => pair.Value)
            .Take(most)
            .Select(pair => (pair.Key, pair.Value * 100.0 / total))
            .ToList();
    }

    public string Describe()
    {
        var colours = DominantColours();

        if (colours.Count == 0) return "empty";

        return string.Join(", ", colours.Select(c => $"{c.Share:0} percent {c.Name}"));
    }

    // ---- writing it out ----------------------------------------------------

    /// <summary>
    /// A PNG, written by hand.
    ///
    /// It is about forty lines and removes an image library from the
    /// dependencies of a video editor. PNG's own compression is deflate, which
    /// the framework already has.
    /// </summary>
    public void WritePng(string path)
    {
        using var file = File.Create(path);

        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        WriteBigEndian(header, 0, Width);
        WriteBigEndian(header, 4, Height);
        header[8] = 8;  // bits per channel
        header[9] = 6;  // colour type: truecolour with alpha
        Chunk(file, "IHDR", header);

        // Each row is prefixed with a filter byte; zero means "no filter",
        // which costs some size and keeps this simple enough to be obviously
        // correct.
        var raw = new byte[(Width * 4 + 1) * Height];

        for (var y = 0; y < Height; y++)
        {
            var source = y * Width * 4;
            var target = y * (Width * 4 + 1);

            raw[target] = 0;
            Array.Copy(Pixels, source, raw, target + 1, Width * 4);
        }

        using var compressed = new MemoryStream();

        using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw);
        }

        Chunk(file, "IDAT", compressed.ToArray());
        Chunk(file, "IEND", []);
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        stream.Write(length);

        var name = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(name);
        stream.Write(data);

        var crc = Crc32(name, data);
        var crcBytes = new byte[4];
        WriteBigEndian(crcBytes, 0, (int)crc);
        stream.Write(crcBytes);
    }

    private static void WriteBigEndian(byte[] target, int at, int value)
    {
        target[at] = (byte)(value >> 24);
        target[at + 1] = (byte)(value >> 16);
        target[at + 2] = (byte)(value >> 8);
        target[at + 3] = (byte)value;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];

        for (var n = 0; n < 256; n++)
        {
            var c = (uint)n;

            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static uint Crc32(byte[] first, byte[] second)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var b in first) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in second) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);

        return crc ^ 0xFFFFFFFF;
    }
}

/// <summary>What a flood fill did, which is the only way to know where it went.</summary>
public readonly record struct FloodResult(int Painted, PixelRect Bounds, int Total)
{
    public double Share => Total == 0 ? 0 : Painted * 100.0 / Total;

    public string Describe(int canvasWidth, int canvasHeight) =>
        Painted == 0
            ? "nothing was filled"
            : $"filled {Share:0} percent, {Bounds.Width} by {Bounds.Height}, "
              + Bounds.DescribePosition(canvasWidth, canvasHeight);
}
