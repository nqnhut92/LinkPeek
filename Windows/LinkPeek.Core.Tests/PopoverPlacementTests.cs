using System.Drawing;
using Xunit;
using LinkPeek.Core;

namespace LinkPeek.Core.Tests;

public sealed class PopoverPlacementTests
{
    private static readonly Rectangle Work = new(0, 0, 1920, 1040);
    private static readonly Size Popover = new(362, 380);

    private static Rectangle Card(PopoverLayout layout) => new(layout.Location, Popover);

    [Fact]
    public void PrefersBelowTheLinkWithTheArrowOnTop()
    {
        var link = new Rectangle(400, 200, 160, 18);
        var layout = PopoverPlacement.Compute(link, new Point(430, 209), Work, Popover);
        Assert.Equal(ArrowSide.Top, layout.Arrow);
        Assert.Equal(link.Bottom + 4, layout.Location.Y); // Tip clears the link by the tip gap.
        Assert.Equal(430, layout.Location.X + layout.ArrowOffset); // Arrow tip is under the pointer.
    }

    [Fact]
    public void FlipsAboveWhenThereIsNoRoomBelow()
    {
        var link = new Rectangle(400, 900, 160, 18);
        var layout = PopoverPlacement.Compute(link, new Point(430, 909), Work, Popover);
        Assert.Equal(ArrowSide.Bottom, layout.Arrow);
        Assert.Equal(link.Top - 4, Card(layout).Bottom);
    }

    [Fact]
    public void GoesBesideTheLinkWhenItFitsNeitherBelowNorAbove()
    {
        var shortWork = new Rectangle(0, 0, 1920, 600); // Taller card than half the screen.
        var link = new Rectangle(100, 300, 200, 18);
        var layout = PopoverPlacement.Compute(link, new Point(150, 309), shortWork, Popover);
        Assert.Equal(ArrowSide.Left, layout.Arrow);
        Assert.Equal(link.Right + 4, layout.Location.X);
        Assert.True(Card(layout).Top >= 0 && Card(layout).Bottom <= 600);
        Assert.Equal(309, layout.Location.Y + layout.ArrowOffset);
    }

    [Fact]
    public void UsesTheLeftSideNearTheRightScreenEdge()
    {
        var shortWork = new Rectangle(0, 0, 1920, 600);
        var link = new Rectangle(1700, 300, 200, 18);
        var layout = PopoverPlacement.Compute(link, new Point(1750, 309), shortWork, Popover);
        Assert.Equal(ArrowSide.Right, layout.Arrow);
        Assert.Equal(link.Left - 4, Card(layout).Right);
    }

    [Fact]
    public void WideLinkFallsBackToBesideThePointer()
    {
        // A link spanning the whole chat pane leaves no room at its ends, but the pointer is mid-line.
        var shortWork = new Rectangle(0, 0, 1000, 600);
        var link = new Rectangle(10, 300, 980, 18);
        var pointer = new Point(300, 309);
        var layout = PopoverPlacement.Compute(link, pointer, shortWork, Popover);
        Assert.Equal(ArrowSide.Left, layout.Arrow);
        Assert.Equal(pointer.X + 8 + 4, layout.Location.X);
        Assert.True(Card(layout).Right <= 1000);
    }

    [Fact]
    public void KeepsTheArrowInsideTheCardAtScreenEdges()
    {
        var link = new Rectangle(1890, 200, 28, 18);
        var layout = PopoverPlacement.Compute(link, new Point(1905, 209), Work, Popover);
        Assert.Equal(ArrowSide.Top, layout.Arrow);
        Assert.True(Card(layout).Right <= Work.Right);
        Assert.InRange(layout.ArrowOffset, 31, Popover.Width - 31);
        Assert.True(layout.Location.X + layout.ArrowOffset <= link.Right); // Still points at the link.
    }

    [Fact]
    public void ClampsWithoutAnArrowWhenNothingFits()
    {
        var tiny = new Rectangle(0, 0, 400, 400);
        var layout = PopoverPlacement.Compute(new Rectangle(20, 200, 100, 18), new Point(60, 209), tiny, Popover);
        Assert.Equal(ArrowSide.None, layout.Arrow);
        Assert.True(Card(layout).Bottom <= 400 && layout.Location.X >= 0);
    }

    [Fact]
    public void ATallAccessibilityRectangleIsNarrowedToTheLineUnderThePointer()
    {
        // Teams clips a long link with an ellipsis, but its rectangle covers every wrapped line.
        var link = new Rectangle(100, 440, 600, 118);
        var pointer = new Point(300, 548);
        var layout = PopoverPlacement.Compute(link, pointer, Work, Popover);
        Assert.Equal(ArrowSide.Top, layout.Arrow);
        Assert.InRange(layout.Location.Y, pointer.Y, pointer.Y + 28); // Right under the line, not under the whole block.
        var shortScreen = new Rectangle(0, 0, 1920, 600);
        var above = PopoverPlacement.Compute(new Rectangle(100, 440, 600, 118), new Point(300, 548), new Rectangle(0, 0, 1920, 1000), new Size(362, 520));
        Assert.Equal(ArrowSide.Bottom, above.Arrow);
        Assert.InRange(above.Location.Y + 520, pointer.Y - 28, pointer.Y); // Arrow tip stays near the pointer's line.
    }

    [Fact]
    public void ThinAnchorPointsAtThePointer()
    {
        var cursor = new Point(800, 500);
        var thin = new Rectangle(cursor, new Size(1, 12));
        var layout = PopoverPlacement.Compute(thin, cursor, Work, Popover);
        Assert.Equal(ArrowSide.Top, layout.Arrow);
        Assert.Equal(cursor.X, layout.Location.X + layout.ArrowOffset);
    }
}
