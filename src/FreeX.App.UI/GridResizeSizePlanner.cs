using FreeX.Core.Calc;

namespace FreeX.App.UI;

internal static class GridResizeSizePlanner
{
    public const double MinimumSizePixels = 0;
    public const double MaximumColumnSizePixels = ColumnWidthPixelMapper.MaximumColumnWidthPixels;
    public const double MaximumRowSizePixels = 409.5;

    public static double ClampColumnSize(double requestedPixels) =>
        ClampToExcelRange(requestedPixels, MaximumColumnSizePixels);

    public static double ClampRowSize(double requestedPixels) =>
        ClampToExcelRange(requestedPixels, MaximumRowSizePixels);

    public static double CalculateLinePosition(double sizeStartPixels, double dragEdgeStart, double resizedSizePixels) =>
        dragEdgeStart - sizeStartPixels + resizedSizePixels;

    private static double ClampToExcelRange(double requestedPixels, double maximumPixels)
    {
        if (double.IsNaN(requestedPixels) || requestedPixels <= MinimumSizePixels)
            return MinimumSizePixels;

        if (requestedPixels >= maximumPixels)
            return maximumPixels;

        return requestedPixels;
    }
}
