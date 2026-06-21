using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed class StatusBarStatsCache
{
    private readonly WorkbookSelectionStatsCache _cache = new();

    public StatusBarCalculator.Stats GetOrCreate(
        Sheet sheet,
        GridRange range,
        ulong revision,
        Func<StatusBarCalculator.Stats> create)
    {
        var sharedStats = _cache.GetOrCreate(
            sheet,
            range,
            revision,
            () => StatusBarCalculator.ToShared(create()));
        return StatusBarCalculator.ToStats(sharedStats);
    }

    public StatusBarCalculator.Stats GetOrCalculate(Sheet sheet, GridRange range, ulong revision) =>
        StatusBarCalculator.ToStats(_cache.GetOrCalculate(sheet, range, revision));

    public void Clear() => _cache.Clear();
}
