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
}
