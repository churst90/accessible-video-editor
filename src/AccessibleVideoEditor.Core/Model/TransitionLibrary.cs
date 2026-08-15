namespace AccessibleVideoEditor.Core.Model;

/// <summary>
/// The transitions on offer, and the ones you have made yourself.
///
/// ffmpeg ships fifty-eight, most of which read as amateur. The short list is
/// what a professional edit actually uses; the rest are reachable by name, and
/// <c>custom</c> takes an expression, which is where your own live.
/// </summary>
public static class TransitionLibrary
{
    /// <summary>Named, in the order they are worth reaching for.</summary>
    public static readonly (string Name, TransitionType Type)[] Common =
    [
        ("cut", TransitionType.Cut),
        ("fade", TransitionType.Fade),
        ("fade through black", TransitionType.FadeBlack),
        ("wipe left", TransitionType.WipeLeft),
        ("wipe right", TransitionType.WipeRight),
        ("slide left", TransitionType.SlideLeft),
        ("slide right", TransitionType.SlideRight),
    ];

    /// <summary>
    /// The rest of what ffmpeg has. Offered separately so the short list stays
    /// short - a menu of fifty-eight is a menu nobody reads to the end of.
    /// </summary>
    public static readonly string[] More =
    [
        "dissolve", "pixelize", "radial", "circleopen", "circleclose",
        "smoothleft", "smoothright", "smoothup", "smoothdown",
        "wipeup", "wipedown", "slideup", "slidedown",
        "fadegrays", "fadewhite", "hlwind", "vuwind", "squeezeh", "squeezev",
    ];

    public static readonly double[] Lengths = [0.15, 0.25, 0.4, 0.6, 1.0, 1.5, 2.0];

    /// <summary>Lengths said as what they are for rather than as numbers.</summary>
    public static string DescribeLength(double seconds) => seconds switch
    {
        <= 0.18 => "a fifth of a second, barely there",
        <= 0.3 => "a quarter of a second, quick",
        <= 0.45 => "four tenths, the usual",
        <= 0.7 => "six tenths, gentle",
        <= 1.1 => "one second, slow",
        <= 1.6 => "a second and a half, very slow",
        _ => $"{seconds:0.#} seconds, a scene change",
    };
}

/// <summary>
/// A transition you made, kept by name so it can be used again.
///
/// The expression is ffmpeg's: it gets the two frames and the progress through
/// the transition, and returns what to show. That is a thing you can write down
/// and read back, which is why it is a better fit here than a node graph.
/// </summary>
public sealed class CustomTransition
{
    public required string Name { get; set; }

    /// <summary>An xfade name, or an expression when <see cref="IsExpression"/>.</summary>
    public required string Definition { get; set; }

    public bool IsExpression { get; set; }

    public double Duration { get; set; } = 0.4;

    public string? SoundPath { get; set; }

    public string Describe() =>
        $"{Name}, {(IsExpression ? "an expression" : Definition)}, {Duration:0.##} seconds"
        + (SoundPath is { Length: > 0 } ? $", with {System.IO.Path.GetFileNameWithoutExtension(SoundPath)}" : string.Empty);

    public Transition ToTransition() => new()
    {
        Type = TransitionType.Custom,
        CustomType = IsExpression ? "custom" : Definition,
        Expression = IsExpression ? Definition : null,
        Duration = Duration,
        SoundPath = SoundPath,
    };

}
