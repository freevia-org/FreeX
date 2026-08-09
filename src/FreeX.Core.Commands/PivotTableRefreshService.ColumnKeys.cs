using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class PivotTableRefreshService
{
    private static void WriteColumnHeader(
        Sheet sheet,
        uint startRow,
        uint outputColumn,
        PivotKey columnKey,
        PivotDataFieldModel dataField,
        bool singleDataField)
    {
        for (var level = 0; level < columnKey.Values.Count; level++)
        {
            var caption = columnKey.Values[level];
            if (!singleDataField && level == columnKey.Values.Count - 1)
                caption = $"{caption} {dataField.Name}";
            SetPivotCell(sheet, new CellAddress(sheet.Id, startRow + (uint)level, outputColumn), new TextValue(caption));
        }
    }

    private static bool ColumnKeyMatches(
        IReadOnlyList<ScalarValue> row,
        IReadOnlyList<PivotFieldModel> columnFields,
        PivotKey columnKey)
    {
        if (columnFields.Count != columnKey.Values.Count)
            return false;

        for (var index = 0; index < columnFields.Count; index++)
        {
            var field = columnFields[index];
            if (!string.Equals(
                    GroupKeyText(row[field.SourceFieldIndex], field),
                    columnKey.Values[index],
                    StringComparison.CurrentCultureIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static PivotKey BuildColumnKey(
        IReadOnlyList<ScalarValue> row,
        IReadOnlyList<PivotFieldModel> columnFields) =>
        new(columnFields.Select(field => GroupKeyText(row[field.SourceFieldIndex], field)).ToArray());

    private static PivotColumnRowMap BuildColumnRowsByKey(
        IEnumerable<IReadOnlyList<ScalarValue>> rows,
        IReadOnlyList<PivotFieldModel> columnFields)
    {
        var rowCapacity = rows is ICollection<IReadOnlyList<ScalarValue>> collection ? collection.Count : 0;
        var map = new PivotColumnRowMap();
        if (columnFields.Count == 1)
        {
            BuildSingleColumnRowsByKey(rows, columnFields[0], map, rowCapacity);
            return map;
        }

        foreach (var row in rows)
        {
            var key = BuildColumnKey(row, columnFields);
            if (!map.RowsByKey.TryGetValue(key, out var keyRows))
            {
                keyRows = [];
                map.RowsByKey.Add(key, keyRows);
            }

            keyRows.Add(row);
        }

        return map;
    }

    private static void BuildSingleColumnRowsByKey(
        IEnumerable<IReadOnlyList<ScalarValue>> rows,
        PivotFieldModel columnField,
        PivotColumnRowMap map,
        int rowCapacity)
    {
        var bucketCapacity = rowCapacity == 0 ? 0 : Math.Min(rowCapacity, 1024);
        var buckets = new Dictionary<string, PivotColumnBucket>(
            bucketCapacity,
            StringComparer.CurrentCultureIgnoreCase);

        foreach (var row in rows)
        {
            var keyText = GroupKeyText(row[columnField.SourceFieldIndex], columnField);
            if (!buckets.TryGetValue(keyText, out var bucket))
            {
                var key = new PivotKey([keyText]);
                var keyRows = new List<IReadOnlyList<ScalarValue>>();
                bucket = new PivotColumnBucket(key, keyRows);
                buckets.Add(keyText, bucket);
                map.RowsByKey.Add(key, keyRows);
            }

            bucket.Rows.Add(row);
        }
    }

    private static IReadOnlyList<IReadOnlyList<ScalarValue>> RowsForColumnKey(
        PivotColumnRowMap rowsByColumnKey,
        PivotKey columnKey) =>
        rowsByColumnKey.RowsByKey.TryGetValue(columnKey, out var rows)
            ? rows
            : Array.Empty<IReadOnlyList<ScalarValue>>();

    private static IReadOnlyList<IReadOnlyList<ScalarValue>> RowsForColumnKeys(
        PivotColumnRowMap rowsByColumnKey,
        IReadOnlyList<PivotKey> columnKeys,
        IReadOnlyList<IReadOnlyList<ScalarValue>> allRows)
    {
        if (ColumnKeysCoverAllRows(rowsByColumnKey, columnKeys))
            return allRows;

        var visibleRowCapacity = 0;
        foreach (var columnKey in columnKeys)
        {
            if (rowsByColumnKey.RowsByKey.TryGetValue(columnKey, out var rows))
                visibleRowCapacity += rows.Count;
        }

        var visibleRowSet = new HashSet<IReadOnlyList<ScalarValue>>(visibleRowCapacity);
        foreach (var columnKey in columnKeys)
        {
            if (!rowsByColumnKey.RowsByKey.TryGetValue(columnKey, out var rows))
                continue;

            foreach (var row in rows)
                visibleRowSet.Add(row);
        }

        var visibleRows = new List<IReadOnlyList<ScalarValue>>(visibleRowCapacity);
        foreach (var row in allRows)
        {
            if (visibleRowSet.Contains(row))
                visibleRows.Add(row);
        }

        return visibleRows;
    }

    private static bool ColumnKeysCoverAllRows(
        PivotColumnRowMap rowsByColumnKey,
        IReadOnlyList<PivotKey> columnKeys)
    {
        if (columnKeys.Count != rowsByColumnKey.RowsByKey.Count)
            return false;

        foreach (var columnKey in columnKeys)
        {
            if (!rowsByColumnKey.RowsByKey.ContainsKey(columnKey))
                return false;
        }

        return true;
    }

    private static List<PivotKey> BuildColumnKeys(
        Workbook workbook,
        PivotTableModel pivotTable,
        IReadOnlyList<IReadOnlyList<ScalarValue>> rows,
        IReadOnlyList<PivotFieldModel> columnFields,
        PivotColumnRowMap? rowsByColumnKey = null)
    {
        var keys = rowsByColumnKey is null
            ? rows
                .Select(row => BuildColumnKey(row, columnFields))
                .Distinct()
                .ToList()
            : rowsByColumnKey.RowsByKey.Keys.ToList();

        if (!pivotTable.ShowItemsWithNoDataOnColumns || columnFields.Count == 0)
            return keys.Order(PivotKeyComparer.Instance).ToList();

        var itemSets = columnFields
            .Select(field => GetFieldItemsWithNoData(workbook, pivotTable, rows, field))
            .ToList();
        foreach (var key in BuildKeyCombinations(itemSets))
        {
            if (!keys.Contains(key))
                keys.Add(key);
        }

        return keys.Order(PivotKeyComparer.Instance).ToList();
    }

    private static List<IGrouping<PivotKey, IReadOnlyList<ScalarValue>>> BuildRowGroups(
        Workbook workbook,
        PivotTableModel pivotTable,
        IReadOnlyList<IReadOnlyList<ScalarValue>> rows,
        IReadOnlyList<PivotFieldModel> rowFields)
    {
        var groups = rows
            .GroupBy(row => new PivotKey(rowFields.Select(field => GroupKeyText(row[field.SourceFieldIndex], field)).ToArray()))
            .Select(group => (IGrouping<PivotKey, IReadOnlyList<ScalarValue>>)new PivotRowGroup(group.Key, group.ToList()))
            .ToList();

        if (!pivotTable.ShowItemsWithNoDataOnRows || rowFields.Count == 0)
            return groups.OrderBy(group => group.Key, PivotKeyComparer.Instance).ToList();

        var itemSets = rowFields
            .Select(field => GetFieldItemsWithNoData(workbook, pivotTable, rows, field))
            .ToList();
        foreach (var key in BuildKeyCombinations(itemSets))
        {
            if (!groups.Any(group => group.Key.Equals(key)))
                groups.Add(new PivotRowGroup(key, []));
        }

        return groups.OrderBy(group => group.Key, PivotKeyComparer.Instance).ToList();
    }

    private static IReadOnlyList<string> GetFieldItemsWithNoData(
        Workbook workbook,
        PivotTableModel pivotTable,
        IReadOnlyList<IReadOnlyList<ScalarValue>> rows,
        PivotFieldModel field)
    {
        var items = new List<string>();
        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var cache = CommandGuards.FindPivotCache(workbook, pivotTable);
        var cacheField = cache is not null &&
            field.SourceFieldIndex >= 0 &&
            field.SourceFieldIndex < cache.Fields.Count
                ? cache.Fields[field.SourceFieldIndex]
                : null;

        if (cacheField?.SharedItems is { Count: > 0 } sharedItems)
        {
            var kinds = cacheField.SharedItemKinds;
            for (var index = 0; index < sharedItems.Count; index++)
            {
                var raw = sharedItems[index];
                if (string.IsNullOrEmpty(raw))
                    continue;

                var kind = kinds is not null && index < kinds.Count ? kinds[index] : (char?)null;
                // The cache's shared items are raw, UNGROUPED values (e.g. a <d v=.../> ISO date
                // string, untouched by locale or grouping). Project each one through the same
                // GroupKeyText transform the real row/column labels go through -- otherwise a
                // grouped field's no-data injection contributes the raw cache value ("2026-01-
                // 05T00:00:00") as its own phantom label alongside the real group label
                // ("2026-01") it should have merged into.
                var value = ParseSharedItemScalarValue(raw, kind, cacheField);
                var item = GroupKeyText(value, field);
                if (!string.IsNullOrEmpty(item) && seen.Add(item))
                    items.Add(item);
            }
        }

        foreach (var item in rows.Select(row => GroupKeyText(row[field.SourceFieldIndex], field)))
        {
            if (seen.Add(item))
                items.Add(item);
        }

        return items;
    }

    /// <summary>
    /// Reparses a raw pivot-cache shared-item attribute string back into the typed
    /// <see cref="ScalarValue"/> it originally represented, so it can be run through the same
    /// <see cref="GroupKeyText(ScalarValue, PivotFieldModel)"/> grouping transform a live cell value
    /// goes through. Mirrors <see cref="SlicerItemResolver"/>'s kind-detection (element kind first,
    /// falling back to the cache field's Contains* flags for items saved before FreeX started
    /// recording per-item kinds).
    /// </summary>
    private static ScalarValue ParseSharedItemScalarValue(string raw, char? kind, PivotCacheFieldModel cacheField)
    {
        var isDateKind = kind == 'd' ||
            (kind is null && cacheField.ContainsDate && !cacheField.ContainsString && !cacheField.ContainsNumber);
        if (isDateKind)
        {
            return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? DateTimeValue.FromDateTime(date)
                : new TextValue(raw);
        }

        var isNumberKind = kind == 'n' ||
            (kind is null && cacheField.ContainsNumber && !cacheField.ContainsString && !cacheField.ContainsDate);
        if (isNumberKind)
        {
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? new NumberValue(number)
                : new TextValue(raw);
        }

        if (kind == 'b' && bool.TryParse(raw, out var boolValue))
            return new BoolValue(boolValue);

        return new TextValue(raw);
    }

    private static IEnumerable<PivotKey> BuildKeyCombinations(IReadOnlyList<IReadOnlyList<string>> itemSets)
    {
        if (itemSets.Count == 0 || itemSets.Any(items => items.Count == 0))
            yield break;

        var values = new string[itemSets.Count];
        foreach (var key in BuildKeyCombinations(itemSets, values, 0))
            yield return key;
    }

    private static IEnumerable<PivotKey> BuildKeyCombinations(
        IReadOnlyList<IReadOnlyList<string>> itemSets,
        string[] values,
        int depth)
    {
        if (depth == itemSets.Count)
        {
            yield return new PivotKey(values.ToArray());
            yield break;
        }

        foreach (var item in itemSets[depth])
        {
            values[depth] = item;
            foreach (var key in BuildKeyCombinations(itemSets, values, depth + 1))
                yield return key;
        }
    }

    private sealed class PivotRowGroup(PivotKey key, IReadOnlyList<IReadOnlyList<ScalarValue>> rows)
        : IGrouping<PivotKey, IReadOnlyList<ScalarValue>>
    {
        public PivotKey Key { get; } = key;

        public IEnumerator<IReadOnlyList<ScalarValue>> GetEnumerator() => rows.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class PivotColumnRowMap
    {
        public Dictionary<PivotKey, List<IReadOnlyList<ScalarValue>>> RowsByKey { get; } = [];
    }

    private sealed class PivotColumnBucket(PivotKey key, List<IReadOnlyList<ScalarValue>> rows)
    {
        public PivotKey Key { get; } = key;

        public List<IReadOnlyList<ScalarValue>> Rows { get; } = rows;
    }
}
