using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Resolves the available item captions a slicer offers, for rendering. There are two source paths,
/// mirroring how Excel wires slicers:
///
/// <list type="bullet">
/// <item><b>Table slicer</b> — the slicer cache carries a <c>&lt;x15:tableSlicerCache tableId column&gt;</c>
/// with NO item cache; the items are the distinct values of the referenced structured-table column
/// (resolved from <see cref="SlicerModel.SourceTableId"/> / <see cref="SlicerModel.SourceTableColumnId"/>).</item>
/// <item><b>Pivot slicer</b> — the slicer cache carries <c>&lt;data&gt;&lt;tabular&gt;&lt;items&gt;</c> whose
/// <c>x</c> indices point into the pivot cache field's shared items; the captions come from those
/// shared items and selection comes from the <c>s</c> flag.</item>
/// </list>
///
/// This runs in the host (where the workbook is available) and projects the resolved captions onto
/// <see cref="SlicerModel.AvailableItems"/> (and, for pivot slicers whose selection is encoded only as
/// cache <c>s</c> flags, onto <see cref="SlicerModel.SelectedItems"/>) so the UI layer renders item
/// buttons without raw workbook access. Mirrors <see cref="FormControlListResolver"/>'s late-resolution
/// pattern. Anything that cannot be resolved leaves <see cref="SlicerModel.AvailableItems"/> empty, so
/// the renderer falls back to the slicer's selected items (or a single caption tile).
/// </summary>
public static class SlicerItemResolver
{
    /// <summary>
    /// Populates <see cref="SlicerModel.AvailableItems"/> for every slicer in the workbook, resolving each
    /// against its source table column or pivot cache field. Safe to call repeatedly.
    /// </summary>
    public static void PopulateAvailableItems(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        foreach (var slicer in workbook.Slicers)
            slicer.AvailableItems = ResolveAvailableItems(slicer, workbook);
    }

    /// <summary>
    /// Resolves the ordered, distinct available-item captions for a single slicer, or an empty list when
    /// neither source path applies. For pivot slicers this also fills the slicer's
    /// <see cref="SlicerModel.SelectedItems"/> from the cache items' selection flags when it was empty.
    /// </summary>
    public static IReadOnlyList<string> ResolveAvailableItems(SlicerModel slicer, Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(slicer);
        ArgumentNullException.ThrowIfNull(workbook);

        // Table path: distinct values of the referenced structured-table column.
        if (slicer.SourceTableId is { } tableId && slicer.SourceTableColumnId is { } columnId)
        {
            var tableItems = ResolveTableColumnItems(workbook, slicer, tableId, columnId);
            if (tableItems.Count > 0)
                return tableItems;
        }

        // Pivot path: captions from the pivot cache field's shared items, indexed by the cache items.
        if (slicer.CacheItems.Count > 0)
        {
            var pivotItems = ResolvePivotCacheItems(workbook, slicer);
            if (pivotItems.Count > 0)
                return pivotItems;
        }

        return [];
    }

    private static IReadOnlyList<string> ResolveTableColumnItems(
        Workbook workbook,
        SlicerModel slicer,
        int tableId,
        int columnId)
    {
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var table in sheet.StructuredTables)
            {
                if (table.Id != tableId)
                    continue;

                var columnOffset = ColumnOffsetForId(table, columnId);
                if (columnOffset < 0)
                    return [];

                return DistinctColumnValues(sheet, table, columnOffset);
            }
        }

        return [];
    }

    // The tableSlicerCache @column is the table column id; map it to the 0-based position within the
    // table range (column order in the table, not the worksheet column letter).
    private static int ColumnOffsetForId(StructuredTableModel table, int columnId)
    {
        for (var index = 0; index < table.Columns.Count; index++)
        {
            if (table.Columns[index].Id == columnId)
                return index;
        }

        return -1;
    }

    private static IReadOnlyList<string> DistinctColumnValues(Sheet sheet, StructuredTableModel table, int columnOffset)
    {
        var range = table.Range;
        var col = range.Start.Col + (uint)columnOffset;
        if (col > range.End.Col)
            return [];

        // Skip the header row (the table's first row is the header) and, when shown, the Totals
        // Row -- R100-commands-filter-totalsrow-1: real Excel never offers the Totals Row as a
        // selectable slicer item, matching GetDataBodyRowBounds's totals-row-aware bound already
        // used by every table-editing command.
        var (firstDataRow, lastDataRow) = StructuredTableEditEffects.GetDataBodyRowBounds(table);
        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var items = new List<string>();
        for (var row = firstDataRow; row <= lastDataRow; row++)
        {
            var text = ToDisplayText(sheet.GetCell(row, col)?.Value ?? BlankValue.Instance);
            if (string.IsNullOrEmpty(text))
                continue;
            if (seen.Add(text))
                items.Add(text);
        }

        return items;
    }

    private static IReadOnlyList<string> ResolvePivotCacheItems(Workbook workbook, SlicerModel slicer)
    {
        var field = ResolveSharedItemsField(workbook, slicer);
        if (field?.SharedItems is not { Count: > 0 } sharedItems)
            return [];

        // P13: the pivot cache stores shared items as raw OOXML attribute strings (e.g. a <d v=.../>
        // date is "2026-01-05T00:00:00", untouched by locale or grouping) but the refresh filter
        // (PivotTableRefreshService.MatchesFieldSelections) compares a clicked caption against
        // GroupKeyText(row), which for an ungrouped date is CurrentCulture ToShortDateString() and for
        // a number is CurrentCulture ToString() — never equal to the raw attribute string, so clicking
        // a date/number tile filtered every row out. Normalize each caption the same way GroupKeyText
        // would format that shared item before it becomes a selectable/comparable caption, so the
        // tile the user clicks and the row key the filter compares it against agree.
        var kinds = field.SharedItemKinds;

        // De-dup while preserving order: two cache items can resolve to the same caption, and the
        // "all-selected => cleared" heuristic below compares selected vs available COUNTS, so a
        // duplicated caption would inflate the denominator and misclassify the filter state.
        var available = new List<string>(slicer.CacheItems.Count);
        var availableSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedFromCache = new List<string>();
        var selectedSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in slicer.CacheItems)
        {
            if (item.Index < 0 || item.Index >= sharedItems.Count)
                continue;
            var raw = sharedItems[item.Index];
            if (string.IsNullOrEmpty(raw))
                continue;
            var kind = kinds is not null && item.Index < kinds.Count ? kinds[item.Index] : (char?)null;
            var caption = NormalizeSharedItemCaption(raw, kind, field);
            if (string.IsNullOrEmpty(caption))
                continue;
            if (availableSeen.Add(caption))
                available.Add(caption);
            if (item.IsSelected && selectedSeen.Add(caption))
                selectedFromCache.Add(caption);
        }

        // Excel stores a pivot slicer's selection as the s="1" flag on the cache items (not as
        // <selectedItem>). Project it onto SelectedItems when the slicer didn't already carry one,
        // and only when the selection is a real subset (all-selected => unfiltered/cleared state).
        if (slicer.SelectedItems.Count == 0 &&
            selectedFromCache.Count > 0 &&
            selectedFromCache.Count < available.Count)
        {
            slicer.SelectedItems.AddRange(selectedFromCache);
        }

        return available;
    }

    /// <summary>
    /// Reformats a raw pivot-cache shared-item attribute string into the same text
    /// <c>PivotTableRefreshService.GroupKeyText</c>/<c>KeyText</c> would compute for that value, so a
    /// caption built here matches the row key the refresh filter compares it against.
    /// </summary>
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
            // Shared-item numbers are always stored with an invariant (dot-decimal) "v" attribute
            // regardless of locale — reparse invariant, then reformat with CurrentCulture to match
            // KeyText(NumberValue) (e.g. "1234.5" -> "1234,5" in a comma-decimal locale).
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? number.ToString(CultureInfo.CurrentCulture)
                : raw;
        }

        return raw;
    }

    private static PivotCacheFieldModel? ResolveSharedItemsField(Workbook workbook, SlicerModel slicer)
    {
        var fieldName = slicer.SourceFieldName;
        if (string.IsNullOrWhiteSpace(fieldName))
            return null;

        // Find the pivot cache field by name across the workbook's pivot caches. Slicer caches don't
        // always carry a stable cache id reachable here, so a name match on the source field is the
        // reliable association (slicer sourceName == pivot cache field name).
        foreach (var cache in workbook.PivotCaches)
        {
            foreach (var field in cache.Fields)
            {
                if (string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase) &&
                    field.SharedItems is { Count: > 0 })
                {
                    return field;
                }
            }
        }

        return null;
    }

    private static string ToDisplayText(ScalarValue value) =>
        value switch
        {
            TextValue t => t.Value,
            NumberValue n => n.Value.ToString(CultureInfo.CurrentCulture),
            BoolValue b => b.Value ? "TRUE" : "FALSE",
            DateTimeValue d => d.ToDateTime().ToString(CultureInfo.CurrentCulture),
            BlankValue => string.Empty,
            ErrorValue => string.Empty,
            _ => value.ToString() ?? string.Empty,
        };
}
