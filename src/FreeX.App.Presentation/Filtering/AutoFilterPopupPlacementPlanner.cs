using Free.Shared.Drawing;

namespace FreeX.App.Presentation.Filtering;

public enum AutoFilterPopupPlacementEdge
{
    BottomStart
}

public readonly record struct AutoFilterPopupPlacement(
    LayoutPoint Anchor,
    AutoFilterPopupPlacementEdge Edge);

/// <summary>
/// Owns the shared AutoFilter popup anchor intent. Renderers translate the bottom-start edge into
/// their native popup primitive and convert the planned point at their coordinate-system boundary.
/// </summary>
public static class AutoFilterPopupPlacementPlanner
{
    public const double PointerVerticalOffset = 18.0;
    public const AutoFilterPopupPlacementEdge PreferredEdge = AutoFilterPopupPlacementEdge.BottomStart;

    public static AutoFilterPopupPlacement FromPointer(LayoutPoint pointer) =>
        new(
            new LayoutPoint(pointer.X, pointer.Y + PointerVerticalOffset),
            PreferredEdge);

    public static AutoFilterPopupPlacement FromHeaderBounds(LayoutRect headerBounds) =>
        new(
            new LayoutPoint(headerBounds.Left, headerBounds.Bottom),
            PreferredEdge);
}
