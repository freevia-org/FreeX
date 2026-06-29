using System.Windows;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public static class GridTextBoxPlacementPlanner
{
    public const double DefaultTextBoxWidth = DrawingInsertionPlanner.DefaultTextBoxWidth;
    public const double DefaultTextBoxHeight = DrawingInsertionPlanner.DefaultTextBoxHeight;
    public const double MinimumTextBoxSize = DrawingObjectPlacementPlanner.MinimumObjectSize;

    public static Rect CalculatePreviewRect(
        Point start,
        Point current,
        double minimumSize = MinimumTextBoxSize) =>
        ToWpfRect(DrawingObjectPlacementPlanner.CalculatePreviewRect(
            ToLayoutPoint(start),
            ToLayoutPoint(current),
            minimumSize));

    public static Point CalculateAnchorPoint(
        Point start,
        Point current,
        double minimumSize = MinimumTextBoxSize) =>
        ToWpfPoint(DrawingObjectPlacementPlanner.CalculateAnchorPoint(
            ToLayoutPoint(start),
            ToLayoutPoint(current),
            minimumSize));

    public static TextBoxPlacementRequest CreateRequest(
        CellAddress anchor,
        Point start,
        Point current)
    {
        var plan = DrawingObjectPlacementPlanner.PlanDrag(
            ToLayoutPoint(start),
            ToLayoutPoint(current),
            DefaultTextBoxWidth,
            DefaultTextBoxHeight,
            MinimumTextBoxSize);
        return new TextBoxPlacementRequest(
            anchor,
            plan.Width,
            plan.Height);
    }

    private static LayoutPoint ToLayoutPoint(Point point) => new(point.X, point.Y);

    private static Point ToWpfPoint(LayoutPoint point) => new(point.X, point.Y);

    private static Rect ToWpfRect(LayoutRect rect) => new(rect.Left, rect.Top, rect.Width, rect.Height);
}
