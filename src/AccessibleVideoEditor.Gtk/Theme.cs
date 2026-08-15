using AccessibleVideoEditor.Core.Timeline;
using Gtk_ = Gtk;

namespace AccessibleVideoEditor.Gtk;

/// <summary>
/// One palette, two consumers.
///
/// The CSS styles the widgets and the same numbers are handed to Cairo when the
/// timeline is drawn, so a lane in the picture and its header in the list are
/// literally the same colour rather than two people's guesses at the same
/// colour.
///
/// Dark, because that is what an editing application is for - a bright surface
/// beside video you are grading is a lie about what you are looking at - and
/// because it is easier on anyone working for hours. Text is held above a 7:1
/// contrast ratio on every surface here, which matters for the low-vision case
/// far more than the aesthetic does.
/// </summary>
public static class Theme
{
    // ---- surfaces --------------------------------------------------------

    public static readonly Rgb Window = Rgb.Hex("#12141a");
    public static readonly Rgb Panel = Rgb.Hex("#191c24");
    public static readonly Rgb Raised = Rgb.Hex("#222633");
    public static readonly Rgb Line = Rgb.Hex("#2c3140");
    public static readonly Rgb LaneOdd = Rgb.Hex("#171a22");
    public static readonly Rgb LaneEven = Rgb.Hex("#1b1f28");
    public static readonly Rgb LaneFocused = Rgb.Hex("#20263a");

    // ---- ink -------------------------------------------------------------

    public static readonly Rgb Text = Rgb.Hex("#e9ecf5");
    public static readonly Rgb Muted = Rgb.Hex("#a9b1c7");
    public static readonly Rgb Faint = Rgb.Hex("#727b93");

    // ---- signal ----------------------------------------------------------

    public static readonly Rgb Accent = Rgb.Hex("#5b8cff");
    public static readonly Rgb Playhead = Rgb.Hex("#ff5f5f");
    public static readonly Rgb Armed = Rgb.Hex("#ff5f5f");
    public static readonly Rgb Selection = Rgb.Hex("#5b8cff");

    /// <summary>
    /// A block's colour is its <i>kind</i>, not its track, so a card looks like
    /// a card wherever it has been put. The hues are far enough apart to survive
    /// the common colour-vision deficiencies, and each is paired with a label,
    /// never left to carry the meaning alone.
    /// </summary>
    public static Rgb ColourFor(BlockKind kind) => kind switch
    {
        BlockKind.Clip => Rgb.Hex("#3d6fd6"),
        BlockKind.Card => Rgb.Hex("#b4569b"),
        BlockKind.Hole => Rgb.Hex("#3b4254"),
        BlockKind.Pause => Rgb.Hex("#333a4a"),
        BlockKind.Broll => Rgb.Hex("#2f9e8f"),
        BlockKind.Title => Rgb.Hex("#d08a2e"),
        BlockKind.Graphic => Rgb.Hex("#c2702c"),
        BlockKind.Music => Rgb.Hex("#7b5cd6"),
        BlockKind.Audio => Rgb.Hex("#6478d8"),
        _ => Rgb.Hex("#3d6fd6"),
    };

    /// <summary>Applied once, to the display, so every window gets it.</summary>
    public static void Install(Gdk.Display display)
    {
        var provider = Gtk_.CssProvider.New();
        provider.LoadFromString(Css);

        Gtk_.StyleContext.AddProviderForDisplay(display, provider, 800);
    }

    private static string Css => $$"""
        window.videoeditor {
            background: {{Window.Css}};
            color: {{Text.Css}};
        }

        /* The menu bar reads as part of the window rather than as a strip
           bolted on top of it. */
        popovermenubar {
            background: {{Panel.Css}};
            color: {{Text.Css}};
            padding: 2px 6px;
            border-bottom: 1px solid {{Line.Css}};
        }

        popovermenubar > item {
            padding: 6px 12px;
            border-radius: 7px;
        }

        popovermenubar > item:hover,
        popovermenubar > item:focus {
            background: {{Raised.Css}};
        }

        /* Pane headings. Small, spaced and quiet - the content is the point. */
        label.pane-heading {
            font-size: 11pt;
            font-weight: 700;
            letter-spacing: 0.08em;
            color: {{Muted.Css}};
            margin-bottom: 2px;
        }

        /* The status line is the one thing that is never a view away, so it is
           given a surface of its own and monospaced digits that do not jitter
           as the timecode counts. */
        label.readout {
            font-family: 'Cascadia Mono', 'JetBrains Mono', monospace;
            font-size: 11pt;
            color: {{Text.Css}};
            background: {{Panel.Css}};
            border: 1px solid {{Line.Css}};
            border-radius: 9px;
            padding: 9px 14px;
        }

        label.footer {
            font-size: 9pt;
            color: {{Faint.Css}};
            padding: 2px 2px 0 2px;
        }

        /* Panes */
        .pane {
            background: {{Panel.Css}};
            border: 1px solid {{Line.Css}};
            border-radius: 12px;
        }

        scrolledwindow {
            border-radius: 12px;
        }

        listview, list {
            background: transparent;
            color: {{Text.Css}};
        }

        row {
            border-radius: 9px;
            margin: 2px 6px;
            transition: background 90ms ease-out;
        }

        row:hover {
            background: {{Raised.Css}};
        }

        row:selected {
            background: alpha({{Accent.Css}}, 0.22);
            color: {{Text.Css}};
        }

        /* A visible focus ring is not decoration here - it is how a sighted
           collaborator finds the row the speech is talking about. */
        row:focus-visible,
        button:focus-visible,
        entry:focus-visible,
        textview:focus-visible {
            outline: 2px solid {{Accent.Css}};
            outline-offset: -2px;
        }

        row label {
            color: {{Text.Css}};
        }

        /* Track headers beside the drawn lanes. Fixed height so a header and
           its lane line up exactly. */
        .lane-header-column {
            background: {{Panel.Css}};
            border-right: 1px solid {{Line.Css}};
        }

        list.lane-headers {
            background: {{Panel.Css}};
        }

        /* Sits above the header column, level with the ruler, so the two read
           as one strip across the top of the timeline. */
        .ruler-gutter {
            background: {{Panel.Css}};
            border-bottom: 1px solid {{Line.Css}};
        }

        row.lane-header {
            margin: 0;
            border-radius: 0;
            border-bottom: 1px solid {{Line.Css}};
            min-height: 59px;
            font-weight: 600;
        }

        row.lane-header:selected {
            background: alpha({{Accent.Css}}, 0.28);
            box-shadow: inset 3px 0 0 {{Accent.Css}};
        }

        textview {
            background: {{Panel.Css}};
            color: {{Text.Css}};
            font-size: 12pt;
            padding: 10px 12px;
        }

        textview text {
            background: {{Panel.Css}};
            color: {{Text.Css}};
        }

        textview text selection {
            background: alpha({{Accent.Css}}, 0.4);
        }

        entry {
            background: {{Raised.Css}};
            color: {{Text.Css}};
            border: 1px solid {{Line.Css}};
            border-radius: 8px;
            padding: 7px 10px;
        }

        button {
            background: {{Raised.Css}};
            color: {{Text.Css}};
            border: 1px solid {{Line.Css}};
            border-radius: 8px;
            padding: 7px 14px;
        }

        button:hover {
            background: {{Line.Css}};
        }

        button.suggested-action {
            background: {{Accent.Css}};
            border-color: {{Accent.Css}};
            color: #0d1017;
            font-weight: 600;
        }

        popover > contents {
            background: {{Raised.Css}};
            color: {{Text.Css}};
            border: 1px solid {{Line.Css}};
            border-radius: 10px;
            padding: 5px;
        }

        popover modelbutton {
            border-radius: 7px;
            padding: 7px 11px;
        }

        popover modelbutton:hover {
            background: alpha({{Accent.Css}}, 0.25);
        }

        scrollbar {
            background: transparent;
        }

        scrollbar slider {
            background: {{Line.Css}};
            border-radius: 8px;
            min-width: 8px;
            min-height: 8px;
        }

        scrollbar slider:hover {
            background: {{Faint.Css}};
        }
        """;
}

/// <summary>A colour both CSS and Cairo can use, so neither drifts from the other.</summary>
public readonly record struct Rgb(double R, double G, double B)
{
    public static Rgb Hex(string hex)
    {
        var text = hex.TrimStart('#');

        return new Rgb(
            Convert.ToInt32(text[..2], 16) / 255.0,
            Convert.ToInt32(text.Substring(2, 2), 16) / 255.0,
            Convert.ToInt32(text.Substring(4, 2), 16) / 255.0);
    }

    public string Css =>
        $"#{(int)Math.Round(R * 255):x2}{(int)Math.Round(G * 255):x2}{(int)Math.Round(B * 255):x2}";

    public Rgb Lighten(double amount) =>
        new(Mix(R, 1, amount), Mix(G, 1, amount), Mix(B, 1, amount));

    public Rgb Darken(double amount) =>
        new(Mix(R, 0, amount), Mix(G, 0, amount), Mix(B, 0, amount));

    private static double Mix(double from, double to, double amount) =>
        from + (to - from) * Math.Clamp(amount, 0, 1);
}
