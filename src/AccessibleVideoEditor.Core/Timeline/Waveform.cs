using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Timeline;

/// <summary>
/// The peaks of one audio file, evenly spaced in source time.
///
/// Stored once per source rather than per segment: a waveform belongs to the
/// media, not to the edit, so splitting a clip in half does not mean extracting
/// anything again. Drawing a block then means resampling the slice of source
/// time that block plays - which is <see cref="Slice"/>, and is pure arithmetic.
/// </summary>
public sealed record WaveformData(SourceId Source, double Duration, IReadOnlyList<float> Peaks)
{
    public double SecondsPerPeak => Peaks.Count == 0 ? 0 : Duration / Peaks.Count;

    /// <summary>
    /// The peaks for one segment, resampled to however many columns of pixels
    /// it occupies. Each output bucket takes the <b>maximum</b> of the peaks it
    /// covers, never the mean: averaging a waveform down to screen resolution
    /// flattens transients and makes loud material look quiet.
    /// </summary>
    public float[] Slice(double fromSeconds, double toSeconds, int buckets)
    {
        if (buckets <= 0 || Peaks.Count == 0) return [];

        var result = new float[buckets];
        if (toSeconds <= fromSeconds) return result;

        var perPeak = SecondsPerPeak;
        if (perPeak <= 0) return result;

        var span = (toSeconds - fromSeconds) / buckets;

        for (var i = 0; i < buckets; i++)
        {
            var start = (int)Math.Floor((fromSeconds + i * span) / perPeak);
            var end = (int)Math.Ceiling((fromSeconds + (i + 1) * span) / perPeak);

            start = Math.Clamp(start, 0, Peaks.Count - 1);
            end = Math.Clamp(end, start + 1, Peaks.Count);

            var peak = 0f;
            for (var p = start; p < end; p++)
            {
                if (Peaks[p] > peak) peak = Peaks[p];
            }

            result[i] = peak;
        }

        return result;
    }

    /// <summary>
    /// Peaks from raw 16-bit mono samples, bucketed to a fixed resolution.
    /// Separate from the extraction so it can be tested without ffmpeg.
    /// </summary>
    public static WaveformData FromSamples(
        SourceId source,
        ReadOnlySpan<short> samples,
        int sampleRate,
        int peaksPerSecond = 100)
    {
        if (sampleRate <= 0 || samples.Length == 0)
        {
            return new WaveformData(source, 0, []);
        }

        var duration = (double)samples.Length / sampleRate;
        var perBucket = Math.Max(1, sampleRate / Math.Max(1, peaksPerSecond));
        var buckets = (int)Math.Ceiling((double)samples.Length / perBucket);

        var peaks = new float[buckets];

        for (var b = 0; b < buckets; b++)
        {
            var start = b * perBucket;
            var end = Math.Min(samples.Length, start + perBucket);

            var peak = 0;
            for (var i = start; i < end; i++)
            {
                var value = Math.Abs((int)samples[i]);
                if (value > peak) peak = value;
            }

            peaks[b] = peak / 32768f;
        }

        return new WaveformData(source, duration, peaks);
    }
}
