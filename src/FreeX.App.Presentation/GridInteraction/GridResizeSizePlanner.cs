using FreeX.Core.Calc;

namespace FreeX.App.Presentation.GridInteraction;

/// <summary>
/// Pure clamping math for committing a row/column resize drag: the new size is clamped to the
/// spreadsheet's allowed range (zero is allowed, meaning hide), and the resize guide line position is
/// derived from the clamped size. Shared by the desktop hosts so a drag commits to the same size.
/// </summary>
public static class GridResizeSizePlanner
{
    public const double MinimumSizePixels = 0;
    public const double MaximumColumnSizePixels = ColumnWidthPixelMapper.MaximumColumnWidthPixels;
    public const double MaximumRowSizePixels = 409.5;

    public static double ClampColumnSize(double requestedPixels) =>
        ClampToAllowedRange(requestedPixels, MaximumColumnSizePixels);

    public static double ClampRowSize(double requestedPixels) =>
        ClampToAllowedRange(requestedPixels, MaximumRowSizePixels);

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
