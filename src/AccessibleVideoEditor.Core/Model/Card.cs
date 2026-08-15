using System.Text.Json.Serialization;

namespace AccessibleVideoEditor.Core.Model;

/// <summary>
/// A composed screen: a background plus text and image layers.
///
/// The same composition is both a title card and a lower third. On the
/// programme track it is a full screen and narration stops for it; on the
/// graphics track, with a transparent background, it composites over whatever
/// is below. One concept, one editor, two placements - so "how do I make a
/// title screen" and "how do I add a lower third" have the same answer.
/// </summary>
public sealed class CardComposition
{
    public CardBackground Background { get; set; } = new();

    /// <summary>
    /// <see cref="CardLayout.Stack"/> is the default because it is what you
    /// want most of the time. Placing a heading, a subheading and a logo on a
    /// grid individually is fiddly; stacking them and letting the layout space
    /// them is one decision instead of three.
    /// </summary>
    public CardLayout Layout { get; set; } = CardLayout.Stack;

    public List<CardLayer> Layers { get; set; } = [];

    /// <summary>Vertical anchor of the stack as a whole. Ignored in grid layout.</summary>
    public VerticalAnchor StackAnchor { get; set; } = VerticalAnchor.Middle;

    /// <summary>Gap between stacked layers, as a fraction of canvas height.</summary>
    public double StackSpacing { get; set; } = 0.04;

    /// <summary>
    /// Where each layer actually lands, normalised 0..1 from the top left.
    /// In grid layout this is just each layer's placement; in stack layout the
    /// layers are distributed vertically and horizontal placement is honoured
    /// only for its column.
    /// </summary>
    public IReadOnlyList<ResolvedLayer> Resolve()
    {
        var resolved = new List<ResolvedLayer>();
        if (Layers.Count == 0) return resolved;

        if (Layout == CardLayout.Grid)
        {
            foreach (var layer in Layers)
            {
                var (x, y) = layer.Placement.Resolve();
                resolved.Add(new ResolvedLayer(layer, x, y));
            }

            return resolved;
        }

        var heights = Layers.Select(l => l.NominalHeight).ToList();
        var total = heights.Sum() + StackSpacing * (Layers.Count - 1);

        var top = StackAnchor switch
        {
            VerticalAnchor.Top => 0.12,
            VerticalAnchor.Bottom => Math.Max(0, 0.88 - total),
            _ => Math.Max(0, (1 - total) / 2),
        };

        for (var i = 0; i < Layers.Count; i++)
        {
            var layer = Layers[i];
            var (x, _) = layer.Placement.Resolve();

            resolved.Add(new ResolvedLayer(layer, x, top + heights[i] / 2));
            top += heights[i] + StackSpacing;
        }

        return resolved;
    }

    /// <summary>
    /// Every word on the card, in stacking order. This is the card's identity -
    /// "card" alone is useless when a video has six of them - so it is what
    /// appears in the transcript and in the terse cursor readout.
    /// </summary>
    public string PlainText() =>
        string.Join(" / ", Layers.OfType<TextLayer>().Select(l => l.Text).Where(t => t.Length > 0));

    /// <summary>
    /// The full read-out of a card: what its background is, how it is laid
    /// out, and every layer with where it actually sits.
    /// </summary>
    public string Summarise()
    {
        if (Layers.Count == 0) return $"empty card. {Background.Describe()}.";

        var layout = Layout == CardLayout.Stack
            ? $"stacked, {StackAnchor.ToString().ToLowerInvariant()}"
            : "placed on the grid";

        return $"{Background.Describe()}. {Layers.Count} layer{(Layers.Count == 1 ? "" : "s")}, "
               + $"{layout}. {string.Join(". ", LayerLines())}.";
    }

    /// <summary>One line per layer, each naming where it lands. Navigable one at a time.</summary>
    public IReadOnlyList<string> LayerLines() =>
        Resolve()
            .Select((r, i) => $"{i + 1}. {r.Layer.Describe()}, {DescribePosition(r.X, r.Y)}")
            .ToList();

    /// <summary>
    /// A normalised point in words. Thirds rather than percentages, because
    /// "upper left" is the unit composition actually works in.
    /// </summary>
    public static string DescribePosition(double x, double y)
    {
        var vertical = y switch { < 0.34 => "upper", > 0.66 => "lower", _ => "middle" };
        var horizontal = x switch { < 0.34 => "left", > 0.66 => "right", _ => "centre" };

        return vertical == "middle" && horizontal == "centre"
            ? "centre"
            : $"{vertical} {horizontal}";
    }

    /// <summary>What the announcer reads when the cursor lands on a card.</summary>
    public string Describe()
    {
        if (Layers.Count == 0) return $"empty card, {Background.Describe()}";

        var text = Layers.OfType<TextLayer>().Select(l => l.Text).FirstOrDefault(t => t.Length > 0);
        var counts = new List<string>();

        var textLayers = Layers.OfType<TextLayer>().Count();
        var imageLayers = Layers.OfType<ImageLayer>().Count();

        if (textLayers > 0) counts.Add($"{textLayers} text");
        if (imageLayers > 0) counts.Add($"{imageLayers} image");

        var layout = Layout == CardLayout.Stack ? "stacked" : "placed";

        return text is null
            ? $"card, {string.Join(" and ", counts)}, {layout}"
            : $"card \"{text}\", {string.Join(" and ", counts)}, {layout}";
    }

    public CardComposition Clone() => new()
    {
        Background = Background.Clone(),
        Layout = Layout,
        StackAnchor = StackAnchor,
        StackSpacing = StackSpacing,
        Layers = Layers.Select(l => l.Clone()).ToList(),
    };
}

public enum CardLayout
{
    /// <summary>Layers flow top to bottom with automatic spacing, like a slide.</summary>
    Stack,

    /// <summary>Each layer sits where its numpad placement puts it.</summary>
    Grid,
}

public sealed class CardBackground
{
    public BackgroundKind Kind { get; set; } = BackgroundKind.Solid;

    /// <summary>Hex RGB. The solid colour, or the first stop of a gradient.</summary>
    public string Colour { get; set; } = "#101014";

    /// <summary>The second stop of a gradient.</summary>
    public string SecondColour { get; set; } = "#2A2A3A";

    /// <summary>Which way a gradient runs.</summary>
    public GradientDirection Direction { get; set; } = GradientDirection.Vertical;

    public SourceId? Source { get; set; }

    /// <summary>
    /// Darkens an image or video background, 0..1, so text stays legible over
    /// it. The most common reason a title is unreadable, and invisible to
    /// someone who cannot see the result.
    /// </summary>
    public double Dim { get; set; } = 0.35;

    public string Describe() => Kind switch
    {
        BackgroundKind.Transparent => "over the video",
        BackgroundKind.Solid => $"solid {Colour}",
        BackgroundKind.Gradient =>
            $"{Direction.ToString().ToLowerInvariant()} gradient, {Colour} to {SecondColour}",
        BackgroundKind.Image => "image background",
        BackgroundKind.Video => "video background",
        _ => "background",
    };

    public CardBackground Clone() => new()
    {
        Kind = Kind,
        Colour = Colour,
        SecondColour = SecondColour,
        Direction = Direction,
        Source = Source,
        Dim = Dim,
    };
}

public enum BackgroundKind
{
    /// <summary>Composites over whatever is below. What makes a card a lower third.</summary>
    Transparent,

    Solid,

    /// <summary>Two colours blended across the frame.</summary>
    Gradient,

    Image,
    Video,
}

public enum GradientDirection
{
    Vertical,
    Horizontal,
    Diagonal,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TextLayer), "text")]
[JsonDerivedType(typeof(ImageLayer), "image")]
public abstract class CardLayer
{
    public Placement Placement { get; set; } = Placement.Centre;

    /// <summary>Fraction of canvas height this layer occupies. Drives stack spacing.</summary>
    [JsonIgnore]
    public abstract double NominalHeight { get; }

    public abstract string Describe();

    public abstract CardLayer Clone();
}

public sealed class TextLayer : CardLayer
{
    public string Text { get; set; } = string.Empty;

    public TextSize Size { get; set; } = TextSize.Medium;

    public bool Bold { get; set; }

    public string Colour { get; set; } = "#FFFFFF";

    /// <summary>Text wraps within this fraction of the canvas width.</summary>
    public double MaxWidth { get; set; } = 0.8;

    public override double NominalHeight => Size switch
    {
        TextSize.Small => 0.05,
        TextSize.Medium => 0.08,
        TextSize.Large => 0.12,
        TextSize.Huge => 0.18,
        _ => 0.08,
    };

    public override string Describe() =>
        $"text \"{Text}\", {Size.ToString().ToLowerInvariant()}{(Bold ? ", bold" : string.Empty)}";

    public override CardLayer Clone() => new TextLayer
    {
        Text = Text,
        Size = Size,
        Bold = Bold,
        Colour = Colour,
        MaxWidth = MaxWidth,
        Placement = Placement,
    };
}

public enum TextSize
{
    Small,
    Medium,
    Large,
    Huge,
}

public sealed class ImageLayer : CardLayer
{
    public required SourceId Source { get; set; }

    /// <summary>Fraction of canvas width the image occupies.</summary>
    public double Scale { get; set; } = 0.25;

    public double Opacity { get; set; } = 1.0;

    public override double NominalHeight => Scale * 0.6;

    public override string Describe() => $"image, {Scale * 100:0}% wide";

    public override CardLayer Clone() => new ImageLayer
    {
        Source = Source,
        Scale = Scale,
        Opacity = Opacity,
        Placement = Placement,
    };
}

public readonly record struct ResolvedLayer(CardLayer Layer, double X, double Y);
