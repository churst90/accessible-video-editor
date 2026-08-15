namespace AccessibleVideoEditor.Core.Model;

/// <summary>
/// A named output target: what to make, at what size, for where it is going.
///
/// Presets are named for <b>what they are for</b> rather than for their numbers,
/// on the same rule the image editor's sizes follow: "YouTube 1080p" is a
/// decision and "1920 by 1080, 8000 kbps, H.264 high" is arithmetic you would
/// have to do first. The numbers are still said, after the name, because you
/// sometimes need them - but you should never need them to choose.
///
/// The part that matters without sight is <see cref="DescribeCost"/>. Exporting
/// a 16:9 edit as a vertical short throws away nearly half the width of every
/// frame, and that is the kind of thing you find out by watching. Here it is
/// said before the render starts.
/// </summary>
public sealed class ExportPreset
{
    public required string Name { get; init; }

    /// <summary>What it is for, in the words you would use to choose it.</summary>
    public required string Purpose { get; init; }

    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>Null keeps the project's frame rate.</summary>
    public double? Fps { get; init; }

    /// <summary>Constant Rate Factor. Lower is better quality and a bigger file.</summary>
    public int Crf { get; init; } = 18;

    public string AudioBitrate { get; init; } = "192k";

    /// <summary>No picture at all. A podcast cut, or a transcript to check by ear.</summary>
    public bool AudioOnly { get; init; }

    /// <summary>
    /// How a shape change is resolved. Cropping keeps the picture full-frame and
    /// loses the edges; padding keeps everything and adds bars. Neither is right
    /// in general, so it is part of the preset rather than a global setting.
    /// </summary>
    public FitMode Fit { get; init; } = FitMode.Fill;

    public string Extension => AudioOnly ? ".m4a" : ".mp4";

    public string FileName => $"{Slug(Name)}{Extension}";

    public double AspectRatio => Height == 0 ? 0 : (double)Width / Height;

    /// <summary>Name, purpose, then the numbers. In that order, on purpose.</summary>
    public string Describe() =>
        AudioOnly
            ? $"{Name}. {Purpose}. Sound only, {AudioBitrate}"
            : $"{Name}. {Purpose}. {Width} by {Height}"
              + (Fps is { } fps ? $", {fps:0.##} frames a second" : string.Empty);

    /// <summary>
    /// What this preset will do to the edit you have - said before it runs.
    /// A shape change is the only export decision that removes picture, and it
    /// is invisible until you watch the result.
    /// </summary>
    public string DescribeCost(int canvasWidth, int canvasHeight)
    {
        if (AudioOnly) return "the picture is dropped entirely";
        if (canvasWidth <= 0 || canvasHeight <= 0) return "same shape as the project";

        var from = (double)canvasWidth / canvasHeight;
        var to = AspectRatio;

        if (Math.Abs(from - to) < 0.01) return "same shape as the project, nothing is lost";

        if (Fit == FitMode.Fit)
        {
            return to < from
                ? "the picture is letterboxed, with bars at the top and bottom"
                : "the picture is pillarboxed, with bars at the sides";
        }

        // Fill crops. Which axis loses depends on which way the shape moved.
        var lost = to < from
            ? 1 - to / from   // narrower target: the sides go
            : 1 - from / to;  // taller target: the top and bottom go

        var what = to < from ? "the sides" : "the top and bottom";

        return $"{what} are cropped, losing about {lost * 100:0} percent of the frame";
    }

    private static string Slug(string name) =>
        new(name.ToLowerInvariant()
                .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')
                .ToArray());

    /// <summary>
    /// The ones that come with the application. Ordinary use is one of these;
    /// the list is short on purpose, because a menu of twenty formats is a menu
    /// you read every time instead of choosing from.
    /// </summary>
    public static IReadOnlyList<ExportPreset> BuiltIn { get; } =
    [
        new()
        {
            Name = "YouTube 1080p",
            Purpose = "the normal one",
            Width = 1920,
            Height = 1080,
            Crf = 18,
            AudioBitrate = "192k",
        },
        new()
        {
            Name = "YouTube 4K",
            Purpose = "when the source is 4K and worth it",
            Width = 3840,
            Height = 2160,
            Crf = 20,
            AudioBitrate = "192k",
        },
        new()
        {
            Name = "Shorts",
            Purpose = "vertical, for Shorts, Reels and TikTok",
            Width = 1080,
            Height = 1920,
            Crf = 20,
            AudioBitrate = "192k",
            Fit = FitMode.Fill,
        },
        new()
        {
            Name = "Square",
            Purpose = "for feeds that crop anything wider",
            Width = 1080,
            Height = 1080,
            Crf = 20,
            AudioBitrate = "192k",
            Fit = FitMode.Fill,
        },
        new()
        {
            Name = "Audio only",
            Purpose = "a podcast cut, or checking the edit by ear",
            AudioOnly = true,
            AudioBitrate = "256k",
        },
        new()
        {
            Name = "Small preview",
            Purpose = "something to send someone",
            Width = 1280,
            Height = 720,
            Crf = 26,
            AudioBitrate = "128k",
        },
    ];

    public static ExportPreset? ByName(string name) =>
        BuiltIn.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}
