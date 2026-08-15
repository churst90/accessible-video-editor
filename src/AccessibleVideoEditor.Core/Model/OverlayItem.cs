using System.Text.Json.Serialization;

namespace AccessibleVideoEditor.Core.Model;

/// <summary>
/// Anything that rides on top of the spine: b-roll, titles, graphics, music.
///
/// Extent is expressed as a start <see cref="TimeAnchor"/> plus either an end
/// anchor or a duration - never absolute programme time. An item can therefore
/// span several spine elements and still survive a ripple.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(BrollItem), "broll")]
[JsonDerivedType(typeof(TitleItem), "title")]
[JsonDerivedType(typeof(GraphicItem), "graphic")]
[JsonDerivedType(typeof(MusicItem), "music")]
[JsonDerivedType(typeof(CardItem), "card")]
[JsonDerivedType(typeof(AudioItem), "audio")]
public abstract class OverlayItem
{
    /// <summary>See <see cref="SpineElement.Id"/> - immutable by convention.</summary>
    public required ItemId Id { get; set; }
    public required TrackId Track { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Silences the segment's audio while keeping its picture.</summary>
    public bool Muted { get; set; }

    /// <summary>Removes the segment's picture while its audio still plays.</summary>
    public bool Hidden { get; set; }

    public required TimeAnchor Start { get; set; }

    /// <summary>Set this or <see cref="Length"/>, not both.</summary>
    public TimeAnchor? End { get; set; }

    /// <summary>Set this or <see cref="End"/>, not both.</summary>
    public double? Length { get; set; }

    public abstract string Describe();

    public bool IsWellFormed() => End.HasValue ^ Length.HasValue;
}

/// <summary>Other footage over your narration. Your audio keeps playing.</summary>
public sealed class BrollItem : OverlayItem
{
    public required SourceId Source { get; init; }
    public double SourceIn { get; set; }
    public int AudioTrack { get; set; }

    /// <summary>Null means mute the b-roll under the voice.</summary>
    public double? GainDb { get; set; }

    public FitMode Fit { get; set; } = FitMode.Auto;

    /// <summary>Loop if the b-roll is shorter than the span it covers.</summary>
    public bool Loop { get; set; } = true;

    public override string Describe() => "b-roll";
}

public sealed class TitleItem : OverlayItem
{
    public string Text { get; set; } = string.Empty;
    public Placement Placement { get; set; } = Placement.LowerThird;
    public TitleStyle Style { get; set; } = TitleStyle.LowerThird;

    public override string Describe() => $"title \"{Text}\", {Placement.Describe()}";
}

public sealed class GraphicItem : OverlayItem
{
    public required SourceId Source { get; init; }
    public Placement Placement { get; set; } = Placement.Centre;

    /// <summary>Fraction of canvas width the graphic occupies.</summary>
    public double Scale { get; set; } = 0.25;

    public double Opacity { get; set; } = 1.0;

    public override string Describe() => $"graphic, {Placement.Describe()}";
}

/// <summary>
/// The same composition as a <see cref="CardElement"/>, riding over the video
/// instead of replacing it. A lower third is this with a transparent
/// background - which is why there is one card editor rather than two.
/// </summary>
public sealed class CardItem : OverlayItem
{
    public CardComposition Composition { get; set; } = new();

    public override string Describe() => Composition.Describe();
}

/// <summary>
/// A piece of audio on an audio track - most often a segment's own sound, moved
/// off the picture so the two can be cut independently.
///
/// <see cref="LinkedTo"/> keeps the pair together: editing one moves the other
/// until they are deliberately unlinked. Without it, detaching audio silently
/// creates two things that drift apart, which is unrecoverable by ear.
/// </summary>
public sealed class AudioItem : OverlayItem
{
    public required SourceId Source { get; init; }
    public double SourceIn { get; set; }
    public double GainDb { get; set; }
    public int AudioTrack { get; set; }

    /// <summary>The segment this was detached from, if any.</summary>
    public ElementId? LinkedTo { get; set; }

    public override string Describe() =>
        LinkedTo is null ? "audio" : "detached audio";
}

/// <summary>One bed for the body of the video, looped, faded, side-chain ducked.</summary>
public sealed class MusicItem : OverlayItem
{
    public required SourceId Source { get; init; }
    public double GainDb { get; set; } = -20;

    /// <summary>Ducking depth in dB under anything on the programme track. 0 disables.</summary>
    public double DuckDb { get; set; } = 9;

    public double FadeIn { get; set; } = 2;
    public double FadeOut { get; set; } = 4;

    public override string Describe() => $"music bed, {GainDb:0} dB";
}

public enum TitleStyle
{
    LowerThird,
    Full,
    Corner,
}
