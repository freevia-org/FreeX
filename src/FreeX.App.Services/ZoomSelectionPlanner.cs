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

    public static double CalculateZoomPercent(
        int requestedZoomPercent,
        bool fitSelection,
        double gridWidth,
        double gridHeight,
        IReadOnlyList<double> selectedColumnWidths,
        IReadOnlyList<double> selectedRowHeights) =>
        fitSelection
            ? CalculateFitPercent(gridWidth, gridHeight, selectedColumnWidths, selectedRowHeights)
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

    public static double CalculateDialogZoomPercent(
        ZoomDialogSelection result,
        double gridWidth,
        double gridHeight,
        IReadOnlyList<double> selectedColumnWidths,
        IReadOnlyList<double> selectedRowHeights) =>
        CalculateZoomPercent(
            result.ZoomPercent,
            result.FitSelection,
            gridWidth,
            gridHeight,
            selectedColumnWidths,
            selectedRowHeights);

    /// <summary>
    /// Fits the given (default-sized) selection extent into the viewport. Kept for callers that
    /// only have a column/row count and not the selection's real pixel metrics -- prefer the
    /// <see cref="IReadOnlyList{T}"/> overload below whenever actual column widths/row heights
    /// (e.g. from <c>ViewportModel.ColMetrics</c>/<c>RowMetrics</c> or <c>Sheet.ColumnWidths</c>/
    /// <c>RowHeights</c>) are available, since Excel's Zoom-to-Selection fits the *actual* selection
    /// extent, not an assumed default-sized one.
    /// </summary>
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

    /// <summary>
    /// Fits the selection's real pixel extent (sum of its actual column widths / row heights --
    /// which may differ from the Excel defaults when columns/rows were resized) into the viewport,
    /// matching Excel's Zoom-to-Selection behavior.
    /// </summary>
    public static double CalculateFitPercent(
        double gridWidth,
        double gridHeight,
        IReadOnlyList<double> selectedColumnWidths,
        IReadOnlyList<double> selectedRowHeights)
    {
        ArgumentNullException.ThrowIfNull(selectedColumnWidths);
        ArgumentNullException.ThrowIfNull(selectedRowHeights);

        var widthFit = CalculateAxisFitPercent(gridWidth, SumPixels(selectedColumnWidths, DefaultColumnWidthPixels));
        var heightFit = CalculateAxisFitPercent(gridHeight, SumPixels(selectedRowHeights, DefaultRowHeightPixels));
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

    public static int CalculateFitWholePercent(
        double gridWidth,
        double gridHeight,
        IReadOnlyList<double> selectedColumnWidths,
        IReadOnlyList<double> selectedRowHeights) =>
        (int)Math.Round(CalculateFitPercent(gridWidth, gridHeight, selectedColumnWidths, selectedRowHeights));

    private static double CalculateAxisFitPercent(double viewportPixels, uint selectedCount, double defaultCellPixels)
    {
        var selectionPixels = Math.Max(1, selectedCount * defaultCellPixels);
        return viewportPixels / selectionPixels * PercentScale;
    }

    private static double CalculateAxisFitPercent(double viewportPixels, double selectionPixels) =>
        viewportPixels / Math.Max(1, selectionPixels) * PercentScale;

    private static double SumPixels(IReadOnlyList<double> pixelSizes, double defaultCellPixels)
    {
        if (pixelSizes.Count == 0)
            return defaultCellPixels;

        double total = 0;
        for (var i = 0; i < pixelSizes.Count; i++)
        {
            var size = pixelSizes[i];
            total += size > 0 ? size : defaultCellPixels;
        }

        return total;
    }
}
