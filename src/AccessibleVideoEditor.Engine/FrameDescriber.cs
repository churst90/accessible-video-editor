using System.Diagnostics;
using System.Globalization;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// Reads back what is actually in a frame.
///
/// This is the piece the whole project started from: the part of editing that
/// genuinely needs eyes, done by something that has them. Everything else here
/// replaces looking with measurement; this replaces it with description.
///
/// It shells out to the <c>claude</c> CLI rather than calling an API directly,
/// which means no key to store, no account to configure, and it uses whatever
/// authentication is already working on this machine.
/// </summary>
public sealed class FrameDescriber(string ffmpegPath = "ffmpeg", string claudePath = "claude")
{
    /// <summary>False when the CLI is not installed; the UI then says so plainly.</summary>
    public bool IsAvailable =>
        File.Exists(claudePath)
        || (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator)
            .Any(directory => File.Exists(Path.Combine(directory, claudePath)));

    /// <summary>
    /// The brief. Deliberately specific about what matters and what to leave
    /// out: an unprompted description wanders into scene-setting prose, and
    /// what is actually needed is the handful of things a sighted editor would
    /// have caught in a glance.
    /// </summary>
    public const string Prompt =
        "Read the image at {0}. You are the eyes for a blind video editor reviewing a frame "
        + "of their own footage. In no more than four short sentences, say only what they "
        + "cannot check themselves: how they are framed (cropped, off-centre, too much "
        + "headroom), exposure and lighting problems, anything distracting or embarrassing "
        + "in the background, and whether any on-screen text is legible and clear of their "
        + "face. If something is fine, do not mention it. Do not describe the scene for its "
        + "own sake and do not add preamble.";

    /// <summary>Pulls a single frame out of a file at a given moment.</summary>
    public async Task<string?> ExtractFrameAsync(
        string videoPath,
        double atSeconds,
        string outputPath,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

        var info = new ProcessStartInfo(ffmpegPath)
        {
            ArgumentList =
            {
                "-y", "-hide_banner", "-loglevel", "error",
                "-ss", atSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                "-i", videoPath,
                "-frames:v", "1", "-q:v", "2", outputPath,
            },
            RedirectStandardError = true,
        };

        using var process = Process.Start(info);
        if (process is null) return null;

        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        return File.Exists(outputPath) ? outputPath : null;
    }

    /// <summary>Describes an image that already exists on disk.</summary>
    public async Task<string> DescribeAsync(string imagePath, CancellationToken ct = default)
    {
        if (!File.Exists(imagePath)) return "there is no frame to describe";
        if (!IsAvailable) return "the claude command is not installed, so frames cannot be described";

        var info = new ProcessStartInfo(claudePath)
        {
            ArgumentList = { "-p", string.Format(CultureInfo.InvariantCulture, Prompt, imagePath) },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = Process.Start(info);
            if (process is null) return "could not start the claude command";

            var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            var error = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);

            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                return $"describing the frame failed: {Tidy(error)}";
            }

            var text = Tidy(output);
            return text.Length == 0 ? "nothing came back" : text;
        }
        catch (Exception exception)
        {
            return $"describing the frame failed: {exception.Message}";
        }
    }

    /// <summary>
    /// Collapses the reply to something worth speaking: no blank lines, no
    /// markdown bullets, no trailing whitespace.
    /// </summary>
    public static string Tidy(string text) =>
        string.Join(
            " ",
            text.Split('\n')
                .Select(line => line.Trim().TrimStart('-', '*', '•').Trim())
                .Where(line => line.Length > 0))
            .Trim();
}
