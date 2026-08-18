using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class WorkbookSelectionStatsCalculator
{
    private static readonly WorkbookSelectionStats EmptyStats = new(0, 0, 0, null, null, null);
    private const int ColumnKeyBits = 15;

    public static WorkbookSelectionStats Calculate(Sheet sheet, GridRange range) =>
        CalculateWithErrorPosition(sheet, range).Stats;

    /// <summary>
    /// Same as <see cref="Calculate(Sheet, GridRange)"/>, but also reports the (row, col) of the
    /// aggregate error cell (the first error encountered in row-major scan order), so a caller
    /// that later needs to merge this result with another region's stats (see
    /// <see cref="CombineWithErrorPosition"/>) can pick the truly-first error across both regions
    /// instead of assuming one region unconditionally precedes the other.
    /// </summary>
    internal static (WorkbookSelectionStats Stats, uint? ErrorRow, uint? ErrorCol) CalculateWithErrorPosition(
        Sheet sheet, GridRange range)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        if (range.Start == range.End)
        {
            if (!IsVisibleCell(sheet, range.Start.Row))
                return (EmptyStats, null, null);

            var singleStats = CalculateSingleCell(sheet.GetValue(range.Start.Row, range.Start.Col));
            return singleStats.AggregateErrorCode is null
                ? (singleStats, null, null)
                : (singleStats, range.Start.Row, range.Start.Col);
        }

        if (sheet.GetUsedRange() is not { } usedRange || !usedRange.Overlaps(range))
            return (EmptyStats, null, null);

        double sum = 0;
        int count = 0;
        int numericalCount = 0;
        double? min = null, max = null;
        string? aggregateError = null;
        uint? errorRow = null, errorCol = null;

        var scanRange = Intersect(range, usedRange);
        long totalCells = scanRange.CellCount;

        if (sheet.CellCount + sheet.SpillValueCount < totalCells)
        {
            // EnumerateValueBearingCells unions the primary cell dictionary with the dynamic-array
            // spill overlay, so spilled cells (everything but a spill's anchor) are still visible
            // to this sparse scan -- GetOccupiedCellMap() alone would silently drop them.
            foreach (var address in sheet.EnumerateValueBearingCells())
            {
                var row = address.Row;
                var col = address.Col;
                if (Contains(scanRange, row, col) &&
                    IsVisibleCell(sheet, row))
                {
                    Accumulate(sheet.GetValue(row, col), row, col, ref sum, ref count, ref numericalCount, ref min, ref max, ref aggregateError, ref errorRow, ref errorCol);
                }
            }
        }
        else
        {
            for (var row = scanRange.Start.Row; row <= scanRange.End.Row; row++)
            {
                if (sheet.IsRowFilterHidden(row))
                    continue;

                for (var col = scanRange.Start.Col; col <= scanRange.End.Col; col++)
                {
                    Accumulate(sheet.GetValue(row, col), row, col, ref sum, ref count, ref numericalCount, ref min, ref max, ref aggregateError, ref errorRow, ref errorCol);
                }
            }
        }

        return (CreateStats(sum, count, numericalCount, min, max, aggregateError), errorRow, errorCol);
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
        string? aggregateError = null;
        // Position isn't consumed by this overload (multi-selection stats are never fed into
        // WorkbookSelectionStatsCache's incremental single-range expansion), but Accumulate's
        // signature always tracks it so the two scan paths stay in lockstep with the single-range
        // overload above.
        uint? errorRow = null, errorCol = null;

        if (sheet.CellCount + sheet.SpillValueCount < totalCells)
        {
            // See the single-range overload above: spilled cells only live in the spill overlay,
            // so this must union it in rather than scanning GetOccupiedCellMap() alone.
            foreach (var address in sheet.EnumerateValueBearingCells())
            {
                var row = address.Row;
                var col = address.Col;
                if (ContainsAny(scanRanges, row, col) &&
                    IsVisibleCell(sheet, row))
                {
                    Accumulate(sheet.GetValue(row, col), row, col, ref sum, ref count, ref numericalCount, ref min, ref max, ref aggregateError, ref errorRow, ref errorCol);
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
                    if (sheet.IsRowFilterHidden(row))
                        continue;

                    for (var col = range.Start.Col; col <= range.End.Col; col++)
                    {
                        if (visited.Add(CreateAddressKey(row, col)))
                            Accumulate(sheet.GetValue(row, col), row, col, ref sum, ref count, ref numericalCount, ref min, ref max, ref aggregateError, ref errorRow, ref errorCol);
                    }
                }
            }
        }

        return CreateStats(sum, count, numericalCount, min, max, aggregateError);
    }

    public static WorkbookSelectionStats Combine(WorkbookSelectionStats left, WorkbookSelectionStats right)
    {
        var sum = left.Sum + right.Sum;
        var count = left.Count + right.Count;
        var numericalCount = left.NumericalCount + right.NumericalCount;
        var min = Min(left.Min, right.Min);
        var max = Max(left.Max, right.Max);
        var aggregateError = left.AggregateErrorCode ?? right.AggregateErrorCode;
        return CreateStats(sum, count, numericalCount, min, max, aggregateError);
    }

    /// <summary>
    /// Combines two regions' stats the same way <see cref="Combine"/> does, except the aggregate
    /// error is chosen by comparing each side's (row, col) position -- whichever side's error cell
    /// comes first in row-major (top-to-bottom, then left-to-right) scan order wins -- instead of
    /// unconditionally preferring <paramref name="left"/>. This is what
    /// <see cref="WorkbookSelectionStatsCache"/> needs when it incrementally merges a
    /// newly-revealed strip with previously-cached stats: the strip can precede, follow, or
    /// interleave (row-band-wise) with the cached region depending on which edge of the selection
    /// was extended, so "left wins" is only correct by coincidence, not by construction.
    /// </summary>
    internal static (WorkbookSelectionStats Stats, uint? ErrorRow, uint? ErrorCol) CombineWithErrorPosition(
        (WorkbookSelectionStats Stats, uint? ErrorRow, uint? ErrorCol) left,
        (WorkbookSelectionStats Stats, uint? ErrorRow, uint? ErrorCol) right)
    {
        var sum = left.Stats.Sum + right.Stats.Sum;
        var count = left.Stats.Count + right.Stats.Count;
        var numericalCount = left.Stats.NumericalCount + right.Stats.NumericalCount;
        var min = Min(left.Stats.Min, right.Stats.Min);
        var max = Max(left.Stats.Max, right.Stats.Max);

        string? aggregateError;
        uint? errorRow;
        uint? errorCol;
        if (left.Stats.AggregateErrorCode is null)
        {
            aggregateError = right.Stats.AggregateErrorCode;
            errorRow = right.ErrorRow;
            errorCol = right.ErrorCol;
        }
        else if (right.Stats.AggregateErrorCode is null)
        {
            aggregateError = left.Stats.AggregateErrorCode;
            errorRow = left.ErrorRow;
            errorCol = left.ErrorCol;
        }
        else if (IsEarlierOrEqualPosition(left.ErrorRow, left.ErrorCol, right.ErrorRow, right.ErrorCol))
        {
            aggregateError = left.Stats.AggregateErrorCode;
            errorRow = left.ErrorRow;
            errorCol = left.ErrorCol;
        }
        else
        {
            aggregateError = right.Stats.AggregateErrorCode;
            errorRow = right.ErrorRow;
            errorCol = right.ErrorCol;
        }

        return (CreateStats(sum, count, numericalCount, min, max, aggregateError), errorRow, errorCol);
    }

    // Row-major comparison: the smaller row wins; on the same row, the smaller column wins.
    // A missing position on either side (should not happen once that side's AggregateErrorCode is
    // non-null, but guarded defensively) keeps the left side, matching Combine's plain left-wins
    // fallback so callers that never track positions see identical behavior.
    private static bool IsEarlierOrEqualPosition(uint? leftRow, uint? leftCol, uint? rightRow, uint? rightCol)
    {
        if (leftRow is not { } lr || rightRow is not { } rr)
            return true;
        if (lr != rr)
            return lr < rr;
        return (leftCol ?? uint.MaxValue) <= (rightCol ?? uint.MaxValue);
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
            // A lone selected error cell still reports as non-empty (Count=1), and Excel's
            // status-bar Sum/Average/Min/Max propagate the error rather than showing nothing --
            // mirrored here via AggregateErrorCode so the formatter/model builder can render it.
            ErrorValue errorValue => new WorkbookSelectionStats(0, 1, 0, null, null, null, errorValue.Code),
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

    private static bool IsVisibleCell(Sheet sheet, uint row) =>
        !sheet.IsRowFilterHidden(row);

    private static long AddCellCount(long totalCells, long cellCount) =>
        totalCells > long.MaxValue - cellCount ? long.MaxValue : totalCells + cellCount;

    private static ulong CreateAddressKey(uint row, uint col) =>
        ((ulong)row << ColumnKeyBits) | col;

    private static WorkbookSelectionStats CreateStats(
        double sum,
        int count,
        int numericalCount,
        double? min,
        double? max,
        string? aggregateError)
    {
        double? average = numericalCount > 0 ? sum / numericalCount : null;
        return new WorkbookSelectionStats(sum, count, numericalCount, average, min, max, aggregateError);
    }

    private static void Accumulate(
        ScalarValue value,
        uint row,
        uint col,
        ref double sum,
        ref int count,
        ref int numericalCount,
        ref double? min,
        ref double? max,
        ref string? aggregateError,
        ref uint? errorRow,
        ref uint? errorCol)
    {
        if (value is not BlankValue)
            count++;

        // Real Excel's status-bar Sum/Average/Min/Max propagate an error cell within the
        // selection instead of silently skipping it (matching SUM/AVERAGE/MIN/MAX's own
        // error-propagation over a plain range reference). Numerical Count still only counts
        // genuinely numeric cells below, so it is unaffected. Keep the first error encountered
        // (and its position) as the representative one, mirroring Excel's left-to-right/
        // top-to-bottom scan order. The position lets a caller that later merges this result with
        // another region's stats (WorkbookSelectionStatsCache's incremental expansion) pick the
        // truly-first error across both regions instead of assuming scan order from call order.
        if (value is ErrorValue errorValue && aggregateError is null)
        {
            aggregateError = errorValue.Code;
            errorRow = row;
            errorCol = col;
        }

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
