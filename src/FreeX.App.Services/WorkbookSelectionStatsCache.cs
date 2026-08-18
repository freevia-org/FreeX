using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed class WorkbookSelectionStatsCache
{
    private readonly record struct Source(Sheet Sheet, GridRange Range, ulong Revision);

    private readonly record struct RangeSetSource(Sheet Sheet, GridRange[] Ranges, ulong Revision)
    {
        public bool Matches(Sheet sheet, IReadOnlyList<GridRange> ranges, ulong revision)
        {
            if (Sheet != sheet || Revision != revision || Ranges.Length != ranges.Count)
                return false;

            for (var index = 0; index < Ranges.Length; index++)
            {
                if (Ranges[index] != ranges[index])
                    return false;
            }

            return true;
        }
    }

    private Source? _lastSource;
    private WorkbookSelectionStats? _lastStats;
    // The (row, col) of _lastStats's AggregateErrorCode, when known. TryCalculateContainingExpansion
    // needs this to decide -- by true row-major position, not by which side of a Combine call
    // happens to be "left" -- whether a newly-revealed strip's error or the previously-cached
    // region's error is the one Excel would actually report first for the expanded selection.
    private uint? _lastErrorRow;
    private uint? _lastErrorCol;
    private RangeSetSource? _lastRangeSetSource;
    private WorkbookSelectionStats? _lastRangeSetStats;

    public WorkbookSelectionStats GetOrCreate(
        Sheet sheet,
        GridRange range,
        ulong revision,
        Func<WorkbookSelectionStats> create)
    {
        var source = new Source(sheet, range, revision);
        if (_lastSource == source && _lastStats is { } cached)
            return cached;

        var stats = create();
        _lastSource = source;
        _lastStats = stats;
        // create() doesn't report where its aggregate error cell lives, so a later incremental
        // expansion from this baseline can't safely position-compare against it -- clear any
        // stale position from a previous GetOrCalculate call instead of letting it leak in.
        _lastErrorRow = null;
        _lastErrorCol = null;
        return stats;
    }

    public WorkbookSelectionStats GetOrCalculate(Sheet sheet, IReadOnlyList<GridRange> ranges, ulong revision)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        if (ranges.Count == 1)
            return GetOrCalculate(sheet, ranges[0], revision);

        if (_lastRangeSetSource is { } source &&
            _lastRangeSetStats is { } cached &&
            source.Matches(sheet, ranges, revision))
        {
            return cached;
        }

        var stats = WorkbookSelectionStatsCalculator.Calculate(sheet, ranges);
        _lastRangeSetSource = new RangeSetSource(sheet, CopyRanges(ranges), revision);
        _lastRangeSetStats = stats;
        return stats;
    }

    public WorkbookSelectionStats GetOrCalculate(Sheet sheet, GridRange range, ulong revision)
    {
        var source = new Source(sheet, range, revision);
        if (_lastSource == source && _lastStats is { } cached)
            return cached;

        if (_lastSource is { } previousSource &&
            _lastStats is { } previousStats &&
            previousSource.Sheet == sheet &&
            previousSource.Revision == revision &&
            TryCalculateContainingExpansion(
                sheet,
                previousSource.Range,
                (previousStats, _lastErrorRow, _lastErrorCol),
                range,
                out var expanded))
        {
            _lastSource = source;
            _lastStats = expanded.Stats;
            _lastErrorRow = expanded.ErrorRow;
            _lastErrorCol = expanded.ErrorCol;
            return expanded.Stats;
        }

        var (stats, errorRow, errorCol) = WorkbookSelectionStatsCalculator.CalculateWithErrorPosition(sheet, range);
        _lastSource = source;
        _lastStats = stats;
        _lastErrorRow = errorRow;
        _lastErrorCol = errorCol;
        return stats;
    }

    public void Clear()
    {
        _lastSource = null;
        _lastStats = null;
        _lastErrorRow = null;
        _lastErrorCol = null;
        _lastRangeSetSource = null;
        _lastRangeSetStats = null;
    }

    private static GridRange[] CopyRanges(IReadOnlyList<GridRange> ranges)
    {
        var copy = new GridRange[ranges.Count];
        for (var index = 0; index < ranges.Count; index++)
            copy[index] = ranges[index];

        return copy;
    }

    /// <summary>
    /// Decomposes the difference between <paramref name="previousRange"/> and the newly-selected
    /// <paramref name="range"/> into up to four disjoint rectangular strips (top/bottom/left/right)
    /// and merges each one's freshly-scanned stats into <paramref name="previous"/> via
    /// <see cref="WorkbookSelectionStatsCalculator.CombineWithErrorPosition"/>. That combine picks
    /// the aggregate error by true (row, col) position rather than by which side of the call is
    /// "left", so the result is correct -- and matches a from-scratch
    /// <see cref="WorkbookSelectionStatsCalculator.Calculate(Sheet, GridRange)"/> over the same
    /// final range exactly -- regardless of which edge(s) of the selection were extended, and
    /// regardless of the fixed top/bottom/left/right processing order below.
    /// </summary>
    private static bool TryCalculateContainingExpansion(
        Sheet sheet,
        GridRange previousRange,
        (WorkbookSelectionStats Stats, uint? ErrorRow, uint? ErrorCol) previous,
        GridRange range,
        out (WorkbookSelectionStats Stats, uint? ErrorRow, uint? ErrorCol) result)
    {
        result = default;
        if (previousRange.Start.Sheet != range.Start.Sheet ||
            !Contains(range, previousRange))
        {
            return false;
        }

        result = previous;
        if (range.Start.Row < previousRange.Start.Row)
        {
            result = WorkbookSelectionStatsCalculator.CombineWithErrorPosition(
                result,
                WorkbookSelectionStatsCalculator.CalculateWithErrorPosition(
                    sheet,
                    new GridRange(
                        new CellAddress(range.Start.Sheet, range.Start.Row, range.Start.Col),
                        new CellAddress(range.Start.Sheet, previousRange.Start.Row - 1, range.End.Col))));
        }

        if (range.End.Row > previousRange.End.Row)
        {
            result = WorkbookSelectionStatsCalculator.CombineWithErrorPosition(
                result,
                WorkbookSelectionStatsCalculator.CalculateWithErrorPosition(
                    sheet,
                    new GridRange(
                        new CellAddress(range.Start.Sheet, previousRange.End.Row + 1, range.Start.Col),
                        new CellAddress(range.Start.Sheet, range.End.Row, range.End.Col))));
        }

        if (range.Start.Col < previousRange.Start.Col)
        {
            result = WorkbookSelectionStatsCalculator.CombineWithErrorPosition(
                result,
                WorkbookSelectionStatsCalculator.CalculateWithErrorPosition(
                    sheet,
                    new GridRange(
                        new CellAddress(range.Start.Sheet, previousRange.Start.Row, range.Start.Col),
                        new CellAddress(range.Start.Sheet, previousRange.End.Row, previousRange.Start.Col - 1))));
        }

        if (range.End.Col > previousRange.End.Col)
        {
            result = WorkbookSelectionStatsCalculator.CombineWithErrorPosition(
                result,
                WorkbookSelectionStatsCalculator.CalculateWithErrorPosition(
                    sheet,
                    new GridRange(
                        new CellAddress(range.Start.Sheet, previousRange.Start.Row, previousRange.End.Col + 1),
                        new CellAddress(range.Start.Sheet, previousRange.End.Row, range.End.Col))));
        }

        return true;
    }

    private static bool Contains(GridRange outer, GridRange inner) =>
        outer.Start.Row <= inner.Start.Row &&
        outer.Start.Col <= inner.Start.Col &&
        outer.End.Row >= inner.End.Row &&
        outer.End.Col >= inner.End.Col;
}
