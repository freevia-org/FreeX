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
    private static readonly ConditionalWeakTable<Sheet, NativeVisualFilterCache> NativeVisualFilterCaches = new();
    private static readonly NativeVisualFilters EmptyNativeVisualFilters = new(
        Array.Empty<SlicerModel>(),
        Array.Empty<TimelineModel>());

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
            return EmptyNativeVisualFilters;

        var activePivotNames = BuildActivePivotNameSet(activeSheet);
        var cache = NativeVisualFilterCaches.GetValue(activeSheet, static _ => new NativeVisualFilterCache());
        return cache.GetOrCreate(workbook, activePivotNames);
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

    private sealed class NativeVisualFilterCache
    {
        private readonly object _sync = new();
        private Workbook? _workbook;
        private IReadOnlySet<string>? _activePivotNames;
        private SlicerSnapshot[] _slicerSnapshots = [];
        private TimelineSnapshot[] _timelineSnapshots = [];
        private NativeVisualFilters _filters = EmptyNativeVisualFilters;

        public NativeVisualFilters GetOrCreate(Workbook workbook, IReadOnlySet<string> activePivotNames)
        {
            lock (_sync)
            {
                if (ReferenceEquals(_workbook, workbook) &&
                    ReferenceEquals(_activePivotNames, activePivotNames) &&
                    SlicersMatch(workbook.Slicers) &&
                    TimelinesMatch(workbook.Timelines))
                {
                    return _filters;
                }

                var slicers = workbook.Slicers.Count == 0
                    ? Array.Empty<SlicerModel>()
                    : GetNativeVisualSlicers(workbook.Slicers, activePivotNames);
                var timelines = workbook.Timelines.Count == 0
                    ? Array.Empty<TimelineModel>()
                    : GetNativeVisualTimelines(workbook.Timelines, activePivotNames);

                _workbook = workbook;
                _activePivotNames = activePivotNames;
                _slicerSnapshots = CaptureSlicers(workbook.Slicers);
                _timelineSnapshots = CaptureTimelines(workbook.Timelines);
                _filters = slicers.Count == 0 && timelines.Count == 0
                    ? EmptyNativeVisualFilters
                    : new NativeVisualFilters(slicers, timelines);
                return _filters;
            }
        }

        private bool SlicersMatch(IReadOnlyList<SlicerModel> slicers)
        {
            if (_slicerSnapshots.Length != slicers.Count)
                return false;

            for (var index = 0; index < _slicerSnapshots.Length; index++)
            {
                var snapshot = _slicerSnapshots[index];
                var slicer = slicers[index];
                if (!ReferenceEquals(snapshot.Model, slicer) ||
                    !string.Equals(snapshot.SourcePivotTableName, slicer.SourcePivotTableName, StringComparison.Ordinal) ||
                    snapshot.HasDrawingAnchor != (slicer.DrawingAnchor is not null))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TimelinesMatch(IReadOnlyList<TimelineModel> timelines)
        {
            if (_timelineSnapshots.Length != timelines.Count)
                return false;

            for (var index = 0; index < _timelineSnapshots.Length; index++)
            {
                var snapshot = _timelineSnapshots[index];
                var timeline = timelines[index];
                if (!ReferenceEquals(snapshot.Model, timeline) ||
                    !string.Equals(snapshot.SourcePivotTableName, timeline.SourcePivotTableName, StringComparison.Ordinal) ||
                    snapshot.HasDrawingAnchor != (timeline.DrawingAnchor is not null))
                {
                    return false;
                }
            }

            return true;
        }

        private static SlicerSnapshot[] CaptureSlicers(IReadOnlyList<SlicerModel> slicers)
        {
            if (slicers.Count == 0)
                return [];

            var snapshots = new SlicerSnapshot[slicers.Count];
            for (var index = 0; index < slicers.Count; index++)
            {
                var slicer = slicers[index];
                snapshots[index] = new SlicerSnapshot(
                    slicer,
                    slicer.SourcePivotTableName,
                    slicer.DrawingAnchor is not null);
            }

            return snapshots;
        }

        private static TimelineSnapshot[] CaptureTimelines(IReadOnlyList<TimelineModel> timelines)
        {
            if (timelines.Count == 0)
                return [];

            var snapshots = new TimelineSnapshot[timelines.Count];
            for (var index = 0; index < timelines.Count; index++)
            {
                var timeline = timelines[index];
                snapshots[index] = new TimelineSnapshot(
                    timeline,
                    timeline.SourcePivotTableName,
                    timeline.DrawingAnchor is not null);
            }

            return snapshots;
        }

        private readonly record struct SlicerSnapshot(
            SlicerModel Model,
            string? SourcePivotTableName,
            bool HasDrawingAnchor);

        private readonly record struct TimelineSnapshot(
            TimelineModel Model,
            string? SourcePivotTableName,
            bool HasDrawingAnchor);
    }
}
