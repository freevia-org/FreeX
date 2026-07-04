using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class WorkbookSelectionStatsCalculator
{
    private static readonly WorkbookSelectionStats EmptyStats = new(0, 0, 0, null, null, null);
    private const int ColumnKeyBits = 15;

    public static WorkbookSelectionStats Calculate(Sheet sheet, GridRange range)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        if (range.Start == range.End)
        {
            if (!IsVisibleCell(sheet, range.Start.Row, range.Start.Col))
                return EmptyStats;

            return CalculateSingleCell(sheet.GetValue(range.Start.Row, range.Start.Col));
        }

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
                if (Contains(scanRange, row, col) &&
                    IsVisibleCell(sheet, row, col))
                {
                    Accumulate(entry.Value.Value, ref sum, ref count, ref numericalCount, ref min, ref max);
                }
            }
        }
        else
        {
            for (var row = scanRange.Start.Row; row <= scanRange.End.Row; row++)
            {
                if (sheet.IsRowEffectivelyHidden(row))
                    continue;

                for (var col = scanRange.Start.Col; col <= scanRange.End.Col; col++)
                {
                    if (sheet.IsColEffectivelyHidden(col))
                        continue;

                    Accumulate(sheet.GetValue(row, col), ref sum, ref count, ref numericalCount, ref min, ref max);
                }
            }
        }

        return CreateStats(sum, count, numericalCount, min, max);
    }

    public static WorkbookSelectionStats Calculate(Sheet sheet, IReadOnlyList<GridRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(ranges);

        if (ranges.Count == 0)
            return EmptyStats;
        if (ranges.Count == 1)
            return Calculate(sheet, ranges[0]);
        if (sheet.GetUsedRange() is not { } usedRange)
            return EmptyStats;

        var scanRanges = new List<GridRange>(ranges.Count);
        long totalCells = 0;
        for (var index = 0; index < ranges.Count; index++)
        {
            var range = ranges[index];
            if (!usedRange.Overlaps(range))
                continue;

            var scanRange = Intersect(range, usedRange);
            scanRanges.Add(scanRange);
            totalCells = AddCellCount(totalCells, scanRange.CellCount);
        }

        if (scanRanges.Count == 0)
            return EmptyStats;

        double sum = 0;
        int count = 0;
        int numericalCount = 0;
        double? min = null, max = null;

        if (sheet.CellCount < totalCells)
        {
            foreach (var entry in sheet.GetOccupiedCellMap())
            {
                var (row, col) = entry.Key;
                if (ContainsAny(scanRanges, row, col) &&
                    IsVisibleCell(sheet, row, col))
                {
                    Accumulate(entry.Value.Value, ref sum, ref count, ref numericalCount, ref min, ref max);
                }
            }
        }
        else
        {
            var visited = new HashSet<ulong>();
            for (var rangeIndex = 0; rangeIndex < scanRanges.Count; rangeIndex++)
            {
                var range = scanRanges[rangeIndex];
                for (var row = range.Start.Row; row <= range.End.Row; row++)
                {
                    if (sheet.IsRowEffectivelyHidden(row))
                        continue;

                    for (var col = range.Start.Col; col <= range.End.Col; col++)
                    {
                        if (sheet.IsColEffectivelyHidden(col))
                            continue;

                        if (visited.Add(CreateAddressKey(row, col)))
                            Accumulate(sheet.GetValue(row, col), ref sum, ref count, ref numericalCount, ref min, ref max);
                    }
                }
            }
        }

        return CreateStats(sum, count, numericalCount, min, max);
    }

    public static WorkbookSelectionStats Combine(WorkbookSelectionStats left, WorkbookSelectionStats right)
    {
        var sum = left.Sum + right.Sum;
        var count = left.Count + right.Count;
        var numericalCount = left.NumericalCount + right.NumericalCount;
        var min = Min(left.Min, right.Min);
        var max = Max(left.Max, right.Max);
        return CreateStats(sum, count, numericalCount, min, max);
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
            DateTimeValue dateTime => new WorkbookSelectionStats(
                dateTime.Value,
                Count: 1,
                NumericalCount: 1,
                Average: dateTime.Value,
                Min: dateTime.Value,
                Max: dateTime.Value),
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

    private static bool ContainsAny(IReadOnlyList<GridRange> ranges, uint row, uint col)
    {
        for (var index = 0; index < ranges.Count; index++)
        {
            if (Contains(ranges[index], row, col))
                return true;
        }

        return false;
    }

    private static bool IsVisibleCell(Sheet sheet, uint row, uint col) =>
        !sheet.IsRowEffectivelyHidden(row) &&
        !sheet.IsColEffectivelyHidden(col);

    private static long AddCellCount(long totalCells, long cellCount) =>
        totalCells > long.MaxValue - cellCount ? long.MaxValue : totalCells + cellCount;

    private static ulong CreateAddressKey(uint row, uint col) =>
        ((ulong)row << ColumnKeyBits) | col;

    private static WorkbookSelectionStats CreateStats(
        double sum,
        int count,
        int numericalCount,
        double? min,
        double? max)
    {
        double? average = numericalCount > 0 ? sum / numericalCount : null;
        return new WorkbookSelectionStats(sum, count, numericalCount, average, min, max);
    }

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

        double? numericValue = value switch
        {
            NumberValue nv => nv.Value,
            DateTimeValue dt => dt.Value,
            _ => null
        };

        if (numericValue is { } number)
        {
            sum += number;
            numericalCount++;
            min = min is null ? number : Math.Min(min.Value, number);
            max = max is null ? number : Math.Max(max.Value, number);
        }
    }
}
