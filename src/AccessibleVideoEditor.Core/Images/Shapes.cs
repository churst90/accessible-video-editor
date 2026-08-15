using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Images;

/// <summary>
/// Drawing, as a language rather than as a gesture.
///
/// This is the answer to brush and fill. Freehand painting is a gesture, and
/// making a gesture accessible is the wrong problem - so it is replaced by
/// something a person can say: <i>"circle at cell 5, radius 20 percent, deep
/// blue"</i>. That is exact, repeatable, editable afterwards, and reads back as
/// a list of statements rather than as a picture nobody can describe.
///
/// Everything is in <b>fractions of the canvas</b>, never in pixels, so the
/// same drawing works at any size and "a fifth of the way across" means what it
/// says.
/// </summary>
public sealed record Shape
{
    public required ShapeKind Kind { get; init; }

    public required string Colour { get; init; }

    /// <summary>Where it sits, in the same 3 by 3 language as cards and scenes.</summary>
    public Placement Placement { get; init; } = new();

    /// <summary>Second point, for a line.</summary>
    public Placement? To { get; init; }

    /// <summary>Fraction of the canvas width. 0.2 is a fifth of the way across.</summary>
    public double Size { get; init; } = 0.2;

    public double Height { get; init; }

    public string Text { get; init; } = string.Empty;

    /// <summary>The second colour of a gradient.</summary>
    public string SecondColour { get; init; } = string.Empty;

    public bool Vertical { get; init; } = true;

    public double Thickness { get; init; } = 0.005;

    public byte Alpha { get; init; } = 255;

    /// <summary>
    /// Read back when stepping through the list. It is deliberately the same
    /// sentence that would create it, so what you hear is what you would say.
    /// </summary>
    public string Describe() => Kind switch
    {
        ShapeKind.Rectangle =>
            $"rectangle at {ShapeLanguage.CellName(Placement)}, {Percent(Size)} wide, {Colour}",

        ShapeKind.Ellipse =>
            $"circle at {ShapeLanguage.CellName(Placement)}, radius {Percent(Size)}, {Colour}",

        ShapeKind.Line =>
            $"line from {ShapeLanguage.CellName(Placement)} to "
            + $"{(To is { } end ? ShapeLanguage.CellName(end) : "nowhere")}, {Colour}",

        ShapeKind.Text =>
            $"text \"{Text}\" at {ShapeLanguage.CellName(Placement)}, {Colour}",

        ShapeKind.Fill =>
            $"fill {Colour}",

        ShapeKind.Gradient =>
            $"gradient {Colour} to {SecondColour}, {(Vertical ? "top to bottom" : "left to right")}",

        _ => Kind.ToString().ToLowerInvariant(),
    };

    private static string Percent(double fraction) => $"{Math.Round(fraction * 100)} percent";

    /// <summary>
    /// Paints itself, and says how much of the canvas it covered - the only
    /// way to know a shape landed where it was meant to without looking.
    /// </summary>
    public string DrawOn(Canvas canvas)
    {
        var colour = Colours.Parse(Colour) ?? (0, 0, 0);

        var (nx, ny) = Placement.Resolve();

        var x = (int)Math.Round(nx * canvas.Width);
        var y = (int)Math.Round(ny * canvas.Height);

        var width = Math.Max(1, (int)Math.Round(Size * canvas.Width));
        var height = Math.Max(1, (int)Math.Round((Height > 0 ? Height : Size) * canvas.Height));

        var painted = Kind switch
        {
            ShapeKind.Rectangle => canvas.FillRect(x - width / 2, y - height / 2, width, height, colour, Alpha),

            ShapeKind.Ellipse => canvas.FillEllipse(x, y, width / 2, width / 2, colour, Alpha),

            ShapeKind.Line => DrawLine(canvas, colour),

            ShapeKind.Fill => canvas.FillRect(0, 0, canvas.Width, canvas.Height, colour, Alpha),

            ShapeKind.Gradient => Paint(canvas, colour),

            // Text is drawn by the renderer, which has fonts; here it only
            // takes part in the description.
            _ => 0,
        };

        var share = canvas.Width * canvas.Height == 0
            ? 0
            : painted * 100.0 / (canvas.Width * canvas.Height);

        return $"{Describe()}, covering {share:0} percent";
    }

    private int DrawLine(Canvas canvas, (byte R, byte G, byte B) colour)
    {
        var (fromX, fromY) = Placement.Resolve();
        var (toX, toY) = (To ?? Placement).Resolve();

        return canvas.DrawLine(
            (int)Math.Round(fromX * canvas.Width),
            (int)Math.Round(fromY * canvas.Height),
            (int)Math.Round(toX * canvas.Width),
            (int)Math.Round(toY * canvas.Height),
            colour,
            Math.Max(1, (int)Math.Round(Thickness * canvas.Width)));
    }

    private int Paint(Canvas canvas, (byte R, byte G, byte B) colour)
    {
        canvas.Gradient(colour, Colours.Parse(SecondColour) ?? (0, 0, 0), Vertical);

        return canvas.Width * canvas.Height;
    }
}

public enum ShapeKind
{
    Rectangle,
    Ellipse,
    Line,
    Text,
    Fill,
    Gradient,
}

/// <summary>
/// Turns a sentence into a shape.
///
/// The grammar is small on purpose. It has to be sayable from memory while
/// thinking about something else, so it is built from the words people already
/// use for this - <i>at</i>, <i>from</i>, <i>to</i>, <i>radius</i>, <i>percent</i>
/// - and the cell names the rest of the application already speaks.
/// </summary>
public static class ShapeLanguage
{
    /// <summary>Examples, read by the help key. The grammar is easier shown than stated.</summary>
    public static readonly string[] Examples =
    [
        "fill navy",
        "gradient navy to black",
        "circle at centre, radius 20 percent, white",
        "rectangle at bottom left, 30 percent, red",
        "line from top left to bottom right, yellow",
        "text \"Chapter one\" at centre, white",
    ];

    public static Shape? Parse(string sentence)
    {
        var text = sentence.Trim().ToLowerInvariant();

        if (text.Length == 0) return null;

        // The quoted part of a text shape is pulled out first so its contents
        // cannot be mistaken for keywords.
        var quoted = string.Empty;
        var quoteStart = sentence.IndexOf('"');
        var quoteEnd = sentence.LastIndexOf('"');

        if (quoteStart >= 0 && quoteEnd > quoteStart)
        {
            quoted = sentence[(quoteStart + 1)..quoteEnd];
            text = (text[..quoteStart] + text[(quoteEnd + 1)..]).Trim();
        }

        var words = text.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return null;

        var colour = FindColour(text);
        var size = FindPercent(text) ?? 0.2;

        return words[0] switch
        {
            "fill" => new Shape { Kind = ShapeKind.Fill, Colour = colour ?? "black" },

            "gradient" => ParseGradient(text),

            "circle" or "ellipse" or "dot" => new Shape
            {
                Kind = ShapeKind.Ellipse,
                Colour = colour ?? "white",
                Placement = FindPlacement(text, "at") ?? Placement.Centre,
                Size = size,
            },

            "rectangle" or "box" or "square" => new Shape
            {
                Kind = ShapeKind.Rectangle,
                Colour = colour ?? "white",
                Placement = FindPlacement(text, "at") ?? Placement.Centre,
                Size = size,
            },

            "line" => new Shape
            {
                Kind = ShapeKind.Line,
                Colour = colour ?? "white",
                Placement = FindPlacement(text, "from") ?? Placement.Centre,
                To = FindPlacement(text, "to") ?? Placement.Centre,
                Thickness = FindPercent(text) ?? 0.005,
            },

            "text" or "write" or "say" => new Shape
            {
                Kind = ShapeKind.Text,
                Colour = colour ?? "white",
                Placement = FindPlacement(text, "at") ?? Placement.Centre,
                Text = quoted,
                Size = size,
            },

            _ => null,
        };
    }

    private static Shape? ParseGradient(string text)
    {
        var at = text.IndexOf(" to ", StringComparison.Ordinal);
        if (at < 0) return null;

        var from = FindColour(text[..at].Replace("gradient", string.Empty));
        var to = FindColour(text[(at + 4)..]);

        if (from is null || to is null) return null;

        return new Shape
        {
            Kind = ShapeKind.Gradient,
            Colour = from,
            SecondColour = to,
            Vertical = !text.Contains("left to right", StringComparison.Ordinal),
        };
    }

    /// <summary>The longest colour name that appears, so "dark blue" beats "blue".</summary>
    public static string? FindColour(string text) =>
        Colours.Named
            .Select(c => c.Name)
            .SelectMany(name => new[] { "light " + name, "dark " + name, "pale " + name, name })
            .Where(name => text.Contains(name, StringComparison.Ordinal))
            .OrderByDescending(name => name.Length)
            .FirstOrDefault();

    /// <summary>
    /// The cell named after a keyword. "at centre", "from top left" - the same
    /// nine names cards and scenes use, plus the numbers for anyone who prefers
    /// them.
    /// </summary>
    public static Placement? FindPlacement(string text, string keyword)
    {
        var at = text.IndexOf(keyword + " ", StringComparison.Ordinal);
        if (at < 0) return null;

        var rest = text[(at + keyword.Length + 1)..];

        foreach (var (name, cell) in Cells)
        {
            if (rest.StartsWith(name, StringComparison.Ordinal)) return new Placement(cell);
        }

        if (rest.StartsWith("cell ", StringComparison.Ordinal)
            && int.TryParse(rest[5..].Split([',', ' '])[0], out var number)
            && number is >= 1 and <= 9)
        {
            return new Placement(number);
        }

        return null;
    }

    /// <summary>
    /// Longest first, so "top left" is not read as "top". The numbers follow
    /// the numeric keypad, which is what the card editor already uses.
    /// </summary>
    private static readonly (string Name, int Cell)[] Cells =
    [
        ("bottom left", 1), ("bottom centre", 2), ("bottom center", 2), ("bottom right", 3),
        ("top left", 7), ("top centre", 8), ("top center", 8), ("top right", 9),
        ("middle left", 4), ("middle right", 6),
        ("centre", 5), ("center", 5), ("middle", 5),
        ("bottom", 2), ("top", 8), ("left", 4), ("right", 6),
    ];

    /// <summary>
    /// The short name of a cell, so a shape reads back as the sentence that
    /// would create it. The fuller placement description is right when you are
    /// positioning something and too much when you are listing what is there.
    /// </summary>
    public static string CellName(Placement placement) =>
        placement.Cell switch
        {
            1 => "bottom left", 2 => "bottom centre", 3 => "bottom right",
            4 => "middle left", 5 => "centre", 6 => "middle right",
            7 => "top left", 8 => "top centre", 9 => "top right",
            _ => "centre",
        };

    public static double? FindPercent(string text)
    {
        var at = text.IndexOf(" percent", StringComparison.Ordinal);
        if (at < 0) return null;

        var before = text[..at].Split([' ', ',']).LastOrDefault();

        return double.TryParse(before, out var value) ? Math.Clamp(value / 100, 0.001, 1) : null;
    }

    /// <summary>Said when a sentence is not understood. Names what it does know.</summary>
    public static string Help() =>
        "say something like: " + string.Join("; ", Examples.Take(3));
}
