namespace FreeX.Core.Model;

public readonly record struct ChartSeriesColumn(int SeriesIndex, uint Column);

/// <summary>Maps chart series indexes to their physical worksheet value columns.</summary>
public static class ChartSeriesColumnPolicy
{
    public static bool HasAuthoritativeMappings(
        ChartModel chart,
        uint dataStartColumn,
        uint endColumn)
    {
        if (chart.SeriesInRows || chart.SeriesColumnMappings.Count == 0)
            return false;

        for (var i = 0; i < chart.SeriesColumnMappings.Count; i++)
        {
            var column = chart.SeriesColumnMappings[i].ValueColumn;
            if (column < dataStartColumn || column > endColumn)
                return false;
        }

        return true;
    }

    public static bool ShouldSkipSourceColumn(
        ChartModel chart,
        uint column,
        uint dataStartColumn) =>
        chart.Type == ChartType.Scatter &&
        !chart.FirstColIsCategories &&
        column == dataStartColumn;

    public static bool ShouldUseSourceColumn(
        ChartModel chart,
        uint column,
        uint dataStartColumn,
        uint endColumn)
    {
        if (ShouldSkipSourceColumn(chart, column, dataStartColumn))
            return false;

        if (!HasAuthoritativeMappings(chart, dataStartColumn, endColumn))
            return true;

        for (var i = 0; i < chart.SeriesColumnMappings.Count; i++)
        {
            if (chart.SeriesColumnMappings[i].ValueColumn == column)
                return true;
        }

        return false;
    }

    public static int ResolveSeriesIndex(
        ChartModel chart,
        uint column,
        uint dataStartColumn,
        uint endColumn = uint.MaxValue)
    {
        if (HasAuthoritativeMappings(chart, dataStartColumn, endColumn))
        {
            for (var i = 0; i < chart.SeriesColumnMappings.Count; i++)
            {
                var mapping = chart.SeriesColumnMappings[i];
                if (mapping.ValueColumn == column)
                    return mapping.SeriesXmlIndex;
            }
        }

        var relativeColumn = column - dataStartColumn;
        if (chart.Type == ChartType.Bubble)
            return checked((int)(relativeColumn / 2));

        var scatterOffset = chart.Type == ChartType.Scatter && !chart.FirstColIsCategories ? 1u : 0u;
        return checked((int)(relativeColumn - scatterOffset));
    }

    public static IReadOnlyList<ChartSeriesColumn> GetCurrentSeriesColumns(
        ChartModel chart,
        uint dataStartColumn,
        uint endColumn)
    {
        if (HasAuthoritativeMappings(chart, dataStartColumn, endColumn))
        {
            return chart.SeriesColumnMappings
                .OrderBy(mapping => mapping.SeriesXmlIndex)
                .Select(mapping => new ChartSeriesColumn(mapping.SeriesXmlIndex, mapping.ValueColumn))
                .ToArray();
        }

        var columns = new List<ChartSeriesColumn>();
        for (var column = dataStartColumn; column <= endColumn; column++)
        {
            if (!ShouldUseSourceColumn(chart, column, dataStartColumn, endColumn))
                continue;

            columns.Add(new ChartSeriesColumn(
                ResolveSeriesIndex(chart, column, dataStartColumn, endColumn),
                column));
        }

        return columns;
    }
}
