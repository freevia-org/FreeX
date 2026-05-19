using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public readonly record struct AutoFitSizePlan(uint Index, double Size);

public static class AutoFitPlanner
{
    public static IReadOnlyList<AutoFitSizePlan> PlanRowHeights(
        GridRange selection,
        GridRange? usedRange,
        Func<uint, uint, string?> getDisplayText,
        double defaultHeight)
    {
        var bounds = GetMeasurementBounds(selection, usedRange, AutoFitAxis.Rows);
        if (bounds is null)
            return [];

        var plans = new List<AutoFitSizePlan>();
        for (var row = bounds.Value.Start.Row; row <= bounds.Value.End.Row; row++)
        {
            var texts = new List<string>();
            for (var col = bounds.Value.Start.Col; col <= bounds.Value.End.Col; col++)
            {
                if (getDisplayText(row, col) is { } text)
                    texts.Add(text);
            }

            plans.Add(new AutoFitSizePlan(row, AutoFitSizingService.EstimateRowHeight(texts, defaultHeight)));
        }

        return plans;
    }

    public static IReadOnlyList<AutoFitSizePlan> PlanColumnWidths(
        GridRange selection,
        GridRange? usedRange,
        Func<uint, uint, string?> getDisplayText,
        double defaultWidth)
    {
        var bounds = GetMeasurementBounds(selection, usedRange, AutoFitAxis.Columns);
        if (bounds is null)
            return [];

        var plans = new List<AutoFitSizePlan>();
        for (var col = bounds.Value.Start.Col; col <= bounds.Value.End.Col; col++)
        {
            var texts = new List<string>();
            for (var row = bounds.Value.Start.Row; row <= bounds.Value.End.Row; row++)
            {
                if (getDisplayText(row, col) is { } text)
                    texts.Add(text);
            }

            plans.Add(new AutoFitSizePlan(col, AutoFitSizingService.EstimateColumnWidth(texts, defaultWidth)));
        }

        return plans;
    }

    private static GridRange? GetMeasurementBounds(GridRange selection, GridRange? usedRange, AutoFitAxis axis)
    {
        if (axis == AutoFitAxis.Rows && selection.RowCount == CellAddress.MaxRow)
            return null;

        if (axis == AutoFitAxis.Columns && selection.ColCount == CellAddress.MaxCol)
            return null;

        if (axis == AutoFitAxis.Columns && selection.RowCount == CellAddress.MaxRow)
        {
            if (usedRange is null)
                return null;

            return new GridRange(
                new CellAddress(selection.Start.Sheet, usedRange.Value.Start.Row, selection.Start.Col),
                new CellAddress(selection.Start.Sheet, usedRange.Value.End.Row, selection.End.Col));
        }

        if (axis == AutoFitAxis.Rows && selection.ColCount == CellAddress.MaxCol)
        {
            if (usedRange is null)
                return null;

            return new GridRange(
                new CellAddress(selection.Start.Sheet, selection.Start.Row, usedRange.Value.Start.Col),
                new CellAddress(selection.Start.Sheet, selection.End.Row, usedRange.Value.End.Col));
        }

        return selection;
    }

    private enum AutoFitAxis
    {
        Rows,
        Columns
    }
}
