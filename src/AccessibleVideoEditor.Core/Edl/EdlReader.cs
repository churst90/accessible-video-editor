using System.Globalization;
using System.Text.RegularExpressions;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Edl;

/// <summary>
/// Parses <c>edit.md</c> back into a project.
///
/// Import matters more than export. If it only went one way, hand-editing in
/// pluma and the Claude skill would both become dead ends the moment the GUI
/// touched a project. Passing the existing project to
/// <see cref="Read(string, Project?)"/> matches spans by source and timestamp
/// so <b>stable IDs survive the round trip</b> - which is what lets overlays,
/// markers and undo history stay attached to elements the text file cannot name.
/// </summary>
public static partial class EdlReader
{
    public static Project Read(string text, Project? existing = null)
    {
        var project = Project.CreateDefault(existing?.Name ?? "Untitled");
        if (existing is not null)
        {
            project.Settings = existing.Settings;
            project.Tracks = existing.Tracks;
            project.Sources = existing.Sources;
        }

        SourceId? currentSource = null;
        Transition? pendingTransition = null;
        var pendingDirectives = new List<(string Directive, Dictionary<string, string> Args, string Quoted)>();

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//")) continue;

            if (line.StartsWith("# "))
            {
                project.Name = line[2..].Trim();
                continue;
            }

            if (line.StartsWith("## source:"))
            {
                currentSource = ResolveSource(project, line["## source:".Length..].Trim());
                continue;
            }

            var enabled = true;
            if (line.StartsWith("x ", StringComparison.Ordinal))
            {
                enabled = false;
                line = line[2..].Trim();
            }

            if (SpanPattern().Match(line) is { Success: true } span)
            {
                if (currentSource is null) continue;

                var element = MatchExisting(existing, currentSource.Value, span) ?? new SpanElement
                {
                    Id = Ids.NewElement(),
                    Source = currentSource.Value,
                    SourceIn = Timecode.Parse(span.Groups["in"].Value),
                    SourceOut = Timecode.Parse(span.Groups["out"].Value),
                };

                element.SourceIn = Timecode.Parse(span.Groups["in"].Value);
                element.SourceOut = Timecode.Parse(span.Groups["out"].Value);
                element.Text = span.Groups["text"].Value.Trim();
                element.Enabled = enabled;
                element.TransitionIn = pendingTransition;
                pendingTransition = null;

                project.Spine.Add(element);
                AttachPending(project, pendingDirectives, element);
                continue;
            }

            if (!line.StartsWith('!')) continue;

            var (directive, args, quoted) = ParseDirective(line);

            switch (directive)
            {
                case "cut":
                    pendingTransition = Transition.Cut;
                    break;

                case "xfade":
                    pendingTransition = new Transition
                    {
                        Type = TransitionType.Custom,
                        CustomType = args.GetValueOrDefault("type", "fade"),
                        Duration = Num(args, "dur", 0.4),
                    };
                    break;

                case "clip":
                {
                    var source = ResolveSource(project, args.GetValueOrDefault("file", quoted));
                    var from = Num(args, "from", 0);
                    var to = args.TryGetValue("to", out var toText)
                        ? Timecode.Parse(toText)
                        : from + Num(args, "dur", 0);

                    var clip = new ClipElement
                    {
                        Id = Ids.NewElement(),
                        Source = source,
                        SourceIn = from,
                        SourceOut = to,
                        AudioTrack = (int)Num(args, "atrack", 0),
                        GainDb = Num(args, "gain", 0),
                        Enabled = enabled,
                        TransitionIn = pendingTransition,
                    };

                    pendingTransition = null;
                    project.Spine.Add(clip);
                    AttachPending(project, pendingDirectives, clip);
                    break;
                }

                case "hole":
                    project.Spine.Add(new HoleElement
                    {
                        Id = Ids.NewElement(),
                        Length = Num(args, "dur", 5),
                        Note = quoted,
                        Enabled = enabled,
                        TransitionIn = pendingTransition,
                    });
                    pendingTransition = null;
                    break;

                case "pause":
                    project.Spine.Add(new PauseElement
                    {
                        Id = Ids.NewElement(),
                        Length = Num(args, "dur", 0.8),
                        Enabled = enabled,
                        TransitionIn = pendingTransition,
                    });
                    pendingTransition = null;
                    break;

                case "music":
                {
                    var track = project.Tracks.First(t => t.Kind == TrackKind.Audio);
                    project.Overlays.Add(new MusicItem
                    {
                        Id = Ids.NewItem(),
                        Track = track.Id,
                        Source = ResolveSource(project, args.GetValueOrDefault("file", quoted)),
                        // Anchored once the spine exists - see below.
                        Start = default,
                        GainDb = Num(args, "gain", -20),
                        DuckDb = Num(args, "duck", 9),
                        FadeIn = Num(args, "fadein", 2),
                        FadeOut = Num(args, "fadeout", 4),
                    });
                    break;
                }

                // Overlays attach to the element that follows them, exactly as in
                // the text format, so they are held until that element appears.
                default:
                    pendingDirectives.Add((directive, args, quoted));
                    break;
            }
        }

        AnchorProjectWideItems(project);
        return project;
    }

    /// <summary>
    /// A music bed is declared before the spine it covers, so it is anchored
    /// afterwards - to the first and last elements, never to absolute time. An
    /// unanchored item would be invisible to every ripple and silently drift.
    /// </summary>
    private static void AnchorProjectWideItems(Project project)
    {
        if (project.Spine.Count == 0) return;

        var first = project.Spine[0];
        var last = project.Spine[^1];

        foreach (var item in project.Overlays.Where(o => o.Start.Element.IsUnset))
        {
            item.Start = new TimeAnchor(first.Id);
            item.End = new TimeAnchor(last.Id, last.Duration);
            item.Length = null;
        }
    }

    public static async Task<Project> ReadFileAsync(string path, Project? existing = null,
        CancellationToken cancellationToken = default) =>
        Read(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), existing);

    /// <summary>
    /// Reuses the ID of the span that covers the same moment of the same take.
    /// Timestamps are compared loosely because a hand edit may nudge them.
    /// </summary>
    private static SpanElement? MatchExisting(Project? existing, SourceId source, Match span)
    {
        if (existing is null) return null;

        var inPoint = Timecode.Parse(span.Groups["in"].Value);

        return existing.Spine
            .OfType<SpanElement>()
            .FirstOrDefault(s => s.Source == source && Math.Abs(s.SourceIn - inPoint) < 0.25);
    }

    private static void AttachPending(
        Project project,
        List<(string Directive, Dictionary<string, string> Args, string Quoted)> pending,
        SpineElement element)
    {
        foreach (var (directive, args, quoted) in pending)
        {
            switch (directive)
            {
                case "broll":
                    project.Overlays.Add(new BrollItem
                    {
                        Id = Ids.NewItem(),
                        Track = project.Tracks.First(t => t.Kind == TrackKind.Overlay).Id,
                        Source = ResolveSource(project, args.GetValueOrDefault("file", quoted)),
                        SourceIn = Num(args, "from", 0),
                        GainDb = args.GetValueOrDefault("gain") is { } gain && gain != "mute"
                            ? double.Parse(gain, CultureInfo.InvariantCulture)
                            : null,
                        AudioTrack = (int)Num(args, "atrack", 0),
                        Start = new TimeAnchor(element.Id),
                        End = new TimeAnchor(element.Id, element.Duration),
                    });
                    break;

                case "title":
                    project.Overlays.Add(new TitleItem
                    {
                        Id = Ids.NewItem(),
                        Track = project.Tracks.First(t => t.Kind == TrackKind.Graphics).Id,
                        Text = quoted,
                        Style = Enum.TryParse<TitleStyle>(
                            args.GetValueOrDefault("style", "lowerthird").Replace("-", string.Empty),
                            ignoreCase: true, out var style)
                            ? style
                            : TitleStyle.LowerThird,
                        Placement = new Placement((int)Num(args, "cell", 2), (int)Num(args, "sub", 0)),
                        Start = new TimeAnchor(element.Id, Num(args, "delay", 0)),
                        Length = Num(args, "dur", 3),
                    });
                    break;

                case "graphic":
                    project.Overlays.Add(new GraphicItem
                    {
                        Id = Ids.NewItem(),
                        Track = project.Tracks.First(t => t.Kind == TrackKind.Graphics).Id,
                        Source = ResolveSource(project, args.GetValueOrDefault("file", quoted)),
                        Placement = new Placement((int)Num(args, "cell", 5), (int)Num(args, "sub", 0)),
                        Scale = Num(args, "scale", 0.25),
                        Start = new TimeAnchor(element.Id, Num(args, "delay", 0)),
                        Length = Num(args, "dur", 3),
                    });
                    break;
            }
        }

        pending.Clear();
    }

    private static SourceId ResolveSource(Project project, string path)
    {
        path = path.Trim().Trim('"');

        var existing = project.Sources.FirstOrDefault(s =>
            string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));

        if (existing is not null) return existing.Id;

        var source = new Source { Id = Ids.NewSource(), Path = path };
        project.Sources.Add(source);
        return source.Id;
    }

    private static (string Directive, Dictionary<string, string> Args, string Quoted) ParseDirective(string line)
    {
        var body = line[1..];
        var quoted = QuotedPattern().Match(body) is { Success: true } q ? q.Groups[1].Value : string.Empty;
        body = QuotedPattern().Replace(body, " ");

        var tokens = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var directive = tokens.Length > 0 ? tokens[0].ToLowerInvariant() : string.Empty;

        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var positional = 0;

        foreach (var token in tokens.Skip(1))
        {
            var split = token.IndexOf('=');
            if (split > 0)
            {
                args[token[..split]] = token[(split + 1)..];
            }
            else if (positional++ == 0)
            {
                args["file"] = token;
            }
        }

        return (directive, args, quoted);
    }

    private static double Num(Dictionary<string, string> args, string key, double fallback) =>
        args.TryGetValue(key, out var text) && Timecode.TryParse(text, out var value) ? value : fallback;

    [GeneratedRegex(@"^\[(?<in>[^\]\->]+)->\s*(?<out>[^\]]+)\]\s*(?<text>.*)$")]
    private static partial Regex SpanPattern();

    [GeneratedRegex("\"([^\"]*)\"")]
    private static partial Regex QuotedPattern();
}
