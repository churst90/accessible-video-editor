using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Images;

/// <summary>
/// A picture being edited, and everything that has been decided about it.
///
/// Nothing here is destructive. A crop is a rectangle, a resize is a size, a
/// rotation is an angle, and a brush stroke is a shape in a list - the file on
/// disk is not touched until it is exported. That is not tidiness: it is what
/// makes every operation reversible, describable and re-orderable, which is the
/// only way an edit you cannot see is an edit you can trust.
/// </summary>
public sealed class ImageDocument
{
    public required string Path { get; init; }

    /// <summary>The size of the file as it was opened.</summary>
    public required int SourceWidth { get; init; }

    public required int SourceHeight { get; init; }

    /// <summary>Dots per inch, from the file's metadata. 72 when it does not say.</summary>
    public int Dpi { get; set; } = 72;

    /// <summary>What is being kept. Starts as the whole picture.</summary>
    public PixelRect Crop { get; set; }

    /// <summary>Positive is clockwise. Straightening a scan sets this.</summary>
    public double RotationDegrees { get; set; }

    /// <summary>The output size. Independent of the crop, which is what makes resampling a separate decision.</summary>
    public int Width { get; set; }

    public int Height { get; set; }

    public bool AspectLocked { get; set; } = true;

    public List<Shape> Shapes { get; set; } = [];

    /// <summary>Colour correction. Applied at export, never baked in.</summary>
    public ColourAdjust Colour { get; set; } = ColourAdjust.None;

    /// <summary>The curve, as numbers with names. Also applied at export.</summary>
    public Levels Levels { get; set; } = Levels.None;

    /// <summary>
    /// A card laid over the picture, using the video editor's own card model.
    ///
    /// This is deliberate reuse rather than a second way of doing the same
    /// thing: a lower third over a photograph and a lower third over a clip are
    /// the same object, edited by the same editor, described by the same
    /// sentence. The shape language handles geometry; cards handle titles and
    /// logos, which is what they are already good at.
    /// </summary>
    public CardComposition? Card { get; set; }

    /// <summary>What the analysis last found, so it can be asked for again without redoing it.</summary>
    public ScanReport? Report { get; set; }

    public static ImageDocument Open(string path, int width, int height, int dpi = 72) =>
        new()
        {
            Path = path,
            SourceWidth = width,
            SourceHeight = height,
            Dpi = dpi <= 0 ? 72 : dpi,
            Crop = new PixelRect(0, 0, width, height),
            Width = width,
            Height = height,
        };

    /// <summary>
    /// A copy, for undo. Shapes are records and never mutated in place, so the
    /// list is copied while its contents are shared - which is what makes a
    /// snapshot cheap enough to take before every edit.
    /// </summary>
    public ImageDocument Clone() => new()
    {
        Path = Path,
        SourceWidth = SourceWidth,
        SourceHeight = SourceHeight,
        Dpi = Dpi,
        Crop = Crop,
        RotationDegrees = RotationDegrees,
        Width = Width,
        Height = Height,
        AspectLocked = AspectLocked,
        Shapes = [.. Shapes],
        Colour = Colour,
        Levels = Levels,
        Card = Card,
        Report = Report,
    };

    public double AspectRatio => Height == 0 ? 0 : (double)Width / Height;

    public bool IsLandscape => Width > Height;

    /// <summary>True once the output size no longer matches what was cropped.</summary>
    public bool IsResampled => Width != Crop.Width || Height != Crop.Height;

    /// <summary>
    /// Enlarging past the original cannot add detail, and the result looks
    /// soft. Worth saying before it happens rather than after.
    /// </summary>
    public bool IsEnlarged => Width > Crop.Width || Height > Crop.Height;

    /// <summary>
    /// Physical size at the current resolution. This is the number that matters
    /// for anything printed, and it is invisible in every image editor until
    /// you go looking for it.
    /// </summary>
    public (double Inches, double Height) PrintSize =>
        Dpi <= 0 ? (0, 0) : ((double)Width / Dpi, (double)Height / Dpi);

    /// <summary>
    /// Roughly how big the file will be. Rough on purpose - the point is
    /// "about two megabytes" rather than a number that will be wrong anyway.
    /// </summary>
    public double EstimatedMegabytes(int quality = 85)
    {
        var pixels = (double)Width * Height;

        // About 0.15 bits per pixel per quality point, which lands within a
        // factor of two for photographs and is plenty for a decision.
        var bytes = pixels * 3 * (quality / 100.0) * 0.12;

        return Math.Round(bytes / 1_048_576, 2);
    }

    /// <summary>
    /// The whole state of the picture, in the order the questions are asked.
    /// Read when the image is opened and whenever you ask where you are.
    /// </summary>
    public string Describe()
    {
        var parts = new List<string>
        {
            $"{Width} by {Height}",
            Ratio(),
            IsLandscape ? "landscape" : Width == Height ? "square" : "portrait",
        };

        var (inches, high) = PrintSize;
        parts.Add($"{inches:0.#} by {high:0.#} inches at {Dpi} dpi");

        if (Crop.Width != SourceWidth || Crop.Height != SourceHeight)
        {
            var kept = SourceWidth * SourceHeight == 0
                ? 0
                : Crop.Area * 100.0 / (SourceWidth * SourceHeight);

            parts.Add($"cropped to {kept:0} percent of the original");
        }

        if (Math.Abs(RotationDegrees) > 0.05)
        {
            parts.Add($"straightened by {Math.Abs(RotationDegrees):0.0} degrees");
        }

        if (Shapes.Count > 0) parts.Add($"{Shapes.Count} shapes on top");
        if (Card is not null) parts.Add("a card on top");
        if (Colour.IsAnything) parts.Add(Colour.Describe());
        if (Levels.IsAnything) parts.Add(Levels.Describe());

        return string.Join(", ", parts);
    }

    /// <summary>
    /// The aspect as a ratio people say out loud. "1.5 to 1" tells you nothing;
    /// "3 by 2" tells you it is a photograph.
    /// </summary>
    public string Ratio()
    {
        if (Width == 0 || Height == 0) return "no size";

        var divisor = GreatestCommonDivisor(Width, Height);
        var w = Width / divisor;
        var h = Height / divisor;

        // Past this the exact ratio is noise; the decimal is more use.
        if (w > 30 || h > 30) return $"{AspectRatio:0.00} to 1";

        return $"{w} by {h}";
    }

    private static int GreatestCommonDivisor(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);

        return Math.Max(1, a);
    }

    /// <summary>
    /// The 3 by 3 cell a point falls in, in the same language as cards and
    /// scenes. One vocabulary for "where on the screen" across the whole
    /// application.
    /// </summary>
    public Placement PlacementAt(double x, double y)
    {
        var column = x < Width / 3.0 ? 0 : x > Width * 2 / 3.0 ? 2 : 1;
        var row = y < Height / 3.0 ? 2 : y > Height * 2 / 3.0 ? 0 : 1;

        return new Placement(row * 3 + column + 1);
    }
}
