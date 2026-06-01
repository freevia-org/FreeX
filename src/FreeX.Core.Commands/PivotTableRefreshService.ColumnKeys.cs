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
            sheet.SetCell(new CellAddress(sheet.Id, startRow + (uint)level, outputColumn), new TextValue(caption));
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
        var map = new PivotColumnRowMap(rowCapacity);
        foreach (var row in rows)
        {
            var key = BuildColumnKey(row, columnFields);
            if (!map.RowsByKey.TryGetValue(key, out var keyRows))
            {
                keyRows = [];
                map.RowsByKey.Add(key, keyRows);
            }

            keyRows.Add(row);
            map.RowsInSourceOrder.Add((row, key));
        }

        return map;
    }

    private static IReadOnlyList<IReadOnlyList<ScalarValue>> RowsForColumnKey(
        PivotColumnRowMap rowsByColumnKey,
        PivotKey columnKey) =>
        rowsByColumnKey.RowsByKey.TryGetValue(columnKey, out var rows)
            ? rows
            : Array.Empty<IReadOnlyList<ScalarValue>>();

    private static List<IReadOnlyList<ScalarValue>> RowsForColumnKeys(
        PivotColumnRowMap rowsByColumnKey,
        IReadOnlyList<PivotKey> columnKeys)
    {
        var visibleKeys = new HashSet<PivotKey>(columnKeys);
        var visibleRowCapacity = 0;
        foreach (var columnKey in columnKeys)
        {
            if (rowsByColumnKey.RowsByKey.TryGetValue(columnKey, out var rows))
                visibleRowCapacity += rows.Count;
        }

        var visibleRows = new List<IReadOnlyList<ScalarValue>>(visibleRowCapacity);
        foreach (var (row, key) in rowsByColumnKey.RowsInSourceOrder)
        {
            if (visibleKeys.Contains(key))
                visibleRows.Add(row);
        }

        return visibleRows;
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
        var cache = workbook.PivotCaches.FirstOrDefault(cache => cache.CacheId == pivotTable.CacheId);
        if (cache is not null &&
            field.SourceFieldIndex >= 0 &&
            field.SourceFieldIndex < cache.Fields.Count &&
            cache.Fields[field.SourceFieldIndex].SharedItems is { Count: > 0 } sharedItems)
        {
            items.AddRange(sharedItems.Where(item => !string.IsNullOrEmpty(item)));
        }

        foreach (var item in rows.Select(row => GroupKeyText(row[field.SourceFieldIndex], field)))
        {
            if (!items.Contains(item, StringComparer.CurrentCultureIgnoreCase))
                items.Add(item);
        }

        return items;
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
        public PivotColumnRowMap(int rowCapacity)
        {
            RowsInSourceOrder = new List<(IReadOnlyList<ScalarValue> Row, PivotKey Key)>(rowCapacity);
        }

        public Dictionary<PivotKey, List<IReadOnlyList<ScalarValue>>> RowsByKey { get; } = [];

        public List<(IReadOnlyList<ScalarValue> Row, PivotKey Key)> RowsInSourceOrder { get; }
    }
}
