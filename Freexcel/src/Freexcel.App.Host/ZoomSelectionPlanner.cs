using Freexcel.App.UI;

namespace Freexcel.App.Host;

public static class ZoomSelectionPlanner
{
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
        var widthFit = gridWidth / Math.Max(1, selectedColumns * 80d) * 100;
        var heightFit = gridHeight / Math.Max(1, selectedRows * 20d) * 100;
        return Math.Clamp(
            Math.Min(widthFit, heightFit),
            ZoomLevelMapper.MinZoomPercent,
            ZoomLevelMapper.MaxZoomPercent);
    }
}
