using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Editing;

/// <summary>
/// Turns a measurement of a recording into advice phrased as the commands that
/// would act on it.
///
/// This is the half of an audio problem that normally happens by listening
/// critically to something you have heard forty times, or by looking at a
/// spectrum. The measurements already exist - loudness, true peak, noise floor -
/// and nothing was reading them back as a decision.
///
/// The rule, taken from the image editor's <c>Shift+V</c>: <b>the advice names
/// the thing you then press.</b> "Noisy, floor at -38 decibels" is a
/// measurement; "add room tone removal" is advice; only the second one can be
/// acted on without knowing the theory.
/// </summary>
public static class AudioAdvice
{
    /// <summary>Above this, the noise floor is audible under speech.</summary>
    public const double NoisyFloorDb = -45;

    /// <summary>Broadcast-ish. YouTube normalises to about -14.</summary>
    public const double TargetLoudness = -14;

    /// <summary>Beyond this from target, loudness is worth a command rather than a note.</summary>
    public const double LoudnessTolerance = 3;

    public static AudioAdviceResult For(
        double? loudnessLufs,
        double? truePeakDb,
        double? noiseFloorDb,
        double? dynamicRangeDb = null)
    {
        var findings = new List<string>();
        var suggested = new List<AudioEffect>();

        if (noiseFloorDb is { } floor && floor > NoisyFloorDb)
        {
            // How far above the threshold decides how hard to reduce, so the
            // suggestion is already set roughly right rather than at a default.
            var over = Math.Clamp((floor - NoisyFloorDb) / 20, 0.2, 1.0);

            findings.Add($"the noise floor is at {floor:0} decibels, which is audible under speech");
            suggested.Add(AudioEffect.Of(AudioEffectKind.NoiseReduction, over));
            suggested.Add(AudioEffect.Of(AudioEffectKind.HighPass, 0.4));
        }

        if (truePeakDb is { } peak && peak > -0.5)
        {
            findings.Add($"it peaks at {peak:0.#} decibels, which is effectively clipping");
            suggested.Add(AudioEffect.Of(AudioEffectKind.Compress, 0.6));
            suggested.Add(AudioEffect.Of(AudioEffectKind.Normalise, 0.5));
        }

        if (loudnessLufs is { } lufs)
        {
            var off = TargetLoudness - lufs;

            if (Math.Abs(off) > LoudnessTolerance)
            {
                findings.Add(off > 0
                    ? $"it is {Math.Abs(off):0} decibels quieter than broadcast loudness, at {lufs:0} LUFS"
                    : $"it is {Math.Abs(off):0} decibels louder than broadcast loudness, at {lufs:0} LUFS");

                if (suggested.All(e => e.Kind != AudioEffectKind.Normalise))
                {
                    suggested.Add(AudioEffect.Of(AudioEffectKind.Normalise, 0.5));
                }
            }
        }

        // A wide dynamic range on a talking head is not a virtue: it means the
        // quiet sentences will be lost on a phone speaker.
        if (dynamicRangeDb is { } range && range > 14
            && suggested.All(e => e.Kind != AudioEffectKind.Compress))
        {
            findings.Add($"the loud and quiet parts are {range:0} decibels apart, "
                         + "which will not survive a phone speaker");
            suggested.Add(AudioEffect.Of(AudioEffectKind.Compress, 0.5));
        }

        return new AudioAdviceResult(findings, AudioChains.InOrder(suggested).ToList());
    }

    /// <summary>Reads the advice back from a report the quality analyser produced.</summary>
    public static AudioAdviceResult ForReport(double? loudness, double? peak, double? noiseFloor) =>
        For(loudness, peak, noiseFloor);
}

public sealed record AudioAdviceResult(
    IReadOnlyList<string> Findings,
    IReadOnlyList<AudioEffect> Suggested)
{
    public bool HasAdvice => Suggested.Count > 0;

    /// <summary>
    /// What was measured, then what to do about it, in the words of the
    /// commands. Silence would be wrong here - "nothing to fix" is a result, and
    /// a command that says nothing reads as one that failed.
    /// </summary>
    public string Announce()
    {
        if (Findings.Count == 0) return "the sound measures fine, nothing to fix";

        var what = string.Join(". ", Findings);
        var names = string.Join(", ", Suggested.Select(e => $"{e.Name} at {e.DescribeAmount()}"));

        return $"{what}. Suggested: {names}";
    }
}
