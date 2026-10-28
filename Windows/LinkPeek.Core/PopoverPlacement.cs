using System.Drawing;

namespace LinkPeek.Core;

/// <summary>Which edge of the card carries the arrow; None means the card could not be attached.</summary>
public enum ArrowSide { None, Top, Bottom, Left, Right }

/// <param name="Location">Top-left of the popover (card plus arrow bands) in screen pixels.</param>
/// <param name="Arrow">Edge of the card the arrow sits on.</param>
/// <param name="ArrowOffset">Distance in pixels from the popover's left (Top/Bottom) or top (Left/Right) edge to the arrow's center.</param>
public readonly record struct PopoverLayout(Point Location, ArrowSide Arrow, int ArrowOffset);

/// <summary>
/// Windows counterpart of NSPopover's edge choice. The popover prefers to sit below the link, then
/// above, then beside it; if the link itself leaves no room (a tall card on a short screen), it
/// sits beside the pointer instead. The arrow always points at the link and the card stays on
/// screen. Only when nothing fits is the card clamped to the screen without an arrow.
/// </summary>
public static class PopoverPlacement
{
    /// <param name="anchor">Bounds of the hovered link, or a thin rectangle at the pointer.</param>
    /// <param name="size">Popover size including the arrow bands on every side.</param>
    /// <param name="gap">Minimum distance kept from the work area edge.</param>
    /// <param name="band">Thickness of the arrow band; arrows keep at least this plus <paramref name="inset"/> from the corners.</param>
    public static PopoverLayout Compute(Rectangle anchor, Point pointer, Rectangle work, Size size,
        int gap = 6, int band = 9, int inset = 22)
    {
        var tipX = anchor.Width <= 1 ? pointer.X : Math.Clamp(pointer.X, anchor.Left, Math.Max(anchor.Left, anchor.Right - 1));
        var tipY = anchor.Height <= 1 ? pointer.Y : Math.Clamp(pointer.Y, anchor.Top, Math.Max(anchor.Top, anchor.Bottom - 1));
        int Across(int tip, int length, int low, int high) =>
            Math.Clamp(tip - length / 2, low + gap, Math.Max(low + gap, high - gap - length));
        int Offset(int tip, int start, int length) =>
            Math.Clamp(tip - start, band + inset, Math.Max(band + inset, length - band - inset));

        var below = anchor.Bottom + size.Height <= work.Bottom - gap;
        var above = anchor.Top - size.Height >= work.Top + gap;
        if (below || above)
        {
            var x = Across(tipX, size.Width, work.Left, work.Right);
            return below
                ? new(new Point(x, anchor.Bottom), ArrowSide.Top, Offset(tipX, x, size.Width))
                : new(new Point(x, anchor.Top - size.Height), ArrowSide.Bottom, Offset(tipX, x, size.Width));
        }

        // Beside the link, then beside the pointer. A wide link (a whole chat line) may leave no
        // room at its ends, but there is usually room next to where the pointer is.
        foreach (var (rightX, leftX) in new[] { (anchor.Right + 2, anchor.Left - 2 - size.Width), (pointer.X + 8, pointer.X - 8 - size.Width) })
        {
            var right = rightX + size.Width <= work.Right - gap;
            var left = leftX >= work.Left + gap;
            if (!right && !left) continue;
            var y = Across(tipY, size.Height, work.Top, work.Bottom);
            return right
                ? new(new Point(rightX, y), ArrowSide.Left, Offset(tipY, y, size.Height))
                : new(new Point(leftX, y), ArrowSide.Right, Offset(tipY, y, size.Height));
        }

        var cx = Across(tipX, size.Width, work.Left, work.Right);
        var cy = Math.Clamp(anchor.Bottom, work.Top, Math.Max(work.Top, work.Bottom - size.Height));
        return new(new Point(cx, cy), ArrowSide.None, 0);
    }
}
