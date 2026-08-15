using System.Globalization;

namespace AccessibleVideoEditor.Core;

/// <summary>
/// Time is <c>double</c> seconds throughout the model, matching what ffmpeg
/// accepts and what the Whisper transcripts already produce. Frame-rational
/// time would be more correct at boundaries; see ARCHITECTURE.md for why that
/// trade was made and what it costs.
/// </summary>
public static class Timecode
{
    /// <summary>Renders <c>00:01:23.400</c>, the long form used in edit.md.</summary>
    public static string Format(double seconds)
    {
        var negative = seconds < 0;
        var t = TimeSpan.FromSeconds(Math.Abs(seconds));
        var text = $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}";
        return negative ? "-" + text : text;
    }

    /// <summary>Renders the shortest unambiguous form, for speaking aloud.</summary>
    public static string FormatShort(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}.{t.Milliseconds / 100}";
    }

    /// <summary>Renders a duration the way it should be spoken: "4.2 seconds".</summary>
    public static string Speak(double seconds)
    {
        var abs = Math.Abs(seconds);
        if (abs < 60) return $"{abs:0.#} seconds";
        var t = TimeSpan.FromSeconds(abs);
        var minutes = (int)t.TotalMinutes;
        return t.Seconds == 0
            ? $"{minutes} minute{(minutes == 1 ? "" : "s")}"
            : $"{minutes} minute{(minutes == 1 ? "" : "s")} {t.Seconds} second{(t.Seconds == 1 ? "" : "s")}";
    }

    /// <summary>Accepts <c>12.5</c>, <c>1:23</c>, <c>1:23.4</c> or <c>00:01:23.400</c>.</summary>
    public static bool TryParse(string? text, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim();
        var negative = trimmed.StartsWith('-');
        if (negative) trimmed = trimmed[1..];

        var parts = trimmed.Split(':');
        if (parts.Length > 3) return false;

        double total = 0;
        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            total = total * 60 + value;
        }

        seconds = negative ? -total : total;
        return true;
    }

    public static double Parse(string text) =>
        TryParse(text, out var seconds)
            ? seconds
            : throw new FormatException($"Not a timestamp: '{text}'");
}
