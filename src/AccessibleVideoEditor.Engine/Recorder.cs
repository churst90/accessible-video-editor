using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Vision;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// Capture through ffmpeg.
///
/// The command building and the output parsing are pure functions, separated
/// deliberately: they are the parts that can be tested without opening a
/// camera, and they are where the mistakes that cost you a take actually live.
///
/// <b>Nothing here runs until recording is asked for.</b> The signal check
/// opens the device, so it happens when you press record - not when you arm a
/// track, and never as a side effect of looking at a list.
/// </summary>
public sealed partial class Recorder(string ffmpegPath = "ffmpeg")
{
    /// <summary>
    /// A one second capture, measured. This is the check that stops an hour of
    /// footage from a muted microphone: it is cheap, it happens every time, and
    /// it fails loudly.
    /// </summary>
    public async Task<SignalCheck> CheckSignalAsync(
        CaptureDevice device,
        CancellationToken ct = default)
    {
        var arguments = device.Kind == CaptureDeviceKind.Camera
            ? CameraProbeArguments(device.Id)
            : MicrophoneProbeArguments(device.Id);

        var (output, failed) = await RunAsync(arguments, ct).ConfigureAwait(false);

        if (failed is not null) return SignalCheck.Failed($"{device.Name} could not be opened: {failed}");

        return device.Kind == CaptureDeviceKind.Camera
            ? InterpretCamera(device.Name, output)
            : InterpretMicrophone(device.Name, output);
    }

    /// <summary>Starts capturing. Call <see cref="RecordingSession.StopAsync"/> to finish.</summary>
    public RecordingSession Start(
        CaptureDevice device,
        string outputPath,
        string? microphoneId,
        InputChannel channel = InputChannel.All)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

        var arguments = device.Kind == CaptureDeviceKind.Camera
            ? CameraArguments(device.Id, microphoneId, outputPath, channel)
            : MicrophoneArguments(device.Id, outputPath, channel);

        var info = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        var process = Process.Start(info)
                      ?? throw new InvalidOperationException("Could not start ffmpeg.");

        return new RecordingSession(process, outputPath);
    }

    // ---- command building, pure and testable -----------------------------

    /// <summary>
    /// Camera plus microphone into one file. Recorded at a fast preset because
    /// dropped frames during capture cannot be recovered later, whereas a
    /// larger file can always be re-encoded.
    /// </summary>
    public static IReadOnlyList<string> CameraArguments(
        string camera,
        string? microphone,
        string output,
        InputChannel channel = InputChannel.All)
    {
        var arguments = new List<string>
        {
            "-y", "-hide_banner", "-loglevel", "warning",
            "-f", "v4l2", "-framerate", "30", "-video_size", "1280x720", "-i", camera,
        };

        if (microphone is { Length: > 0 })
        {
            arguments.AddRange(["-f", "pulse", "-i", microphone]);
        }

        arguments.AddRange(["-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p"]);

        if (microphone is { Length: > 0 })
        {
            if (PanFilter(channel) is { } pan) arguments.AddRange(["-af", pan]);
            arguments.AddRange(["-c:a", "aac", "-b:a", "192k"]);
        }

        arguments.Add(output);
        return arguments;
    }

    public static IReadOnlyList<string> MicrophoneArguments(
        string microphone,
        string output,
        InputChannel channel = InputChannel.All)
    {
        var arguments = new List<string>
        {
            "-y", "-hide_banner", "-loglevel", "warning",
            "-f", "pulse", "-i", microphone,
        };

        if (PanFilter(channel) is { } pan) arguments.AddRange(["-af", pan]);

        arguments.AddRange(["-c:a", "aac", "-b:a", "192k", output]);
        return arguments;
    }

    /// <summary>
    /// Takes one input of a multi-input interface and makes it the whole
    /// recording.
    ///
    /// A two-input interface presents as a single stereo source, so recording
    /// it whole puts the microphone on the left and silence on the right. That
    /// sounds like a broken take, and there is no meter to notice it on.
    /// </summary>
    public static string? PanFilter(InputChannel channel) => channel switch
    {
        InputChannel.Left => "pan=mono|c0=c0",
        InputChannel.Right => "pan=mono|c0=c1",
        _ => null,
    };

    public static IReadOnlyList<string> MicrophoneProbeArguments(string microphone) =>
    [
        "-hide_banner", "-f", "pulse", "-i", microphone,
        "-t", "1", "-af", "volumedetect", "-f", "null", "-",
    ];

    public static IReadOnlyList<string> CameraProbeArguments(string camera) =>
    [
        "-hide_banner", "-f", "v4l2", "-i", camera,
        "-t", "1", "-vf", "blackdetect=d=0.5:pic_th=0.98", "-f", "null", "-",
    ];

    // ---- output interpretation, pure and testable ------------------------

    /// <summary>
    /// A microphone reading below about -55 dB over a whole second is silence,
    /// not quiet speech. That is the muted-input case, and it is worth refusing
    /// to record over.
    /// </summary>
    public static SignalCheck InterpretMicrophone(string name, string ffmpegOutput)
    {
        var mean = ParseMeanVolume(ffmpegOutput);

        if (mean is null) return SignalCheck.Failed($"{name} produced no measurable audio");

        if (mean.Value < -55)
        {
            return SignalCheck.Failed(
                $"{name} is silent, {mean.Value:0} decibels. Check it is not muted");
        }

        var peak = ParseMaxVolume(ffmpegOutput);

        if (peak is > -0.5)
        {
            return SignalCheck.Warned($"{name} is clipping at {peak.Value:0.#} decibels", mean.Value);
        }

        return SignalCheck.Passed($"{name}, {mean.Value:0} decibels", mean.Value);
    }

    /// <summary>A frame that is entirely black for the whole probe is a lens cap or a dead device.</summary>
    public static SignalCheck InterpretCamera(string name, string ffmpegOutput)
    {
        if (BlackDetectPattern().IsMatch(ffmpegOutput))
        {
            return SignalCheck.Failed($"{name} is producing a black picture. Check the lens cover");
        }

        return SignalCheck.Passed($"{name} is producing picture", null);
    }

    public static double? ParseMeanVolume(string output) => ParseDb(MeanVolumePattern(), output);

    public static double? ParseMaxVolume(string output) => ParseDb(MaxVolumePattern(), output);

    private static double? ParseDb(Regex pattern, string output) =>
        pattern.Match(output) is { Success: true } match
        && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private async Task<(string Output, string? Failure)> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken ct)
    {
        var info = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(info);
            if (process is null) return (string.Empty, "ffmpeg would not start");

            var stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            // ffmpeg writes its measurements to stderr, and exits non-zero for
            // a device it could not open - which is the case worth reporting.
            return process.ExitCode == 0 ? (stderr, null) : (stderr, FirstError(stderr));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (string.Empty, exception.Message);
        }
    }

    private static string FirstError(string stderr) =>
        stderr.Split('\n').LastOrDefault(line => line.Trim().Length > 0)?.Trim()
        ?? "no detail";

    [GeneratedRegex(@"mean_volume:\s*(-?\d+(?:\.\d+)?) dB")]
    private static partial Regex MeanVolumePattern();

    [GeneratedRegex(@"max_volume:\s*(-?\d+(?:\.\d+)?) dB")]
    private static partial Regex MaxVolumePattern();

    [GeneratedRegex(@"black_start:0(\D|$)")]
    private static partial Regex BlackDetectPattern();
}

/// <summary>
/// The result of opening a device and looking at what came out. Warnings do not
/// stop a recording; failures do.
/// </summary>
public sealed record SignalCheck(bool Ok, bool IsWarning, string Message, double? LevelDb)
{
    public static SignalCheck Passed(string message, double? level) => new(true, false, message, level);

    public static SignalCheck Warned(string message, double? level) => new(true, true, message, level);

    public static SignalCheck Failed(string message) => new(false, false, message, null);
}

/// <summary>A capture in progress.</summary>
public sealed class RecordingSession(Process process, string outputPath)
{
    public string OutputPath { get; } = outputPath;

    public bool IsRunning => !process.HasExited;

    /// <summary>
    /// Asks ffmpeg to finish cleanly. Writing "q" lets it flush and close the
    /// container; killing it would leave an unplayable file, which is the worst
    /// possible outcome for a take you cannot repeat.
    /// </summary>
    public async Task<string?> StopAsync(CancellationToken ct = default)
    {
        if (process.HasExited) return File.Exists(OutputPath) ? OutputPath : null;

        try
        {
            await process.StandardInput.WriteAsync("q").ConfigureAwait(false);
            await process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Already closing.
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
        }

        return File.Exists(OutputPath) ? OutputPath : null;
    }
}
