using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Vision;

/// <summary>
/// Device enumeration is the easy half. The half that saves a shoot is the arm
/// check: verifying that the selected microphone is producing sound and the
/// selected camera is producing pictures <i>before</i> recording starts.
/// Recording an hour into a dead device is the classic disaster, and it is
/// entirely preventable.
/// </summary>
public interface ICaptureDevices
{
    Task<IReadOnlyList<CaptureDevice>> EnumerateAsync(CaptureDeviceKind kind, CancellationToken ct = default);

    /// <summary>Runs when a track is armed. Never silently passes a dead device.</summary>
    Task<ArmCheckResult> ArmCheckAsync(CaptureDevice device, CancellationToken ct = default);
}

public sealed record CaptureDevice(
    string Id,
    string Name,
    CaptureDeviceKind Kind,
    int Channels = 0)
{
    /// <summary>Spoken when cycling devices. Never a bare index.</summary>
    public string Describe() =>
        Channels > 1 ? $"{Name}, {Channels} channels" : Name;
}

public enum CaptureDeviceKind
{
    Camera,
    Microphone,
    SystemAudio,
    Screen,

    /// <summary>Where monitoring is heard. Headphones, speakers, an interface.</summary>
    Output,
}

public sealed record ArmCheckResult(bool Passed, string Summary, IReadOnlyList<string> Problems)
{
    public static ArmCheckResult Pass(string summary) => new(true, summary, []);

    public static ArmCheckResult Fail(params string[] problems) =>
        new(false, "arm check failed", problems);
}

/// <summary>A single decoded camera frame, in whatever layout the detector wants.</summary>
public sealed record VisionFrame(int Width, int Height, ReadOnlyMemory<byte> Pixels, double Timestamp);

public interface IFrameSource : IDisposable
{
    Task<VisionFrame?> ReadAsync(CancellationToken ct = default);
}

/// <summary>
/// Face box plus landmarks. Landmarks matter because framing is judged on the
/// eyeline, not the centre of the box.
/// </summary>
public sealed record FaceObservation(
    double X,
    double Y,
    double Width,
    double Height,
    double? LeftEyeY,
    double? RightEyeY,
    double Confidence)
{
    public double CentreX => X + Width / 2;

    /// <summary>Falls back to the upper third of the box when landmarks are missing.</summary>
    public double EyelineY =>
        LeftEyeY is { } left && RightEyeY is { } right ? (left + right) / 2 : Y + Height * 0.35;

    public double RollDegrees =>
        LeftEyeY is { } l && RightEyeY is { } r ? Math.Atan2(r - l, Width) * 180 / Math.PI : 0;
}

public interface IFaceDetector : IDisposable
{
    /// <summary>Must run in a few milliseconds; the sonification loop depends on it.</summary>
    IReadOnlyList<FaceObservation> Detect(VisionFrame frame);
}

/// <summary>
/// Exposure problems a sighted person catches instantly and you currently
/// cannot: backlighting, silhouetting, a face too dark to grade.
/// </summary>
public interface IExposureAnalyser
{
    ExposureReading Analyse(VisionFrame frame, FaceObservation? face);
}

public sealed record ExposureReading(
    double FaceLuminance,
    double BackgroundLuminance,
    bool Backlit,
    bool TooDark,
    bool BlownHighlights)
{
    public CaptureIssueKind? AsIssue() =>
        Backlit ? CaptureIssueKind.Backlit
        : TooDark ? CaptureIssueKind.TooDark
        : null;
}
