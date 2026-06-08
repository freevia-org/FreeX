using System.Windows;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public static class GridShapePlacementPlanner
{
    public const double DefaultShapeWidth = 120;
    public const double DefaultShapeHeight = 70;
    public const double MinimumShapeSize = GridObjectDragPlanner.MinimumObjectSize;
    public const double MeaningfulDragThreshold = 4;

    public static bool IsMeaningfulDrag(Point start, Point current, double threshold = MeaningfulDragThreshold) =>
        Math.Abs(current.X - start.X) >= threshold ||
        Math.Abs(current.Y - start.Y) >= threshold;

    public static Rect CalculatePreviewRect(
        Point start,
        Point current,
        double minimumSize = MinimumShapeSize)
    {
        var left = Math.Min(start.X, current.X);
        var top = Math.Min(start.Y, current.Y);
        var width = Math.Max(minimumSize, Math.Abs(current.X - start.X));
        var height = Math.Max(minimumSize, Math.Abs(current.Y - start.Y));
        return new Rect(left, top, width, height);
    }

    public static ShapePlacementRequest CreateRequest(
        DrawingShapeKind kind,
        CellAddress anchor,
        Point start,
        Point current)
    {
        if (!IsMeaningfulDrag(start, current))
            return new ShapePlacementRequest(kind, anchor, DefaultShapeWidth, DefaultShapeHeight);

        return new ShapePlacementRequest(
            kind,
            anchor,
            Math.Max(MinimumShapeSize, Math.Abs(current.X - start.X)),
            Math.Max(MinimumShapeSize, Math.Abs(current.Y - start.Y)));
    }
}
