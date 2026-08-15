using System.Diagnostics;
using System.Globalization;
using AccessibleVideoEditor.Core.Streaming;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// One encode, sent to every destination at once through ffmpeg's tee muxer.
/// Every service therefore gets the same picture, so the settings must satisfy
/// the strictest of them - see <see cref="EncoderSettings.ForTargets"/>.
/// <c>onfail=ignore</c> keeps the others alive when one drops.
/// </summary>
public sealed class StreamEncoder(string ffmpegPath = "ffmpeg")
{
    private Process? _process;

    public bool IsRunning => _process is { HasExited: false };

    /// <summary>
    /// Raised for every statistics line the encoder writes, on a background
    /// thread. This is where dropped frames and a falling bitrate come from -
    /// the encoder already knows, and nobody was listening.
    /// </summary>
    public event Action<StreamStats>? Progress;

    /// <summary>Raised when the encoder says something that means real trouble.</summary>
    public event Action<string>? Trouble;

    public static IReadOnlyList<string> BuildArguments(
        StreamSetup setup,
        Scene scene,
        EncoderSettings settings,
        Func<StreamSource, string>? devices = null)
    {
        var plan = SceneComposer.Build(setup, scene, settings, devices);

        var arguments = new List<string> { "-hide_banner", "-loglevel", "warning" };

        arguments.AddRange(plan.Inputs);

        arguments.AddRange([
            "-filter_complex", plan.FilterComplex,
            "-map", $"[{plan.VideoLabel}]",
            "-map", $"[{plan.AudioLabel}]",
        ]);

        var gop = Math.Max(1, (int)Math.Round(settings.Fps * settings.KeyframeSeconds));

        arguments.AddRange([
            "-c:v", "libx264",
            "-preset", "veryfast",
            "-tune", "zerolatency",
            "-pix_fmt", "yuv420p",
            "-b:v", $"{settings.VideoBitrateKbps}k",
            "-maxrate", $"{settings.VideoBitrateKbps}k",
            "-bufsize", $"{settings.VideoBitrateKbps * 2}k",
            "-g", gop.ToString(CultureInfo.InvariantCulture),
            "-keyint_min", gop.ToString(CultureInfo.InvariantCulture),
            "-sc_threshold", "0",
            "-r", SceneComposer.Num(settings.Fps),
            "-c:a", "aac",
            "-b:a", $"{settings.AudioBitrateKbps}k",
            "-ar", "48000", "-ac", "2",
        ]);

        var urls = setup.Targets
            .Where(t => t.Enabled && t.Url.Length > 0)
            .Select(t => t.Url)
            .ToList();

        var destinations = urls.Select(url => $"[f=flv:onfail=ignore]{url}").ToList();

        if (destinations.Count == 0)
        {
            // Nowhere to send it is not an error here - it is how the preview
            // runs, and it means "go live" and "look at yourself" are the same
            // pipeline rather than two that can disagree.
            arguments.AddRange(["-f", "null", "-"]);
        }
        else if (destinations.Count == 1)
        {
            // One destination does not need the tee muxer, and going through it
            // anyway would swallow the connection error that says why it failed.
            arguments.AddRange(["-f", "flv", urls[0]]);
        }
        else
        {
            arguments.AddRange(["-f", "tee", string.Join('|', destinations)]);
        }

        return arguments;
    }

    /// <summary>
    /// What is about to happen, in one sentence, before it happens. Never
    /// includes a stream key.
    /// </summary>
    public static string Describe(StreamSetup setup, EncoderSettings settings)
    {
        var live = setup.Targets.Where(t => t.Enabled && t.HasKey).Select(t => t.Name).ToList();

        var where = live.Count switch
        {
            0 => "nowhere; this is a local preview",
            1 => live[0],
            _ => string.Join(" and ", [string.Join(", ", live[..^1]), live[^1]]),
        };

        return $"going live to {where}. {settings.Describe()}";
    }

    public async Task<string> StartAsync(
        StreamSetup setup,
        Scene scene,
        EncoderSettings settings,
        Func<StreamSource, string>? devices = null,
        CancellationToken ct = default)
    {
        if (IsRunning) return "already streaming";

        var info = new ProcessStartInfo(ffmpegPath) { RedirectStandardError = true };

        foreach (var argument in BuildArguments(setup, scene, settings, devices))
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            _process = Process.Start(info);
            if (_process is null) return "could not start the encoder";

            // A failure to connect happens in the first second or two, so a
            // short wait here turns "it silently never started" into a spoken
            // reason.
            await Task.Delay(1500, ct).ConfigureAwait(false);

            if (_process.HasExited)
            {
                var error = await _process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
                _process = null;

                return $"the stream did not start: {FirstLine(error)}";
            }

            setup.IsLive = true;

            // Everything ffmpeg says about itself from here on is watched: the
            // statistics become earcons and the errors become sentences.
            _ = Task.Run(() => WatchAsync(_process), CancellationToken.None);

            return StreamEncoder.Describe(setup, settings);
        }
        catch (Exception exception)
        {
            _process = null;

            return $"the stream did not start: {exception.Message}";
        }
    }

    /// <summary>
    /// Stopping is not undoable and an audience notices, so this is only ever
    /// reached from a key that confirms first.
    /// </summary>
    public string Stop(StreamSetup setup)
    {
        if (_process is not { HasExited: false })
        {
            setup.IsLive = false;
            return "not streaming";
        }

        try
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(3000);
        }
        catch (Exception)
        {
            // Already gone is the outcome we wanted anyway.
        }

        _process = null;
        setup.IsLive = false;

        return "stopped streaming";
    }

    private async Task WatchAsync(Process process)
    {
        try
        {
            while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (StreamHealth.Parse(line) is { } stats)
                {
                    Progress?.Invoke(stats);
                    continue;
                }

                if (StreamHealth.Trouble(line) is { } problem) Trouble?.Invoke(problem);
            }
        }
        catch (Exception)
        {
            // The encoder going away is handled by whoever is holding it; there
            // is nothing useful to say from in here.
        }
    }

    private static string FirstLine(string text) =>
        text.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "no reason given";
}
