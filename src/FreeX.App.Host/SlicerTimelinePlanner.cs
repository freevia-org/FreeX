using System.Runtime.CompilerServices;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record SlicerPaneItem(string Name, string FieldName, IReadOnlyList<SlicerTileItem> Tiles);

public sealed record SlicerTileItem(string SlicerName, string Caption, bool IsSelected);

public sealed class TimelinePaneItem
{
    public string Name { get; init; } = "";
    public string FieldName { get; init; } = "";
    public string SelectedStartDate { get; set; } = "";
    public string SelectedEndDate { get; set; } = "";
}

public sealed record NativeVisualFilters(
    IReadOnlyList<SlicerModel> Slicers,
    IReadOnlyList<TimelineModel> Timelines);

public static class SlicerTimelinePlanner
{
    private static readonly ConditionalWeakTable<Sheet, ActivePivotNameSetCache> ActivePivotNameSets = new();

    public static IReadOnlyList<SlicerTileItem> BuildSlicerTiles(SlicerModel slicer, IEnumerable<string> sourceItems)
    {
        var selected = slicer.SelectedItems.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var items = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var item in sourceItems)
            items.Add(item);

        if (items.Count == 0)
        {
            foreach (var item in slicer.SelectedItems)
                items.Add(item);
        }

        var tiles = new List<SlicerTileItem>(items.Count);
        foreach (var item in items)
            tiles.Add(new SlicerTileItem(slicer.Name, item, selected.Count == 0 || selected.Contains(item)));

        return tiles;
    }

    public static IReadOnlyList<string> ToggleSlicerSelection(
        IReadOnlyCollection<string> allItems,
        IReadOnlyCollection<string> selectedItems,
        string caption)
    {
        var selected = selectedItems.Count == 0
            ? allItems.ToHashSet(StringComparer.CurrentCultureIgnoreCase)
            : selectedItems.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!selected.Remove(caption))
            selected.Add(caption);
        if (selected.Count == allItems.Count)
            selected.Clear();

        return selected.ToList();
    }

    public static TimelinePaneItem BuildTimelineItem(TimelineModel timeline) =>
        new()
        {
            Name = timeline.Name,
            FieldName = timeline.SourceFieldName ?? timeline.CacheName,
            SelectedStartDate = timeline.SelectedStartDate ?? timeline.StartDate ?? "",
            SelectedEndDate = timeline.SelectedEndDate ?? timeline.EndDate ?? ""
        };

    public static string? NormalizeTimelineDateInput(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static IReadOnlyList<SlicerModel> GetNativeVisualSlicers(Workbook workbook, Sheet activeSheet)
    {
        if (workbook.Slicers.Count == 0 || activeSheet.PivotTables.Count == 0)
            return Array.Empty<SlicerModel>();

        return GetNativeVisualSlicers(workbook.Slicers, BuildActivePivotNameSet(activeSheet));
    }

    public static IReadOnlyList<TimelineModel> GetNativeVisualTimelines(Workbook workbook, Sheet activeSheet)
    {
        if (workbook.Timelines.Count == 0 || activeSheet.PivotTables.Count == 0)
            return Array.Empty<TimelineModel>();

        return GetNativeVisualTimelines(workbook.Timelines, BuildActivePivotNameSet(activeSheet));
    }

    public static NativeVisualFilters GetNativeVisualFilters(Workbook workbook, Sheet activeSheet)
    {
        if (activeSheet.PivotTables.Count == 0)
        {
            return new NativeVisualFilters(
                Array.Empty<SlicerModel>(),
                Array.Empty<TimelineModel>());
        }

        var activePivotNames = BuildActivePivotNameSet(activeSheet);
        var slicers = workbook.Slicers.Count == 0
            ? Array.Empty<SlicerModel>()
            : GetNativeVisualSlicers(workbook.Slicers, activePivotNames);
        var timelines = workbook.Timelines.Count == 0
            ? Array.Empty<TimelineModel>()
            : GetNativeVisualTimelines(workbook.Timelines, activePivotNames);

        return new NativeVisualFilters(slicers, timelines);
    }

    private static IReadOnlyList<SlicerModel> GetNativeVisualSlicers(
        IReadOnlyList<SlicerModel> slicers,
        IReadOnlySet<string> activePivotNames)
    {
        List<SlicerModel>? visible = null;
        foreach (var slicer in slicers)
        {
            if (slicer.DrawingAnchor is not null && IsConnectedToPivotOnSheet(slicer.SourcePivotTableName, activePivotNames))
            {
                visible ??= new List<SlicerModel>(slicers.Count);
                visible.Add(slicer);
            }
        }

        return visible is null ? Array.Empty<SlicerModel>() : visible;
    }

    private static IReadOnlyList<TimelineModel> GetNativeVisualTimelines(
        IReadOnlyList<TimelineModel> timelines,
        IReadOnlySet<string> activePivotNames)
    {
        List<TimelineModel>? visible = null;
        foreach (var timeline in timelines)
        {
            if (timeline.DrawingAnchor is not null && IsConnectedToPivotOnSheet(timeline.SourcePivotTableName, activePivotNames))
            {
                visible ??= new List<TimelineModel>(timelines.Count);
                visible.Add(timeline);
            }
        }

        return visible is null ? Array.Empty<TimelineModel>() : visible;
    }

    private static IReadOnlySet<string> BuildActivePivotNameSet(Sheet activeSheet)
    {
        var cache = ActivePivotNameSets.GetValue(activeSheet, static _ => new ActivePivotNameSetCache());
        return cache.GetOrCreate(activeSheet.PivotTables);
    }

    private static bool IsConnectedToPivotOnSheet(string? pivotTableName, IReadOnlySet<string> activePivotNames) =>
        !string.IsNullOrWhiteSpace(pivotTableName) && activePivotNames.Contains(pivotTableName);

    private sealed class ActivePivotNameSetCache
    {
        private readonly object _sync = new();
        private string?[] _names = [];
        private HashSet<string> _nameSet = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlySet<string> GetOrCreate(IReadOnlyList<PivotTableModel> pivotTables)
        {
            lock (_sync)
            {
                if (Matches(pivotTables))
                    return _nameSet;

                var names = new string?[pivotTables.Count];
                var nameSet = new HashSet<string>(pivotTables.Count, StringComparer.OrdinalIgnoreCase);
                for (var index = 0; index < pivotTables.Count; index++)
                {
                    var name = pivotTables[index].Name;
                    names[index] = name;
                    if (!string.IsNullOrWhiteSpace(name))
                        nameSet.Add(name);
                }

                _names = names;
                _nameSet = nameSet;
                return _nameSet;
            }
        }

        private bool Matches(IReadOnlyList<PivotTableModel> pivotTables)
        {
            if (_names.Length != pivotTables.Count)
                return false;

            for (var index = 0; index < _names.Length; index++)
            {
                if (!string.Equals(_names[index], pivotTables[index].Name, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }
    }
}
