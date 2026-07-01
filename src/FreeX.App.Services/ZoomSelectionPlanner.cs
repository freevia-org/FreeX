namespace FreeX.App.Services;

public static class ZoomSelectionPlanner
{
    private const double DefaultColumnWidthPixels = 80d;
    private const double DefaultRowHeightPixels = 20d;
    private const double PercentScale = 100d;

    public static double CalculateZoomPercent(
        int requestedZoomPercent,
        bool fitSelection,
        double gridWidth,
        double gridHeight,
        uint selectedColumns,
        uint selectedRows) =>
        fitSelection
            ? CalculateFitPercent(gridWidth, gridHeight, selectedColumns, selectedRows)
            : requestedZoomPercent;

    public static double CalculateDialogZoomPercent(
        ZoomDialogSelection result,
        double gridWidth,
        double gridHeight,
        uint selectedColumns,
        uint selectedRows) =>
        CalculateZoomPercent(
            result.ZoomPercent,
            result.FitSelection,
            gridWidth,
            gridHeight,
            selectedColumns,
            selectedRows);

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

    public static int CalculateFitWholePercent(
        double gridWidth,
        double gridHeight,
        uint selectedColumns,
        uint selectedRows) =>
        (int)Math.Round(CalculateFitPercent(gridWidth, gridHeight, selectedColumns, selectedRows));

    private static double CalculateAxisFitPercent(double viewportPixels, uint selectedCount, double defaultCellPixels)
    {
        var selectionPixels = Math.Max(1, selectedCount * defaultCellPixels);
        return viewportPixels / selectionPixels * PercentScale;
    }
}
