using FreeX.App.UI;

namespace FreeX.App.Host;

public static class ZoomSelectionPlanner
{
    private const double DefaultColumnWidthPixels = 80d;
    private const double DefaultRowHeightPixels = 20d;
    private const double PercentScale = 100d;

    public static double CalculateDialogZoomPercent(
        ZoomDialogResult result,
        double gridWidth,
        double gridHeight,
        uint selectedColumns,
        uint selectedRows) =>
        result.FitSelection
            ? CalculateFitPercent(gridWidth, gridHeight, selectedColumns, selectedRows)
            : result.ZoomPercent;

    public static double CalculateFitPercent(
        double gridWidth,
        double gridHeight,
        uint selectedColumns,
        uint selectedRows)
    {
        var widthFit = CalculateAxisFitPercent(gridWidth, selectedColumns, DefaultColumnWidthPixels);
        var heightFit = CalculateAxisFitPercent(gridHeight, selectedRows, DefaultRowHeightPixels);
        return Math.Clamp(
            Math.Min(widthFit, heightFit),
            ZoomLevelMapper.MinZoomPercent,
            ZoomLevelMapper.MaxZoomPercent);
    }

    private static double CalculateAxisFitPercent(double viewportPixels, uint selectedCount, double defaultCellPixels)
    {
        var selectionPixels = Math.Max(1, selectedCount * defaultCellPixels);
        return viewportPixels / selectionPixels * PercentScale;
    }
}
