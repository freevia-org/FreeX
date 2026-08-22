using FreeX.Core.Model;

namespace FreeX.App.Presentation.SlicerTimeline;

/// <summary>
/// Resolves the model-backed captions offered by table and pivot slicers.
/// </summary>
public static class SlicerItemResolver
{
    public static void PopulateAvailableItems(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        foreach (var slicer in workbook.Slicers)
            slicer.AvailableItems = ResolveAvailableItems(slicer, workbook);
    }

    public static IReadOnlyList<string> ResolveAvailableItems(SlicerModel slicer, Workbook workbook) =>
        ResolveAvailableItems(slicer, workbook, sourcePivotTable: null);

    /// <summary>
    /// Resolves a slicer against its table column or pivot-cache field. Supplying the connected
    /// PivotTable binds cache lookup to that table's cache before the field-name fallback is used.
    /// </summary>
    public static IReadOnlyList<string> ResolveAvailableItems(
        SlicerModel slicer,
        Workbook workbook,
        PivotTableModel? sourcePivotTable)
    {
        ArgumentNullException.ThrowIfNull(slicer);
        ArgumentNullException.ThrowIfNull(workbook);

        if (slicer.SourceTableId is { } tableId &&
            slicer.SourceTableColumnId is { } columnId &&
            StructuredTableCaptionResolver.TryResolveColumnCaptions(workbook, tableId, columnId, out var tableItems))
        {
            if (tableItems.Count > 0)
                return tableItems;
        }

        var field = ResolveSharedItemsField(workbook, slicer, sourcePivotTable);
        return field?.SharedItems is { Count: > 0 }
            ? ResolvePivotCacheItems(slicer, field)
            : [];
    }

    private static IReadOnlyList<string> ResolvePivotCacheItems(
        SlicerModel slicer,
        PivotCacheFieldModel field)
    {
        var sharedItems = field.SharedItems!;
        var kinds = field.SharedItemKinds;
        var available = new List<string>(slicer.CacheItems.Count > 0 ? slicer.CacheItems.Count : sharedItems.Count);
        var availableSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedFromCache = new List<string>();
        var selectedSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (slicer.CacheItems.Count == 0)
        {
            for (var index = 0; index < sharedItems.Count; index++)
                AddPivotCacheItem(index, isSelected: false);
        }
        else
        {
            foreach (var item in slicer.CacheItems)
                AddPivotCacheItem(item.Index, item.IsSelected);
        }

        if (slicer.SelectedItems.Count == 0 &&
            selectedFromCache.Count > 0 &&
            selectedFromCache.Count < available.Count)
        {
            slicer.SelectedItems.AddRange(selectedFromCache);
        }

        return available;

        void AddPivotCacheItem(int index, bool isSelected)
        {
            if (index < 0 || index >= sharedItems.Count)
                return;

            var raw = sharedItems[index];
            if (string.IsNullOrEmpty(raw))
                return;

            var kind = kinds is not null && index < kinds.Count ? kinds[index] : (char?)null;
            var caption = PivotSharedItemCaptionResolver.Resolve(raw, kind, field);
            if (string.IsNullOrEmpty(caption))
                return;

            if (availableSeen.Add(caption))
                available.Add(caption);
            if (isSelected && selectedSeen.Add(caption))
                selectedFromCache.Add(caption);
        }
    }

    private static PivotCacheFieldModel? ResolveSharedItemsField(
        Workbook workbook,
        SlicerModel slicer,
        PivotTableModel? sourcePivotTable)
    {
        var fieldName = slicer.SourceFieldName;
        if (string.IsNullOrWhiteSpace(fieldName))
            return null;

        if (sourcePivotTable is not null)
        {
            var boundCache = workbook.PivotCaches.FirstOrDefault(cache => cache.CacheId == sourcePivotTable.CacheId);
            if (boundCache is not null)
                return FindSharedItemsField(boundCache, fieldName);
        }

        foreach (var cache in workbook.PivotCaches)
        {
            var field = FindSharedItemsField(cache, fieldName);
            if (field is not null)
                return field;
        }

        return null;
    }

    private static PivotCacheFieldModel? FindSharedItemsField(PivotCacheModel cache, string fieldName)
    {
        foreach (var field in cache.Fields)
        {
            if (string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase) &&
                field.SharedItems is { Count: > 0 })
            {
                return field;
            }
        }

        return null;
    }

}
