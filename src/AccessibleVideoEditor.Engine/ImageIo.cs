using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AccessibleVideoEditor.Core.Images;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// Getting a picture in, and getting it back out again.
///
/// The decode side deliberately produces a <b>tiny grey raster</b> rather than
/// the real image: everything the analysis asks is about where things are, and
/// a 400-pixel-wide copy answers all of it in milliseconds. The full resolution
/// is only ever touched when something is actually exported.
/// </summary>
public sealed class ImageIo(string ffmpegPath = "ffmpeg", string ffprobePath = "ffprobe")
{
    /// <summary>Width, height and dots per inch, from the file's own metadata.</summary>
    public async Task<ImageFacts?> ProbeAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path)) return null;

        var info = new ProcessStartInfo(ffprobePath)
        {
            ArgumentList =
            {
                "-v", "error",
                "-select_streams", "v:0",
                "-show_entries", "stream=width,height",
                "-of", "json",
                path,
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = Process.Start(info);
            if (process is null) return null;

            var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            using var document = JsonDocument.Parse(output);

            var stream = document.RootElement.GetProperty("streams").EnumerateArray().FirstOrDefault();

            if (stream.ValueKind != JsonValueKind.Object) return null;

            return new ImageFacts(
                stream.GetProperty("width").GetInt32(),
                stream.GetProperty("height").GetInt32(),
                DpiFrom(path));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Dots per inch, read out of the file rather than assumed.
    ///
    /// It matters for exactly one thing and that thing matters a lot: physical
    /// size when printed. ffprobe does not report it, so PNG's <c>pHYs</c> and
    /// JPEG's JFIF density are read directly. 72 when the file does not say,
    /// which is the convention every other application uses.
    /// </summary>
    public static int DpiFrom(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            var head = new byte[Math.Min(4096, file.Length)];
            var read = file.Read(head);

            // PNG: a pHYs chunk holds pixels per metre.
            for (var i = 0; i < read - 12; i++)
            {
                if (head[i] != 'p' || head[i + 1] != 'H' || head[i + 2] != 'Y' || head[i + 3] != 's') continue;

                var perMetre = (head[i + 4] << 24) | (head[i + 5] << 16) | (head[i + 6] << 8) | head[i + 7];
                var unit = head[i + 12];

                if (unit == 1 && perMetre > 0) return (int)Math.Round(perMetre * 0.0254);
            }

            // JPEG: the JFIF header holds density and its unit.
            for (var i = 0; i < read - 14; i++)
            {
                if (head[i] != 'J' || head[i + 1] != 'F' || head[i + 2] != 'I' || head[i + 3] != 'F') continue;

                var unit = head[i + 5];
                var x = (head[i + 6] << 8) | head[i + 7];

                if (x <= 0) break;

                return unit switch
                {
                    1 => x,                                  // already per inch
                    2 => (int)Math.Round(x * 2.54),          // per centimetre
                    _ => 72,
                };
            }
        }
        catch (Exception)
        {
        }

        return 72;
    }

    /// <summary>
    /// A small grey copy, for the analysis to work on. Everything downstream
    /// scales its answers back up, so nothing depends on this size beyond how
    /// fine the measurements can be.
    /// </summary>
    public async Task<Raster?> DecodeAsync(string path, int width = 400, CancellationToken ct = default)
    {
        if (!File.Exists(path)) return null;

        var info = new ProcessStartInfo(ffmpegPath)
        {
            ArgumentList =
            {
                "-hide_banner", "-loglevel", "error",
                "-i", path,
                "-vf", $"scale={width}:-1:flags=area,format=gray",
                "-frames:v", "1",
                "-f", "rawvideo", "-pix_fmt", "gray", "-",
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = Process.Start(info);
            if (process is null) return null;

            using var buffer = new MemoryStream();
            await process.StandardOutput.BaseStream.CopyToAsync(buffer, ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            var bytes = buffer.ToArray();
            if (bytes.Length == 0) return null;

            // The height came from the aspect ratio rather than from us, so it
            // is worked back out from how much data arrived.
            var height = bytes.Length / width;
            if (height == 0) return null;

            return new Raster(width, height, bytes[..(width * height)]);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// A small colour copy, for the questions grey cannot answer. A cast is
    /// invisible to a brightness histogram - a photograph that is too blue and
    /// one that is right have the same shape in grey.
    /// </summary>
    public async Task<ColourRaster?> DecodeColourAsync(
        string path,
        int width = 240,
        CancellationToken ct = default)
    {
        if (!File.Exists(path)) return null;

        var info = new ProcessStartInfo(ffmpegPath)
        {
            ArgumentList =
            {
                "-hide_banner", "-loglevel", "error",
                "-i", path,
                "-vf", $"scale={width}:-1:flags=area",
                "-frames:v", "1",
                "-f", "rawvideo", "-pix_fmt", "rgb24", "-",
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = Process.Start(info);
            if (process is null) return null;

            using var buffer = new MemoryStream();
            await process.StandardOutput.BaseStream.CopyToAsync(buffer, ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            var bytes = buffer.ToArray();
            if (bytes.Length == 0) return null;

            var height = bytes.Length / (width * 3);
            if (height == 0) return null;

            return new ColourRaster(width, height, bytes[..(width * height * 3)]);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Everything the analysis knows about a file, in one call, because the
    /// answer to "what have I got" should cost one key.
    /// </summary>
    public async Task<(ImageFacts Facts, ScanReport Report)?> ExamineAsync(
        string path,
        CancellationToken ct = default)
    {
        if (await ProbeAsync(path, ct).ConfigureAwait(false) is not { } facts) return null;
        if (await DecodeAsync(path, ct: ct).ConfigureAwait(false) is not { } raster) return null;

        return (facts, ImageAnalysis.Examine(raster, facts.Width, facts.Height));
    }

    /// <summary>
    /// The colour at a point, in the real image rather than in the small copy -
    /// asking what colour something is deserves the true answer.
    /// </summary>
    public async Task<(byte R, byte G, byte B)?> SampleAsync(
        string path,
        int x,
        int y,
        CancellationToken ct = default)
    {
        var info = new ProcessStartInfo(ffmpegPath)
        {
            ArgumentList =
            {
                "-hide_banner", "-loglevel", "error",
                "-i", path,
                "-vf", $"crop=1:1:{x}:{y}",
                "-frames:v", "1",
                "-f", "rawvideo", "-pix_fmt", "rgb24", "-",
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = Process.Start(info);
            if (process is null) return null;

            using var buffer = new MemoryStream();
            await process.StandardOutput.BaseStream.CopyToAsync(buffer, ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            var bytes = buffer.ToArray();

            return bytes.Length >= 3 ? (bytes[0], bytes[1], bytes[2]) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The filters that turn the original into what has been decided: rotate,
    /// then crop, then resize. That order matters - cropping before
    /// straightening would cut the picture at the angle it arrived at.
    /// </summary>
    public static IReadOnlyList<string> FiltersFor(ImageDocument document)
    {
        var filters = new List<string>();

        if (Math.Abs(document.RotationDegrees) > 0.01)
        {
            // ffmpeg rotates clockwise for a positive angle, which is the same
            // direction the document means, so this is not negated.
            var radians = document.RotationDegrees * Math.PI / 180;

            filters.Add(
                $"rotate={radians.ToString("0.#####", CultureInfo.InvariantCulture)}"
                + ":fillcolor=none:bilinear=1");
        }

        var crop = document.Crop;

        if (crop.Width != document.SourceWidth || crop.Height != document.SourceHeight)
        {
            filters.Add($"crop={crop.Width}:{crop.Height}:{crop.X}:{crop.Y}");
        }

        if (document.Width != crop.Width || document.Height != crop.Height)
        {
            // Area for shrinking, Lanczos for enlarging: the wrong one either
            // aliases or softens, and neither is visible to the person choosing.
            var shrinking = document.Width < crop.Width;

            filters.Add($"scale={document.Width}:{document.Height}:flags={(shrinking ? "area" : "lanczos")}");
        }

        // Levels before colour, and both after the geometry: the points are set
        // from the picture's own histogram, so they have to be applied before
        // anything else moves the tones around.
        filters.AddRange(LevelFilters(document.Levels));
        filters.AddRange(ColourFilters(document.Colour));

        return filters;
    }

    /// <summary>
    /// Colour correction, as filters.
    ///
    /// Exposure goes through gamma rather than brightness: adding brightness
    /// shifts the whole picture and flattens it, while gamma lifts the middle
    /// and leaves black as black, which is what "a third of a stop brighter"
    /// actually means. Shadows and highlights are the same idea applied to one
    /// end of the range at a time.
    /// </summary>
    public static IReadOnlyList<string> ColourFilters(ColourAdjust colour)
    {
        if (!colour.IsAnything) return [];

        var filters = new List<string>();

        if (Math.Abs(colour.TemperatureK - 6500) > 1)
        {
            filters.Add($"colortemperature=temperature={Number(colour.TemperatureK)}:mix=1");
        }

        if (Math.Abs(colour.Tint) > 0.001)
        {
            // Magenta is more red and blue, less green; the shadows, midtones
            // and highlights are moved together so it reads as white balance
            // rather than as a colour cast.
            var amount = Number(colour.Tint / 100 * 0.5);
            var inverse = Number(-colour.Tint / 100 * 0.5);

            filters.Add($"colorbalance=rm={amount}:gm={inverse}:bm={amount}");
        }

        var eq = new List<string>();

        if (Math.Abs(colour.Exposure) > 0.001)
        {
            eq.Add($"gamma={Number(Math.Pow(2, colour.Exposure * 0.6))}");
        }

        if (Math.Abs(colour.Contrast) > 0.001) eq.Add($"contrast={Number(1 + colour.Contrast / 100)}");

        if (colour.Monochrome) eq.Add("saturation=0");
        else if (Math.Abs(colour.Saturation) > 0.001)
        {
            eq.Add($"saturation={Number(Math.Clamp(1 + colour.Saturation / 100, 0, 3))}");
        }

        if (eq.Count > 0) filters.Add($"eq={string.Join(':', eq)}");

        if (Math.Abs(colour.Shadows) > 0.001)
        {
            filters.Add($"curves=all='0/{Number(Math.Clamp(colour.Shadows / 200, -0.3, 0.3))} 1/1'");
        }

        if (Math.Abs(colour.Highlights) > 0.001)
        {
            filters.Add($"curves=all='0/0 1/{Number(Math.Clamp(1 + colour.Highlights / 200, 0.7, 1))}'");
        }

        return filters;
    }

    /// <summary>
    /// The curve, as filters.
    ///
    /// <c>colorlevels</c> does the black and white points, which is the part
    /// that matters most: it is what turns a flat scan into a picture. The zone
    /// adjustments go through <c>curves</c>, which takes explicit points - so
    /// "midtones up 8 percent" is one point moved rather than a shape drawn.
    /// </summary>
    public static IReadOnlyList<string> LevelFilters(Levels levels)
    {
        if (!levels.IsAnything) return [];

        var filters = new List<string>();

        if (levels.BlackPoint > 0 || levels.WhitePoint < 255)
        {
            var low = Number(levels.BlackPoint / 255.0);
            var high = Number(levels.WhitePoint / 255.0);

            filters.Add(
                $"colorlevels=rimin={low}:gimin={low}:bimin={low}"
                + $":rimax={high}:gimax={high}:bimax={high}");
        }

        // Per channel, which is the only thing that reaches a cast the
        // temperature control cannot: temperature moves the picture along one
        // axis, and a yellowed page or a mixed light is off in a direction that
        // axis does not pass through.
        if (levels.HasChannels)
        {
            filters.Add(
                "colorlevels="
                + $"rimin={Number(levels.Red.BlackPoint / 255.0)}"
                + $":gimin={Number(levels.Green.BlackPoint / 255.0)}"
                + $":bimin={Number(levels.Blue.BlackPoint / 255.0)}"
                + $":rimax={Number(levels.Red.WhitePoint / 255.0)}"
                + $":gimax={Number(levels.Green.WhitePoint / 255.0)}"
                + $":bimax={Number(levels.Blue.WhitePoint / 255.0)}");
        }

        var points = new List<(double X, double Y)>();

        if (Math.Abs(levels.Shadows) > 0.001)
        {
            points.Add((0.25, Math.Clamp(0.25 + levels.Shadows / 200, 0.02, 0.98)));
        }

        if (Math.Abs(levels.Midtones) > 0.001)
        {
            points.Add((0.5, Math.Clamp(0.5 + levels.Midtones / 200, 0.05, 0.95)));
        }

        if (Math.Abs(levels.Highlights) > 0.001)
        {
            points.Add((0.75, Math.Clamp(0.75 + levels.Highlights / 200, 0.02, 0.98)));
        }

        if (points.Count > 0)
        {
            var curve = string.Join(
                ' ',
                new[] { (X: 0.0, Y: 0.0) }
                    .Concat(points.OrderBy(p => p.X))
                    .Append((X: 1.0, Y: 1.0))
                    .Select(p => $"{Number(p.X)}/{Number(p.Y)}"));

            filters.Add($"curves=all='{curve}'");
        }

        return filters;
    }

    /// <summary>
    /// Text, as <c>drawtext</c> filters.
    ///
    /// Text is the one shape Core cannot draw: it has arithmetic but no fonts.
    /// So it is described and listed there and rendered here, where ffmpeg has
    /// the fonts - which also means it comes out with real hinting and kerning
    /// rather than something hand-plotted.
    ///
    /// The outline is chosen from the text's own brightness: light text gets a
    /// dark edge and dark text a light one. Nobody is going to look at the
    /// result and notice white text on a white wall.
    /// </summary>
    public static IReadOnlyList<string> TextFilters(ImageDocument document, string? fontPath = null)
    {
        var font = fontPath ?? Fonts.Path();
        var filters = new List<string>();

        foreach (var shape in document.Shapes.Where(s => s.Kind == ShapeKind.Text))
        {
            if (shape.Text.Length == 0) continue;

            var colour = Colours.Parse(shape.Colour) ?? ((byte)255, (byte)255, (byte)255);
            var light = Colours.Luminance(colour.Item1, colour.Item2, colour.Item3) > 0.5;

            var (x, y) = shape.Placement.Resolve();

            // Size is a fraction of the height, so the same sentence gives the
            // same-looking text whatever the picture is.
            var size = Math.Max(8, (int)Math.Round(document.Height * Math.Clamp(shape.Size, 0.01, 1) * 0.5));

            filters.Add(
                $"drawtext=fontfile='{font}'"
                + $":text='{OverlayFilters.Escape(shape.Text)}'"
                + $":fontsize={size}"
                + $":fontcolor=0x{colour.Item1:X2}{colour.Item2:X2}{colour.Item3:X2}"
                + $":borderw=3:bordercolor={(light ? "black" : "white")}@0.8"
                + $":x=(w*{Number(x)})-(text_w/2)"
                + $":y=(h*{Number(y)})-(text_h/2)");
        }

        return filters;
    }

    private static string Number(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// Writes the result. Shapes are drawn into a transparent overlay by Core
    /// and composited here; text is added afterwards by ffmpeg, which has the
    /// fonts.
    /// </summary>
    public async Task<string> ExportAsync(
        ImageDocument document,
        string output,
        int quality = 92,
        CancellationToken ct = default)
    {
        var arguments = new List<string> { "-y", "-hide_banner", "-loglevel", "error", "-i", document.Path };

        // Text is not painted into the overlay: Core has no fonts, so it goes
        // to ffmpeg after everything else has been composited.
        var painted = document.Shapes.Where(shape => shape.Kind != ShapeKind.Text).ToList();

        // A card is drawn by the video editor's own card renderer, so a lower
        // third over a photograph is the same object as one over a clip.
        var text = TextFilters(document)
            .Concat(document.Card is { } card
                ? OverlayFilters.Card(card, document.Width, document.Height, Fonts.Path())
                : [])
            .ToList();

        var overlay = painted.Count > 0 ? Path.GetTempFileName() + ".png" : null;

        if (overlay is not null)
        {
            var canvas = new Canvas(document.Width, document.Height);

            foreach (var shape in painted) shape.DrawOn(canvas);

            canvas.WritePng(overlay);
            arguments.AddRange(["-i", overlay]);
        }

        var filters = FiltersFor(document).ToList();

        if (overlay is null)
        {
            var chain = filters.Concat(text).ToList();

            if (chain.Count > 0) arguments.AddRange(["-vf", string.Join(',', chain)]);
        }
        else
        {
            var chain = filters.Count > 0 ? string.Join(',', filters) : "null";

            // Text goes on last so it sits above the shapes, which is what
            // anyone means by putting a caption on a picture.
            var after = text.Count > 0 ? "," + string.Join(',', text) : string.Empty;

            arguments.AddRange([
                "-filter_complex",
                $"[0:v]{chain}[base];[base][1:v]overlay=0:0{after}",
            ]);
        }

        if (output.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || output.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            // ffmpeg's scale is 2 to 31, backwards from the familiar 0 to 100.
            arguments.AddRange(["-q:v", Math.Clamp(31 - quality * 29 / 100, 2, 31).ToString()]);
        }

        arguments.Add(output);

        try
        {
            var info = new ProcessStartInfo(ffmpegPath) { RedirectStandardError = true };

            foreach (var argument in arguments) info.ArgumentList.Add(argument);

            using var process = Process.Start(info);
            if (process is null) return "could not start ffmpeg";

            var error = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            if (overlay is not null && File.Exists(overlay)) File.Delete(overlay);

            if (process.ExitCode != 0)
            {
                return $"export failed: {error.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)}";
            }

            var size = new FileInfo(output).Length / 1_048_576.0;

            return $"saved {Path.GetFileName(output)}, {document.Width} by {document.Height}, {size:0.##} megabytes";
        }
        catch (Exception exception)
        {
            return $"export failed: {exception.Message}";
        }
    }

    /// <summary>
    /// Splits a scan holding several photographs into one file each. This is
    /// what a scanning session actually produces, and doing it by hand means
    /// finding four rectangles by eye.
    /// </summary>
    public async Task<string> SplitAsync(
        ImageDocument document,
        string directory,
        CancellationToken ct = default)
    {
        if (document.Report is not { Regions.Count: > 1 } report)
        {
            return "there is only one picture here";
        }

        Directory.CreateDirectory(directory);

        var stem = Path.GetFileNameWithoutExtension(document.Path);
        var written = 0;

        for (var i = 0; i < report.Regions.Count; i++)
        {
            var region = report.Regions[i];

            var part = ImageDocument.Open(document.Path, document.SourceWidth, document.SourceHeight, document.Dpi);
            part.RotationDegrees = document.RotationDegrees;
            ImageEdits.CropTo(part, region);

            var target = Path.Combine(directory, $"{stem}-{i + 1}.png");

            var result = await ExportAsync(part, target, ct: ct).ConfigureAwait(false);

            if (result.StartsWith("saved", StringComparison.Ordinal)) written++;
        }

        return $"{written} of {report.Regions.Count} pictures written to {directory}";
    }
}

/// <summary>What a file says about itself before anything has been decided.</summary>
public readonly record struct ImageFacts(int Width, int Height, int Dpi);
