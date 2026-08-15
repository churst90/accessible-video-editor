using System.Globalization;
using System.Text;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Edl;

/// <summary>
/// Exports the project as <c>edit.md</c>, the line-oriented format the CLI and
/// the Claude skill already speak.
///
/// The export is written on every save. It is not a backup - it is a live
/// second face of the document, so pluma stays a working escape hatch and the
/// project remains diffable in git.
/// </summary>
public static class EdlWriter
{
    public static string Write(Project project)
    {
        var builder = new StringBuilder();
        var map = TimelineMap.Build(project);

        builder.AppendLine($"# {project.Name}");
        builder.AppendLine();
        builder.AppendLine("// Exported by AccessibleVideoEditor. project.json is canonical; edits here are");
        builder.AppendLine("// reconciled back in on the next open.");
        builder.AppendLine();

        foreach (var music in project.Overlays.OfType<MusicItem>().Where(m => m.Enabled))
        {
            var path = PathOf(project, music.Source);
            builder.AppendLine(
                $"!music {path} gain={Number(music.GainDb)} duck={Number(music.DuckDb)} " +
                $"fadein={Number(music.FadeIn)} fadeout={Number(music.FadeOut)}");
        }

        builder.AppendLine();

        SourceId? currentSource = null;

        foreach (var element in project.Spine)
        {
            var source = SourceOf(element);
            if (source is not null && source != currentSource)
            {
                currentSource = source;
                builder.AppendLine();
                builder.AppendLine($"## source: {PathOf(project, source.Value)}");
                builder.AppendLine();
            }

            foreach (var line in DirectivesAnchoredTo(project, element))
            {
                builder.AppendLine(line);
            }

            if (element.TransitionIn is { } transition)
            {
                builder.AppendLine(transition.Type == TransitionType.Cut
                    ? "!cut"
                    : $"!xfade dur={Number(transition.Duration)} type={transition.FfmpegName}");
            }

            builder.AppendLine(LineFor(project, element));
        }

        return builder.ToString();
    }

    public static async Task ExportAsync(Project project, string directory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "edit.md"), Write(project), cancellationToken)
            .ConfigureAwait(false);
    }

    private static string LineFor(Project project, SpineElement element)
    {
        var prefix = element.Enabled ? string.Empty : "x ";

        return element switch
        {
            SpanElement span =>
                $"{prefix}[{Timecode.Format(span.SourceIn)} -> {Timecode.Format(span.SourceOut)}] {span.Text}",

            ClipElement clip =>
                $"{prefix}!clip {PathOf(project, clip.Source)} from={Timecode.Format(clip.SourceIn)} " +
                $"to={Timecode.Format(clip.SourceOut)} atrack={clip.AudioTrack}" +
                (clip.GainDb != 0 ? $" gain={Number(clip.GainDb)}" : string.Empty),

            HoleElement hole =>
                $"{prefix}!hole dur={Number(hole.Length)}" +
                (hole.Note.Length > 0 ? $" \"{hole.Note}\"" : string.Empty),

            PauseElement pause => $"{prefix}!pause dur={Number(pause.Length)}",

            _ => $"// unsupported element {element.Id}",
        };
    }

    private static IEnumerable<string> DirectivesAnchoredTo(Project project, SpineElement element)
    {
        foreach (var item in project.Overlays.Where(o => o.Start.Element == element.Id && o.Enabled))
        {
            switch (item)
            {
                case BrollItem broll:
                    yield return
                        $"!broll {PathOf(project, broll.Source)} from={Timecode.Format(broll.SourceIn)}" +
                        $" gain={(broll.GainDb is { } g ? Number(g) : "mute")}" +
                        $" fit={broll.Fit.ToString().ToLowerInvariant()}";
                    break;

                case TitleItem title:
                    yield return
                        $"!title \"{title.Text}\" style={title.Style.ToString().ToLowerInvariant()}" +
                        $" cell={title.Placement.Cell}" +
                        (item.Length is { } len ? $" dur={Number(len)}" : string.Empty);
                    break;

                case GraphicItem graphic:
                    yield return
                        $"!graphic {PathOf(project, graphic.Source)} cell={graphic.Placement.Cell}" +
                        $" sub={graphic.Placement.SubCell} scale={Number(graphic.Scale)}" +
                        (item.Length is { } graphicLength ? $" dur={Number(graphicLength)}" : string.Empty);
                    break;
            }
        }
    }

    private static SourceId? SourceOf(SpineElement element) => element switch
    {
        SpanElement span => span.Source,
        ClipElement clip => clip.Source,
        _ => null,
    };

    private static string PathOf(Project project, SourceId id) =>
        project.SourceOf(id)?.Path ?? id.ToString();

    private static string Number(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
