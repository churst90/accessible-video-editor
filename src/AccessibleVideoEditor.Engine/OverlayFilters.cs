using System.Globalization;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// Composites the overlay tracks onto the joined programme: titles and cards,
/// and the music bed underneath.
///
/// Overlays are applied in one pass over the finished picture rather than
/// per segment, because an overlay can span several segments - a lower third
/// that survives a cut is the normal case, not an exception.
/// </summary>
public static class OverlayFilters
{
    /// <summary>
    /// The video filter chain that draws every text layer of every card and
    /// title, each switched on only for the stretch it is meant to be visible.
    /// Returns null when there is nothing to draw.
    /// </summary>
    public static string? Video(Project project, TimelineMap map, int width, int height, string fontPath)
    {
        var steps = new List<string>();

        foreach (var item in project.Overlays.Where(o => o.Enabled && !o.Hidden))
        {
            var start = map.ResolveAnchor(item.Start);
            if (start is null) continue;

            var end = item.End is { } anchor ? map.ResolveAnchor(anchor) : start + (item.Length ?? 0);
            if (end is null || end <= start) continue;

            switch (item)
            {
                case CardItem card:
                    steps.AddRange(DrawCard(card.Composition, start.Value, end.Value, width, height, fontPath));
                    break;

                case TitleItem title:
                    steps.Add(DrawText(
                        title.Text,
                        title.Placement,
                        SizeFor(title.Style, height),
                        start.Value,
                        end.Value,
                        width,
                        height,
                        fontPath));
                    break;
            }
        }

        return steps.Count == 0 ? null : string.Join(',', steps);
    }

    /// <summary>
    /// A card over a still picture.
    ///
    /// The same drawing the timeline uses, with the time window opened wide
    /// enough to cover a single frame - so a lower third looks the same over a
    /// photograph as it does over a clip, and there is one card renderer rather
    /// than two that can disagree.
    /// </summary>
    public static IReadOnlyList<string> Card(
        CardComposition composition,
        int width,
        int height,
        string fontPath) =>
        DrawCard(composition, 0, 1e9, width, height, fontPath).ToList();

    private static IEnumerable<string> DrawCard(
        CardComposition card,
        double start,
        double end,
        int width,
        int height,
        string fontPath)
    {
        foreach (var resolved in card.Resolve())
        {
            if (resolved.Layer is not TextLayer text || text.Text.Length == 0) continue;

            yield return DrawTextAt(
                text.Text,
                resolved.X,
                resolved.Y,
                (int)(height * text.NominalHeight * 0.72),
                start,
                end,
                fontPath,
                text.Bold);
        }
    }

    private static string DrawText(
        string text,
        Placement placement,
        int size,
        double start,
        double end,
        int width,
        int height,
        string fontPath)
    {
        var (x, y) = placement.Resolve();
        return DrawTextAt(text, x, y, size, start, end, fontPath, bold: true);
    }

    /// <summary>
    /// One <c>drawtext</c>. Text is centred on its point, given a heavy outline
    /// and a shadow so it stays legible over any picture - which matters more
    /// here than anywhere, since nobody is going to look at the result and
    /// notice white text on a white wall.
    /// </summary>
    private static string DrawTextAt(
        string text,
        double x,
        double y,
        int size,
        double start,
        double end,
        string fontPath,
        bool bold)
    {
        var escaped = Escape(text);

        return $"drawtext=fontfile='{fontPath}'"
               + $":text='{escaped}'"
               + $":fontsize={Math.Max(12, size)}"
               + ":fontcolor=white"
               + $":borderw={(bold ? 4 : 3)}:bordercolor=black@0.85"
               + ":shadowx=2:shadowy=2:shadowcolor=black@0.6"
               + $":x=(w*{Number(x)})-(text_w/2)"
               + $":y=(h*{Number(y)})-(text_h/2)"
               + $":enable='between(t,{Number(start)},{Number(end)})'";
    }

    private static int SizeFor(TitleStyle style, int height) => style switch
    {
        TitleStyle.Full => (int)(height * 0.12),
        TitleStyle.Corner => (int)(height * 0.045),
        _ => (int)(height * 0.07),
    };

    /// <summary>
    /// drawtext takes its text through two levels of parsing, so colons,
    /// backslashes, quotes and percent signs all have to be escaped or the
    /// whole filter graph fails to parse.
    /// </summary>
    public static string Escape(string text) =>
        text.Replace(@"\", @"\\")
            .Replace(":", @"\:")
            .Replace("'", @"\'")
            .Replace("%", @"\%")
            .Replace("\n", " ");

    /// <summary>
    /// The music bed, ducked under the programme. Returns null when there is no
    /// music, so the simple case stays a simple command.
    /// </summary>
    public static MusicMix? Music(Project project, TimelineMap map)
    {
        var music = project.Overlays.OfType<MusicItem>().FirstOrDefault(m => m.Enabled && !m.Muted);
        if (music is null) return null;

        var source = project.SourceOf(music.Source);
        if (source is null) return null;

        var filter =
            $"[1:a]volume={Number(music.GainDb)}dB,afade=t=in:st=0:d={Number(music.FadeIn)}[bed];"
            + (music.DuckDb > 0
                ? $"[bed][0:a]sidechaincompress=threshold=0.05:ratio={Number(1 + music.DuckDb / 3)}"
                  + ":attack=20:release=400[ducked];[0:a][ducked]amix=inputs=2:normalize=0[aout]"
                : "[0:a][bed]amix=inputs=2:normalize=0[aout]");

        return new MusicMix(source.Path, filter);
    }

    internal static string Number(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}

public sealed record MusicMix(string Path, string Filter);
