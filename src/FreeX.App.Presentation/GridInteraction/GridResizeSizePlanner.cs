using FreeX.Core.Calc;
using FreeX.Core.Commands;

namespace FreeX.App.Presentation.GridInteraction;

/// <summary>
/// Pure clamping math for committing a row/column resize drag: the new size is clamped to the
/// spreadsheet's allowed range (zero is allowed, meaning hide), and the resize guide line position is
/// derived from the clamped size. Shared by the desktop hosts so a drag commits to the same size.
/// </summary>
public static class GridResizeSizePlanner
{
    public const double MinimumSizePixels = 0;
    public const double MeaningfulDragThresholdPixels = 4;
    public const double MaximumColumnSizePixels = ColumnWidthPixelMapper.MaximumColumnWidthPixels;

    // R105: the drag delta this planner clamps is a pixel value (it feeds SetRowHeightCommand's
    // pixel-space height directly -- see GridView.Input.cs / MainWindow.GridStatus.cs.OnRowResized),
    // so the ceiling must be in pixels. It was previously the raw 409.5, which is Excel's row-height
    // ceiling expressed in POINTS -- that mismatch capped interactive drag-resize at ~409px even
    // though SetRowHeightCommand itself (fixed in R102) legally accepts up to 546px. Reuse the
    // already-converted shared constant instead of a fourth copy of the 96/72 arithmetic.
    public const double MaximumRowSizePixels = AutoFitSizingService.MaximumRowHeight;

    public static double ClampColumnSize(double requestedPixels) =>
        ClampToAllowedRange(requestedPixels, MaximumColumnSizePixels);

    public static double ClampRowSize(double requestedPixels) =>
        ClampToAllowedRange(requestedPixels, MaximumRowSizePixels);

    public static bool IsMeaningfulDrag(double startPointer, double currentPointer) =>
        Math.Abs(currentPointer - startPointer) >= MeaningfulDragThresholdPixels;

    public static double CalculateLinePosition(double sizeStartPixels, double dragEdgeStart, double resizedSizePixels) =>
        dragEdgeStart - sizeStartPixels + resizedSizePixels;

    private static double ClampToAllowedRange(double requestedPixels, double maximumPixels)
    {
        if (double.IsNaN(requestedPixels) || requestedPixels <= MinimumSizePixels)
            return MinimumSizePixels;

        if (requestedPixels >= maximumPixels)
            return maximumPixels;

        return requestedPixels;
    }
}
