using System.Security.Cryptography;
using System.Text;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// Content-addressed segment cache. Changing one line re-renders one segment.
///
/// The key deliberately covers only what affects the pixels and samples of that
/// one segment - not its position in the timeline. Reordering the video
/// therefore costs nothing, which is what makes structural editing cheap enough
/// to do by ear.
/// </summary>
public sealed class RenderCache(string directory)
{
    public string Directory { get; } = directory;

    public string KeyFor(Project project, PlacedElement placed, RenderQuality quality)
    {
        var builder = new StringBuilder();
        builder.Append(quality).Append('|');
        builder.Append(project.Settings.CanvasWidth).Append('x').Append(project.Settings.CanvasHeight);
        builder.Append('@').Append(project.Settings.Fps).Append('|');

        if (placed.Media is { } media)
        {
            var source = project.SourceOf(media.Source);
            builder.Append(source?.Path ?? media.Source.ToString()).Append('|');
            builder.Append(media.In.ToString("0.####")).Append('|');
            builder.Append(media.Out.ToString("0.####")).Append('|');
        }

        switch (placed.Element)
        {
            case ClipElement clip:
                builder.Append("atrack=").Append(clip.AudioTrack)
                       .Append(";gain=").Append(clip.GainDb)
                       .Append(";fit=").Append(clip.Fit);
                break;

            case HoleElement hole:
                builder.Append("hole;").Append(hole.Note);
                break;

            case PauseElement:
                builder.Append("pause");
                break;
        }

        // Overlays burn into the segment, so they belong in the key.
        foreach (var overlay in project.Overlays
                     .Where(o => o.Enabled && o.Start.Element == placed.Element.Id)
                     .OrderBy(o => o.Id.ToString(), StringComparer.Ordinal))
        {
            builder.Append('|').Append(overlay.Describe());
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    public string PathFor(string key) => Path.Combine(Directory, key + ".mkv");

    public bool Has(string key) => File.Exists(PathFor(key));

    /// <summary>Drops everything not referenced by the current timeline.</summary>
    public int Sweep(IReadOnlySet<string> liveKeys)
    {
        if (!System.IO.Directory.Exists(Directory)) return 0;

        var removed = 0;

        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.mkv"))
        {
            if (liveKeys.Contains(Path.GetFileNameWithoutExtension(file))) continue;

            File.Delete(file);
            removed++;
        }

        return removed;
    }
}
