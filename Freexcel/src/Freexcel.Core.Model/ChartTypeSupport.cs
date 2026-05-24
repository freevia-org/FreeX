namespace Freexcel.Core.Model;

public static class ChartTypeSupport
{
    public static bool IsKnown(ChartType type) => Enum.IsDefined(type);

    public static bool IsRenderable(ChartType type) =>
        type is ChartType.Column
            or ChartType.StackedColumn
            or ChartType.PercentStackedColumn
            or ChartType.Line
            or ChartType.ThreeDLine
            or ChartType.Pie
            or ChartType.ThreeDPie
            or ChartType.Doughnut
            or ChartType.Bar
            or ChartType.StackedBar
            or ChartType.PercentStackedBar
            or ChartType.Scatter
            or ChartType.Bubble
            or ChartType.Area
            or ChartType.Radar
            or ChartType.Stock
            or ChartType.Surface
            or ChartType.ThreeDSurface
            or ChartType.ThreeDColumn
            or ChartType.ThreeDBar
            or ChartType.ThreeDArea;

    public static bool SupportsTrendlines(ChartType type) =>
        type is ChartType.Column or ChartType.Line or ChartType.ThreeDLine or ChartType.Bar or ChartType.Scatter or ChartType.Bubble or ChartType.Area or ChartType.ThreeDArea;

    public static bool SupportsSecondaryAxis(ChartType type) =>
        type is ChartType.Column or ChartType.Line or ChartType.ThreeDLine or ChartType.Area or ChartType.ThreeDArea or ChartType.Scatter;

    public static bool SupportsAxes(ChartType type) =>
        type is not ChartType.Pie and not ChartType.ThreeDPie and not ChartType.Doughnut;

    public static bool SupportsComboLineOverlay(ChartType type) =>
        type is ChartType.Column or ChartType.StackedColumn or ChartType.PercentStackedColumn or ChartType.Area or ChartType.ThreeDArea;

    public static bool SupportsComboLineOverlay(ChartModel chart) =>
        SupportsComboLineOverlay(chart.Type) && GetDataSeriesCount(chart) >= 2;

    public static bool SupportsXAxisLogScale(ChartType type) =>
        type is ChartType.Bar or ChartType.StackedBar or ChartType.PercentStackedBar or ChartType.ThreeDBar or ChartType.Scatter or ChartType.Bubble;

    public static bool SupportsYAxisLogScale(ChartType type) =>
        type is ChartType.Column or ChartType.StackedColumn or ChartType.PercentStackedColumn or ChartType.Line or ChartType.ThreeDLine or ChartType.Scatter or ChartType.Bubble or ChartType.Area or ChartType.ThreeDArea;

    public static bool SupportsXAxisBounds(ChartType type) => SupportsXAxisLogScale(type);

    public static bool SupportsYAxisBounds(ChartType type) => SupportsYAxisLogScale(type);

    public static bool SupportsSeriesMarkers(ChartType type) =>
        type is ChartType.Line or ChartType.ThreeDLine or ChartType.Scatter;

    public static bool SupportsInvertIfNegative(ChartType type) =>
        type is ChartType.Column
            or ChartType.StackedColumn
            or ChartType.PercentStackedColumn
            or ChartType.Bar
            or ChartType.StackedBar
            or ChartType.PercentStackedBar
            or ChartType.ThreeDColumn
            or ChartType.ThreeDBar;

    public static bool SupportsPercentageDataLabels(ChartType type) =>
        type is ChartType.Pie or ChartType.ThreeDPie or ChartType.Doughnut or ChartType.PercentStackedColumn or ChartType.PercentStackedBar;

    public static bool SupportsFirstSliceAngle(ChartType type) =>
        type is ChartType.Pie or ChartType.ThreeDPie or ChartType.Doughnut;

    public static bool SupportsExplodedSlices(ChartType type) =>
        type is ChartType.Pie or ChartType.ThreeDPie or ChartType.Doughnut;

    public static bool SupportsDoughnutHoleSize(ChartType type) =>
        type is ChartType.Doughnut;

    public static int GetDataSeriesCount(ChartModel chart)
    {
        if (chart.Type == ChartType.Bubble)
            return Math.Max(0, (int)(chart.DataRange.End.Col - chart.DataRange.Start.Col) / 2);

        var startCol = chart.FirstColIsCategories ? chart.DataRange.Start.Col + 1 : chart.DataRange.Start.Col;
        if (chart.Type == ChartType.Scatter && !chart.FirstColIsCategories)
            startCol++;

        return startCol > chart.DataRange.End.Col
            ? 0
            : (int)(chart.DataRange.End.Col - startCol + 1);
    }

    public static int GetDataPointCount(ChartModel chart)
    {
        var startRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        return startRow > chart.DataRange.End.Row
            ? 0
            : (int)(chart.DataRange.End.Row - startRow + 1);
    }

    public static uint? GetXAxisValueColumn(ChartModel chart)
    {
        if (chart.Type is ChartType.Scatter or ChartType.Bubble)
            return chart.DataRange.Start.Col;

        return chart.FirstColIsCategories ? chart.DataRange.Start.Col : null;
    }

    public static IReadOnlyList<uint> GetXAxisValueColumns(ChartModel chart)
    {
        if (chart.Type is ChartType.Scatter or ChartType.Bubble)
            return [chart.DataRange.Start.Col];

        if (chart.Type is ChartType.Bar or ChartType.StackedBar or ChartType.PercentStackedBar or ChartType.ThreeDBar)
            return GetSeriesValueColumns(chart);

        return [];
    }

    public static IReadOnlyList<uint> GetYAxisValueColumns(ChartModel chart)
    {
        if (chart.Type == ChartType.Bubble)
        {
            var columns = new List<uint>();
            for (var col = chart.DataRange.Start.Col + 1; col < chart.DataRange.End.Col; col += 2)
                columns.Add(col);
            return columns;
        }

        return GetSeriesValueColumns(chart);
    }

    private static IReadOnlyList<uint> GetSeriesValueColumns(ChartModel chart)
    {
        var startCol = chart.FirstColIsCategories ? chart.DataRange.Start.Col + 1 : chart.DataRange.Start.Col;
        if (chart.Type == ChartType.Scatter && !chart.FirstColIsCategories)
            startCol++;
        if (startCol > chart.DataRange.End.Col)
            return [];

        var columns = new List<uint>();
        for (var col = startCol; col <= chart.DataRange.End.Col; col++)
            columns.Add(col);
        return columns;
    }
}
