namespace AccessibleVideoEditor.Vision;

/// <summary>
/// Finds a face well enough to say where it is. Detection, not recognition: it
/// never says who. The test is on chrominance rather than red-green-blue,
/// because the usual RGB rules are tuned on pale skin and fail on everyone else.
/// </summary>
public static class FaceFinder
{
    public const int CbLow = 77;
    public const int CbHigh = 127;
    public const int CrLow = 133;
    public const int CrHigh = 173;

    /// <summary>Below this share of the frame it is a hand, a lamp, or noise.</summary>
    public const double MinimumArea = 0.004;

    public static bool IsSkin(byte r, byte g, byte b)
    {
        var cb = 128 + (-0.169 * r - 0.331 * g + 0.5 * b);
        var cr = 128 + (0.5 * r - 0.419 * g - 0.081 * b);

        // Very dark pixels carry no reliable colour at all, so they are not
        // called skin however their chrominance lands.
        var luma = 0.299 * r + 0.587 * g + 0.114 * b;

        return luma > 30 && cb >= CbLow && cb <= CbHigh && cr >= CrLow && cr <= CrHigh;
    }

    /// <summary>
    /// The largest plausible skin region in an RGB frame, as a fraction of the
    /// frame. Null when there is nothing face-shaped in it - which the
    /// viewfinder says out loud rather than guessing.
    /// </summary>
    public static FaceObservation? Find(ReadOnlySpan<byte> rgb, int width, int height)
    {
        if (width <= 0 || height <= 0 || rgb.Length < width * height * 3) return null;

        var mask = new bool[width * height];
        var skin = 0;

        for (var i = 0; i < width * height; i++)
        {
            var at = i * 3;

            if (!IsSkin(rgb[at], rgb[at + 1], rgb[at + 2])) continue;

            mask[i] = true;
            skin++;
        }

        if (skin < width * height * MinimumArea) return null;

        var best = LargestRegion(mask, width, height);

        if (best is not { } region) return null;

        var area = region.Width * region.Height;

        if (area < width * height * MinimumArea) return null;

        // A face is roughly as tall as it is wide, or taller. A long horizontal
        // smear is a wall, a desk, or a wooden door - all of which sit inside
        // the skin band.
        var aspect = (double)region.Height / Math.Max(1, region.Width);

        if (aspect is < 0.55 or > 2.6) return null;

        return new FaceObservation(
            (double)region.X / width,
            (double)region.Y / height,
            (double)region.Width / width,
            (double)region.Height / height,
            null,
            null,
            Math.Clamp((double)region.Width * region.Height / Math.Max(1, skin), 0, 1));
    }

    private static (int X, int Y, int Width, int Height)? LargestRegion(bool[] mask, int width, int height)
    {
        var visited = new bool[mask.Length];
        var queue = new Queue<int>();

        (int X, int Y, int Width, int Height)? best = null;
        var bestArea = 0;

        for (var start = 0; start < mask.Length; start++)
        {
            if (!mask[start] || visited[start]) continue;

            visited[start] = true;
            queue.Clear();
            queue.Enqueue(start);

            int minX = start % width, maxX = minX, minY = start / width, maxY = minY, count = 0;

            while (queue.Count > 0)
            {
                var index = queue.Dequeue();
                var x = index % width;
                var y = index / width;

                count++;

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;

                if (x > 0) Consider(index - 1);
                if (x < width - 1) Consider(index + 1);
                if (y > 0) Consider(index - width);
                if (y < height - 1) Consider(index + width);
            }

            if (count <= bestArea) continue;

            bestArea = count;
            best = (minX, minY, maxX - minX + 1, maxY - minY + 1);

            void Consider(int neighbour)
            {
                if (visited[neighbour] || !mask[neighbour]) return;

                visited[neighbour] = true;
                queue.Enqueue(neighbour);
            }
        }

        return best;
    }
}
