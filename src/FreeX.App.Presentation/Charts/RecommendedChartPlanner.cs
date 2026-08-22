using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts;

/// <summary>
/// Produces a small, deterministic set of chart types suited to the visible shape of a selected range.
/// This is deliberately a local heuristic: it does not call an external service or attempt to emulate
/// Excel's proprietary recommendation engine.
/// </summary>
public static class RecommendedChartPlanner
{
    private const uint MaximumSampleRows = 500;

    private static readonly ChartType[] DefaultRecommendations =
    [
        ChartType.Column,
        ChartType.Line,
        ChartType.Bar,
        ChartType.Pie,
        ChartType.Scatter,
    ];

    public static IReadOnlyList<ChartType> Recommend(Sheet? sheet, GridRange selectedRange)
    {
        if (sheet is null || selectedRange.Start.Sheet != sheet.Id)
            return DefaultRecommendations;

        var sourceRange = ChartInsertionPlanner.ResolveDataRange(sheet, selectedRange);
        if (sourceRange.Start.Sheet != sheet.Id || sourceRange.RowCount < 2 || sourceRange.ColCount < 2)
            return DefaultRecommendations;

        var firstDataRow = sourceRange.Start.Row + 1;
        var lastSampleRow = Math.Min(sourceRange.End.Row, firstDataRow + MaximumSampleRows - 1);
        var numericSeriesCount = 0;
        for (var column = sourceRange.Start.Col; column <= sourceRange.End.Col; column++)
        {
            if (ContainsNumber(sheet, firstDataRow, lastSampleRow, column))
                numericSeriesCount++;
        }

        if (numericSeriesCount == 0)
            return DefaultRecommendations;

        var firstColumnHasTemporalValues = ContainsTemporalValue(sheet, firstDataRow, lastSampleRow, sourceRange.Start.Col);
        var firstColumnHasTextValues = ContainsTextValue(sheet, firstDataRow, lastSampleRow, sourceRange.Start.Col);
        var recommendations = new List<ChartType>(4);

        if (firstColumnHasTemporalValues)
        {
            Add(recommendations, ChartType.Line);
            Add(recommendations, ChartType.Column);
            Add(recommendations, ChartType.Area);
        }
        else if (numericSeriesCount == 1 && firstColumnHasTextValues)
        {
            Add(recommendations, ChartType.Column);
            Add(recommendations, ChartType.Pie);
            Add(recommendations, ChartType.Bar);
        }
        else if (numericSeriesCount == 2 && !firstColumnHasTextValues)
        {
            Add(recommendations, ChartType.Scatter);
            Add(recommendations, ChartType.Line);
            Add(recommendations, ChartType.Column);
        }
        else
        {
            Add(recommendations, ChartType.Column);
            Add(recommendations, ChartType.StackedColumn);
            Add(recommendations, ChartType.Line);
            Add(recommendations, ChartType.Bar);
        }

        return recommendations;
    }

    private static bool ContainsNumber(Sheet sheet, uint firstRow, uint lastRow, uint column)
    {
        for (var row = firstRow; row <= lastRow; row++)
        {
            if (sheet.GetValue(row, column) is NumberValue)
                return true;
        }

        return false;
    }

    private static bool ContainsTemporalValue(Sheet sheet, uint firstRow, uint lastRow, uint column)
    {
        for (var row = firstRow; row <= lastRow; row++)
        {
            if (sheet.GetValue(row, column) is DateTimeValue)
                return true;
        }

        return false;
    }

    private static bool ContainsTextValue(Sheet sheet, uint firstRow, uint lastRow, uint column)
    {
        for (var row = firstRow; row <= lastRow; row++)
        {
            if (sheet.GetValue(row, column) is TextValue { Value.Length: > 0 })
                return true;
        }

        return false;
    }

    private static void Add(List<ChartType> recommendations, ChartType type)
    {
        if (ChartAuthoringPlanner.CanAuthor(type) && !recommendations.Contains(type))
            recommendations.Add(type);
    }
}
