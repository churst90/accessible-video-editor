using System.Diagnostics;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// Live input levels, for monitoring.
///
/// Reads raw samples from <c>parec</c> and computes RMS here rather than
/// parsing ffmpeg's log output. That is both lower latency and simpler: a meter
/// that lags behind what you are saying is worse than no meter, because you
/// correct for a level you are no longer at.
///
/// <b>This opens the microphone.</b> It runs only while monitoring is switched
/// on, and stops the moment it is switched off.
/// </summary>
public sealed class LevelReader : IDisposable
{
    private Process? _process;
    private CancellationTokenSource? _stop;

    /// <summary>Roughly twenty readings a second: fast enough to follow speech.</summary>
    private const int SampleRate = 48000;
    private const int WindowSamples = SampleRate / 20;

    public bool IsRunning => _process is { HasExited: false };

    /// <summary>
    /// Starts reading. <paramref name="onLevel"/> is called with dBFS on a
    /// background thread; marshal to the UI yourself.
    /// </summary>
    public void Start(string sourceId, Action<double> onLevel, Action<string> onError)
    {
        Stop();

        var stop = new CancellationTokenSource();
        _stop = stop;

        var info = new ProcessStartInfo("parec")
        {
            ArgumentList =
            {
                "--device", sourceId,
                "--rate", SampleRate.ToString(),
                "--channels", "1",
                "--format", "s16le",
                "--latency-msec", "20",
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            _process = Process.Start(info);
        }
        catch (Exception exception)
        {
            onError($"could not open the input: {exception.Message}");
            return;
        }

        if (_process is null)
        {
            onError("parec would not start");
            return;
        }

        _ = Task.Run(() => ReadLoop(_process, onLevel, stop.Token), stop.Token);
    }

    private static void ReadLoop(Process process, Action<double> onLevel, CancellationToken ct)
    {
        var stream = process.StandardOutput.BaseStream;
        var buffer = new byte[WindowSamples * 2];

        while (!ct.IsCancellationRequested && !process.HasExited)
        {
            var read = 0;

            while (read < buffer.Length)
            {
                var got = stream.Read(buffer, read, buffer.Length - read);
                if (got <= 0) return;
                read += got;
            }

            onLevel(RootMeanSquareDb(buffer));
        }
    }

    /// <summary>
    /// RMS of a block of signed 16-bit samples, in dBFS. RMS rather than peak
    /// because it corresponds to perceived loudness, which is what a meter is
    /// actually for.
    /// </summary>
    public static double RootMeanSquareDb(ReadOnlySpan<byte> pcm)
    {
        if (pcm.Length < 2) return double.NegativeInfinity;

        double sum = 0;
        var count = pcm.Length / 2;

        for (var i = 0; i < count; i++)
        {
            var sample = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8)) / 32768.0;
            sum += sample * sample;
        }

        var rms = Math.Sqrt(sum / count);

        return rms <= 1e-9 ? -100 : 20 * Math.Log10(rms);
    }

    public void Stop()
    {
        _stop?.Cancel();
        _stop = null;

        if (_process is { HasExited: false } process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }
        }

        _process = null;
    }

    public void Dispose() => Stop();
}
