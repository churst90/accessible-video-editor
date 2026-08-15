using System.Diagnostics;
using System.Globalization;

namespace AccessibleVideoEditor.Vision;

/// <summary>
/// A camera, running, handing over every frame as it arrives.
///
/// One long-lived ffmpeg rather than a still grabbed per tick: opening a camera
/// takes the best part of a second, so grabbing stills would give a viewfinder
/// that answers a second after you moved - which is worse than no viewfinder,
/// because you would correct against stale information.
///
/// The frames are tiny on purpose. Everything asked of them is "where is the
/// face", and a 160-pixel-wide frame answers that in well under a millisecond.
///
/// <b>The camera is only ever opened by an explicit request.</b> Nothing in this
/// class runs on a timer, at startup, or as a side effect of arming a track.
/// </summary>
public sealed class ViewfinderCamera(string ffmpegPath = "ffmpeg") : IDisposable
{
    public const int Width = 160;
    public const int Height = 120;
    public const int Fps = 12;

    private Process? _process;
    private CancellationTokenSource? _cancel;
    private byte[]? _latest;
    private readonly Lock _gate = new();

    public bool IsRunning => _process is { HasExited: false };

    /// <summary>Raised off the UI thread for every frame. The front end marshals it.</summary>
    public event Action<byte[]>? Frame;

    public event Action<string>? Failed;

    public string Start(string device)
    {
        if (IsRunning) return "the viewfinder is already open";

        var info = new ProcessStartInfo(ffmpegPath)
        {
            ArgumentList =
            {
                "-hide_banner", "-loglevel", "error",
                "-f", "v4l2",
                "-framerate", Fps.ToString(CultureInfo.InvariantCulture),
                "-i", device.Length > 0 ? device : "/dev/video0",
                "-vf", $"scale={Width}:{Height}",
                "-f", "rawvideo", "-pix_fmt", "rgb24", "-",
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            _process = Process.Start(info);

            if (_process is null) return "could not start the camera";

            _cancel = new CancellationTokenSource();

            _ = Task.Run(() => ReadAsync(_process, _cancel.Token), CancellationToken.None);
            _ = Task.Run(() => WatchAsync(_process), CancellationToken.None);

            return $"viewfinder open on {device}";
        }
        catch (Exception exception)
        {
            _process = null;

            return $"could not open the camera: {exception.Message}";
        }
    }

    private async Task ReadAsync(Process process, CancellationToken ct)
    {
        var size = Width * Height * 3;
        var buffer = new byte[size];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var filled = 0;

                // A frame is fixed length, so it is read to completion rather
                // than trusting one read to return all of it - a partial frame
                // would put the face somewhere it is not.
                while (filled < size)
                {
                    var read = await process.StandardOutput.BaseStream
                        .ReadAsync(buffer.AsMemory(filled, size - filled), ct)
                        .ConfigureAwait(false);

                    if (read <= 0) return;

                    filled += read;
                }

                var frame = buffer[..size];

                lock (_gate) _latest = frame;

                Frame?.Invoke(frame);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Failed?.Invoke(exception.Message);
        }
    }

    private async Task WatchAsync(Process process)
    {
        var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);

        if (process.HasExited && process.ExitCode != 0 && error.Trim().Length > 0)
        {
            Failed?.Invoke(error.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim()
                           ?? "the camera stopped");
        }
    }

    /// <summary>The most recent frame, for anything that wants one on demand.</summary>
    public byte[]? Latest
    {
        get
        {
            lock (_gate) return _latest;
        }
    }

    public string Stop()
    {
        _cancel?.Cancel();

        try
        {
            if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
        }

        _process = null;
        _latest = null;

        return "viewfinder closed, the camera is off";
    }

    public void Dispose() => Stop();
}
