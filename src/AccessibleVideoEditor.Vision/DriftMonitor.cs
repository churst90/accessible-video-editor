using AccessibleVideoEditor.Core;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Vision;

/// <summary>
/// Watches framing and exposure while a take is recording and logs the spans
/// where something was wrong.
///
/// This is the reshoot-preventer. Instead of discovering at review that you
/// drifted out of frame, the take arrives carrying "out of frame from 2:10 to
/// 2:24" - which drops straight into the edit as issue markers.
/// </summary>
public sealed class DriftMonitor
{
    private readonly Dictionary<CaptureIssueKind, double> _open = [];
    private readonly List<CaptureIssue> _closed = [];

    /// <summary>Ignore blips shorter than this; a hand passing the lens is not a problem.</summary>
    public double MinimumDuration { get; init; } = 0.75;

    public IReadOnlyList<CaptureIssue> Issues => _closed;

    public void Observe(double timestamp, IReadOnlyCollection<CaptureIssueKind> active)
    {
        foreach (var kind in active.Where(k => !_open.ContainsKey(k)))
        {
            _open[kind] = timestamp;
        }

        foreach (var kind in _open.Keys.Where(k => !active.Contains(k)).ToList())
        {
            Close(kind, timestamp);
        }
    }

    public IReadOnlyList<CaptureIssue> Finish(double timestamp)
    {
        foreach (var kind in _open.Keys.ToList())
        {
            Close(kind, timestamp);
        }

        return _closed;
    }

    private void Close(CaptureIssueKind kind, double timestamp)
    {
        var start = _open[kind];
        _open.Remove(kind);

        if (timestamp - start < MinimumDuration) return;

        _closed.Add(new CaptureIssue { Start = start, End = timestamp, Kind = kind });
    }

    /// <summary>The spoken summary after a take ends.</summary>
    public string Summarise() =>
        _closed.Count == 0
            ? "clean take"
            : string.Join(". ", _closed.Select(i =>
                $"{Describe(i.Kind)} from {Timecode.FormatShort(i.Start)} to {Timecode.FormatShort(i.End)}"));

    private static string Describe(CaptureIssueKind kind) => kind switch
    {
        CaptureIssueKind.OutOfFrame => "out of frame",
        CaptureIssueKind.Cropped => "cropped",
        CaptureIssueKind.Backlit => "backlit",
        CaptureIssueKind.TooDark => "too dark",
        CaptureIssueKind.Clipping => "audio clipping",
        CaptureIssueKind.Silence => "silence",
        CaptureIssueKind.NoFaceDetected => "no face detected",
        _ => kind.ToString(),
    };
}
