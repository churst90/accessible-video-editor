namespace AccessibleVideoEditor.Core.Model;

/// <summary>
/// Boundary treatment entering an element. Boundaries are navigable objects in
/// the scrubber, so a transition is something you can move to, hear auditioned,
/// and change - not a property buried in a dialog.
/// </summary>
public sealed class Transition
{
    public static Transition Cut => new() { Type = TransitionType.Cut, Duration = 0 };

    public TransitionType Type { get; set; } = TransitionType.Fade;

    public double Duration { get; set; } = 0.4;

    /// <summary>
    /// Any ffmpeg xfade name when <see cref="Type"/> is
    /// <see cref="TransitionType.Custom"/>. The short list above covers what
    /// reads as professional; the exotic ones read as amateur.
    /// </summary>
    public string? CustomType { get; set; }

    /// <summary>
    /// A sound under the transition - a whoosh under a wipe. Held here rather
    /// than as an item on a track because it belongs to the boundary: move the
    /// cut and the sound moves with it.
    /// </summary>
    public string? SoundPath { get; set; }

    /// <summary>Level of that sound, in decibels relative to the file.</summary>
    public double SoundGainDb { get; set; } = -6;

    /// <summary>
    /// How much to pull the programme down under the sound. <b>Zero by
    /// default</b>: automatic ducking that you did not ask for is a mix
    /// decision made on your behalf, and the track faders are there to make it
    /// deliberately.
    /// </summary>
    public double DuckDb { get; set; }

    public bool HasSound => SoundPath is { Length: > 0 };

    /// <summary>
    /// The expression for a transition you wrote yourself. Passed straight to
    /// ffmpeg's <c>xfade</c>, which hands it both frames and the progress
    /// through the transition.
    /// </summary>
    public string? Expression { get; set; }

    public string FfmpegName => Type switch
    {
        TransitionType.Cut => "cut",
        TransitionType.Fade => "fade",
        TransitionType.FadeBlack => "fadeblack",
        TransitionType.WipeLeft => "wipeleft",
        TransitionType.WipeRight => "wiperight",
        TransitionType.SlideLeft => "slideleft",
        TransitionType.SlideRight => "slideright",
        TransitionType.Custom => CustomType ?? "fade",
        _ => "fade",
    };

    public string Describe()
    {
        if (Type == TransitionType.Cut && !HasSound) return "cut";

        var parts = new List<string> { FfmpegName, $"{Duration:0.##} seconds" };

        if (HasSound)
        {
            parts.Add($"with {System.IO.Path.GetFileNameWithoutExtension(SoundPath)}");

            if (Math.Abs(DuckDb) > 0.01) parts.Add($"ducking {DuckDb:0.#} dB");
        }

        return string.Join(", ", parts);
    }

    public Transition Copy() => new()
    {
        Type = Type,
        Duration = Duration,
        CustomType = CustomType,
        Expression = Expression,
        SoundPath = SoundPath,
        SoundGainDb = SoundGainDb,
        DuckDb = DuckDb,
    };
}

public enum TransitionType
{
    Cut,
    Fade,
    FadeBlack,
    WipeLeft,
    WipeRight,
    SlideLeft,
    SlideRight,
    Custom,
}
