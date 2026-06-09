namespace FreeX.App.UI;

internal static class GridResizeSizePlanner
{
    public const double MinimumSizePixels = 0;
    public const double MaximumColumnSizePixels = 255 * ColumnWidthCommitScale;
    public const double MaximumRowSizePixels = 409.5;

    private const double ColumnWidthCommitScale = 8.0;

    public static double ClampColumnSize(double requestedPixels) =>
        ClampToExcelRange(requestedPixels, MaximumColumnSizePixels);

    public static double ClampRowSize(double requestedPixels) =>
        ClampToExcelRange(requestedPixels, MaximumRowSizePixels);

    private static double ClampToExcelRange(double requestedPixels, double maximumPixels)
    {
        if (double.IsNaN(requestedPixels) || requestedPixels <= MinimumSizePixels)
            return MinimumSizePixels;

        if (requestedPixels >= maximumPixels)
            return maximumPixels;

        return requestedPixels;
    }
}
