using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Tests;

public class PlacementTests
{
    [Fact]
    public void Numpad_layout_matches_the_keypad()
    {
        Assert.Equal((0, 0), (new Placement(7).Column, new Placement(7).Row));
        Assert.Equal((1, 1), (new Placement(5).Column, new Placement(5).Row));
        Assert.Equal((2, 2), (new Placement(3).Column, new Placement(3).Row));
    }

    [Fact]
    public void Centre_cell_resolves_to_the_centre_of_the_canvas()
    {
        var (x, y) = new Placement(5).Resolve();

        Assert.Equal(0.5, x, 3);
        Assert.Equal(0.5, y, 3);
    }

    [Theory]
    [InlineData(7, HorizontalAnchor.Left, VerticalAnchor.Top)]
    [InlineData(5, HorizontalAnchor.Centre, VerticalAnchor.Middle)]
    [InlineData(3, HorizontalAnchor.Right, VerticalAnchor.Bottom)]
    public void Anchor_is_derived_from_the_cell(int cell, HorizontalAnchor horizontal, VerticalAnchor vertical)
    {
        // Corner placements must grow inward, or they drift off-canvas the
        // moment the text length changes.
        var anchor = new Placement(cell).Anchor;

        Assert.Equal(horizontal, anchor.Horizontal);
        Assert.Equal(vertical, anchor.Vertical);
    }

    [Fact]
    public void Sub_cells_give_eighty_one_distinct_positions()
    {
        var positions = new HashSet<(double, double)>();

        for (var cell = 1; cell <= 9; cell++)
        {
            for (var sub = 1; sub <= 9; sub++)
            {
                positions.Add(new Placement(cell, sub).Resolve());
            }
        }

        Assert.Equal(81, positions.Count);
    }

    [Fact]
    public void Nudging_cannot_push_a_graphic_off_canvas()
    {
        var placement = new Placement(9).Nudge(5, 5);
        var (x, y) = placement.Resolve();

        Assert.InRange(x, 0, 1);
        Assert.InRange(y, 0, 1);
    }
}
