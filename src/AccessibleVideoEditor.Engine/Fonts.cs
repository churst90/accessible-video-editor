namespace AccessibleVideoEditor.Engine;

/// <summary>
/// A font that exists, so <c>drawtext</c> cannot fail for want of one.
///
/// Shared by the video renderer and the image editor rather than written twice:
/// the failure it prevents is a whole filter graph refusing to parse, and one
/// of the two copies drifting would mean text disappearing from renders and
/// nowhere else.
/// </summary>
public static class Fonts
{
    public static readonly string[] Candidates =
    [
        "/usr/share/fonts/liberation-fonts/LiberationSans-Regular.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/usr/share/fonts/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/noto/NotoSans-Regular.ttf",
    ];

    public static string Path() => Candidates.FirstOrDefault(File.Exists) ?? Candidates[0];

    public static bool Available => Candidates.Any(File.Exists);
}
