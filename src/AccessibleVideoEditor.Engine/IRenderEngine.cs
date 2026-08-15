using AccessibleVideoEditor.Core;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// Three fidelity tiers, because they answer three different questions:
///
/// <list type="number">
/// <item><b>Live</b> - mpv plays the decision list. Instant, no encode. What
/// makes the transcript editor feel alive.</item>
/// <item><b>Draft</b> - background re-render of only the dirty segments at
/// 540p. What makes transitions, titles and ducking accurate, since none of
/// those can be faked in playback.</item>
/// <item><b>Master</b> - 1080p plus captions, on demand.</item>
/// </list>
///
/// The draft tier is incremental: segments are content-hash cached, so changing
/// one line re-renders one segment.
/// </summary>
public interface IRenderEngine
{
    Task<RenderOutput> RenderAsync(
        Project project,
        RenderQuality quality,
        IProgress<RenderProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>Audio-only mix. Fast, and the preview that matters most here.</summary>
    Task<RenderOutput> RenderAudioAsync(Project project, CancellationToken ct = default);

    /// <summary>Stills for the frame review - the part that replaces sighted QA.</summary>
    Task<IReadOnlyList<ExtractedFrame>> ExtractFramesAsync(
        Project project,
        RenderOutput draft,
        CancellationToken ct = default);
}

public enum RenderQuality
{
    /// <summary>540p, veryfast. No NVENC in this ffmpeg build, so CPU x264.</summary>
    Draft,

    /// <summary>1080p plus captions.srt, loudness normalised.</summary>
    Master,
}

public sealed record RenderOutput(string Path, double Duration, RenderQuality Quality);

public sealed record RenderProgress(string Stage, double Fraction, int SegmentsDone, int SegmentsTotal)
{
    /// <summary>Spoken periodically during a long render, not on every tick.</summary>
    public string Announce() => $"{Stage}, {Fraction * 100:0} percent";
}

/// <summary>
/// A still tied back to the element that produced it, so a review finding can
/// name an element rather than a timestamp the user then has to hunt for.
/// </summary>
public sealed record ExtractedFrame(string Path, double ProgrammeTime, ElementId? Element, string Reason);

/// <summary>
/// Holes block the master render. This is the lint pass that enforces it, plus
/// the other things that are invisible without sight: a title with nothing
/// under it, an overlay that outlives its anchor, a source file gone missing.
/// </summary>
public interface IProjectValidator
{
    IReadOnlyList<ValidationIssue> Validate(Project project, RenderQuality intent);
}

public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Message,
    ElementId? Element = null,
    ItemId? Item = null);

public enum ValidationSeverity
{
    /// <summary>Worth knowing. Does not stop a render.</summary>
    Note,

    /// <summary>Probably wrong. Renders anyway, announced loudly.</summary>
    Warning,

    /// <summary>Blocks the master render. Unfilled holes land here.</summary>
    Blocking,
}
