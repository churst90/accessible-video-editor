namespace AccessibleVideoEditor.Core.Model;

/// <summary>
/// One attempt at a segment.
///
/// Takes come from the DAW world and no video editor does them well. They are
/// exactly right for talking-head work: you say the same sentence four times
/// and want the best one. Recording into a segment again gives take 2 rather
/// than a second segment, so the structure of the video does not change while
/// you are still getting the words right.
///
/// The rejected takes stay in the document. Nothing is thrown away by choosing.
/// </summary>
public sealed class Take
{
    public required TakeId Id { get; set; }

    public required SourceId Source { get; init; }

    public double SourceIn { get; set; }

    public required double SourceOut { get; set; }

    /// <summary>Free text - "the one where I did not stumble".</summary>
    public string? Label { get; set; }

    /// <summary>Framing and level problems logged while this take was recorded.</summary>
    public List<CaptureIssue> Issues { get; set; } = [];

    public double Duration => Math.Max(0, SourceOut - SourceIn);

    /// <summary>
    /// What cycling announces. Capture issues are included because a take that
    /// drifted out of frame is exactly the one you need to know about, and it
    /// is the thing you cannot hear when auditioning.
    /// </summary>
    public string Describe(int index, int total)
    {
        var text = $"take {index + 1} of {total}, {Timecode.Speak(Duration)}";

        if (Label is { Length: > 0 } label) text += $", {label}";
        if (Issues.Count > 0) text += $", {Issues.Count} capture issue{(Issues.Count == 1 ? "" : "s")}";

        return text;
    }
}

public readonly record struct TakeId(string Value) : IStableId
{
    public bool IsUnset => string.IsNullOrEmpty(Value);

    public override string ToString() => Value ?? string.Empty;
}
