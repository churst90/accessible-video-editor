using AccessibleVideoEditor.Core.Images;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// Runs a <see cref="BatchJob"/> over a folder.
///
/// Every picture is <b>measured on its own terms</b> before the shared
/// corrections are applied to it, because a photograph lands somewhere
/// different on the scanner bed every time. Carrying one crop rectangle across
/// a hundred files is the difference between a batch that saves an afternoon
/// and one that ruins a hundred files in a single keystroke.
///
/// Progress is reported per file rather than only at the end: a batch that says
/// nothing for four minutes is indistinguishable from one that has hung.
/// </summary>
public sealed class BatchProcessor(ImageIo? io = null)
{
    private readonly ImageIo _io = io ?? new ImageIo();

    /// <summary>The extensions worth looking for. Anything else in the folder is left alone.</summary>
    public static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".webp"];

    public static IReadOnlyList<string> PicturesIn(string directory) =>
        !Directory.Exists(directory)
            ? []
            : Directory.EnumerateFiles(directory)
                .Where(file => Extensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToList();

    public async Task<BatchResult> RunAsync(
        string sourceDirectory,
        string outputDirectory,
        BatchJob job,
        Action<string>? progress = null,
        CancellationToken ct = default)
    {
        var pictures = PicturesIn(sourceDirectory);
        var items = new List<BatchItem>();

        if (pictures.Count == 0) return new BatchResult(items, outputDirectory);

        Directory.CreateDirectory(outputDirectory);

        for (var i = 0; i < pictures.Count; i++)
        {
            if (ct.IsCancellationRequested) break;

            var picture = pictures[i];

            // Counted out loud. "Four of sixty" is the difference between
            // waiting and wondering whether it is still going.
            progress?.Invoke($"{i + 1} of {pictures.Count}, {Path.GetFileName(picture)}");

            items.AddRange(await OneAsync(picture, outputDirectory, job, ct).ConfigureAwait(false));
        }

        return new BatchResult(items, outputDirectory);
    }

    private async Task<IReadOnlyList<BatchItem>> OneAsync(
        string picture,
        string outputDirectory,
        BatchJob job,
        CancellationToken ct)
    {
        try
        {
            var examined = await _io.ExamineAsync(picture, ct).ConfigureAwait(false);

            if (examined is not { } found)
            {
                return [new BatchItem(picture, false, "could not be read")];
            }

            var (facts, report) = found;

            var document = ImageDocument.Open(picture, facts.Width, facts.Height, facts.Dpi);
            document.Report = report;

            // Geometry first, from this picture's own measurement.
            if (job.FixEachScan) ImageEdits.FixScan(document);

            if (job.AutoLevels && await _io.DecodeAsync(picture, ct: ct).ConfigureAwait(false) is { } raster)
            {
                LevelEdits.Auto(document, raster);
            }
            else if (job.Levels.IsAnything)
            {
                document.Levels = job.Levels;
            }

            document.Colour = job.Colour;

            if (job.FitWidth > 0 && job.FitHeight > 0)
            {
                ImageEdits.FitWithin(document, job.FitWidth, job.FitHeight);
            }

            if (job.SplitMultiples && report.Regions.Count > 1)
            {
                var said = await _io.SplitAsync(document, outputDirectory, ct).ConfigureAwait(false);

                return [new BatchItem(picture, said.Contains(" of "), said)];
            }

            var target = Path.Combine(outputDirectory, job.NameFor(picture));

            // Never overwrite the file it came from. A batch pointed at its own
            // folder would otherwise eat the originals.
            if (Path.GetFullPath(target) == Path.GetFullPath(picture))
            {
                return [new BatchItem(picture, false, "that would overwrite the original")];
            }

            var result = await _io.ExportAsync(document, target, ct: ct).ConfigureAwait(false);

            return [new BatchItem(
                picture,
                result.StartsWith("saved", StringComparison.Ordinal),
                result)];
        }
        catch (Exception exception)
        {
            // One bad file does not stop the other ninety-nine.
            return [new BatchItem(picture, false, exception.Message)];
        }
    }

    /// <summary>
    /// What is about to happen, before it happens: how many pictures, and what
    /// each one will get. A batch is the one operation here that can go wrong a
    /// hundred times before anybody notices.
    /// </summary>
    public static string Preview(string directory, BatchJob job)
    {
        var pictures = PicturesIn(directory);

        return pictures.Count == 0
            ? "there are no pictures in that folder"
            : $"{pictures.Count} pictures. Each one: {job.Describe()}";
    }
}
