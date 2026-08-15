using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AccessibleVideoEditor.Core;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// Wraps <c>ffprobe</c>. The system binary is used rather than a bundled one -
/// this build is personal-use, and shelling out keeps the x264 GPL question off
/// the table until there is something to distribute.
/// </summary>
public sealed class FfmpegProbe(string ffprobePath = "ffprobe")
{
    public async Task<Source> ProbeAsync(string path, SourceId? id = null, CancellationToken ct = default)
    {
        var json = await RunAsync(
        [
            "-v", "quiet",
            "-print_format", "json",
            "-show_format",
            "-show_streams",
            path,
        ], ct).ConfigureAwait(false);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var source = new Source
        {
            Id = id ?? Ids.NewSource(),
            Path = path,
        };

        if (root.TryGetProperty("format", out var format)
            && format.TryGetProperty("duration", out var duration)
            && double.TryParse(duration.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            source.Duration = seconds;
        }

        if (!root.TryGetProperty("streams", out var streams)) return source;

        var audioIndex = 0;

        foreach (var stream in streams.EnumerateArray())
        {
            var kind = stream.TryGetProperty("codec_type", out var type) ? type.GetString() : null;

            if (kind == "video" && source.Width == 0)
            {
                source.Width = stream.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
                source.Height = stream.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
                source.Fps = ParseRate(stream);
            }
            else if (kind == "audio")
            {
                source.AudioTracks.Add(new AudioTrackInfo
                {
                    Index = audioIndex,
                    // record-screen.sh writes these in a known order. Naming them
                    // here is why the UI can offer "mic only" instead of "track 1".
                    Label = audioIndex switch
                    {
                        0 => "mix",
                        1 => "microphone",
                        2 => "system audio",
                        _ => null,
                    },
                    Channels = stream.TryGetProperty("channels", out var c) ? c.GetInt32() : 0,
                });

                audioIndex++;
            }
        }

        if (source.AudioTracks.Count <= 1 && source.AudioTracks.Count > 0)
        {
            source.AudioTracks[0].Label = null;
        }

        return source;
    }

    private static double ParseRate(JsonElement stream)
    {
        if (!stream.TryGetProperty("r_frame_rate", out var rate)) return 0;

        var parts = (rate.GetString() ?? string.Empty).Split('/');
        return parts.Length == 2
               && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
               && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator)
               && denominator != 0
            ? numerator / denominator
            : 0;
    }

    private async Task<string> RunAsync(string[] arguments, CancellationToken ct)
    {
        var info = new ProcessStartInfo(ffprobePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException($"Could not start {ffprobePath}.");

        var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"ffprobe failed: {error.Trim()}");
        }

        return output;
    }
}
