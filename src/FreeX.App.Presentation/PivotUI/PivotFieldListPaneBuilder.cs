using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

/// <summary>
/// Derives the portable <see cref="PivotFieldListPaneModel"/> from a <see cref="PivotTableModel"/> and the
/// source-range column headers. The header list is indexed by source field index (column 0 of the source
/// range is index 0). Ported from the field-list planning logic in the desktop hosts.
/// </summary>
public static class PivotFieldListPaneBuilder
{
    public static IReadOnlyList<PivotAvailableFieldItemModel> BuildAvailableFields(
        IReadOnlyList<string> headers,
        PivotFieldAreas areas)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(areas);

        var used = areas.RowFields
            .Select(field => field.SourceFieldIndex)
            .Concat(areas.ColumnFields.Select(field => field.SourceFieldIndex))
            .Concat(areas.PageFields.Select(field => field.SourceFieldIndex))
            .Concat(areas.DataFields.Select(field => field.SourceFieldIndex))
            .ToHashSet();
        return headers
            .Select((caption, index) => new PivotAvailableFieldItemModel(
                index,
                caption,
                used.Contains(index)))
            .ToList();
    }

    public static IReadOnlyList<PivotAvailableFieldItemModel> FilterAvailableFields(
        IEnumerable<PivotAvailableFieldItemModel> fields,
        string? searchText)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var needle = searchText?.Trim();
        return string.IsNullOrEmpty(needle)
            ? fields.ToList()
            : fields
                .Where(field => field.Caption.Contains(needle, StringComparison.OrdinalIgnoreCase))
                .ToList();
    }

    public static string? GetItemCaption(object? item) =>
        item switch
        {
            string value when !string.IsNullOrWhiteSpace(value) => value,
            PivotAvailableFieldItemModel field when !string.IsNullOrWhiteSpace(field.Caption) => field.Caption,
            PivotFieldListItemModel field when !string.IsNullOrWhiteSpace(field.Caption) => field.Caption,
            _ => null,
        };

    /// <summary>
    /// Builds the field-list pane model. A field placed in a layout area (rows/columns/filters or values)
    /// is removed from the available pool; every remaining source field stays available.
    /// </summary>
    public static PivotFieldListPaneModel Build(PivotTableModel pivotTable, IReadOnlyList<string> headers)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        ArgumentNullException.ThrowIfNull(headers);

        var placed = new HashSet<int>();

        var rows = BuildAxisBucket(PivotFieldBucket.Rows, pivotTable.RowFields, headers, placed);
        var columns = BuildAxisBucket(PivotFieldBucket.Columns, pivotTable.ColumnFields, headers, placed);
        var filters = BuildAxisBucket(PivotFieldBucket.Filters, pivotTable.PageFields, headers, placed);
        var values = BuildValuesBucket(pivotTable, headers, placed);
        var available = BuildAvailableBucket(headers, placed);

        return new PivotFieldListPaneModel(pivotTable.Name, available, rows, columns, values, filters);
    }

    /// <summary>
    /// Filters available-field items by a case-insensitive substring (the pane's search box). A null or
    /// blank needle returns the items unchanged. Ported from the field-list filter in the desktop hosts.
    /// </summary>
    public static IReadOnlyList<PivotFieldListItemModel> FilterByCaption(
        IEnumerable<PivotFieldListItemModel> fields,
        string? searchText)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var needle = searchText?.Trim();
        if (string.IsNullOrEmpty(needle))
            return fields.ToList();

        return fields
            .Where(field => field.Caption.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Caption for a source field: the header text when the index is in range, otherwise a synthesized
    /// "Column N" fallback. Ported from the field-caption helper in the desktop hosts.
    /// </summary>
    public static string FieldCaption(IReadOnlyList<string> headers, int sourceFieldIndex) =>
        sourceFieldIndex >= 0 && sourceFieldIndex < headers.Count
            ? headers[sourceFieldIndex]
            : $"Column {sourceFieldIndex + 1}";

    private static PivotFieldListBucketModel BuildAxisBucket(
        PivotFieldBucket bucket,
        IReadOnlyList<PivotFieldModel> fields,
        IReadOnlyList<string> headers,
        HashSet<int> placed)
    {
        var items = new List<PivotFieldListItemModel>(fields.Count);
        foreach (var field in fields)
        {
            placed.Add(field.SourceFieldIndex);
            items.Add(new PivotFieldListItemModel(
                field.SourceFieldIndex,
                FieldCaption(headers, field.SourceFieldIndex),
                bucket));
        }

        return new PivotFieldListBucketModel(bucket, items);
    }

    private static PivotFieldListBucketModel BuildValuesBucket(
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        HashSet<int> placed)
    {
        var items = new List<PivotFieldListItemModel>(pivotTable.DataFields.Count);
        for (var index = 0; index < pivotTable.DataFields.Count; index++)
        {
            var dataField = pivotTable.DataFields[index];
            placed.Add(dataField.SourceFieldIndex);
            var caption = string.IsNullOrWhiteSpace(dataField.Name)
                ? FieldCaption(headers, dataField.SourceFieldIndex)
                : dataField.Name;
            items.Add(new PivotFieldListItemModel(
                dataField.SourceFieldIndex,
                caption,
                PivotFieldBucket.Values,
                DataFieldIndex: index,
                SummaryFunction: dataField.SummaryFunction));
        }

        return new PivotFieldListBucketModel(PivotFieldBucket.Values, items);
    }

    private static PivotFieldListBucketModel BuildAvailableBucket(
        IReadOnlyList<string> headers,
        HashSet<int> placed)
    {
        var items = new List<PivotFieldListItemModel>();
        for (var index = 0; index < headers.Count; index++)
        {
            if (placed.Contains(index))
                continue;

            items.Add(new PivotFieldListItemModel(
                index,
                FieldCaption(headers, index),
                PivotFieldBucket.Available));
        }

        return new PivotFieldListBucketModel(PivotFieldBucket.Available, items);
    }
}
