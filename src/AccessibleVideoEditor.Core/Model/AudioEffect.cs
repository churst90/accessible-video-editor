namespace AccessibleVideoEditor.Core.Model;

/// <summary>
/// One audio treatment, named for what it is for.
///
/// A mixer gives you knobs and a spectrum display, and both are answers to
/// "what is happening" that arrive by eye. The replacement is the same one the
/// image editor uses for colour: <b>named presets plus a spoken number</b>. You
/// choose "room tone removal", not "afftdn at 12 dB with noise floor tracking";
/// the number is still said, and can still be nudged, but you never need it to
/// choose.
///
/// Effects are a <b>list you can read back</b>, in order, each describing itself
/// as the sentence that would create it - the same idea as the shape language in
/// the image editor, and for the same reason: a stack of processing you cannot
/// see is a stack you stop trusting.
/// </summary>
public sealed class AudioEffect
{
    public required AudioEffectKind Kind { get; init; }

    /// <summary>
    /// How hard, from 0 to 1, mapped per effect onto whatever unit that effect
    /// actually has. One control per effect on purpose: a compressor with four
    /// numbers is a compressor you have to be taught, and the presets already
    /// encode the shape that matters.
    /// </summary>
    public double Amount { get; set; } = 0.5;

    /// <summary>Off without being removed, so an A/B is one keystroke.</summary>
    public bool Enabled { get; set; } = true;

    public string Name => Kind switch
    {
        AudioEffectKind.NoiseReduction => "room tone removal",
        AudioEffectKind.HighPass => "rumble filter",
        AudioEffectKind.DeEss => "de-ess",
        AudioEffectKind.Presence => "presence lift",
        AudioEffectKind.Warmth => "warmth",
        AudioEffectKind.Compress => "levelling",
        AudioEffectKind.Gate => "noise gate",
        _ => "normalise",
    };

    /// <summary>What it is for, in the words you would use to reach for it.</summary>
    public string Purpose => Kind switch
    {
        AudioEffectKind.NoiseReduction => "takes out steady hiss and room noise",
        AudioEffectKind.HighPass => "takes out traffic, footsteps and desk bumps",
        AudioEffectKind.DeEss => "softens harsh S sounds",
        AudioEffectKind.Presence => "brings the voice forward and makes it clearer",
        AudioEffectKind.Warmth => "adds body to a thin voice",
        AudioEffectKind.Compress => "evens out loud and quiet parts",
        AudioEffectKind.Gate => "silences the gaps between sentences",
        _ => "brings the whole thing to broadcast loudness",
    };

    /// <summary>
    /// The setting in the unit an engineer would say out loud - decibels, hertz,
    /// a ratio - rather than as a percentage of an invisible slider.
    /// </summary>
    public string DescribeAmount() => Kind switch
    {
        AudioEffectKind.NoiseReduction => $"{NoiseReductionDb:0} dB",
        AudioEffectKind.HighPass => $"below {HighPassHz:0} hertz",
        AudioEffectKind.DeEss => $"{Amount * 100:0} percent",
        AudioEffectKind.Presence => $"{PresenceDb:0.#} dB at 3 kilohertz",
        AudioEffectKind.Warmth => $"{WarmthDb:0.#} dB at 200 hertz",
        AudioEffectKind.Compress => $"{CompressRatio:0.#} to 1",
        AudioEffectKind.Gate => $"below {GateDb:0} dB",
        _ => $"{LoudnessTarget:0.#} LUFS",
    };

    /// <summary>Reads back as the sentence that would create it.</summary>
    public string Describe() =>
        $"{Name}, {DescribeAmount()}" + (Enabled ? string.Empty : ", off");

    // ---- the mappings -----------------------------------------------------
    // Ranges are chosen so that the whole span is useful: an effect whose bottom
    // half does nothing audible is one you cannot set by ear.

    public double NoiseReductionDb => 6 + Amount * 18;        // 6-24 dB
    public double HighPassHz => 40 + Amount * 120;            // 40-160 Hz
    public double PresenceDb => Amount * 6;                   // 0-6 dB
    public double WarmthDb => Amount * 5;                     // 0-5 dB
    public double CompressRatio => 1.5 + Amount * 6.5;        // 1.5:1 - 8:1
    public double GateDb => -60 + Amount * 30;                // -60 to -30 dB
    public double LoudnessTarget => -23 + Amount * 9;         // -23 to -14 LUFS

    public static AudioEffect Of(AudioEffectKind kind, double amount = 0.5) =>
        new() { Kind = kind, Amount = Math.Clamp(amount, 0, 1) };
}

/// <summary>
/// Deliberately a short list. Every one of these is a problem a talking-head
/// recording actually has; a full plugin rack would be more capable and less
/// usable, because choosing from thirty things you cannot audition quickly is
/// worse than choosing from eight you can.
/// </summary>
public enum AudioEffectKind
{
    NoiseReduction,
    HighPass,
    Gate,
    DeEss,
    Presence,
    Warmth,
    Compress,
    Normalise,
}

/// <summary>
/// The ready-made chains. A chain is what you actually want - "close mic"
/// rather than four separate decisions - and the order inside it matters and is
/// not something anyone should have to know.
/// </summary>
public static class AudioChains
{
    public static IReadOnlyList<(string Name, string Purpose, AudioEffectKind[] Kinds)> Presets { get; } =
    [
        ("Close mic", "a microphone near your mouth in a normal room",
            [AudioEffectKind.HighPass, AudioEffectKind.NoiseReduction,
             AudioEffectKind.Compress, AudioEffectKind.Presence]),

        ("Laptop mic", "a built-in microphone, which needs all the help there is",
            [AudioEffectKind.HighPass, AudioEffectKind.NoiseReduction,
             AudioEffectKind.Presence, AudioEffectKind.Compress, AudioEffectKind.Normalise]),

        ("Noisy room", "traffic, a fan, or a computer you can hear",
            [AudioEffectKind.HighPass, AudioEffectKind.NoiseReduction, AudioEffectKind.Gate]),

        ("Voice polish", "a good recording that wants finishing",
            [AudioEffectKind.Compress, AudioEffectKind.Presence, AudioEffectKind.Normalise]),

        ("Broadcast", "levelled and normalised, nothing else",
            [AudioEffectKind.Compress, AudioEffectKind.Normalise]),
    ];

    public static List<AudioEffect>? Build(string name)
    {
        var preset = Presets.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        return preset.Kinds is null
            ? null
            : preset.Kinds.Select(k => AudioEffect.Of(k)).ToList();
    }

    /// <summary>
    /// The order effects are applied in, regardless of the order they were
    /// added. Filtering before compression, compression before loudness: a
    /// compressor pumping on rumble you were about to remove is a mistake you
    /// would have to know the theory to predict.
    /// </summary>
    public static int Stage(AudioEffectKind kind) => kind switch
    {
        AudioEffectKind.HighPass => 0,
        AudioEffectKind.NoiseReduction => 1,
        AudioEffectKind.Gate => 2,
        AudioEffectKind.DeEss => 3,
        AudioEffectKind.Warmth => 4,
        AudioEffectKind.Presence => 5,
        AudioEffectKind.Compress => 6,
        _ => 7,
    };

    /// <summary>Reads a whole chain back, in the order it will actually run.</summary>
    public static string Describe(IReadOnlyList<AudioEffect> chain)
    {
        if (chain.Count == 0) return "no effects";

        var live = chain.Where(e => e.Enabled).ToList();
        if (live.Count == 0) return $"{chain.Count} effects, all off";

        return string.Join(", then ", InOrder(chain).Select(e => e.Describe()));
    }

    public static IEnumerable<AudioEffect> InOrder(IEnumerable<AudioEffect> chain) =>
        chain.OrderBy(e => Stage(e.Kind));
}
