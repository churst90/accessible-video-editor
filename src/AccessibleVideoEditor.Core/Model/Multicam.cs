using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Model;

/// <summary>
/// Several cameras pointed at the same thing, lined up in time, with one key
/// per angle to cut between them.
///
/// The recording side of this already worked - nothing stops you recording two
/// cameras. What was missing was the two things that make the footage usable:
/// knowing how far apart the files start, and being able to switch without
/// finding the same moment in each one by hand.
///
/// The sync is done by <b>sound</b>, not by timecode, because two consumer
/// cameras have no shared clock and a clap is something you can do without
/// seeing anything. See <see cref="MulticamSync"/>.
/// </summary>
public sealed class MulticamGroup
{
    public required GroupId Id { get; init; }

    public required string Name { get; set; }

    public List<CameraAngle> Angles { get; set; } = [];

    /// <summary>
    /// Which angle new cuts use. Kept so that switching announces a change
    /// rather than restating - "still angle 2" is noise.
    /// </summary>
    public int ActiveAngle { get; set; }

    public CameraAngle? Active =>
        Angles.Count == 0 ? null : Angles[Math.Clamp(ActiveAngle, 0, Angles.Count - 1)];

    public string Describe() =>
        $"{Name}, {Angles.Count} angle{(Angles.Count == 1 ? string.Empty : "s")}"
        + (Active is { } active ? $", on {active.Name}" : string.Empty);
}

/// <summary>
/// One camera in a multicam group: which file it is, what to call it, and how
/// far its recording started from the reference angle's.
/// </summary>
public sealed class CameraAngle
{
    public required SourceId Source { get; init; }

    /// <summary>
    /// What you would call it out loud - "wide", "close", "screen". Never "angle
    /// 2": a number tells you where it is in a list and nothing about what you
    /// would be cutting to, which is the whole question when switching blind.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Seconds to add to this angle's source time to reach the reference
    /// angle's. Positive means this camera started recording <i>before</i> the
    /// reference.
    /// </summary>
    public double Offset { get; set; }

    /// <summary>
    /// How well the sync matched, 0 to 1. Kept and spoken because a confident
    /// wrong answer is the failure mode of audio sync, and a low number is the
    /// only warning available.
    /// </summary>
    public double SyncConfidence { get; set; }

    public bool Synced => SyncConfidence > 0;

    public string Describe() =>
        Name + (Synced
            ? $", offset {Offset:0.###} seconds, {MulticamSync.DescribeConfidence(SyncConfidence)}"
            : ", not synced");
}

/// <summary>
/// Lining several recordings up by their sound.
///
/// The method is envelope cross-correlation: take the loudness over time of
/// each recording, slide one against the other, and find the shift where they
/// match best. It works on the peak data already cached for drawing waveforms,
/// so it needs no extra decoding, and it is robust to the two cameras having
/// completely different microphones - which correlating raw samples is not.
/// </summary>
public static class MulticamSync
{
    /// <summary>
    /// Below this, the answer is a guess. Two recordings of genuinely different
    /// moments will still produce a best shift, and reporting that as a sync is
    /// how a whole edit ends up a second out.
    /// </summary>
    public const double MinimumConfidence = 0.35;

    /// <summary>
    /// How far apart two recordings are allowed to have started, in seconds.
    /// Searching further costs time and finds spurious matches; if two cameras
    /// started more than five minutes apart, they are not the same take.
    /// </summary>
    public const double MaximumOffset = 300;

    /// <summary>
    /// The offset, in seconds, to add to <paramref name="candidate"/>'s time to
    /// reach <paramref name="reference"/>'s, plus how well it matched.
    /// </summary>
    public static SyncResult Align(WaveformData reference, WaveformData candidate)
    {
        var a = Normalise(reference.Peaks);
        var b = Normalise(candidate.Peaks);

        if (a.Length < 8 || b.Length < 8) return new SyncResult(0, 0, "too short to sync by sound");

        // Peaks are evenly spaced across the whole file, so the time each one
        // covers falls out of the duration rather than needing a sample rate.
        var secondsPerPeak = reference.Duration / Math.Max(1, a.Length);
        if (secondsPerPeak <= 0) return new SyncResult(0, 0, "no duration to sync against");

        var limit = (int)Math.Min(
            Math.Max(a.Length, b.Length),
            Math.Round(MaximumOffset / secondsPerPeak));

        var bestShift = 0;
        var best = double.NegativeInfinity;

        for (var shift = -limit; shift <= limit; shift++)
        {
            var score = Correlate(a, b, shift);
            if (score <= best) continue;

            best = score;
            bestShift = shift;
        }

        var confidence = Math.Clamp(best, 0, 1);
        var offset = bestShift * secondsPerPeak;

        return confidence < MinimumConfidence
            ? new SyncResult(offset, confidence,
                $"the sound did not match well enough to trust - {DescribeConfidence(confidence)}")
            : new SyncResult(offset, confidence, DescribeOffset(offset, confidence));
    }

    /// <summary>
    /// Normalised cross-correlation over the overlapping part only, so a shift
    /// that leaves two samples overlapping cannot win by having little to
    /// disagree about.
    /// </summary>
    private static double Correlate(float[] a, float[] b, int shift)
    {
        var from = Math.Max(0, shift);
        var to = Math.Min(a.Length, b.Length + shift);

        var overlap = to - from;
        if (overlap < 8) return double.NegativeInfinity;

        double sum = 0, sumA = 0, sumB = 0;

        for (var i = from; i < to; i++)
        {
            double x = a[i];
            double y = b[i - shift];

            sum += x * y;
            sumA += x * x;
            sumB += y * y;
        }

        if (sumA <= 0 || sumB <= 0) return double.NegativeInfinity;

        var score = sum / Math.Sqrt(sumA * sumB);

        // Weighted by how much actually overlapped: a perfect match on a tenth
        // of the file is worth less than a good match on all of it.
        return score * Math.Min(1, (double)overlap / Math.Min(a.Length, b.Length));
    }

    /// <summary>
    /// Mean-removed, so a recording with a higher noise floor does not correlate
    /// with everything. This is what makes two different microphones comparable.
    /// </summary>
    private static float[] Normalise(IReadOnlyList<float> peaks)
    {
        if (peaks.Count == 0) return [];

        var mean = peaks.Average();
        return peaks.Select(p => p - mean).ToArray();
    }

    public static string DescribeConfidence(double confidence) => confidence switch
    {
        >= 0.8 => "a strong match",
        >= 0.6 => "a good match",
        >= MinimumConfidence => "a weak match, worth checking",
        _ => "no real match",
    };

    private static string DescribeOffset(double offset, double confidence)
    {
        var direction = offset switch
        {
            > 0.001 => $"started {Math.Abs(offset):0.###} seconds earlier",
            < -0.001 => $"started {Math.Abs(offset):0.###} seconds later",
            _ => "started at the same moment",
        };

        return $"{direction}, {DescribeConfidence(confidence)}";
    }
}

public readonly record struct SyncResult(double Offset, double Confidence, string Announce)
{
    public bool Trustworthy => Confidence >= MulticamSync.MinimumConfidence;
}
