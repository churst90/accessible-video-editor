using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// Measures a piece of media and says what is wrong with it.
///
/// This is the part that replaces looking. Exposure, colour cast, focus and
/// levels are all things a sighted editor takes in at a glance and a blind one
/// cannot check at all - and they are the difference between footage that looks
/// considered and footage that looks amateur.
///
/// The measuring is ffmpeg's; the judgement is here, and it is pure so it can
/// be tested against known numbers.
/// </summary>
public sealed partial class QualityAnalyser(string ffmpegPath = "ffmpeg")
{
    public async Task<QualityReport> AnalyseAsync(
        string path,
        double atSeconds = 0,
        double seconds = 2,
        CancellationToken ct = default)
    {
        var output = await RunAsync(
        [
            "-hide_banner", "-nostats",
            "-ss", Number(atSeconds), "-t", Number(seconds), "-i", path,
            "-vf", "signalstats,metadata=mode=print",
            "-af", "astats=metadata=1:reset=0,ebur128=peak=true",
            "-f", "null", "-",
        ], ct).ConfigureAwait(false);

        return Interpret(Path.GetFileName(path), output);
    }

    /// <summary>Turns ffmpeg's measurements into findings and advice.</summary>
    public static QualityReport Interpret(string name, string ffmpegOutput)
    {
        var findings = new List<string>();

        var luma = Value(ffmpegOutput, "YAVG");
        var lumaLow = Value(ffmpegOutput, "YLOW");
        var lumaHigh = Value(ffmpegOutput, "YHIGH");
        var u = Value(ffmpegOutput, "UAVG");
        var v = Value(ffmpegOutput, "VAVG");
        var saturation = Value(ffmpegOutput, "SATAVG");

        // Exposure. 16-235 is the usable range for video, so the midpoint is
        // around 125 rather than 128.
        if (luma is { } brightness)
        {
            if (brightness < 60) findings.Add($"under-exposed, average brightness {brightness:0}");
            else if (brightness > 180) findings.Add($"over-exposed, average brightness {brightness:0}");

            if (lumaHigh is > 250) findings.Add("highlights are clipped");
            if (lumaLow is < 8) findings.Add("blacks are crushed");
        }

        // Colour cast. U is blue-yellow, V is red-green, both centred on 128.
        if (u is { } blue && v is { } red)
        {
            var warmth = (red - 128) - (blue - 128);

            if (warmth > 12) findings.Add($"warm colour cast, {warmth:0} points toward red");
            else if (warmth < -12) findings.Add($"cool colour cast, {-warmth:0} points toward blue");
        }

        if (saturation is { } chroma)
        {
            if (chroma < 12) findings.Add("very desaturated, close to monochrome");
            else if (chroma > 110) findings.Add("heavily saturated");
        }

        var loudness = Value(ffmpegOutput, "I:", suffix: "LUFS") ?? IntegratedLoudness(ffmpegOutput);
        var peak = Value(ffmpegOutput, "Peak_level");

        if (loudness is { } lufs)
        {
            if (lufs < -30) findings.Add($"very quiet, {lufs:0} LUFS");
            else if (lufs > -9) findings.Add($"very loud, {lufs:0} LUFS");
        }

        if (peak is > -0.5) findings.Add($"audio peaks at {peak:0.#} decibels, effectively clipping");

        var noise = Value(ffmpegOutput, "Noise_floor");
        if (noise is > -45) findings.Add($"noisy, floor at {noise:0} decibels");

        return new QualityReport(
            name,
            luma,
            u is { } b && v is { } r ? (r - 128) - (b - 128) : null,
            saturation,
            loudness,
            peak,
            findings);
    }

    /// <summary>ebur128 prints its summary in a block rather than as metadata.</summary>
    private static double? IntegratedLoudness(string output) =>
        IntegratedPattern().Match(output) is { Success: true } match
        && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static double? Value(string output, string key, string? suffix = null)
    {
        var pattern = new Regex(
            $@"{Regex.Escape(key)}[=:\s]+(-?\d+(?:\.\d+)?)" + (suffix is null ? string.Empty : $@"\s*{suffix}"),
            RegexOptions.IgnoreCase);

        return pattern.Match(output) is { Success: true } match
               && double.TryParse(
                   match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>
    /// Compares takes against each other.
    ///
    /// This is what makes a set of clips look like one video rather than
    /// several: not whether each is acceptable on its own, but whether they
    /// match. It is invisible without eyes and it is the usual reason amateur
    /// footage looks amateur.
    /// </summary>
    public static string CompareShots(IReadOnlyList<QualityReport> reports)
    {
        var usable = reports.Where(r => r.Brightness is not null).ToList();

        if (usable.Count < 2) return "need at least two takes to compare";

        var averageBrightness = usable.Average(r => r.Brightness!.Value);
        var averageWarmth = usable.Where(r => r.Warmth is not null).Select(r => r.Warmth!.Value).ToList();
        var warmthMean = averageWarmth.Count > 0 ? averageWarmth.Average() : 0;

        var differences = new List<string>();

        foreach (var report in usable)
        {
            var notes = new List<string>();

            var brightnessGap = report.Brightness!.Value - averageBrightness;

            // Roughly a third of a stop per 20 points at video levels.
            if (Math.Abs(brightnessGap) > 15)
            {
                notes.Add($"{Math.Abs(brightnessGap) / 45:0.0} stops {(brightnessGap < 0 ? "darker" : "brighter")}");
            }

            if (report.Warmth is { } warmth && Math.Abs(warmth - warmthMean) > 8)
            {
                notes.Add($"{(warmth > warmthMean ? "warmer" : "cooler")}");
            }

            if (report.Loudness is { } loudness)
            {
                var loudnessMean = usable.Where(r => r.Loudness is not null)
                    .Select(r => r.Loudness!.Value).DefaultIfEmpty(loudness).Average();

                if (Math.Abs(loudness - loudnessMean) > 3)
                {
                    notes.Add($"{Math.Abs(loudness - loudnessMean):0} decibels "
                              + $"{(loudness < loudnessMean ? "quieter" : "louder")}");
                }
            }

            if (notes.Count > 0) differences.Add($"{report.Name} is {string.Join(" and ", notes)} than the rest");
        }

        return differences.Count == 0
            ? "the takes match each other"
            : string.Join(". ", differences);
    }

    private async Task<string> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var info = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException("Could not start ffmpeg.");

        var stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        return stderr;
    }

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"I:\s+(-?\d+(?:\.\d+)?)\s+LUFS")]
    private static partial Regex IntegratedPattern();
}

/// <summary>
/// What was measured, and what is wrong with it. An empty
/// <see cref="Findings"/> means nothing stood out - which is worth saying
/// rather than leaving as silence.
/// </summary>
public sealed record QualityReport(
    string Name,
    double? Brightness,
    double? Warmth,
    double? Saturation,
    double? Loudness,
    double? PeakDb,
    IReadOnlyList<string> Findings)
{
    public string Announce() =>
        Findings.Count == 0
            ? $"{Name} looks and sounds fine"
            : $"{Name}: {string.Join(". ", Findings)}";
}
