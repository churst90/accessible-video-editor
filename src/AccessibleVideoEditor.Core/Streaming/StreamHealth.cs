namespace AccessibleVideoEditor.Core.Streaming;

/// <summary>
/// Whether the stream is actually all right, from what the encoder says about
/// itself.
/// </summary>
public static class StreamHealth
{
    /// <summary>
    /// One line of ffmpeg's progress output. It writes
    /// <c>frame= 1234 fps= 30 q=25.0 size= 4096kB time=00:00:41.20 bitrate=8143.1kbits/s dup=0 drop=12 speed=1.00x</c>
    /// and repeats it, so a line is a snapshot rather than an event.
    /// </summary>
    public static StreamStats? Parse(string line)
    {
        if (!line.Contains("frame=", StringComparison.Ordinal)) return null;

        return new StreamStats(
            Frames: (int)(Number(line, "frame=") ?? 0),
            Fps: Number(line, "fps=") ?? 0,
            Dropped: (int)(Number(line, "drop=") ?? 0),
            Duplicated: (int)(Number(line, "dup=") ?? 0),
            BitrateKbps: Number(line, "bitrate=") ?? 0,
            Speed: Number(line, "speed=") ?? 1);
    }

    private static double? Number(string line, string key)
    {
        var at = line.IndexOf(key, StringComparison.Ordinal);
        if (at < 0) return null;

        var span = line.AsSpan(at + key.Length).TrimStart();

        var length = 0;
        while (length < span.Length && (char.IsDigit(span[length]) || span[length] is '.' or '-'))
        {
            length++;
        }

        return length == 0
            ? null
            : double.TryParse(span[..length], System.Globalization.NumberFormatInfo.InvariantInfo, out var value)
                ? value
                : null;
    }

    /// <summary>
    /// A line of ffmpeg's error output that means the stream is in trouble.
    /// These are the ones worth interrupting for; everything else it says
    /// during a broadcast is noise you cannot act on.
    /// </summary>
    public static string? Trouble(string line)
    {
        var text = line.ToLowerInvariant();

        if (text.Contains("connection refused")) return "the server refused the connection";
        if (text.Contains("connection reset")) return "the connection was reset";
        if (text.Contains("broken pipe")) return "the connection dropped";
        if (text.Contains("end of file") && text.Contains("rtmp")) return "the server closed the stream";
        if (text.Contains("failed to update header")) return "the server stopped accepting data";
        if (text.Contains("no space left")) return "the disk is full";

        return null;
    }
}

public readonly record struct StreamStats(
    int Frames,
    double Fps,
    int Dropped,
    int Duplicated,
    double BitrateKbps,
    double Speed)
{
    /// <summary>
    /// Below this the encoder is behind the clock, which means the stream is
    /// falling behind in real time rather than merely looking worse.
    /// </summary>
    public const double SlowSpeed = 0.97;

    public bool IsBehind => Speed < SlowSpeed;
}

/// <summary>
/// Turns a run of snapshots into the few things worth saying.
///
/// Deliberately reluctant. A single dropped frame is not worth a sound while
/// you are talking; frames dropping <i>steadily</i> is, and so is the moment it
/// stops. Anything that fires on every sample would be turned off within a
/// minute, and then it protects nobody.
/// </summary>
public sealed class StreamHealthMonitor
{
    private int _lastDropped;
    private bool _warnedDropping;
    private bool _warnedBehind;
    private double _lastReport;

    /// <summary>Frames dropped between two samples before it is worth saying.</summary>
    public int DropThreshold { get; set; } = 5;

    /// <summary>Seconds between routine reports when nothing is wrong.</summary>
    public double ReportEvery { get; set; } = 300;

    public StreamStats Latest { get; private set; }

    public StreamAlert Update(StreamStats stats, double atSeconds)
    {
        var dropped = stats.Dropped - _lastDropped;
        _lastDropped = stats.Dropped;
        Latest = stats;

        if (dropped >= DropThreshold)
        {
            // Said once when it starts, not on every sample. The recovery is
            // said too, because "has it stopped?" is the question you are left
            // with otherwise.
            if (_warnedDropping) return StreamAlert.None;

            _warnedDropping = true;

            return new StreamAlert(
                $"dropping frames, {dropped} in the last few seconds",
                StreamAlertKind.Dropping);
        }

        if (_warnedDropping && dropped == 0)
        {
            _warnedDropping = false;

            return new StreamAlert("frames are steady again", StreamAlertKind.Recovered);
        }

        if (stats.IsBehind && !_warnedBehind)
        {
            _warnedBehind = true;

            return new StreamAlert(
                $"the encoder is behind, {stats.Speed:0.00} times real time",
                StreamAlertKind.Behind);
        }

        if (!stats.IsBehind && _warnedBehind)
        {
            _warnedBehind = false;

            return new StreamAlert("the encoder has caught up", StreamAlertKind.Recovered);
        }

        if (atSeconds - _lastReport >= ReportEvery)
        {
            _lastReport = atSeconds;

            return new StreamAlert(
                $"{Math.Round(atSeconds / 60)} minutes live, "
                + $"{stats.BitrateKbps:0} kilobits, {stats.Dropped} frames dropped in total",
                StreamAlertKind.Routine);
        }

        return StreamAlert.None;
    }

    /// <summary>Asked for by a key, rather than waiting for the next report.</summary>
    public string Describe(bool live) =>
        !live ? "not streaming"
        : Latest.Frames == 0 ? "streaming, no statistics yet"
        : $"{Latest.BitrateKbps:0} kilobits, {Latest.Fps:0} frames per second, "
          + $"{Latest.Dropped} dropped, {(Latest.IsBehind ? "behind" : "keeping up")}";
}

public readonly record struct StreamAlert(string? Speak, StreamAlertKind Kind)
{
    public static StreamAlert None => new(null, StreamAlertKind.None);

    public bool IsSomething => Speak is not null;
}

public enum StreamAlertKind
{
    None,
    Dropping,
    Behind,
    Recovered,
    Routine,
    Disconnected,
}
