using System.Runtime.CompilerServices;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.SlicerTimeline;

public sealed record SlicerPaneItem(
    string Name,
    string FieldName,
    IReadOnlyList<SlicerTileItem> Tiles,
    bool HasActiveFilter);

public sealed record SlicerTileItem(string SlicerName, string Caption, bool IsSelected);

public sealed class TimelinePaneItem
{
    public string Name { get; init; } = "";
    public string FieldName { get; init; } = "";
    public string SelectedStartDate { get; set; } = "";
    public string SelectedEndDate { get; set; } = "";
    public bool HasActiveFilter { get; init; }
}

public sealed record NativeVisualFilters(
    IReadOnlyList<SlicerModel> Slicers,
    IReadOnlyList<TimelineModel> Timelines);

public static class SlicerTimelinePanePlanner
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

    /// <summary>
    /// Plain-click semantics for the slicer side pane (R88-app-slicer-timeline-interaction-5-2):
    /// replaces the whole selection with just <paramref name="caption"/>, unless it is already the
    /// lone selected item -- in which case Excel treats the second plain click as clearing the filter
    /// back to "everything selected". Mirrors <c>SlicerLayoutBuilder.Toggle</c>'s <c>additive: false</c>
    /// branch (the native on-grid overlay's plain-click behaviour for the same slicer model), so both
    /// surfaces agree instead of the pane being toggle-only.
    /// </summary>
    public static IReadOnlyList<string> ReplaceSlicerSelection(
        IReadOnlyCollection<string> selectedItems,
        string caption)
    {
        var isSoleSelection = selectedItems.Count == 1 &&
            selectedItems.Contains(caption, StringComparer.CurrentCultureIgnoreCase);
        return isSoleSelection ? [] : [caption];
    }

    /// <summary>
    /// Shift-click semantics for the slicer side pane: extends the selection to the contiguous range
    /// (in <paramref name="allItems"/> display order) between the earliest currently-selected item and
    /// <paramref name="caption"/>, replacing the whole selection with that range -- Excel's shift-click
    /// range-select behaviour. Falls back to selecting just <paramref name="caption"/> when nothing is
    /// selected yet or either endpoint cannot be located in <paramref name="allItems"/>.
    /// </summary>
    public static IReadOnlyList<string> ExtendSlicerSelection(
        IReadOnlyList<string> allItems,
        IReadOnlyCollection<string> selectedItems,
        string caption)
    {
        if (selectedItems.Count == 0)
            return [caption];

        var selectedSet = selectedItems.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var anchorIndex = -1;
        var targetIndex = -1;
        for (var index = 0; index < allItems.Count; index++)
        {
            if (anchorIndex < 0 && selectedSet.Contains(allItems[index]))
                anchorIndex = index;
            if (string.Equals(allItems[index], caption, StringComparison.CurrentCultureIgnoreCase))
                targetIndex = index;
        }

        if (anchorIndex < 0 || targetIndex < 0)
            return [caption];

        var start = Math.Min(anchorIndex, targetIndex);
        var end = Math.Max(anchorIndex, targetIndex);
        return allItems.Skip(start).Take(end - start + 1).ToList();
    }

    public static bool HasActiveSlicerFilter(SlicerModel slicer) =>
        slicer.SelectedItems.Count > 0;

    public static bool HasActiveTimelineFilter(TimelineModel timeline) =>
        !string.IsNullOrWhiteSpace(timeline.SelectedStartDate) ||
        !string.IsNullOrWhiteSpace(timeline.SelectedEndDate);

    public static TimelinePaneItem BuildTimelineItem(TimelineModel timeline) =>
        new()
        {
            Name = timeline.Name,
            FieldName = timeline.SourceFieldName ?? timeline.CacheName,
            SelectedStartDate = timeline.SelectedStartDate ?? timeline.StartDate ?? "",
            SelectedEndDate = timeline.SelectedEndDate ?? timeline.EndDate ?? "",
            HasActiveFilter = HasActiveTimelineFilter(timeline)
        };

    public static string? NormalizeTimelineDateInput(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static IReadOnlyList<SlicerModel> GetNativeVisualSlicers(Workbook workbook, Sheet activeSheet)
    {
        if (workbook.Slicers.Count == 0)
            return Array.Empty<SlicerModel>();

        return GetNativeVisualSlicers(workbook.Slicers, BuildActivePivotNameSet(activeSheet), activeSheet.Name);
    }

    public static IReadOnlyList<TimelineModel> GetNativeVisualTimelines(Workbook workbook, Sheet activeSheet)
    {
        if (workbook.Timelines.Count == 0)
            return Array.Empty<TimelineModel>();

        return GetNativeVisualTimelines(workbook.Timelines, BuildActivePivotNameSet(activeSheet), activeSheet.Name);
    }

    public static NativeVisualFilters GetNativeVisualFilters(Workbook workbook, Sheet activeSheet)
    {
        if (workbook.Slicers.Count == 0 && workbook.Timelines.Count == 0)
            return EmptyNativeVisualFilters;

        var activePivotNames = BuildActivePivotNameSet(activeSheet);
        var cache = NativeVisualFilterCaches.GetValue(activeSheet, static _ => new NativeVisualFilterCache());
        return cache.GetOrCreate(workbook, activePivotNames, activeSheet.Name);
    }

    private static IReadOnlyList<SlicerModel> GetNativeVisualSlicers(
        IReadOnlyList<SlicerModel> slicers,
        IReadOnlySet<string> activePivotNames,
        string activeSheetName)
    {
        List<SlicerModel>? visible = null;
        foreach (var slicer in slicers)
        {
            // A slicer renders on the active sheet when it has a drawing anchor AND either it is connected
            // to a pivot on this sheet (pivot slicers) OR its drawing is hosted on this sheet (table
            // slicers, which carry no SourcePivotTableName).
            if (slicer.DrawingAnchor is not null &&
                (IsConnectedToPivotOnSheet(slicer.SourcePivotTableName, activePivotNames) ||
                 IsAnchoredOnSheet(slicer.SourceSheetName, activeSheetName)))
            {
                visible ??= new List<SlicerModel>(slicers.Count);
                visible.Add(slicer);
            }
        }

        return visible is null ? Array.Empty<SlicerModel>() : visible;
    }

    private static IReadOnlyList<TimelineModel> GetNativeVisualTimelines(
        IReadOnlyList<TimelineModel> timelines,
        IReadOnlySet<string> activePivotNames,
        string activeSheetName)
    {
        List<TimelineModel>? visible = null;
        foreach (var timeline in timelines)
        {
            if (timeline.DrawingAnchor is not null &&
                (IsConnectedToPivotOnSheet(timeline.SourcePivotTableName, activePivotNames) ||
                 IsAnchoredOnSheet(timeline.SourceSheetName, activeSheetName)))
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

    private static bool IsAnchoredOnSheet(string? sourceSheetName, string activeSheetName) =>
        !string.IsNullOrWhiteSpace(sourceSheetName) &&
        string.Equals(sourceSheetName, activeSheetName, StringComparison.Ordinal);

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
        private string? _activeSheetName;
        private SlicerSnapshot[] _slicerSnapshots = [];
        private TimelineSnapshot[] _timelineSnapshots = [];
        private NativeVisualFilters _filters = EmptyNativeVisualFilters;

        public NativeVisualFilters GetOrCreate(Workbook workbook, IReadOnlySet<string> activePivotNames, string activeSheetName)
        {
            lock (_sync)
            {
                if (ReferenceEquals(_workbook, workbook) &&
                    ReferenceEquals(_activePivotNames, activePivotNames) &&
                    string.Equals(_activeSheetName, activeSheetName, StringComparison.Ordinal) &&
                    SlicersMatch(workbook.Slicers) &&
                    TimelinesMatch(workbook.Timelines))
                {
                    return _filters;
                }

                var slicers = workbook.Slicers.Count == 0
                    ? Array.Empty<SlicerModel>()
                    : GetNativeVisualSlicers(workbook.Slicers, activePivotNames, activeSheetName);
                var timelines = workbook.Timelines.Count == 0
                    ? Array.Empty<TimelineModel>()
                    : GetNativeVisualTimelines(workbook.Timelines, activePivotNames, activeSheetName);

                _workbook = workbook;
                _activePivotNames = activePivotNames;
                _activeSheetName = activeSheetName;
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
                    !string.Equals(snapshot.SourceSheetName, slicer.SourceSheetName, StringComparison.Ordinal) ||
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
                    !string.Equals(snapshot.SourceSheetName, timeline.SourceSheetName, StringComparison.Ordinal) ||
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
                    slicer.SourceSheetName,
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
                    timeline.SourceSheetName,
                    timeline.DrawingAnchor is not null);
            }

            return snapshots;
        }

        private readonly record struct SlicerSnapshot(
            SlicerModel Model,
            string? SourcePivotTableName,
            string? SourceSheetName,
            bool HasDrawingAnchor);

        private readonly record struct TimelineSnapshot(
            TimelineModel Model,
            string? SourcePivotTableName,
            string? SourceSheetName,
            bool HasDrawingAnchor);
    }
}
