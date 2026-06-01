using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed class StatusBarStatsCache
{
    private readonly record struct Source(Sheet Sheet, GridRange Range, ulong Revision);

    private Source? _lastSource;
    private StatusBarCalculator.Stats? _lastStats;

    public StatusBarCalculator.Stats GetOrCreate(
        Sheet sheet,
        GridRange range,
        ulong revision,
        Func<StatusBarCalculator.Stats> create)
    {
        var source = new Source(sheet, range, revision);
        if (_lastSource == source && _lastStats is { } cached)
            return cached;

        var stats = create();
        _lastSource = source;
        _lastStats = stats;
        return stats;
    }

    public StatusBarCalculator.Stats GetOrCalculate(Sheet sheet, GridRange range, ulong revision)
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

        var stats = StatusBarCalculator.Calculate(sheet, range);
        _lastSource = source;
        _lastStats = stats;
        return stats;
    }

    public void Clear()
    {
        _lastSource = null;
        _lastStats = null;
    }

    private static bool TryCalculateContainingExpansion(
        Sheet sheet,
        GridRange previousRange,
        StatusBarCalculator.Stats previousStats,
        GridRange range,
        out StatusBarCalculator.Stats stats)
    {
        stats = default;
        if (previousRange.Start.Sheet != range.Start.Sheet ||
            !Contains(range, previousRange))
            return false;

        stats = previousStats;
        if (range.Start.Row < previousRange.Start.Row)
        {
            stats = StatusBarCalculator.Combine(
                stats,
                StatusBarCalculator.Calculate(
                    sheet,
                    new GridRange(
                        new CellAddress(range.Start.Sheet, range.Start.Row, range.Start.Col),
                        new CellAddress(range.Start.Sheet, previousRange.Start.Row - 1, range.End.Col))));
        }

        if (range.End.Row > previousRange.End.Row)
        {
            stats = StatusBarCalculator.Combine(
                stats,
                StatusBarCalculator.Calculate(
                    sheet,
                    new GridRange(
                        new CellAddress(range.Start.Sheet, previousRange.End.Row + 1, range.Start.Col),
                        new CellAddress(range.Start.Sheet, range.End.Row, range.End.Col))));
        }

        if (range.Start.Col < previousRange.Start.Col)
        {
            stats = StatusBarCalculator.Combine(
                stats,
                StatusBarCalculator.Calculate(
                    sheet,
                    new GridRange(
                        new CellAddress(range.Start.Sheet, previousRange.Start.Row, range.Start.Col),
                        new CellAddress(range.Start.Sheet, previousRange.End.Row, previousRange.Start.Col - 1))));
        }

        if (range.End.Col > previousRange.End.Col)
        {
            stats = StatusBarCalculator.Combine(
                stats,
                StatusBarCalculator.Calculate(
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
