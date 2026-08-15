using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Images;

/// <summary>
/// Resizing and cropping, and what is said while they happen.
///
/// The announcements are not decoration on top of these operations - they are
/// the operations. A resize you cannot see is a resize you know only by the
/// number it reports, so every step says the new size, what it did to the
/// aspect, and anything it has cost you.
/// </summary>
public static class ImageEdits
{
    // ---- resizing --------------------------------------------------------

    /// <summary>
    /// The sizes worth having as one key. Named by what they are for, because
    /// "1920 by 1080" is a number and "fit 1080" is a decision.
    /// </summary>
    public static readonly (string Name, int Width, int Height)[] Presets =
    [
        ("half", 0, 0),
        ("double", 0, 0),
        ("fit 1080", 1920, 1080),
        ("fit 4K", 3840, 2160),
        ("square 1080", 1080, 1080),
        ("vertical 1080 by 1920", 1080, 1920),
        ("thumbnail 640", 640, 640),
    ];

    public static EditResult Resize(ImageDocument document, int width, int height)
    {
        if (width < 1 || height < 1) return EditResult.NoChange("a picture cannot be smaller than a pixel");

        var before = (document.Width, document.Height);
        var wasRatio = document.AspectRatio;

        document.Width = width;
        document.Height = height;

        var warnings = new List<string>();

        if (document.IsEnlarged)
        {
            warnings.Add("this is bigger than the original, so it will look softer");
        }

        var drift = Math.Abs(document.AspectRatio - wasRatio) / (wasRatio == 0 ? 1 : wasRatio);

        if (drift > 0.01)
        {
            warnings.Add($"the shape has changed, now {document.Ratio()}");
        }

        return EditResult.Ok(
            $"{width} by {height}, was {before.Width} by {before.Height}. {SizeNote(document)}",
            warnings);
    }

    /// <summary>
    /// One dimension changed, the other following if the aspect is locked.
    /// This is the arrow-key path, so it says the least it can get away with -
    /// the full sentence on every press would be unusable at speed.
    /// </summary>
    public static EditResult Nudge(ImageDocument document, bool horizontal, int by)
    {
        var ratio = document.AspectRatio;

        if (horizontal)
        {
            document.Width = Math.Max(1, document.Width + by);

            if (document.AspectLocked && ratio > 0)
            {
                document.Height = Math.Max(1, (int)Math.Round(document.Width / ratio));
            }
        }
        else
        {
            document.Height = Math.Max(1, document.Height + by);

            if (document.AspectLocked && ratio > 0)
            {
                document.Width = Math.Max(1, (int)Math.Round(document.Height * ratio));
            }
        }

        return EditResult.Ok($"{document.Width} by {document.Height}");
    }

    public static EditResult Scale(ImageDocument document, double factor)
    {
        if (factor <= 0) return EditResult.NoChange("that is not a size");

        return Resize(
            document,
            Math.Max(1, (int)Math.Round(document.Width * factor)),
            Math.Max(1, (int)Math.Round(document.Height * factor)));
    }

    /// <summary>
    /// Fits inside a box without changing the shape, which is what "fit 1080"
    /// means and what typing two numbers into two boxes does not do.
    /// </summary>
    public static EditResult FitWithin(ImageDocument document, int width, int height)
    {
        if (width < 1 || height < 1) return EditResult.NoChange("that is not a size");

        var scale = Math.Min((double)width / document.Width, (double)height / document.Height);

        return Resize(
            document,
            Math.Max(1, (int)Math.Round(document.Width * scale)),
            Math.Max(1, (int)Math.Round(document.Height * scale)));
    }

    public static EditResult ApplyPreset(ImageDocument document, string name) => name switch
    {
        "half" => Scale(document, 0.5),
        "double" => Scale(document, 2),
        _ => Presets.FirstOrDefault(p => p.Name == name) is { Width: > 0 } preset
            ? FitWithin(document, preset.Width, preset.Height)
            : EditResult.NoChange($"there is no preset called {name}"),
    };

    public static EditResult ToggleAspectLock(ImageDocument document)
    {
        document.AspectLocked = !document.AspectLocked;

        return EditResult.Ok(document.AspectLocked
            ? "shape locked"
            : $"shape unlocked, currently {document.Ratio()}");
    }

    /// <summary>What the new size costs, said once rather than as three separate facts.</summary>
    private static string SizeNote(ImageDocument document)
    {
        var (inches, high) = document.PrintSize;

        return $"{inches:0.#} by {high:0.#} inches at {document.Dpi} dpi, "
               + $"about {document.EstimatedMegabytes():0.##} megabytes";
    }

    // ---- cropping --------------------------------------------------------

    /// <summary>
    /// Remove the paper around the picture. The single most useful crop there
    /// is, and the one nobody can do by eye without a mouse.
    /// </summary>
    public static EditResult CropToContent(ImageDocument document)
    {
        if (document.Report is not { } report) return EditResult.NoChange("nothing has been measured yet");
        if (report.Regions.Count == 0) return EditResult.NoChange("there is nothing to crop to");

        return CropTo(document, report.Regions[0], "cropped to the picture");
    }

    /// <summary>
    /// A ratio, anchored on a cell. "Square, anchored top centre" is one
    /// instruction and is a crop you can mean without pointing at anything.
    /// </summary>
    public static EditResult CropToRatio(ImageDocument document, double ratio, Placement anchor)
    {
        if (ratio <= 0) return EditResult.NoChange("that is not a shape");

        var crop = document.Crop;

        var width = crop.Width;
        var height = (int)Math.Round(width / ratio);

        if (height > crop.Height)
        {
            height = crop.Height;
            width = (int)Math.Round(height * ratio);
        }

        var (nx, ny) = anchor.Resolve();

        var x = crop.X + (int)Math.Round((crop.Width - width) * nx);
        var y = crop.Y + (int)Math.Round((crop.Height - height) * ny);

        return CropTo(
            document,
            new PixelRect(x, y, width, height),
            $"cropped to {Describe(ratio)}, {anchor.Describe()}");
    }

    public static EditResult CropTo(ImageDocument document, PixelRect area, string? said = null)
    {
        var x = Math.Clamp(area.X, 0, Math.Max(0, document.SourceWidth - 1));
        var y = Math.Clamp(area.Y, 0, Math.Max(0, document.SourceHeight - 1));

        var width = Math.Clamp(area.Width, 1, document.SourceWidth - x);
        var height = Math.Clamp(area.Height, 1, document.SourceHeight - y);

        var before = document.Crop;

        document.Crop = new PixelRect(x, y, width, height);

        // Resampling and cropping are separate decisions, so a crop takes the
        // output size with it unless one has been chosen deliberately.
        if (!document.IsResampled || before.Width == document.Width)
        {
            document.Width = width;
            document.Height = height;
        }

        var lost = document.SourceWidth * document.SourceHeight == 0
            ? 0
            : 100 - document.Crop.Area * 100.0 / (document.SourceWidth * document.SourceHeight);

        return EditResult.Ok(
            $"{said ?? "cropped"}, {width} by {height}, {document.Ratio()}"
            + (lost > 0.5 ? $", {lost:0} percent removed" : string.Empty));
    }

    /// <summary>
    /// One edge at a time, which is how a crop is adjusted without a mouse.
    /// Each press says which edge, where it now is, and how much is being cut -
    /// the last part being what you would otherwise have to imagine.
    /// </summary>
    public static EditResult NudgeEdge(ImageDocument document, CropEdge edge, int by)
    {
        var crop = document.Crop;

        var updated = edge switch
        {
            CropEdge.Left => new PixelRect(crop.X + by, crop.Y, crop.Width - by, crop.Height),
            CropEdge.Right => crop with { Width = crop.Width + by },
            CropEdge.Top => new PixelRect(crop.X, crop.Y + by, crop.Width, crop.Height - by),
            _ => crop with { Height = crop.Height + by },
        };

        if (updated.Width < 1 || updated.Height < 1)
        {
            return EditResult.NoChange("that would leave nothing");
        }

        if (updated.X < 0 || updated.Y < 0
            || updated.Right > document.SourceWidth || updated.Bottom > document.SourceHeight)
        {
            return EditResult.NoChange($"the {edge.ToString().ToLowerInvariant()} edge is already at the end");
        }

        document.Crop = updated;
        document.Width = updated.Width;
        document.Height = updated.Height;

        var cut = edge switch
        {
            CropEdge.Left => updated.X,
            CropEdge.Right => document.SourceWidth - updated.Right,
            CropEdge.Top => updated.Y,
            _ => document.SourceHeight - updated.Bottom,
        };

        var side = edge is CropEdge.Left or CropEdge.Right ? document.SourceWidth : document.SourceHeight;

        return EditResult.Ok(
            $"{edge.ToString().ToLowerInvariant()} edge, {cut} pixels cut, "
            + $"{(side == 0 ? 0 : cut * 100.0 / side):0} percent, "
            + $"now {updated.Width} by {updated.Height}");
    }

    public static EditResult ResetCrop(ImageDocument document)
    {
        document.Crop = new PixelRect(0, 0, document.SourceWidth, document.SourceHeight);
        document.Width = document.SourceWidth;
        document.Height = document.SourceHeight;

        return EditResult.Ok($"back to the whole picture, {document.Width} by {document.Height}");
    }

    // ---- straightening ---------------------------------------------------

    /// <summary>
    /// The scanner-bed fix, as one command: straighten, then crop to what was
    /// found. Offered as a sentence first so it can be pictured before it is
    /// pressed.
    /// </summary>
    public static EditResult Straighten(ImageDocument document)
    {
        if (document.Report is not { } report) return EditResult.NoChange("nothing has been measured yet");

        if (report.IsStraight)
        {
            return EditResult.NoChange($"it is already straight, within {ScanReport.StraightEnough} of a degree");
        }

        document.RotationDegrees -= report.SkewDegrees;

        return EditResult.Ok(
            $"straightened by {Math.Abs(report.SkewDegrees):0.0} degrees "
            + (report.SkewDegrees > 0 ? "anticlockwise" : "clockwise"));
    }

    /// <summary>Quarter turns, for a photograph that went on the bed sideways.</summary>
    public static EditResult Rotate(ImageDocument document, int quarterTurns)
    {
        document.RotationDegrees = (document.RotationDegrees + quarterTurns * 90) % 360;

        (document.Width, document.Height) = (document.Height, document.Width);

        return EditResult.Ok(
            $"turned {(quarterTurns > 0 ? "right" : "left")}, "
            + $"now {document.Width} by {document.Height}, "
            + (document.IsLandscape ? "landscape" : "portrait"));
    }

    /// <summary>
    /// Straighten and crop together, which is what anyone actually wants after
    /// scanning something.
    /// </summary>
    public static EditResult FixScan(ImageDocument document)
    {
        if (document.Report is not { } report) return EditResult.NoChange("nothing has been measured yet");

        var done = new List<string>();

        if (!report.IsStraight)
        {
            document.RotationDegrees -= report.SkewDegrees;
            done.Add($"straightened by {Math.Abs(report.SkewDegrees):0.0} degrees");
        }

        if (report.Regions.Count > 0 && report.HasBorder)
        {
            CropTo(document, report.Regions[0]);
            done.Add($"cropped to {document.Width} by {document.Height}");
        }

        var warnings = report.Regions.Count > 1
            ? new List<string> { $"there are {report.Regions.Count} pictures here; only the largest was used" }
            : [];

        return done.Count == 0
            ? EditResult.NoChange("nothing needed fixing")
            : EditResult.Ok(string.Join(", ", done), warnings);
    }

    private static string Describe(double ratio) => ratio switch
    {
        > 1.76 and < 1.79 => "16 by 9",
        > 1.32 and < 1.34 => "4 by 3",
        > 1.49 and < 1.51 => "3 by 2",
        > 0.99 and < 1.01 => "square",
        > 0.79 and < 0.81 => "4 by 5",
        > 0.55 and < 0.57 => "9 by 16",
        _ => $"{ratio:0.00} to 1",
    };
}

public enum CropEdge
{
    Left,
    Right,
    Top,
    Bottom,
}
