namespace AccessibleVideoEditor.Core.Model;

/// <summary>A media file on disk, plus what <c>ffprobe</c> found in it.</summary>
public sealed class Source
{
    public required SourceId Id { get; init; }

    /// <summary>Relative to the project root, so projects stay portable.</summary>
    public required string Path { get; set; }

    public SourceKind Kind { get; set; } = SourceKind.Video;

    public double Duration { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double Fps { get; set; }

    /// <summary>
    /// <c>record-screen.sh</c> writes three: 0 = mix, 1 = mic, 2 = system. The
    /// UI shows these by name with an audition button, never as a bare index.
    /// </summary>
    public List<AudioTrackInfo> AudioTracks { get; set; } = [];

    /// <summary>Word-level Whisper output, once transcribed.</summary>
    public string? TranscriptPath { get; set; }

    /// <summary>Framing and level problems found by the viewfinder during the take.</summary>
    public List<CaptureIssue> CaptureIssues { get; set; } = [];
}

public enum SourceKind
{
    Video,
    Audio,
    Image,
}

public sealed class AudioTrackInfo
{
    public required int Index { get; init; }
    public string? Label { get; set; }
    public int Channels { get; set; }

    public string Describe() => Label is not null ? $"{Index}: {Label}" : $"track {Index}";
}

/// <summary>
/// Logged live while recording, so a take can report "you were out of frame
/// from 2:10 to 2:24" afterwards instead of that being discovered at review.
/// </summary>
public sealed class CaptureIssue
{
    public required double Start { get; init; }
    public required double End { get; init; }
    public required CaptureIssueKind Kind { get; init; }
    public string? Detail { get; init; }
}

public enum CaptureIssueKind
{
    OutOfFrame,
    Cropped,
    Backlit,
    TooDark,
    Clipping,
    Silence,
    NoFaceDetected,
}
