namespace AccessibleVideoEditor.Core.Images;

/// <summary>
/// The same treatment applied to a folder of pictures.
///
/// The important decision here is what "the same treatment" means. A batch that
/// applied one crop rectangle to every scan would be useless - the photograph
/// lands somewhere different on the bed every time. So the <b>corrections</b>
/// travel (colour, levels, size, the card) and the <b>geometry is measured per
/// picture</b>: each one is found, straightened and cropped on its own terms.
///
/// That is the difference between a batch that saves an afternoon and one that
/// ruins a hundred files in a single keystroke.
/// </summary>
public sealed record BatchJob
{
    /// <summary>Straighten and crop each picture from its own measurement.</summary>
    public bool FixEachScan { get; init; } = true;

    /// <summary>Split any scan holding several photographs into one file each.</summary>
    public bool SplitMultiples { get; init; }

    public ColourAdjust Colour { get; init; } = ColourAdjust.None;

    public Levels Levels { get; init; } = Levels.None;

    /// <summary>Auto levels per picture, from its own histogram, rather than one setting for all.</summary>
    public bool AutoLevels { get; init; }

    /// <summary>Fit each picture inside this, keeping its shape. Zero leaves the size alone.</summary>
    public int FitWidth { get; init; }

    public int FitHeight { get; init; }

    public string Suffix { get; init; } = "-edited";

    public string Extension { get; init; } = ".png";

    /// <summary>
    /// Taken from a picture you have already got right, which is how anyone
    /// actually wants to set this up: fix one, then do the rest like that one.
    /// </summary>
    public static BatchJob From(ImageDocument document, bool autoLevels = false) => new()
    {
        FixEachScan = document.Crop.Width != document.SourceWidth
                      || Math.Abs(document.RotationDegrees) > 0.05,
        Colour = document.Colour,
        Levels = autoLevels ? Levels.None : document.Levels,
        AutoLevels = autoLevels,
        FitWidth = document.IsResampled ? document.Width : 0,
        FitHeight = document.IsResampled ? document.Height : 0,
    };

    /// <summary>
    /// Read back before it runs. A batch is the one operation here that can go
    /// wrong a hundred times before anybody notices, so what it is about to do
    /// is said as a sentence first.
    /// </summary>
    public string Describe()
    {
        var steps = new List<string>();

        if (FixEachScan) steps.Add("straighten and crop each one from its own measurement");
        if (SplitMultiples) steps.Add("split any scan holding several pictures");
        if (AutoLevels) steps.Add("set the levels from each picture's own histogram");
        else if (Levels.IsAnything) steps.Add(Levels.Describe());

        if (Colour.IsAnything) steps.Add(Colour.Describe());

        if (FitWidth > 0 && FitHeight > 0) steps.Add($"fit inside {FitWidth} by {FitHeight}");

        return steps.Count == 0
            ? "copy each picture unchanged"
            : string.Join(", then ", steps);
    }

    public string NameFor(string path) =>
        Path.GetFileNameWithoutExtension(path) + Suffix + Extension;
}

/// <summary>What happened to one picture.</summary>
public readonly record struct BatchItem(string Path, bool Succeeded, string Note);

/// <summary>
/// What happened to all of them, in a form that can be read out without
/// listing a hundred filenames.
/// </summary>
public sealed record BatchResult(IReadOnlyList<BatchItem> Items, string Directory)
{
    public int Succeeded => Items.Count(i => i.Succeeded);

    public int Failed => Items.Count - Succeeded;

    /// <summary>
    /// The count first, then the failures by name. Nobody wants to hear a
    /// hundred successes, and everybody wants to hear the three that did not
    /// work and why.
    /// </summary>
    public string Describe()
    {
        if (Items.Count == 0) return "there were no pictures in that folder";

        var summary = $"{Succeeded} of {Items.Count} written to {Directory}";

        if (Failed == 0) return summary;

        var problems = Items
            .Where(i => !i.Succeeded)
            .Take(5)
            .Select(i => $"{Path.GetFileName(i.Path)}, {i.Note}");

        var more = Failed > 5 ? $", and {Failed - 5} more" : string.Empty;

        return $"{summary}. {Failed} failed: {string.Join("; ", problems)}{more}";
    }

    /// <summary>The failures on their own, for stepping through afterwards.</summary>
    public IReadOnlyList<BatchItem> Failures => Items.Where(i => !i.Succeeded).ToList();
}
