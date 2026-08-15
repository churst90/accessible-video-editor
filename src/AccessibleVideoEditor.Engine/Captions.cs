using System.Text;
using AccessibleVideoEditor.Core;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// Writes <c>captions.srt</c> from the timeline.
///
/// Captions come out of the edit rather than being made separately, which is
/// the whole reason for keeping the transcript alongside the cut: the words are
/// already there, already timed, already corrected. A video that ships without
/// captions because writing them was a separate chore is the normal failure,
/// and this removes the chore.
/// </summary>
public static class Captions
{
    /// <summary>
    /// One cue per captioned segment, in programme time. Segments with captions
    /// switched off, and those with nothing to say, are skipped rather than
    /// emitted empty - an empty cue shows as a flicker of nothing on screen.
    /// </summary>
    public static string Build(Project project, TimelineMap map)
    {
        var builder = new StringBuilder();
        var index = 1;

        foreach (var placed in map.Elements)
        {
            var text = placed.Element.EffectiveCaption;
            if (string.IsNullOrWhiteSpace(text)) continue;

            builder.Append(index++).Append('\n');
            builder
                .Append(Stamp(placed.ProgrammeStart))
                .Append(" --> ")
                .Append(Stamp(placed.ProgrammeEnd))
                .Append('\n');

            builder.Append(text.Trim()).Append('\n').Append('\n');
        }

        return builder.ToString();
    }

    public static async Task WriteAsync(
        Project project,
        TimelineMap map,
        string path,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        await File.WriteAllTextAsync(path, Build(project, map), ct).ConfigureAwait(false);
    }

    /// <summary>SRT wants a comma before the milliseconds, not a full stop.</summary>
    public static string Stamp(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));

        return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}";
    }
}
