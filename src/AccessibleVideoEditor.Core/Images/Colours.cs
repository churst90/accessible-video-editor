namespace AccessibleVideoEditor.Core.Images;

/// <summary>
/// Colours, by name in both directions.
///
/// Naming is the whole point. "#3d6fd6" is not a colour to anyone who cannot
/// see it; "a mid blue" is. So every colour that gets spoken goes through here,
/// and the value is offered afterwards for anyone who wants it - the name
/// first, because the name is the part that means something.
/// </summary>
public static class Colours
{
    /// <summary>
    /// Deliberately a small list. A hundred names would be more precise and far
    /// less useful: the point is to tell colours apart, and "cerulean" does not
    /// help you do that.
    /// </summary>
    public static readonly (string Name, byte R, byte G, byte B)[] Named =
    [
        ("black", 0, 0, 0),
        ("dark grey", 64, 64, 64),
        ("grey", 128, 128, 128),
        ("light grey", 192, 192, 192),
        ("white", 255, 255, 255),
        ("red", 220, 40, 40),
        ("dark red", 130, 20, 20),
        ("orange", 240, 140, 30),
        ("brown", 130, 90, 50),
        ("yellow", 240, 220, 60),
        ("green", 50, 170, 70),
        ("dark green", 25, 90, 45),
        ("teal", 40, 160, 160),
        ("cyan", 80, 210, 220),
        ("blue", 50, 100, 210),
        ("dark blue", 25, 45, 120),
        ("navy", 15, 25, 70),
        ("purple", 130, 70, 190),
        ("magenta", 200, 60, 170),
        ("pink", 240, 150, 180),
        ("cream", 245, 235, 210),
        ("skin", 225, 185, 155),
    ];

    /// <summary>
    /// The nearest name, weighted the way an eye weighs it: green carries most
    /// of the perceived brightness, blue least. Plain distance in RGB calls a
    /// dark blue and a dark green the same thing.
    /// </summary>
    public static string NameOf(byte r, byte g, byte b)
    {
        var best = Named[0].Name;
        var bestDistance = double.MaxValue;

        foreach (var (name, nr, ng, nb) in Named)
        {
            var dr = (r - nr) * 0.30;
            var dg = (g - ng) * 0.59;
            var db = (b - nb) * 0.11;

            var distance = dr * dr + dg * dg + db * db;

            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = name;
        }

        return best;
    }
    public static string Describe(byte r, byte g, byte b) =>
        $"{NameOf(r, g, b)}, {Hex(r, g, b)}";

    public static string Hex(byte r, byte g, byte b) => $"#{r:x2}{g:x2}{b:x2}";

    /// <summary>
    /// Accepts a name or a hex value, so the shape language reads naturally and
    /// still takes an exact colour when one is meant.
    /// </summary>
    public static (byte R, byte G, byte B)? Parse(string text)
    {
        var value = text.Trim().ToLowerInvariant();

        if (value.StartsWith('#')) value = value[1..];

        if (value.Length == 6 && value.All(Uri.IsHexDigit))
        {
            return (
                Convert.ToByte(value[..2], 16),
                Convert.ToByte(value.Substring(2, 2), 16),
                Convert.ToByte(value.Substring(4, 2), 16));
        }

        var named = Named.FirstOrDefault(c => c.Name == value);

        if (named.Name is not null) return (named.R, named.G, named.B);

        // "light blue" and "dark pink" are how people actually speak, so a
        // qualifier in front of a known colour is understood rather than
        // refused.
        foreach (var (prefix, factor) in new[] { ("light ", 1.4), ("dark ", 0.6), ("pale ", 1.6) })
        {
            if (!value.StartsWith(prefix, StringComparison.Ordinal)) continue;

            var basis = Named.FirstOrDefault(c => c.Name == value[prefix.Length..]);
            if (basis.Name is null) continue;

            return (Clamp(basis.R * factor), Clamp(basis.G * factor), Clamp(basis.B * factor));
        }

        return null;
    }

    private static byte Clamp(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);

    /// <summary>
    /// How light it is, 0 to 1. Used to decide whether text on it should be
    /// black or white, and to say "a dark blue" rather than just "blue".
    /// </summary>
    public static double Luminance(byte r, byte g, byte b) =>
        (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255;

    /// <summary>
    /// The contrast ratio between two colours, on the scale accessibility
    /// guidelines use: 4.5 is the minimum for body text, 7 is comfortable.
    /// Worth checking whenever text is put over anything.
    /// </summary>
    public static double Contrast(
        (byte R, byte G, byte B) first,
        (byte R, byte G, byte B) second)
    {
        var a = Channel(first);
        var b = Channel(second);

        var lighter = Math.Max(a, b);
        var darker = Math.Min(a, b);

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Channel((byte R, byte G, byte B) colour)
    {
        static double Linear(byte value)
        {
            var v = value / 255.0;

            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linear(colour.R) + 0.7152 * Linear(colour.G) + 0.0722 * Linear(colour.B);
    }

    /// <summary>Spoken when text is placed, because unreadable text is invisible to everybody.</summary>
    public static string DescribeContrast(
        (byte R, byte G, byte B) text,
        (byte R, byte G, byte B) background)
    {
        var ratio = Contrast(text, background);

        return ratio switch
        {
            >= 7 => $"{ratio:0.0} to 1, comfortable",
            >= 4.5 => $"{ratio:0.0} to 1, readable",
            >= 3 => $"{ratio:0.0} to 1, only large text will be readable",
            _ => $"{ratio:0.0} to 1, too low to read",
        };
    }
}
