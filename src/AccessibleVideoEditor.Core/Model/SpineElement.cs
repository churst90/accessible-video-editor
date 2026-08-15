using System.Text.Json.Serialization;

namespace AccessibleVideoEditor.Core.Model;

/// <summary>
/// The spine is the ordered list that defines programme time. Everything else
/// in the project anchors to it.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SpanElement), "span")]
[JsonDerivedType(typeof(ClipElement), "clip")]
[JsonDerivedType(typeof(HoleElement), "hole")]
[JsonDerivedType(typeof(PauseElement), "pause")]
[JsonDerivedType(typeof(CardElement), "card")]
public abstract class SpineElement
{
    /// <summary>
    /// Assigned once at creation and treated as immutable thereafter. Settable
    /// only so a clipboard paste can mint a fresh ID on the copy.
    /// </summary>
    public required ElementId Id { get; set; }

    /// <summary>
    /// False is the non-destructive cut - the <c>x </c> prefix in edit.md. The
    /// element keeps its place and its ID, contributes no programme time, and
    /// can be restored.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The boundary treatment entering this element. Null means project default.</summary>
    public Transition? TransitionIn { get; set; }

    /// <summary>
    /// Three separate states, because collapsing them makes the announcement
    /// ambiguous and the ambiguity is unrecoverable without sight:
    /// <list type="bullet">
    /// <item><b>Muted</b> - audio silenced, picture stays. "audio muted".</item>
    /// <item><b>Hidden</b> - picture removed, audio stays. "picture hidden".</item>
    /// <item><b>Enabled false</b> - gone from the programme entirely.</item>
    /// </list>
    /// </summary>
    public bool Muted { get; set; }

    /// <summary>Picture removed, audio still plays. See <see cref="Muted"/>.</summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// What appears as a caption over this segment. Null falls back to the
    /// transcript text for speech, and to nothing for everything else.
    /// </summary>
    public string? Caption { get; set; }

    /// <summary>
    /// A title card or a b-roll cutaway usually should not carry the
    /// narration's caption, so this is per segment rather than global.
    /// </summary>
    public bool Captioned { get; set; } = true;

    /// <summary>
    /// Alternative recordings of this segment. Empty for everything that was
    /// not recorded into. When present, the active take supplies the media the
    /// segment plays - the segment keeps its identity, its position and
    /// anything anchored to it.
    /// </summary>
    public List<Take> Takes { get; set; } = [];

    public int TakeIndex { get; set; }

    [JsonIgnore]
    public Take? ActiveTake =>
        Takes.Count == 0 ? null : Takes[Math.Clamp(TakeIndex, 0, Takes.Count - 1)];

    /// <summary>The text that will actually be burned into captions.srt.</summary>
    public string? EffectiveCaption =>
        !Captioned ? null : Caption ?? (this as SpanElement)?.Text;

    /// <summary>
    /// Fade in at the head of this segment, in seconds.
    ///
    /// A fade belongs to a <b>segment</b>; a transition belongs to the boundary
    /// <i>between</i> two. Fading up from black at the top of a video is a fade;
    /// dissolving between two shots is a transition. They are not the same
    /// thing and collapsing them would make one of them unexpressible.
    /// </summary>
    public double FadeIn { get; set; }

    public double FadeOut { get; set; }

    /// <summary>
    /// What a fade touches. Both by default: fading the picture up from black
    /// while the sound arrives at full volume is almost never what anyone
    /// wants, and it is not something you would notice without watching.
    /// </summary>
    public FadeTarget FadeTarget { get; set; } = FadeTarget.Both;

    /// <summary>Reads back the fades that are actually set.</summary>
    public string? DescribeFades()
    {
        if (FadeIn <= 0 && FadeOut <= 0) return null;

        var what = FadeTarget switch
        {
            FadeTarget.Video => "picture",
            FadeTarget.Audio => "sound",
            _ => "picture and sound",
        };

        var parts = new List<string>();
        if (FadeIn > 0) parts.Add($"fade in {FadeIn:0.##} seconds");
        if (FadeOut > 0) parts.Add($"fade out {FadeOut:0.##} seconds");

        return $"{string.Join(", ", parts)}, {what}";
    }

    /// <summary>
    /// Audio treatment for this segment alone - a fix for one bad take rather
    /// than a property of the microphone. Track effects handle the latter, and
    /// keeping them separate is what makes it possible to say which one you just
    /// changed. Track effects run first; see <see cref="Track.Effects"/>.
    /// </summary>
    public List<AudioEffect> Effects { get; set; } = [];

    /// <summary>
    /// Values that change over this segment - volume ducking, an opacity ramp.
    /// Named shapes rather than keyframes; see <see cref="Automation"/>.
    /// </summary>
    public List<Automation> Automation { get; set; } = [];

    /// <summary>
    /// A slow move across a still. Meaningless on moving footage and ignored
    /// there.
    /// </summary>
    public KenBurns KenBurns { get; set; } = KenBurns.None;

    /// <summary>
    /// Playback rate. 2.0 plays twice as fast and takes half as long. Applied
    /// to media-backed elements only; a hole or a pause has an explicit length
    /// instead.
    /// </summary>
    public double Speed { get; set; } = 1.0;

    /// <summary>Length in programme time, before transition overlap.</summary>
    [JsonIgnore]
    public abstract double Duration { get; }

    /// <summary>Short phrase for the announcer when the cursor lands here.</summary>
    public abstract string Describe();
}

/// <summary>A run of speech from a transcribed take. The unit the transcript editor works in.</summary>
public sealed class SpanElement : SpineElement
{
    /// <summary>
    /// Settable, not init-only: cutting to another camera angle changes which
    /// file a segment plays while it keeps its identity, its place, its words
    /// and anything anchored to it - the same rule takes already follow.
    /// </summary>
    public required SourceId Source { get; set; }
    public required double SourceIn { get; set; }
    public required double SourceOut { get; set; }

    /// <summary>
    /// Editing this changes captions only, never the cut - the rule inherited
    /// from edit.md. The UI has to say so loudly, because it surprises people.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Word-level timings, when available. Drives word-granularity navigation.</summary>
    public List<Word> Words { get; set; } = [];

    public override double Duration => Math.Max(0, SourceOut - SourceIn);

    public override string Describe() =>
        Text.Length > 0 ? Text : $"speech, {Timecode.Speak(Duration)}";
}

public readonly record struct Word(string Text, double Start, double End);

/// <summary>
/// A cutaway that takes over picture and sound - the "let people hear the thing
/// I am talking about" element. Narration stops for its duration.
/// </summary>
public sealed class ClipElement : SpineElement
{
    /// <summary>Settable for the same reason as <see cref="SpanElement.Source"/>.</summary>
    public required SourceId Source { get; set; }
    public double SourceIn { get; set; }
    public required double SourceOut { get; set; }
    public int AudioTrack { get; set; }
    public double GainDb { get; set; }
    public FitMode Fit { get; set; } = FitMode.Auto;

    public override double Duration => Math.Max(0, SourceOut - SourceIn);

    public override string Describe() => $"clip, {Timecode.Speak(Duration)}";
}

/// <summary>
/// Deliberately blank space, reserved to be filled later. Renders as black with
/// the note burned in on drafts, appears in the To-Do pane, and blocks the
/// master render as a lint error so it cannot ship by accident.
/// </summary>
public sealed class HoleElement : SpineElement
{
    public required double Length { get; set; }
    public string Note { get; set; } = string.Empty;

    public override double Duration => Math.Max(0, Length);

    public override string Describe() =>
        $"hole, {Timecode.Speak(Duration)}{(Note.Length > 0 ? $" - {Note}" : string.Empty)}";
}

/// <summary>
/// A full-screen composed screen - a title card, a section break, an end
/// screen. Narration stops for its duration, which is what distinguishes it
/// from the same composition sitting on the graphics track as an overlay.
/// </summary>
public sealed class CardElement : SpineElement
{
    public required double Length { get; set; }

    public CardComposition Composition { get; set; } = new();

    public override double Duration => Math.Max(0, Length);

    public override string Describe() => $"{Composition.Describe()}, {Timecode.Speak(Duration)}";
}

/// <summary>A beat of black and silence. Distinct from a hole: this one is finished.</summary>
public sealed class PauseElement : SpineElement
{
    public required double Length { get; set; }

    public override double Duration => Math.Max(0, Length);

    public override string Describe() => $"pause, {Timecode.Speak(Duration)}";
}

/// <summary>
/// The slow drift given to a still. Named for the documentary convention of
/// moving over photographs rather than cutting between them.
/// </summary>
public enum KenBurns
{
    None,
    ZoomIn,
    ZoomOut,
    PanLeft,
    PanRight,
}

/// <summary>What a fade applies to.</summary>
public enum FadeTarget
{
    Both,
    Video,
    Audio,
}

public enum FitMode
{
    /// <summary>Pick per source; pillarboxes 16:10 screen captures onto a 16:9 canvas.</summary>
    Auto,

    /// <summary>Crop to fill the canvas.</summary>
    Fill,

    /// <summary>Letterbox to fit.</summary>
    Fit,
}
