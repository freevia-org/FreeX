using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
}
