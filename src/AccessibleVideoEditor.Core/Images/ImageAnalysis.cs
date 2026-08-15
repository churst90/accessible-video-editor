namespace AccessibleVideoEditor.Core.Images;

/// <summary>
/// Answering, from the numbers, the questions a sighted person answers with a
/// glance: where is the picture in this scan, is it straight, and how much of
/// what I am looking at is empty paper.
///
/// This is the part of image editing that decides whether the rest is usable.
/// Cropping and straightening are easy once you know <i>what</i> to crop and
/// <i>by how much</i> to straighten; the whole difficulty is that those two
/// facts normally arrive through the eyes.
/// </summary>
public static class ImageAnalysis
{
    /// <summary>
    /// How far a pixel must differ from the surrounding paper before it counts
    /// as content. Low enough to catch a dark photograph on a dark bed, high
    /// enough not to trip on scanner noise.
    /// </summary>
    public const int DefaultTolerance = 24;

    /// <summary>
    /// The smallest rectangle containing everything that is not background.
    /// This is "crop to content", and it is also how much white there is around
    /// the edges - the same measurement answers both questions.
    /// </summary>
    public static PixelRect ContentBounds(Raster raster, int tolerance = DefaultTolerance)
    {
        var background = raster.BorderMean();

        var minX = raster.Width;
        var minY = raster.Height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < raster.Height; y++)
        {
            for (var x = 0; x < raster.Width; x++)
            {
                if (Math.Abs(raster.At(x, y) - background) <= tolerance) continue;

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        return maxX < 0
            ? new PixelRect(0, 0, raster.Width, raster.Height)
            : new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>
    /// The separate things lying on the bed.
    ///
    /// Two photographs on one scanner is the normal case, not the exotic one,
    /// and treating them as a single content rectangle would crop to a box
    /// containing both plus the gap between them. So regions are grown from
    /// seeds and reported individually.
    /// </summary>
    public static IReadOnlyList<PixelRect> DetectRegions(
        Raster raster,
        int tolerance = DefaultTolerance,
        double minimumArea = 0.01)
    {
        var background = raster.BorderMean();
        var visited = new bool[raster.Width * raster.Height];
        var regions = new List<PixelRect>();
        var smallest = raster.Width * raster.Height * minimumArea;

        var queue = new Queue<(int X, int Y)>();

        for (var startY = 0; startY < raster.Height; startY++)
        {
            for (var startX = 0; startX < raster.Width; startX++)
            {
                var index = startY * raster.Width + startX;

                if (visited[index]) continue;
                if (Math.Abs(raster.At(startX, startY) - background) <= tolerance) continue;

                visited[index] = true;
                queue.Clear();
                queue.Enqueue((startX, startY));

                int minX = startX, minY = startY, maxX = startX, maxY = startY;

                while (queue.Count > 0)
                {
                    var (x, y) = queue.Dequeue();

                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;

                    foreach (var (dx, dy) in Neighbours)
                    {
                        var nx = x + dx;
                        var ny = y + dy;

                        if (nx < 0 || ny < 0 || nx >= raster.Width || ny >= raster.Height) continue;

                        var neighbour = ny * raster.Width + nx;

                        if (visited[neighbour]) continue;
                        if (Math.Abs(raster.At(nx, ny) - background) <= tolerance) continue;

                        visited[neighbour] = true;
                        queue.Enqueue((nx, ny));
                    }
                }

                var region = new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1);

                // Dust and scanner noise are not photographs.
                if (region.Area >= smallest) regions.Add(region);
            }
        }

        return regions.OrderByDescending(r => r.Area).ToList();
    }

    private static readonly (int X, int Y)[] Neighbours =
        [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1)];

    /// <summary>
    /// How far the picture is rotated, in degrees, positive meaning clockwise.
    ///
    /// Found by asking, for each candidate angle, how <b>sharply</b> the rows
    /// line up when the image is projected onto the vertical axis. A straight
    /// picture has strong horizontal structure - edges, a horizon, the top of a
    /// photograph against the bed - and that structure smears out as soon as it
    /// is tilted. The angle where it is sharpest is the angle it is tilted by.
    ///
    /// This is the standard projection-profile method, which works because it
    /// needs no line detection and no assumptions about what is in the picture.
    /// </summary>
    public static double EstimateSkew(Raster raster, double limit = 15, double step = 0.25)
    {
        if (raster.Width < 8 || raster.Height < 8) return 0;

        var best = 0.0;
        var bestScore = double.MinValue;

        for (var angle = -limit; angle <= limit + 1e-9; angle += step)
        {
            var score = ProjectionSharpness(raster, angle);

            if (score > bestScore)
            {
                bestScore = score;
                best = angle;
            }
        }

        // A flat area has no horizontal structure to line up, so every angle
        // scores about the same and the winner is noise. Without this guard a
        // plain photograph would be reported as crooked by a fraction of a
        // degree, and offering to straighten something already straight is
        // worse than saying nothing.
        var straight = ProjectionSharpness(raster, 0);

        if (straight > double.MinValue && bestScore <= straight * 1.05) return 0;

        // Positive means clockwise. The shear that lines an edge up runs the
        // other way from the tilt that put it there, so the winning angle is
        // negated on the way out.
        return Math.Round(-best, 2);
    }

    /// <summary>
    /// The variance of the row sums after tilting by an angle. Higher means the
    /// rows differ more from each other, which means the horizontal structure
    /// is lined up rather than smeared across neighbouring rows.
    /// </summary>
    private static double ProjectionSharpness(Raster raster, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        var sin = Math.Sin(radians);

        var rows = new double[raster.Height];
        var counts = new int[raster.Height];

        var centreX = raster.Width / 2.0;
        var centreY = raster.Height / 2.0;

        for (var y = 0; y < raster.Height; y++)
        {
            for (var x = 0; x < raster.Width; x++)
            {
                // Only the vertical shift matters for a row projection, so the
                // full rotation is not needed - a shear is enough and is far
                // cheaper over hundreds of candidate angles.
                var shifted = (int)Math.Round(centreY + (y - centreY) + (x - centreX) * sin);

                if (shifted < 0 || shifted >= raster.Height) continue;

                rows[shifted] += raster.At(x, y);
                counts[shifted]++;
            }
        }

        var used = new List<double>();

        for (var i = 0; i < rows.Length; i++)
        {
            if (counts[i] > 0) used.Add(rows[i] / counts[i]);
        }

        if (used.Count < 4) return double.MinValue;

        // The difference between neighbouring rows, squared: a sharp edge shows
        // up as one big difference rather than several small ones, so this
        // peaks when the edge is on one row rather than spread over three.
        var score = 0.0;

        for (var i = 1; i < used.Count; i++)
        {
            var difference = used[i] - used[i - 1];
            score += difference * difference;
        }

        return score;
    }

    /// <summary>
    /// What the analysis found, in one place, so it can be spoken as a whole
    /// rather than as five separate questions.
    /// </summary>
    public static ScanReport Examine(Raster raster, int fullWidth, int fullHeight)
    {
        var regions = DetectRegions(raster);
        var content = ContentBounds(raster);

        var skew = regions.Count == 1
            ? EstimateSkew(Crop(raster, regions[0]))
            : EstimateSkew(raster);

        return new ScanReport(
            regions.Select(r => r.ScaleTo(raster.Width, raster.Height, fullWidth, fullHeight)).ToList(),
            content.ScaleTo(raster.Width, raster.Height, fullWidth, fullHeight),
            skew,
            fullWidth,
            fullHeight);
    }

    public static Raster Crop(Raster raster, PixelRect area)
    {
        var width = Math.Clamp(area.Width, 1, raster.Width);
        var height = Math.Clamp(area.Height, 1, raster.Height);

        var cropped = new byte[width * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                cropped[y * width + x] = raster.At(area.X + x, area.Y + y);
            }
        }

        return new Raster(width, height, cropped);
    }
}

/// <summary>
/// The answer to "what have I actually got here", ready to be read out.
/// </summary>
public sealed record ScanReport(
    IReadOnlyList<PixelRect> Regions,
    PixelRect Content,
    double SkewDegrees,
    int Width,
    int Height)
{
    /// <summary>Below this, straightening would move things without improving them.</summary>
    public const double StraightEnough = 0.4;

    public bool IsStraight => Math.Abs(SkewDegrees) < StraightEnough;

    public bool HasBorder =>
        Content.Width < Width * 0.97 || Content.Height < Height * 0.97;

    /// <summary>
    /// The whitespace on each side, as a percentage. Named individually because
    /// "there is white around it" is not actionable and "340 pixels on the
    /// left" is.
    /// </summary>
    public (double Left, double Right, double Top, double Bottom) Margins => (
        Width == 0 ? 0 : Content.X * 100.0 / Width,
        Width == 0 ? 0 : (Width - Content.Right) * 100.0 / Width,
        Height == 0 ? 0 : Content.Y * 100.0 / Height,
        Height == 0 ? 0 : (Height - Content.Bottom) * 100.0 / Height);

    /// <summary>
    /// Everything worth knowing, in the order it matters: how many pictures,
    /// how big, which way up, how crooked, how much waste.
    /// </summary>
    public string Describe()
    {
        if (Regions.Count == 0) return "this looks empty";

        var parts = new List<string>();

        parts.Add(Regions.Count == 1
            ? "one picture found"
            : $"{Regions.Count} pictures found");

        var first = Regions[0];

        parts.Add($"{first.Width} by {first.Height}");
        parts.Add(first.IsLandscape ? "landscape" : "portrait");

        if (!IsStraight)
        {
            parts.Add($"rotated {Math.Abs(SkewDegrees):0.0} degrees "
                      + (SkewDegrees > 0 ? "clockwise" : "anticlockwise"));
        }

        var fill = Width * Height == 0 ? 0 : first.Area * 100.0 / (Width * Height);
        parts.Add($"filling {fill:0} percent of the scan");

        if (Regions.Count == 1 && HasBorder)
        {
            var (left, right, top, bottom) = Margins;
            var biggest = Math.Max(Math.Max(left, right), Math.Max(top, bottom));

            var side = biggest == left ? "left"
                : biggest == right ? "right"
                : biggest == top ? "top"
                : "bottom";

            parts.Add($"with {biggest:0} percent empty on the {side}");
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// What one key would do about all of it. Offered as a sentence first,
    /// because a fix you cannot picture is a fix you will not press.
    /// </summary>
    public string Offer()
    {
        var steps = new List<string>();

        if (!IsStraight) steps.Add($"straighten by {Math.Abs(SkewDegrees):0.0} degrees");
        if (HasBorder) steps.Add("crop to the picture");

        if (Regions.Count > 1) steps.Add($"split into {Regions.Count} files");

        return steps.Count == 0
            ? "nothing needs fixing"
            : string.Join(", then ", steps);
    }
}
