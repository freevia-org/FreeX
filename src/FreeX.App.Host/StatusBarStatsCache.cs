using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed class StatusBarStatsCache
{
    private readonly WorkbookSelectionStatsCache _cache = new();

    public WorkbookSelectionStats GetOrCreate(
        Sheet sheet,
        GridRange range,
        ulong revision,
        Func<WorkbookSelectionStats> create) =>
        _cache.GetOrCreate(sheet, range, revision, create);

    public WorkbookSelectionStats GetOrCalculate(Sheet sheet, GridRange range, ulong revision) =>
        _cache.GetOrCalculate(sheet, range, revision);

    public WorkbookSelectionStats GetOrCalculate(Sheet sheet, IReadOnlyList<GridRange> ranges, ulong revision) =>
        _cache.GetOrCalculate(sheet, ranges, revision);

    public void Clear() => _cache.Clear();
}
