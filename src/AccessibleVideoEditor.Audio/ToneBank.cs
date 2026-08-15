namespace AccessibleVideoEditor.Audio;

/// <summary>
/// The synthesiser behind earcons and the level tick.
///
/// Deliberately tiny: sine partials with a short envelope, mixed additively.
/// Everything this application needs to say without words is a short pitched
/// blip, and anything richer would be decoration that costs latency.
///
/// Pure and sample-based so it can be tested without an audio device.
/// </summary>
public sealed class ToneBank
{
    private readonly List<Voice> _voices = [];
    private readonly object _gate = new();

    public int SampleRate { get; init; } = 48000;

    /// <summary>Master level. Earcons sit under speech, never over it.</summary>
    public double Gain { get; set; } = 0.22;

    public int ActiveVoices
    {
        get
        {
            lock (_gate) return _voices.Count;
        }
    }

    /// <summary>
    /// Starts a blip. <paramref name="pan"/> is -1 left to 1 right, which the
    /// viewfinder uses to put a tone where your face is.
    /// </summary>
    public void Play(double frequency, double seconds, double amplitude = 1.0, double pan = 0)
    {
        lock (_gate)
        {
            // A backlog of overlapping blips turns into mush; the newest is
            // always the one that matters.
            if (_voices.Count > 6) _voices.RemoveRange(0, _voices.Count - 6);

            _voices.Add(new Voice
            {
                Frequency = frequency,
                TotalSamples = Math.Max(1, (int)(seconds * SampleRate)),
                Amplitude = amplitude,
                Pan = Math.Clamp(pan, -1, 1),
            });
        }
    }

    public void Silence()
    {
        lock (_gate) _voices.Clear();
    }

    /// <summary>
    /// Fills an interleaved stereo buffer. Called from the audio callback, so
    /// it allocates nothing.
    /// </summary>
    public void Fill(Span<float> stereo)
    {
        stereo.Clear();

        lock (_gate)
        {
            for (var v = _voices.Count - 1; v >= 0; v--)
            {
                var voice = _voices[v];

                for (var i = 0; i < stereo.Length; i += 2)
                {
                    if (voice.Position >= voice.TotalSamples) break;

                    var sample = Math.Sin(
                        2 * Math.PI * voice.Frequency * voice.Position / SampleRate);

                    var value = (float)(sample * Envelope(voice) * voice.Amplitude * Gain);

                    // Equal-power panning, so moving across the field does not
                    // change how loud the tone seems.
                    var angle = (voice.Pan + 1) * Math.PI / 4;

                    stereo[i] += (float)(value * Math.Cos(angle));
                    stereo[i + 1] += (float)(value * Math.Sin(angle));

                    voice.Position++;
                }

                if (voice.Position >= voice.TotalSamples) _voices.RemoveAt(v);
            }
        }

        for (var i = 0; i < stereo.Length; i++)
        {
            stereo[i] = Math.Clamp(stereo[i], -1f, 1f);
        }
    }

    /// <summary>
    /// A short rise and a longer fall. Without the rise every blip starts with
    /// a click; without the fall it ends with one.
    /// </summary>
    private static double Envelope(Voice voice)
    {
        var attack = Math.Min(64, voice.TotalSamples / 4);
        var release = Math.Min(voice.TotalSamples / 2, voice.TotalSamples - attack);

        if (voice.Position < attack) return (double)voice.Position / attack;

        var remaining = voice.TotalSamples - voice.Position;
        return remaining < release ? (double)remaining / release : 1.0;
    }

    private sealed class Voice
    {
        public double Frequency;
        public int TotalSamples;
        public int Position;
        public double Amplitude;
        public double Pan;
    }
}

/// <summary>
/// What each earcon sounds like.
///
/// The set is small and the pitches are spaced widely, because earcons are only
/// useful if they are told apart instantly - a family of similar beeps is worse
/// than none.
/// </summary>
public static class Earcons
{
    public static (double Frequency, double Seconds, double Amplitude) Voice(
        AccessibleVideoEditor.Speech.Earcon earcon) => earcon switch
    {
        AccessibleVideoEditor.Speech.Earcon.Boundary => (880, 0.035, 0.7),
        AccessibleVideoEditor.Speech.Earcon.Transition => (660, 0.09, 0.7),
        AccessibleVideoEditor.Speech.Earcon.TitleOn => (1320, 0.05, 0.55),
        AccessibleVideoEditor.Speech.Earcon.BrollEnter => (520, 0.07, 0.7),
        AccessibleVideoEditor.Speech.Earcon.BrollExit => (440, 0.07, 0.6),
        AccessibleVideoEditor.Speech.Earcon.HoleEnter => (300, 0.12, 0.8),
        AccessibleVideoEditor.Speech.Earcon.SelectionEdge => (990, 0.03, 0.6),
        AccessibleVideoEditor.Speech.Earcon.Start => (700, 0.06, 0.7),
        AccessibleVideoEditor.Speech.Earcon.End => (350, 0.14, 0.8),
        AccessibleVideoEditor.Speech.Earcon.Refused => (180, 0.16, 0.9),
        AccessibleVideoEditor.Speech.Earcon.Confirmed => (1050, 0.04, 0.5),

        // Chat, ordered by how much each wants you. Being named is the highest
        // and brightest; a moderator is low and short so it does not nag.
        AccessibleVideoEditor.Speech.Earcon.ChatMention => (1560, 0.07, 0.55),
        AccessibleVideoEditor.Speech.Earcon.ChatFirstTime => (1180, 0.06, 0.5),
        AccessibleVideoEditor.Speech.Earcon.ChatQuestion => (940, 0.05, 0.45),
        AccessibleVideoEditor.Speech.Earcon.ChatModerator => (620, 0.04, 0.4),
        AccessibleVideoEditor.Speech.Earcon.ChatEvent => (1400, 0.10, 0.6),

        // Going on air must be unmistakable, but not by being longer: every
        // earcon here stays under a fifth of a second so it cannot delay the
        // next one. These are told apart by being the highest and the loudest.
        AccessibleVideoEditor.Speech.Earcon.SceneSwitch => (760, 0.05, 0.5),
        AccessibleVideoEditor.Speech.Earcon.OnAir => (1200, 0.19, 0.9),
        AccessibleVideoEditor.Speech.Earcon.OffAir => (400, 0.19, 0.85),
        _ => (800, 0.04, 0.6),
    };
}
