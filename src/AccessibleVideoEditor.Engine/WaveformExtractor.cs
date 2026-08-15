using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AccessibleVideoEditor.Core;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// Peaks for a media file, extracted once and kept.
///
/// Decoded at 8 kHz mono, which is far below anything you would listen to but
/// far above what a waveform drawn a few hundred pixels wide can show. The
/// result is cached on disk keyed by the file's path, size and modification
/// time, so opening a project the second time draws immediately.
///
/// This is decoration. It is the one part of the application that exists purely
/// for sighted collaborators, so it must never block anything: extraction runs
/// off the UI thread and the timeline draws solid blocks until it arrives.
/// </summary>
public sealed class WaveformExtractor(string directory, string ffmpegPath = "ffmpeg")
{
    private const int SampleRate = 8000;

    public string Directory { get; } = directory;

    private readonly Dictionary<SourceId, WaveformData> _memory = [];

    /// <summary>What is already available to draw, without waiting for anything.</summary>
    public WaveformData? Peek(SourceId source) =>
        _memory.TryGetValue(source, out var data) ? data : null;

    public string CacheFileFor(Source source)
    {
        var info = new FileInfo(source.Path);

        var key = new StringBuilder()
            .Append(source.Path).Append('|')
            .Append(info.Exists ? info.Length : 0).Append('|')
            .Append(info.Exists ? info.LastWriteTimeUtc.Ticks : 0)
            .ToString();

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];

        return Path.Combine(Directory, $"wave-{hash}.peaks");
    }

    public async Task<WaveformData?> LoadAsync(Source source, CancellationToken ct = default)
    {
        if (_memory.TryGetValue(source.Id, out var cached)) return cached;
        if (!File.Exists(source.Path)) return null;

        System.IO.Directory.CreateDirectory(Directory);

        var cacheFile = CacheFileFor(source);

        var data = File.Exists(cacheFile)
            ? Read(source.Id, cacheFile)
            : await ExtractAsync(source, cacheFile, ct).ConfigureAwait(false);

        if (data is not null) _memory[source.Id] = data;

        return data;
    }

    private async Task<WaveformData?> ExtractAsync(Source source, string cacheFile, CancellationToken ct)
    {
        var info = new ProcessStartInfo(ffmpegPath)
        {
            ArgumentList =
            {
                "-hide_banner", "-loglevel", "error",
                "-i", source.Path,
                "-vn", "-ac", "1",
                "-ar", SampleRate.ToString(CultureInfo.InvariantCulture),
                "-f", "s16le", "-",
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = Process.Start(info);
            if (process is null) return null;

            using var buffer = new MemoryStream();
            await process.StandardOutput.BaseStream.CopyToAsync(buffer, ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            if (process.ExitCode != 0) return null;

            var bytes = buffer.ToArray();
            var samples = new short[bytes.Length / 2];
            Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 2);

            var data = WaveformData.FromSamples(source.Id, samples, SampleRate);

            Write(cacheFile, data);

            return data;
        }
        catch (Exception)
        {
            // A waveform is decoration; failing to draw one must never be an
            // error the editor has to hear about.
            return null;
        }
    }

    /// <summary>
    /// A trivial binary format: duration, then one byte per peak. Bytes are
    /// plenty - the drawing is a few hundred pixels tall at most - and it keeps
    /// an hour of audio to about 350 kB.
    /// </summary>
    public static void Write(string path, WaveformData data)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        writer.Write(data.Duration);
        writer.Write(data.Peaks.Count);

        foreach (var peak in data.Peaks)
        {
            writer.Write((byte)Math.Clamp((int)Math.Round(peak * 255), 0, 255));
        }
    }

    public static WaveformData? Read(SourceId source, string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            var duration = reader.ReadDouble();
            var count = reader.ReadInt32();

            if (count < 0 || count > 50_000_000) return null;

            var peaks = new float[count];
            for (var i = 0; i < count; i++) peaks[i] = reader.ReadByte() / 255f;

            return new WaveformData(source, duration, peaks);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
