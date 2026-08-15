namespace AccessibleVideoEditor.Core.Model;

/// <summary>
/// Ready-made compositions.
///
/// Templates matter more here than in a visual editor. Sighted users nudge
/// things until they look right; that feedback loop does not exist without
/// sight, so the fast path has to be a composition that is already correct -
/// legible sizes, title-safe margins, sensible hierarchy - which you then only
/// have to fill in with words.
/// </summary>
public static class CardTemplates
{
    public static IReadOnlyList<CardTemplate> All { get; } =
    [
        new("title", "Title card", "Big title, optional subtitle, centred on a solid background."),
        new("section", "Section break", "One line, centred. For chapter transitions."),
        new("quote", "Quote", "Quoted text with an attribution beneath it."),
        new("lower-third", "Lower third", "Name and role over the video, bottom left."),
        new("end", "End screen", "Closing line, centred, with room for a subscribe prompt."),
    ];

    public static CardComposition Build(string templateId, params string[] text) => templateId switch
    {
        "title" => TitleCard(At(text, 0), At(text, 1)),
        "section" => SectionBreak(At(text, 0)),
        "quote" => Quote(At(text, 0), At(text, 1)),
        "lower-third" => LowerThird(At(text, 0), At(text, 1)),
        "end" => EndScreen(At(text, 0)),
        _ => throw new ArgumentException($"No card template '{templateId}'.", nameof(templateId)),
    };

    public static CardComposition TitleCard(string title, string? subtitle = null)
    {
        var card = new CardComposition
        {
            Background = new CardBackground { Kind = BackgroundKind.Solid },
            Layout = CardLayout.Stack,
            Layers = [new TextLayer { Text = title, Size = TextSize.Huge, Bold = true }],
        };

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            card.Layers.Add(new TextLayer { Text = subtitle, Size = TextSize.Medium });
        }

        return card;
    }

    public static CardComposition SectionBreak(string text) => new()
    {
        Background = new CardBackground { Kind = BackgroundKind.Solid },
        Layout = CardLayout.Stack,
        Layers = [new TextLayer { Text = text, Size = TextSize.Large, Bold = true }],
    };

    public static CardComposition Quote(string quote, string? attribution = null)
    {
        var card = new CardComposition
        {
            Background = new CardBackground { Kind = BackgroundKind.Solid },
            Layout = CardLayout.Stack,
            Layers = [new TextLayer { Text = $"“{quote}”", Size = TextSize.Large, MaxWidth = 0.7 }],
        };

        if (!string.IsNullOrWhiteSpace(attribution))
        {
            card.Layers.Add(new TextLayer { Text = $"— {attribution}", Size = TextSize.Small });
        }

        return card;
    }

    /// <summary>
    /// The one template that is an overlay rather than a screen: transparent
    /// background, bottom left, so it composites over the video below.
    /// </summary>
    public static CardComposition LowerThird(string name, string? role = null)
    {
        var card = new CardComposition
        {
            Background = new CardBackground { Kind = BackgroundKind.Transparent },
            Layout = CardLayout.Stack,
            StackAnchor = VerticalAnchor.Bottom,
            StackSpacing = 0.01,
            Layers =
            [
                new TextLayer
                {
                    Text = name,
                    Size = TextSize.Large,
                    Bold = true,
                    Placement = new Placement(1),
                },
            ],
        };

        if (!string.IsNullOrWhiteSpace(role))
        {
            card.Layers.Add(new TextLayer
            {
                Text = role,
                Size = TextSize.Small,
                Placement = new Placement(1),
            });
        }

        return card;
    }

    public static CardComposition EndScreen(string text) => new()
    {
        Background = new CardBackground { Kind = BackgroundKind.Solid },
        Layout = CardLayout.Stack,
        Layers =
        [
            new TextLayer { Text = text, Size = TextSize.Large, Bold = true },
            new TextLayer { Text = "Subscribe for more", Size = TextSize.Small },
        ],
    };

    private static string At(IReadOnlyList<string> values, int index) =>
        index < values.Count ? values[index] : string.Empty;
}

public sealed record CardTemplate(string Id, string Name, string Description)
{
    public string Announce() => $"{Name}. {Description}";
}
