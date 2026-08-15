using System.Diagnostics;
using System.Text;
using AccessibleVideoEditor.Core;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// Renders a project through ffmpeg, one segment at a time.
///
/// Segments are rendered separately into a content-addressed cache and then
/// joined. That is slower for a first render and enormously faster for every
/// one after it: changing a line re-renders that line, and reordering the video
/// re-renders nothing at all.
/// </summary>
public sealed class FfmpegRenderEngine(string ffmpegPath = "ffmpeg") : IRenderEngine
{
    public async Task<RenderOutput> RenderAsync(
        Project project,
        RenderQuality quality,
        IProgress<RenderProgress>? progress = null,
        CancellationToken ct = default)
    {
        var root = project.RootPath ?? Directory.GetCurrentDirectory();
        var map = TimelineMap.Build(project);

        if (map.Elements.Count == 0) throw new InvalidOperationException("There is nothing to render.");

        // Holes are a deliberate to-do, so a master render refuses while any
        // remain. A draft renders them as black, which is the point of a draft.
        if (quality == RenderQuality.Master && project.Holes.Any())
        {
            var holes = project.Holes.Count();
            throw new InvalidOperationException(
                $"{holes} hole{(holes == 1 ? "" : "s")} still to fill. A draft will render them as black.");
        }

        var cache = new RenderCache(Path.Combine(root, "work", "cache", quality.ToString().ToLowerInvariant()));
        Directory.CreateDirectory(cache.Directory);

        var pieces = new List<string>();
        var done = 0;

        foreach (var placed in map.Elements)
        {
            ct.ThrowIfCancellationRequested();

            var key = cache.KeyFor(project, placed, quality);
            var path = cache.PathFor(key);

            if (!cache.Has(key))
            {
                var source = placed.Media is { } media ? project.SourceOf(media.Source) : null;
                var sourcePath = source is null
                    ? string.Empty
                    : Path.IsPathRooted(source.Path) ? source.Path : Path.Combine(root, source.Path);

                var arguments = SegmentFilters.Build(project, placed, quality, sourcePath, path);
                await RunAsync(arguments, ct).ConfigureAwait(false);
            }

            pieces.Add(path);
            done++;

            progress?.Report(new RenderProgress(
                "rendering segments", (double)done / map.Elements.Count, done, map.Elements.Count));
        }

        progress?.Report(new RenderProgress("joining", 0.85, done, map.Elements.Count));

        var outputDirectory = Path.Combine(root, "out");
        Directory.CreateDirectory(outputDirectory);

        var outputPath = Path.Combine(
            outputDirectory,
            quality == RenderQuality.Draft ? "draft.mp4" : "master.mp4");

        var runs = RenderPlan.Runs(project, map);
        var joined = Path.Combine(cache.Directory, "joined.mkv");

        if (runs.Count == 1)
        {
            // No transitions: a straight concatenation, which needs no
            // re-encoding at all.
            await ConcatAsync(pieces, joined, RenderQuality.Draft, ct).ConfigureAwait(false);
        }
        else
        {
            await JoinWithTransitionsAsync(runs, pieces, cache, joined, ct).ConfigureAwait(false);
        }

        progress?.Report(new RenderProgress("overlays", 0.92, done, map.Elements.Count));

        await FinishAsync(project, map, joined, outputPath, quality, ct).ConfigureAwait(false);

        if (quality == RenderQuality.Master)
        {
            await Captions.WriteAsync(
                project, map, Path.Combine(outputDirectory, "captions.srt"), ct).ConfigureAwait(false);
        }

        progress?.Report(new RenderProgress("done", 1, done, map.Elements.Count));

        return new RenderOutput(outputPath, map.Duration, quality);
    }

    /// <summary>
    /// Joins runs of segments with a crossfade between each pair.
    ///
    /// Each run is concatenated first, so only the joins that actually have a
    /// transition are re-encoded. The offsets accumulate: xfade overlaps its two
    /// inputs, so every transition shortens what follows it, and getting that
    /// arithmetic wrong makes every later transition drift.
    /// </summary>
    private async Task JoinWithTransitionsAsync(
        IReadOnlyList<RenderRun> runs,
        IReadOnlyList<string> pieces,
        RenderCache cache,
        string output,
        CancellationToken ct)
    {
        var runFiles = new List<string>();
        var taken = 0;

        for (var i = 0; i < runs.Count; i++)
        {
            var slice = pieces.Skip(taken).Take(runs[i].Segments.Count).ToList();
            taken += runs[i].Segments.Count;

            if (slice.Count == 1)
            {
                runFiles.Add(slice[0]);
                continue;
            }

            var runPath = Path.Combine(cache.Directory, $"run-{i}.mkv");
            await ConcatAsync(slice, runPath, RenderQuality.Draft, ct).ConfigureAwait(false);
            runFiles.Add(runPath);
        }

        var arguments = new List<string> { "-y", "-hide_banner", "-loglevel", "error" };

        foreach (var file in runFiles) arguments.AddRange(["-i", file]);

        var graph = new StringBuilder();
        var video = "[0:v]";
        var audio = "[0:a]";
        var accumulated = runs[0].Duration;

        // Where each transition sound lands, gathered on the way through so the
        // sounds can be mixed in once the joins are laid out.
        var sounds = new List<(string Path, double At, double GainDb)>();

        for (var i = 1; i < runs.Count; i++)
        {
            var transition = runs[i].LeadIn ?? new Transition { Type = TransitionType.Fade, Duration = 0.4 };
            var seconds = Math.Max(0.04, transition.Duration);
            var offset = RenderPlan.OffsetFor(accumulated, seconds);

            graph.Append(video).Append('[').Append(i).Append(":v]")
                 .Append($"xfade=transition={transition.FfmpegName}")
                 .Append($":duration={SegmentFilters.Number(seconds)}")
                 .Append($":offset={SegmentFilters.Number(offset)}")
                 .Append(transition.Expression is { Length: > 0 } expression
                     ? $":expr='{expression}'"
                     : string.Empty)
                 .Append($"[v{i}];");

            graph.Append(audio).Append('[').Append(i).Append(":a]")
                 .Append($"acrossfade=d={SegmentFilters.Number(seconds)}")
                 .Append($"[a{i}];");

            video = $"[v{i}]";
            audio = $"[a{i}]";

            if (transition.HasSound)
            {
                // Landed on the start of the overlap rather than the midpoint:
                // a whoosh reads as leading the cut, not following it.
                sounds.Add((transition.SoundPath!, offset, transition.SoundGainDb));
            }

            accumulated += runs[i].Duration - seconds;
        }

        audio = MixTransitionSounds(graph, arguments, sounds, audio, runFiles.Count);

        arguments.AddRange([
            "-filter_complex", graph.ToString().TrimEnd(';'),
            "-map", video, "-map", audio,
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "192k",
            output,
        ]);

        await RunAsync(arguments, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The last pass: overlays drawn onto the joined picture, music mixed
    /// underneath, and the master normalised for loudness.
    /// </summary>
    private async Task FinishAsync(
        Project project,
        TimelineMap map,
        string joined,
        string output,
        RenderQuality quality,
        CancellationToken ct)
    {
        var height = quality == RenderQuality.Draft ? 540 : project.Settings.CanvasHeight;
        var width = quality == RenderQuality.Draft
            ? (int)Math.Round(height * (double)project.Settings.CanvasWidth
                              / project.Settings.CanvasHeight / 2) * 2
            : project.Settings.CanvasWidth;

        var overlays = OverlayFilters.Video(project, map, width, height, FontPath());
        var music = OverlayFilters.Music(project, map);

        var loudness = quality == RenderQuality.Master ? "loudnorm=I=-14:TP=-1.5:LRA=11" : null;

        // Nothing to add: the joined file is already the answer.
        if (overlays is null && music is null && loudness is null)
        {
            File.Copy(joined, output, overwrite: true);
            return;
        }

        var arguments = new List<string> { "-y", "-hide_banner", "-loglevel", "error", "-i", joined };

        if (music is not null)
        {
            var path = Path.IsPathRooted(music.Path) || project.RootPath is null
                ? music.Path
                : Path.Combine(project.RootPath, music.Path);

            arguments.AddRange(["-stream_loop", "-1", "-i", path]);
        }

        if (music is not null)
        {
            var graph = music.Filter;
            if (overlays is not null) graph = $"[0:v]{overlays}[vout];{graph}";

            arguments.AddRange(["-filter_complex", graph]);
            arguments.AddRange(["-map", overlays is not null ? "[vout]" : "0:v", "-map", "[aout]"]);
        }
        else if (overlays is not null)
        {
            arguments.AddRange(["-vf", overlays]);
        }

        if (loudness is not null && music is null) arguments.AddRange(["-af", loudness]);

        arguments.AddRange([
            "-c:v", "libx264",
            "-preset", quality == RenderQuality.Draft ? "veryfast" : "medium",
            "-crf", quality == RenderQuality.Draft ? "26" : "18",
            "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "192k",
            "-shortest",
            output,
        ]);

        await RunAsync(arguments, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Mixes the transition sounds into the joined audio.
    ///
    /// Each is delayed to its own boundary and added to the mix. The programme
    /// is <b>not</b> ducked under them unless the transition says to: automatic
    /// ducking is a mix decision made on your behalf, and the track faders are
    /// there to make it deliberately.
    /// </summary>
    private static string MixTransitionSounds(
        StringBuilder graph,
        List<string> arguments,
        IReadOnlyList<(string Path, double At, double GainDb)> sounds,
        string audio,
        int firstInput)
    {
        if (sounds.Count == 0) return audio;

        var labels = new List<string> { audio };

        for (var i = 0; i < sounds.Count; i++)
        {
            var (path, at, gain) = sounds[i];

            arguments.AddRange(["-i", path]);

            var input = firstInput + i;
            var delay = (int)Math.Round(Math.Max(0, at) * 1000);

            graph.Append($"[{input}:a]volume={SegmentFilters.Number(gain)}dB")
                 .Append($",adelay={delay}|{delay}")
                 .Append($"[s{i}];");

            labels.Add($"[s{i}]");
        }

        // normalize=0 keeps the programme at its own level; normalising here
        // would quietly duck everything every time a sound was added.
        graph.Append(string.Join(string.Empty, labels))
             .Append($"amix=inputs={labels.Count}:normalize=0:duration=first[amix];");

        return "[amix]";
    }

    /// <summary>A font that exists, so drawtext cannot fail for want of one.</summary>
    private static string FontPath() => Fonts.Path();

    /// <summary>
    /// Joins the rendered segments. The concat demuxer is used rather than the
    /// filter because every segment was normalised to the same size, frame rate
    /// and audio layout on the way out - so they can be stitched without
    /// re-encoding on a draft.
    /// </summary>
    private async Task ConcatAsync(
        IReadOnlyList<string> pieces,
        string output,
        RenderQuality quality,
        CancellationToken ct)
    {
        var listPath = Path.Combine(Path.GetTempPath(), $"videoedit-concat-{Guid.NewGuid():N}.txt");

        var list = new StringBuilder();
        foreach (var piece in pieces)
        {
            list.Append("file '").Append(piece.Replace("'", @"'\''")).Append("'\n");
        }

        await File.WriteAllTextAsync(listPath, list.ToString(), ct).ConfigureAwait(false);

        try
        {
            var arguments = new List<string>
            {
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "concat", "-safe", "0", "-i", listPath,
            };

            if (quality == RenderQuality.Draft)
            {
                arguments.AddRange(["-c", "copy"]);
            }
            else
            {
                // The master is normalised toward broadcast loudness on the way
                // out, so uploads do not get turned down for being quiet.
                arguments.AddRange([
                    "-c:v", "libx264", "-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p",
                    "-af", "loudnorm=I=-14:TP=-1.5:LRA=11",
                    "-c:a", "aac", "-b:a", "192k",
                ]);
            }

            arguments.Add(output);

            await RunAsync(arguments, ct).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(listPath)) File.Delete(listPath);
        }
    }

    public async Task<RenderOutput> RenderAudioAsync(Project project, CancellationToken ct = default)
    {
        var render = await RenderAsync(project, RenderQuality.Draft, null, ct).ConfigureAwait(false);
        var audioPath = Path.ChangeExtension(render.Path, ".m4a");

        await RunAsync(
            ["-y", "-hide_banner", "-loglevel", "error", "-i", render.Path, "-vn", "-c:a", "aac", audioPath],
            ct).ConfigureAwait(false);

        return render with { Path = audioPath };
    }

    public async Task<IReadOnlyList<ExtractedFrame>> ExtractFramesAsync(
        Project project,
        RenderOutput draft,
        CancellationToken ct = default)
    {
        var root = project.RootPath ?? Directory.GetCurrentDirectory();
        var directory = Path.Combine(root, "review", "frames");
        Directory.CreateDirectory(directory);

        var map = TimelineMap.Build(project);
        var frames = new List<ExtractedFrame>();

        foreach (var placed in map.Elements)
        {
            // A frame from the middle of a segment, not its first: the first is
            // often mid-transition and shows nothing useful.
            var at = placed.ProgrammeStart + placed.Duration / 2;
            var path = Path.Combine(directory, $"{SegmentFilters.Number(at)}.jpg");

            await RunAsync(
                [
                    "-y", "-hide_banner", "-loglevel", "error",
                    "-ss", SegmentFilters.Number(at), "-i", draft.Path,
                    "-frames:v", "1", "-q:v", "3", path,
                ],
                ct).ConfigureAwait(false);

            frames.Add(new ExtractedFrame(path, at, placed.Element.Id, placed.Element.Describe()));
        }

        return frames;
    }

    private async Task RunAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var info = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException("Could not start ffmpeg.");

        var stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var detail = stderr.Split('\n').LastOrDefault(line => line.Trim().Length > 0)?.Trim();
            throw new InvalidOperationException($"ffmpeg failed: {detail ?? "no detail"}");
        }
    }
}
