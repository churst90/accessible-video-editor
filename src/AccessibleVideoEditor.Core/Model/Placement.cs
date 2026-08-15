namespace AccessibleVideoEditor.Core.Model;

/// <summary>
/// Where a graphic or title sits on the canvas, expressed as a numpad cell
/// rather than coordinates.
///
/// <code>
///   7 8 9      top-left    top     top-right
///   4 5 6      left        centre  right
///   1 2 3      bottom-left bottom  bottom-right
/// </code>
///
/// 3x3 rather than 4x4 deliberately: an even grid has no centre cell, and 3x3
/// is the rule-of-thirds grid that video composition already uses. Precision
/// comes from <see cref="SubCell"/> - pressing a second numpad key selects a
/// cell within the cell, giving 81 addressable positions from two keystrokes -
/// and then from <see cref="NudgeX"/>/<see cref="NudgeY"/> in 1% steps.
/// </summary>
public readonly record struct Placement(int Cell = 5, int SubCell = 0, double NudgeX = 0, double NudgeY = 0)
{
    public static Placement Centre => new(5);
    public static Placement LowerThird => new(2);

    /// <summary>Column 0..2, left to right.</summary>
    public int Column => (Cell - 1) % 3;

    /// <summary>Row 0..2, top to bottom.</summary>
    public int Row => 2 - (Cell - 1) / 3;

    /// <summary>
    /// The anchor is derived from the cell, never set separately. A graphic in
    /// cell 7 anchors top-left so it grows down and right; one in cell 5
    /// anchors centre. Without this, corner placements drift off-canvas as soon
    /// as the text length changes.
    /// </summary>
    public Anchor Anchor => new(
        Column switch { 0 => HorizontalAnchor.Left, 2 => HorizontalAnchor.Right, _ => HorizontalAnchor.Centre },
        Row switch { 0 => VerticalAnchor.Top, 2 => VerticalAnchor.Bottom, _ => VerticalAnchor.Middle });

    /// <summary>
    /// Resolves to a normalised anchor point on the canvas, 0..1 from the top
    /// left. Nudges are applied last and the result is clamped, so no sequence
    /// of key presses can put a graphic off-canvas.
    /// </summary>
    public (double X, double Y) Resolve()
    {
        var (cellX, cellY) = (Column / 3.0, Row / 3.0);

        // Sub-cell 0 means "centre of the cell"; 1-9 uses the same numpad layout
        // one level down.
        var (offsetX, offsetY) = SubCell is >= 1 and <= 9
            ? (((SubCell - 1) % 3 + 0.5) / 9.0, ((2 - (SubCell - 1) / 3) + 0.5) / 9.0)
            : (1 / 6.0, 1 / 6.0);

        return (Math.Clamp(cellX + offsetX + NudgeX, 0, 1),
                Math.Clamp(cellY + offsetY + NudgeY, 0, 1));
    }

    public Placement Nudge(double dx, double dy) =>
        this with { NudgeX = Math.Clamp(NudgeX + dx, -1, 1), NudgeY = Math.Clamp(NudgeY + dy, -1, 1) };

    /// <summary>What the announcer reads back after a placement change.</summary>
    public string Describe()
    {
        var name = Cell switch
        {
            7 => "top left", 8 => "top centre", 9 => "top right",
            4 => "left", 5 => "centre", 6 => "right",
            1 => "bottom left", 2 => "bottom centre", 3 => "bottom right",
            _ => "centre",
        };

        var (x, y) = Resolve();
        var detail = SubCell is >= 1 and <= 9 ? $", sub-cell {SubCell}" : string.Empty;
        var nudged = NudgeX != 0 || NudgeY != 0 ? ", nudged" : string.Empty;
        return $"{name}{detail}{nudged} - x {x * 100:0}%, y {y * 100:0}%";
    }
}

public readonly record struct Anchor(HorizontalAnchor Horizontal, VerticalAnchor Vertical);

public enum HorizontalAnchor
{
    Left,
    Centre,
    Right,
}

public enum VerticalAnchor
{
    Top,
    Middle,
    Bottom,
}
