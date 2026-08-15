using System.Globalization;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// Turns an effect chain into ffmpeg filters.
///
/// Every filter here is in the installed ffmpeg already - no plugin rack, no
/// rebuild. The mapping is the interesting part: each effect exposes one number
/// to the user and sets several here, because the extra numbers are the ones
/// that have a right answer rather than a preference.
/// </summary>
public static class AudioEffectFilters
{
    /// <summary>
    /// The chain, in processing order, as a comma-separated filter string. Empty
    /// when nothing is enabled, so callers can append it unconditionally.
    /// </summary>
    public static string Build(IReadOnlyList<AudioEffect> chain)
    {
        var steps = AudioChains.InOrder(chain.Where(e => e.Enabled))
                               .Select(Filter)
                               .Where(f => f.Length > 0);

        return string.Join(',', steps);
    }

    public static string Filter(AudioEffect effect) => effect.Kind switch
    {
        // Two poles rather than one: a single-pole roll-off at 80 Hz still
        // passes plenty of 50 Hz, which is exactly the rumble being removed.
        AudioEffectKind.HighPass =>
            $"highpass=f={N(effect.HighPassHz)}:poles=2",

        // afftdn tracks the noise floor rather than being told it, so it works
        // on a take whose room changed halfway through.
        AudioEffectKind.NoiseReduction =>
            $"afftdn=nr={N(effect.NoiseReductionDb)}:nf=-40:tn=1",

        // A slow release, because a gate that snaps shut between words sounds
        // worse than the noise it removed.
        AudioEffectKind.Gate =>
            $"agate=threshold={N(Db(effect.GateDb))}:ratio=2:attack=10:release=250",

        // A compressor keyed on the sibilance band is what a de-esser is; there
        // is no dedicated filter for it in this build.
        AudioEffectKind.DeEss =>
            $"deesser=i={N(effect.Amount)}:m=0.5:f=0.5",

        AudioEffectKind.Presence =>
            $"equalizer=f=3000:t=q:w=1.2:g={N(effect.PresenceDb)}",

        AudioEffectKind.Warmth =>
            $"equalizer=f=200:t=q:w=1.0:g={N(effect.WarmthDb)}",

        // Makeup gain rises with the ratio: compressing without it just makes
        // everything quieter, which reads as the effect having done nothing.
        AudioEffectKind.Compress =>
            $"acompressor=threshold=-18dB:ratio={N(effect.CompressRatio)}"
            + $":attack=5:release=150:makeup={N(1 + effect.Amount * 2)}",

        // Single-pass loudnorm. The two-pass form is more accurate and needs a
        // full analysis run first, which is not worth it inside a segment render.
        AudioEffectKind.Normalise =>
            $"loudnorm=I={N(effect.LoudnessTarget)}:TP=-1.5:LRA=11",

        _ => string.Empty,
    };

    /// <summary>agate wants a linear threshold, not decibels.</summary>
    private static double Db(double decibels) => Math.Pow(10, decibels / 20);

    private static string N(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);
}
