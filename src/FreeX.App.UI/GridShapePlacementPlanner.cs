using System.Windows;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public static class GridShapePlacementPlanner
{
    public const double DefaultShapeWidth = DrawingInsertionPlanner.DefaultShapeWidth;
    public const double DefaultShapeHeight = DrawingInsertionPlanner.DefaultShapeHeight;
    public const double MinimumShapeSize = DrawingObjectPlacementPlanner.MinimumObjectSize;
    public const double MeaningfulDragThreshold = DrawingObjectPlacementPlanner.MeaningfulDragThreshold;

    public static bool IsMeaningfulDrag(Point start, Point current, double threshold = MeaningfulDragThreshold) =>
        DrawingObjectPlacementPlanner.IsMeaningfulDrag(ToLayoutPoint(start), ToLayoutPoint(current), threshold);

    public static Rect CalculatePreviewRect(
        Point start,
        Point current,
        double minimumSize = MinimumShapeSize)
        => ToWpfRect(DrawingObjectPlacementPlanner.CalculatePreviewRect(
            ToLayoutPoint(start),
            ToLayoutPoint(current),
            minimumSize));

    public static Point CalculateAnchorPoint(
        Point start,
        Point current,
        double minimumSize = MinimumShapeSize) =>
        ToWpfPoint(DrawingObjectPlacementPlanner.CalculateAnchorPoint(
            ToLayoutPoint(start),
            ToLayoutPoint(current),
            minimumSize));

    public static ShapePlacementRequest CreateRequest(
        DrawingShapeKind kind,
        CellAddress anchor,
        Point start,
        Point current)
    {
        var plan = DrawingObjectPlacementPlanner.PlanDrag(
            ToLayoutPoint(start),
            ToLayoutPoint(current),
            DefaultShapeWidth,
            DefaultShapeHeight,
            MinimumShapeSize);
        return new ShapePlacementRequest(
            kind,
            anchor,
            plan.Width,
            plan.Height);
    }

    private static LayoutPoint ToLayoutPoint(Point point) => new(point.X, point.Y);

    private static Point ToWpfPoint(LayoutPoint point) => new(point.X, point.Y);

    private static Rect ToWpfRect(LayoutRect rect) => new(rect.Left, rect.Top, rect.Width, rect.Height);
}
