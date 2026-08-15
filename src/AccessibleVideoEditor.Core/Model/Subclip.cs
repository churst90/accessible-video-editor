namespace AccessibleVideoEditor.Core.Model;

/// <summary>
/// A named range of a source. "The good intro" becomes a thing you can insert
/// rather than a pair of numbers you have to find again.
///
/// This matters more without sight than with it. A sighted editor scrubs to the
/// right part of a clip by looking at it, and re-finds it the same way; the
/// equivalent here is listening through the whole take again. A subclip is
/// therefore not a convenience but the mechanism that stops you paying that
/// cost twice - and naming it is the point, because the name is the only handle
/// the range has.
///
/// It is a <b>reference, not a copy</b>: no media is extracted and nothing is
/// rendered. Inserting one puts the range on the timeline, and the source it
/// points at is the same file the rest of the project uses.
/// </summary>
public sealed class Subclip
{
    public required SubclipId Id { get; init; }

    public required SourceId Source { get; init; }

    /// <summary>
    /// Freely renameable. This is the whole value of the thing, so it is
    /// required rather than defaulted - an unnamed subclip is a pair of numbers
    /// again.
    /// </summary>
    public required string Name { get; set; }

    public required double In { get; set; }
    public required double Out { get; set; }

    /// <summary>Which audio track of the source to take. 0 mix, 1 microphone, 2 system.</summary>
    public int AudioTrack { get; set; }

    public string? Note { get; set; }

    public double Duration => Math.Max(0, Out - In);

    /// <summary>
    /// Name first, because the name is what you are looking for; the length
    /// second, because it is what decides whether it fits where you are about to
    /// put it. Where it sits inside the source is deliberately not said - you
    /// made the subclip so you would not have to think about that again.
    /// </summary>
    public string Describe() =>
        $"{Name}, {Timecode.Speak(Duration)}" + (Note is { Length: > 0 } ? $", {Note}" : string.Empty);
}
