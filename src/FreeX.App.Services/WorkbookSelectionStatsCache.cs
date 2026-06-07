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
            TryCalculateContainingExpansion(sheet, previousSource.Range, previousStats, range, out var expandedStats))
        {
            _lastSource = source;
            _lastStats = expandedStats;
            return expandedStats;
        }

        var stats = WorkbookSelectionStatsCalculator.Calculate(sheet, range);
        _lastSource = source;
        _lastStats = stats;
        return stats;
    }

    public void Clear()
    {
        _lastSource = null;
        _lastStats = null;
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

    private static bool TryCalculateContainingExpansion(
        Sheet sheet,
        GridRange previousRange,
        WorkbookSelectionStats previousStats,
        GridRange range,
        out WorkbookSelectionStats stats)
    {
        stats = default;
        if (previousRange.Start.Sheet != range.Start.Sheet ||
            !Contains(range, previousRange))
        {
            return false;
        }

        stats = previousStats;
        if (range.Start.Row < previousRange.Start.Row)
        {
            stats = WorkbookSelectionStatsCalculator.Combine(
                stats,
                WorkbookSelectionStatsCalculator.Calculate(
                    sheet,
                    new GridRange(
                        new CellAddress(range.Start.Sheet, range.Start.Row, range.Start.Col),
                        new CellAddress(range.Start.Sheet, previousRange.Start.Row - 1, range.End.Col))));
        }

        if (range.End.Row > previousRange.End.Row)
        {
            stats = WorkbookSelectionStatsCalculator.Combine(
                stats,
                WorkbookSelectionStatsCalculator.Calculate(
                    sheet,
                    new GridRange(
                        new CellAddress(range.Start.Sheet, previousRange.End.Row + 1, range.Start.Col),
                        new CellAddress(range.Start.Sheet, range.End.Row, range.End.Col))));
        }

        if (range.Start.Col < previousRange.Start.Col)
        {
            stats = WorkbookSelectionStatsCalculator.Combine(
                stats,
                WorkbookSelectionStatsCalculator.Calculate(
                    sheet,
                    new GridRange(
                        new CellAddress(range.Start.Sheet, previousRange.Start.Row, range.Start.Col),
                        new CellAddress(range.Start.Sheet, previousRange.End.Row, previousRange.Start.Col - 1))));
        }

        if (range.End.Col > previousRange.End.Col)
        {
            stats = WorkbookSelectionStatsCalculator.Combine(
                stats,
                WorkbookSelectionStatsCalculator.Calculate(
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
