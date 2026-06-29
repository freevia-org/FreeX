using FreeX.App.Presentation.DrawingInteraction;

namespace FreeX.App.Presentation.DrawingUI;

public readonly record struct DrawingObjectPlacementDragPlan(
    LayoutRect PreviewRect,
    LayoutPoint AnchorPoint,
    bool IsMeaningfulDrag,
    double Width,
    double Height);

/// <summary>
/// Portable drag-placement math shared by shape and text-box insertion renderers.
/// </summary>
public static class DrawingObjectPlacementPlanner
{
    public const double MinimumObjectSize = ObjectDragPlanner.MinimumObjectSize;
    public const double MeaningfulDragThreshold = 4d;

    public static bool IsMeaningfulDrag(
        LayoutPoint start,
        LayoutPoint current,
        double threshold = MeaningfulDragThreshold) =>
        Math.Abs(current.X - start.X) >= threshold ||
        Math.Abs(current.Y - start.Y) >= threshold;

    public static LayoutRect CalculatePreviewRect(
        LayoutPoint start,
        LayoutPoint current,
        double minimumSize = MinimumObjectSize)
    {
        var minSize = NormalizeMinimumSize(minimumSize);
        var left = Math.Min(start.X, current.X);
        var top = Math.Min(start.Y, current.Y);
        var width = Math.Max(minSize, Math.Abs(current.X - start.X));
        var height = Math.Max(minSize, Math.Abs(current.Y - start.Y));
        return new LayoutRect(left, top, width, height);
    }

    public static LayoutPoint CalculateAnchorPoint(
        LayoutPoint start,
        LayoutPoint current,
        double minimumSize = MinimumObjectSize,
        double threshold = MeaningfulDragThreshold)
    {
        if (!IsMeaningfulDrag(start, current, threshold))
            return start;

        var previewRect = CalculatePreviewRect(start, current, minimumSize);
        return new LayoutPoint(previewRect.Left, previewRect.Top);
    }

    public static DrawingObjectPlacementDragPlan PlanDrag(
        LayoutPoint start,
        LayoutPoint current,
        double defaultWidth,
        double defaultHeight,
        double minimumSize = MinimumObjectSize,
        double threshold = MeaningfulDragThreshold)
    {
        var previewRect = CalculatePreviewRect(start, current, minimumSize);
        if (!IsMeaningfulDrag(start, current, threshold))
        {
            return new DrawingObjectPlacementDragPlan(
                previewRect,
                start,
                IsMeaningfulDrag: false,
                defaultWidth,
                defaultHeight);
        }

        return new DrawingObjectPlacementDragPlan(
            previewRect,
            new LayoutPoint(previewRect.Left, previewRect.Top),
            IsMeaningfulDrag: true,
            Math.Max(NormalizeMinimumSize(minimumSize), Math.Abs(current.X - start.X)),
            Math.Max(NormalizeMinimumSize(minimumSize), Math.Abs(current.Y - start.Y)));
    }

    private static double NormalizeMinimumSize(double minimumSize) =>
        double.IsFinite(minimumSize) && minimumSize > 0 ? minimumSize : MinimumObjectSize;
}
