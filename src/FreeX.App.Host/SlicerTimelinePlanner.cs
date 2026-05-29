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

public static class SlicerTimelinePlanner
{
    public static IReadOnlyList<SlicerTileItem> BuildSlicerTiles(SlicerModel slicer, IEnumerable<string> sourceItems)
    {
        var selected = slicer.SelectedItems.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var items = sourceItems.ToList();
        if (items.Count == 0)
            items.AddRange(slicer.SelectedItems);

        return items
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => new SlicerTileItem(slicer.Name, item, selected.Count == 0 || selected.Contains(item)))
            .ToList();
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

    public static IReadOnlyList<SlicerModel> GetNativeVisualSlicers(Workbook workbook, Sheet activeSheet) =>
        GetNativeVisualSlicers(workbook.Slicers, BuildActivePivotNameSet(activeSheet));

    public static IReadOnlyList<TimelineModel> GetNativeVisualTimelines(Workbook workbook, Sheet activeSheet) =>
        GetNativeVisualTimelines(workbook.Timelines, BuildActivePivotNameSet(activeSheet));

    private static IReadOnlyList<SlicerModel> GetNativeVisualSlicers(
        IReadOnlyList<SlicerModel> slicers,
        IReadOnlySet<string> activePivotNames)
    {
        var visible = new List<SlicerModel>();
        foreach (var slicer in slicers)
        {
            if (slicer.DrawingAnchor is not null && IsConnectedToPivotOnSheet(slicer.SourcePivotTableName, activePivotNames))
                visible.Add(slicer);
        }

        return visible;
    }

    private static IReadOnlyList<TimelineModel> GetNativeVisualTimelines(
        IReadOnlyList<TimelineModel> timelines,
        IReadOnlySet<string> activePivotNames)
    {
        var visible = new List<TimelineModel>();
        foreach (var timeline in timelines)
        {
            if (timeline.DrawingAnchor is not null && IsConnectedToPivotOnSheet(timeline.SourcePivotTableName, activePivotNames))
                visible.Add(timeline);
        }

        return visible;
    }

    private static HashSet<string> BuildActivePivotNameSet(Sheet activeSheet)
    {
        var pivotNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pivot in activeSheet.PivotTables)
        {
            if (!string.IsNullOrWhiteSpace(pivot.Name))
                pivotNames.Add(pivot.Name);
        }

        return pivotNames;
    }

    private static bool IsConnectedToPivotOnSheet(string? pivotTableName, IReadOnlySet<string> activePivotNames) =>
        !string.IsNullOrWhiteSpace(pivotTableName) && activePivotNames.Contains(pivotTableName);
}
