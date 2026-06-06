using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class WorkbookSelectionStatsCalculator
{
    private static readonly WorkbookSelectionStats EmptyStats = new(0, 0, 0, null, null, null);

    public static WorkbookSelectionStats Calculate(Sheet sheet, GridRange range)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        if (range.Start == range.End)
            return CalculateSingleCell(sheet.GetValue(range.Start.Row, range.Start.Col));

        if (sheet.GetUsedRange() is not { } usedRange || !usedRange.Overlaps(range))
            return EmptyStats;

        double sum = 0;
        int count = 0;
        int numericalCount = 0;
        double? min = null, max = null;

        var scanRange = Intersect(range, usedRange);
        long totalCells = scanRange.CellCount;

        if (sheet.CellCount < totalCells)
        {
            foreach (var entry in sheet.GetOccupiedCellMap())
            {
                var (row, col) = entry.Key;
                if (Contains(scanRange, row, col))
                    Accumulate(entry.Value.Value, ref sum, ref count, ref numericalCount, ref min, ref max);
            }
        }
        else
        {
            for (var row = scanRange.Start.Row; row <= scanRange.End.Row; row++)
            {
                for (var col = scanRange.Start.Col; col <= scanRange.End.Col; col++)
                    Accumulate(sheet.GetValue(row, col), ref sum, ref count, ref numericalCount, ref min, ref max);
            }
        }

        double? average = numericalCount > 0 ? sum / numericalCount : null;
        return new WorkbookSelectionStats(sum, count, numericalCount, average, min, max);
    }

    public static WorkbookSelectionStats Combine(WorkbookSelectionStats left, WorkbookSelectionStats right)
    {
        var sum = left.Sum + right.Sum;
        var count = left.Count + right.Count;
        var numericalCount = left.NumericalCount + right.NumericalCount;
        double? average = numericalCount > 0 ? sum / numericalCount : null;
        var min = Min(left.Min, right.Min);
        var max = Max(left.Max, right.Max);
        return new WorkbookSelectionStats(sum, count, numericalCount, average, min, max);
    }

    private static WorkbookSelectionStats CalculateSingleCell(ScalarValue value) =>
        value switch
        {
            BlankValue => EmptyStats,
            NumberValue number => new WorkbookSelectionStats(
                number.Value,
                Count: 1,
                NumericalCount: 1,
                Average: number.Value,
                Min: number.Value,
                Max: number.Value),
            _ => new WorkbookSelectionStats(0, 1, 0, null, null, null)
        };

    private static double? Min(double? left, double? right) =>
        left.HasValue
            ? right.HasValue ? Math.Min(left.Value, right.Value) : left
            : right;

    private static double? Max(double? left, double? right) =>
        left.HasValue
            ? right.HasValue ? Math.Max(left.Value, right.Value) : left
            : right;

    private static GridRange Intersect(GridRange range, GridRange usedRange) =>
        new(
            new CellAddress(
                range.Start.Sheet,
                Math.Max(range.Start.Row, usedRange.Start.Row),
                Math.Max(range.Start.Col, usedRange.Start.Col)),
            new CellAddress(
                range.Start.Sheet,
                Math.Min(range.End.Row, usedRange.End.Row),
                Math.Min(range.End.Col, usedRange.End.Col)));

    private static bool Contains(GridRange range, uint row, uint col) =>
        row >= range.Start.Row && row <= range.End.Row &&
        col >= range.Start.Col && col <= range.End.Col;

    private static void Accumulate(
        ScalarValue value,
        ref double sum,
        ref int count,
        ref int numericalCount,
        ref double? min,
        ref double? max)
    {
        if (value is not BlankValue)
            count++;

        if (value is NumberValue nv)
        {
            sum += nv.Value;
            numericalCount++;
            min = min is null ? nv.Value : Math.Min(min.Value, nv.Value);
            max = max is null ? nv.Value : Math.Max(max.Value, nv.Value);
        }
    }
}
