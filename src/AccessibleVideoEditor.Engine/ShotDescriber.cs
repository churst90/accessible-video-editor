using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AccessibleVideoEditor.Core;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Review;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// Finds where the picture changes in a source, and has each shot described
/// once.
///
/// This is <see cref="FrameDescriber"/> turned from a question you ask into
/// something the project already knows - the same move the waveform cache
/// makes, and for the same reason: the work is slow, the answer does not
/// change, and navigation cannot wait for it.
///
/// <b>It never runs by itself.</b> Each shot is a call to a language model, so
/// a twenty minute video is tens of them. Doing that silently on import would
/// be spending someone's quota on a question they had not asked; the command
/// says how many shots it found and what it is about to do before it starts.
/// </summary>
public sealed partial class ShotDescriber(
    string cacheDirectory,
    string ffmpegPath = "ffmpeg",
    string claudePath = "claude")
{
    /// <summary>
    /// The brief for locating yourself, which is a different question from the
    /// one <see cref="FrameDescriber.Prompt"/> asks.
    ///
    /// That one reviews your own footage for problems - framing, exposure, a
    /// mess in the background - and deliberately says nothing when things are
    /// fine. This one is the opposite: it always describes, because the point
    /// is to know where you are, and "nothing wrong here" does not tell you
    /// which shot you are standing in.
    /// </summary>
    public const string Prompt =
        "Read the image at {0}. It is one shot from a video, and you are the eyes for a "
        + "blind editor who needs to know what is on screen at this point so they can find "
        + "it again. Reply in exactly this form:\n"
        + "LABEL: a phrase of at most six words naming this shot, as you would in a shot "
        + "list - who or what is in it and how close.\n"
        + "Then one or two plain sentences saying what is visible, including any on-screen "
        + "text word for word. No preamble, no markdown, no commentary on quality.";

    [GeneratedRegex(@"pts_time:(\d+(?:\.\d+)?)")]
    private static partial Regex PtsTime();

    /// <summary>
    /// Where the picture changes, in seconds, always including the opening
    /// frame.
    ///
    /// ffmpeg's own scene score rather than anything cleverer. 0.3 is loose
    /// enough to catch a cut between two similar talking-head angles and tight
    /// enough not to fire on someone gesturing.
    /// </summary>
    public async Task<IReadOnlyList<double>> DetectShotsAsync(
        string videoPath, double threshold = 0.3, CancellationToken ct = default)
    {
        var info = new ProcessStartInfo(ffmpegPath)
        {
            ArgumentList =
            {
                "-hide_banner", "-nostats",
                "-i", videoPath,
                "-filter:v", $"select='gt(scene,{threshold.ToString("0.##", CultureInfo.InvariantCulture)})',showinfo",
                "-f", "null", "-",
            },
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        var times = new List<double> { 0 };

        try
        {
            using var process = Process.Start(info);
            if (process is null) return times;

            // showinfo writes to stderr; read it before waiting or a long file
            // fills the pipe buffer and the process never exits.
            var report = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            foreach (Match match in PtsTime().Matches(report))
            {
                if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out var seconds)
                    && seconds > 0.5)
                {
                    times.Add(seconds);
                }
            }
        }
        catch (Exception)
        {
            // No ffmpeg, or a file it cannot read. One shot covering the whole
            // source is a truthful answer rather than an error: it is what an
            // unbroken take actually is.
        }

        return [.. times.Distinct().Order()];
    }

    /// <summary>
    /// Describes each shot and returns them in order. Progress is reported as
    /// it goes, because a run of forty descriptions is minutes long and silence
    /// for minutes is indistinguishable from a hang.
    /// </summary>
    public async Task<IReadOnlyList<Shot>> DescribeShotsAsync(
        string videoPath,
        IReadOnlyList<double> starts,
        double duration,
        Action<int, int>? progress = null,
        CancellationToken ct = default)
    {
        var describer = new FrameDescriber(ffmpegPath, claudePath);
        var shots = new List<Shot>();

        for (var i = 0; i < starts.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var start = starts[i];
            var until = i + 1 < starts.Count ? starts[i + 1] : Math.Max(duration, start + 0.001);

            // A frame from just inside the shot rather than exactly on the cut:
            // the first frame of a shot can still be halfway through a
            // dissolve, which describes as a double exposure of two scenes.
            var sampleAt = Math.Min(start + 0.25, Math.Max(start, until - 0.05));

            var frame = Path.Combine(
                Path.GetTempPath(), $"videoedit-shot-{Guid.NewGuid():N}.jpg");

            try
            {
                var extracted = await describer.ExtractFrameAsync(videoPath, sampleAt, frame, ct)
                    .ConfigureAwait(false);

                var reply = extracted is null
                    ? string.Empty
                    : await DescribeAsync(describer, frame, ct).ConfigureAwait(false);

                var (label, detail) = Split(reply, start);

                shots.Add(new Shot(start, until, label, detail));
            }
            finally
            {
                if (File.Exists(frame)) File.Delete(frame);
            }

            progress?.Invoke(i + 1, starts.Count);
        }

        return shots;
    }

    private async Task<string> DescribeAsync(FrameDescriber describer, string frame, CancellationToken ct)
    {
        // The shot brief rather than the review brief, so this does not need
        // FrameDescriber to grow a second mode.
        var info = new ProcessStartInfo(claudePath)
        {
            ArgumentList = { "-p", string.Format(CultureInfo.InvariantCulture, Prompt, frame) },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = Process.Start(info);
            if (process is null) return string.Empty;

            var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            return process.ExitCode == 0 ? output : string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
        finally
        {
            _ = describer;
        }
    }

    /// <summary>
    /// Pulls the label off the front of the reply.
    ///
    /// A shot that could not be described still gets a label - its timecode -
    /// rather than an empty one. A blank announcement at a shot boundary reads
    /// as the application having missed the key.
    /// </summary>
    public static (string Label, string Detail) Split(string reply, double at)
    {
        var lines = reply.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        if (lines.Count == 0) return ($"shot at {Timecode.Speak(at)}, not described", string.Empty);

        var label = lines[0];
        var detailFrom = 1;

        if (label.StartsWith("LABEL:", StringComparison.OrdinalIgnoreCase))
        {
            label = label["LABEL:".Length..].Trim();
        }
        else
        {
            // No label line came back. The first six words of the description
            // are a serviceable one, and better than refusing the whole reply.
            label = string.Join(' ', label.Split(' ').Take(6));
            detailFrom = 0;
        }

        // Joined with newlines rather than spaces so Tidy still sees the lines:
        // it strips a bullet from the front of each one, and pre-joining would
        // leave every bullet but the first in the middle of the sentence.
        var detail = FrameDescriber.Tidy(string.Join('\n', lines.Skip(detailFrom)));

        return (label.Trim('.', ' ').Length == 0 ? $"shot at {Timecode.Speak(at)}" : label.Trim('.', ' '), detail);
    }

    // ---- the disk cache, keyed exactly as the waveform cache is -------------

    public string CacheFileFor(Source source)
    {
        var info = new FileInfo(source.Path);

        var key = new StringBuilder()
            .Append(source.Path).Append('|')
            .Append(info.Exists ? info.Length : 0).Append('|')
            .Append(info.Exists ? info.LastWriteTimeUtc.Ticks : 0)
            .ToString();

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];

        return Path.Combine(cacheDirectory, $"shots-{hash}.json");
    }

    public IReadOnlyList<Shot>? Cached(Source source)
    {
        var file = CacheFileFor(source);
        if (!File.Exists(file)) return null;

        try
        {
            var index = ShotIndex.Deserialise(File.ReadAllText(file));
            var shots = index.For(source.Id);

            return shots.Count > 0 ? shots : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Cache(Source source, IReadOnlyList<Shot> shots)
    {
        Directory.CreateDirectory(cacheDirectory);

        var index = new ShotIndex();
        index.Set(source.Id, shots);

        File.WriteAllText(CacheFileFor(source), index.Serialise());
    }
}
