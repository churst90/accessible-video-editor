using AccessibleVideoEditor.Core;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// Bringing a file into a project: probe it, classify it, name its audio
/// tracks, and report what was found.
///
/// The report is the point. Importing silently is fine when you can see a
/// thumbnail appear; here the import has to say what it got - resolution,
/// length, how many audio tracks and what they are - because that is the only
/// chance to notice you grabbed the wrong take.
/// </summary>
public sealed class MediaImporter(FfmpegProbe? probe = null)
{
    private static readonly string[] VideoExtensions =
        [".mkv", ".mp4", ".mov", ".webm", ".avi", ".m4v"];

    private static readonly string[] AudioExtensions =
        [".wav", ".flac", ".mp3", ".m4a", ".ogg", ".opus", ".aac"];

    private static readonly string[] ImageExtensions =
        [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif"];

    private readonly FfmpegProbe _probe = probe ?? new FfmpegProbe();

    public static SourceKind ClassifyByExtension(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();

        if (VideoExtensions.Contains(extension)) return SourceKind.Video;
        if (AudioExtensions.Contains(extension)) return SourceKind.Audio;
        if (ImageExtensions.Contains(extension)) return SourceKind.Image;

        return SourceKind.Video;
    }

    /// <summary>The filter list for the open dialog, so one list drives both.</summary>
    public static IReadOnlyList<string> SupportedExtensions =>
        [.. VideoExtensions, .. AudioExtensions, .. ImageExtensions];

    public async Task<ImportResult> ImportAsync(
        Project project,
        string path,
        CancellationToken ct = default)
    {
        if (!File.Exists(path))
        {
            return new ImportResult(null, $"{Path.GetFileName(path)} does not exist");
        }

        var relative = project.RootPath is { } root
            ? Path.GetRelativePath(root, path)
            : path;

        if (project.Sources.FirstOrDefault(s => s.Path == relative) is { } already)
        {
            return new ImportResult(already, $"{Path.GetFileName(path)} is already in the project");
        }

        var kind = ClassifyByExtension(path);

        Source source;
        if (kind == SourceKind.Image)
        {
            // Images have no duration and ffprobe reports nonsense for them.
            source = new Source { Id = Ids.NewSource(), Path = relative, Kind = SourceKind.Image };
        }
        else
        {
            source = await _probe.ProbeAsync(path, ct: ct).ConfigureAwait(false);
            source.Path = relative;
            source.Kind = kind;
        }

        project.Sources.Add(source);
        return new ImportResult(source, Describe(source));
    }

    /// <summary>What the announcer says after an import.</summary>
    public static string Describe(Source source)
    {
        var name = Path.GetFileName(source.Path);

        return source.Kind switch
        {
            SourceKind.Image => $"{name}, image",

            SourceKind.Audio => $"{name}, audio, {Timecode.Speak(source.Duration)}",

            _ => $"{name}, {source.Width} by {source.Height}, {source.Fps:0.##} frames per second, " +
                 $"{Timecode.Speak(source.Duration)}, " +
                 $"{DescribeAudio(source)}",
        };
    }

    private static string DescribeAudio(Source source) => source.AudioTracks.Count switch
    {
        0 => "no audio",
        1 => "one audio track",
        _ => $"{source.AudioTracks.Count} audio tracks: " +
             string.Join(", ", source.AudioTracks.Select(t => t.Label ?? $"track {t.Index}")),
    };
}

public sealed record ImportResult(Source? Source, string Summary)
{
    public bool Succeeded => Source is not null;
}
