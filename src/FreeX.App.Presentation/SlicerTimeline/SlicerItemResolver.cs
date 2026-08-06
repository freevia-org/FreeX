using System.Globalization;

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

        if (slicer.SourceTableId is { } tableId && slicer.SourceTableColumnId is { } columnId)
        {
            var tableItems = ResolveTableColumnItems(workbook, tableId, columnId);
            if (tableItems.Count > 0)
                return tableItems;
        }

        var field = ResolveSharedItemsField(workbook, slicer, sourcePivotTable);
        return field?.SharedItems is { Count: > 0 }
            ? ResolvePivotCacheItems(slicer, field)
            : [];
    }

    private static IReadOnlyList<string> ResolveTableColumnItems(Workbook workbook, int tableId, int columnId)
    {
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var table in sheet.StructuredTables)
            {
                if (table.Id != tableId)
                    continue;

                var columnOffset = ColumnOffsetForId(table, columnId);
                return columnOffset < 0 ? [] : DistinctColumnValues(sheet, table, columnOffset);
            }
        }

        return [];
    }

    private static int ColumnOffsetForId(StructuredTableModel table, int columnId)
    {
        for (var index = 0; index < table.Columns.Count; index++)
        {
            if (table.Columns[index].Id == columnId)
                return index;
        }

        return -1;
    }

    private static IReadOnlyList<string> DistinctColumnValues(
        Sheet sheet,
        StructuredTableModel table,
        int columnOffset)
    {
        var range = table.Range;
        var col = range.Start.Col + (uint)columnOffset;
        if (col > range.End.Col)
            return [];

        var hasHeaderRow = table.HeaderRowCount is null or > 0;
        var firstDataRow = range.Start.Row + (hasHeaderRow ? 1u : 0u);
        var lastDataRow = table.TotalsRowShown && range.End.Row > range.Start.Row
            ? range.End.Row - 1
            : range.End.Row;
        lastDataRow = Math.Max(firstDataRow, lastDataRow);
        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var items = new List<string>();
        for (var row = firstDataRow; row <= lastDataRow; row++)
        {
            var text = ToDisplayText(sheet.GetCell(row, col)?.Value ?? BlankValue.Instance);
            if (!string.IsNullOrEmpty(text) && seen.Add(text))
                items.Add(text);
        }

        return items;
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
            var caption = NormalizeSharedItemCaption(raw, kind, field);
            if (string.IsNullOrEmpty(caption))
                return;

            if (availableSeen.Add(caption))
                available.Add(caption);
            if (isSelected && selectedSeen.Add(caption))
                selectedFromCache.Add(caption);
        }
    }

    private static string NormalizeSharedItemCaption(string raw, char? kind, PivotCacheFieldModel field)
    {
        if (kind == 'd' || (kind is null && field.ContainsDate && !field.ContainsString && !field.ContainsNumber))
        {
            if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return raw;

            return field.Grouping switch
            {
                PivotFieldGrouping.Year => date.Year.ToString(CultureInfo.InvariantCulture),
                PivotFieldGrouping.Quarter => $"{date.Year}-Q{((date.Month - 1) / 3) + 1}",
                PivotFieldGrouping.Month => date.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                PivotFieldGrouping.Day => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                _ => date.ToShortDateString()
            };
        }

        if (kind == 'n' || (kind is null && field.ContainsNumber && !field.ContainsString && !field.ContainsDate))
        {
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? number.ToString(CultureInfo.CurrentCulture)
                : raw;
        }

        return raw;
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

    private static string ToDisplayText(ScalarValue value) => value switch
    {
        TextValue text => text.Value,
        NumberValue number => number.Value.ToString(CultureInfo.CurrentCulture),
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        DateTimeValue date => date.ToDateTime().ToString(CultureInfo.CurrentCulture),
        BlankValue => string.Empty,
        ErrorValue => string.Empty,
        _ => value.ToString() ?? string.Empty,
    };
}
