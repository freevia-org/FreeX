using System.Globalization;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record PivotFieldFilterState(
    string FieldCaption,
    int SourceFieldIndex,
    IReadOnlyList<string> AllItems,
    IReadOnlyList<string> SelectedItems,
    PivotLabelFilterModel? LabelFilter,
    PivotValueFilterModel? ValueFilter,
    IReadOnlyList<PivotDataFieldModel> DataFields)
{
    public bool HasItemFilter => PivotFieldFilterSummary.HasExplicitItemSelection(SelectedItems, AllItems.Count);
    public bool HasLabelFilter => LabelFilter is not null;
    public bool HasValueFilter => ValueFilter is not null;
    public bool HasAnyFilter => HasItemFilter || HasLabelFilter || HasValueFilter;
    public string ItemSummary => PivotFieldFilterSummary.FormatItemFilterSummary(SelectedItems, AllItems.Count);
    public string LabelSummary => PivotFieldFilterSummary.FormatLabelFilterSummary(LabelFilter);
    public string ValueSummary => PivotFieldFilterSummary.FormatValueFilterSummary(ValueFilter, DataFields);
    public string OverallSummary => PivotFieldFilterSummary.FormatOverallSummary(this);
}

public static class PivotFieldFilterSummary
{
    public static PivotFieldFilterState CreateState(
        PivotTableModel pivotTable,
        int sourceFieldIndex,
        string fieldCaption,
        IReadOnlyList<string> allItems)
    {
        var layoutField = FindLayoutField(pivotTable, sourceFieldIndex);
        var selectedItems = GetSelectedItems(layoutField);
        return new PivotFieldFilterState(
            fieldCaption,
            sourceFieldIndex,
            allItems,
            selectedItems,
            FindLabelFilter(pivotTable, sourceFieldIndex),
            FindValueFilter(pivotTable, sourceFieldIndex),
            pivotTable.DataFields.ToList());
    }

    public static PivotLabelFilterModel? FindLabelFilter(PivotTableModel pivotTable, int sourceFieldIndex) =>
        pivotTable.LabelFilters.LastOrDefault(filter => filter.SourceFieldIndex == sourceFieldIndex);

    public static PivotValueFilterModel? FindValueFilter(PivotTableModel pivotTable, int sourceFieldIndex) =>
        pivotTable.ValueFilters.LastOrDefault(filter => PivotFilterOwnership.BelongsToSourceField(filter, sourceFieldIndex));

    public static bool HasExplicitItemSelection(IReadOnlyList<string> selectedItems, int allItemCount)
    {
        var explicitCount = selectedItems.Count(IsExplicitSelection);
        return explicitCount > 0 && explicitCount < allItemCount;
    }

    public static string FormatItemFilterSummary(IReadOnlyList<string> selectedItems, int allItemCount)
    {
        var explicitItems = selectedItems.Where(IsExplicitSelection).ToList();
        if (explicitItems.Count == 0 || explicitItems.Count >= allItemCount)
            return UiText.Get("PivotFieldFilter_NoItemFilter");

        return explicitItems.Count == 1
            ? $"Item filter: {Quote(explicitItems[0])}"
            : $"Item filter: {explicitItems.Count} items ({JoinPreview(explicitItems)})";
    }

    public static string FormatLabelFilterSummary(PivotLabelFilterModel? filter) =>
        filter is null
            ? UiText.Get("PivotFieldFilter_NoLabelFilter")
            : $"Label filter: {FormatLabelFilter(filter)}";

    public static string FormatValueFilterSummary(
        PivotValueFilterModel? filter,
        IReadOnlyList<PivotDataFieldModel> dataFields) =>
        filter is null
            ? UiText.Get("PivotFieldFilter_NoValueFilter")
            : $"Value filter: {FormatValueFilter(filter, dataFields)}";

    public static string FormatOverallSummary(PivotFieldFilterState state)
    {
        var parts = new List<string>();
        if (state.HasItemFilter)
            parts.Add(state.ItemSummary);
        if (state.HasLabelFilter)
            parts.Add(state.LabelSummary);
        if (state.HasValueFilter)
            parts.Add(state.ValueSummary);

        return parts.Count == 0
            ? $"No active filters for {state.FieldCaption}."
            : $"Active filters for {state.FieldCaption}: {string.Join("; ", parts)}";
    }

    public static string FormatClearFilterHeader(PivotFieldFilterState state) =>
        $"Clear Filters from \"{state.FieldCaption}\"";

    public static string FormatSelectItemsHeader(PivotFieldFilterState state) =>
        state.HasItemFilter
            ? $"Select Items... ({FormatSelectedItemCount(state.SelectedItems, state.AllItems.Count)})"
            : "Select Items...";

    public static string FormatLabelFilterHeader(PivotFieldFilterState state) =>
        state.LabelFilter is null
            ? "Label Filter..."
            : $"Label Filter... ({FormatLabelFilter(state.LabelFilter)})";

    public static string FormatValueFilterHeader(PivotFieldFilterState state) =>
        state.ValueFilter is null
            ? "Value Filter..."
            : $"Value Filter... ({FormatValueFilter(state.ValueFilter, state.DataFields)})";

    public static string FormatSortSummary(PivotSortModel? sort, IReadOnlyList<PivotDataFieldModel> dataFields)
    {
        if (sort is null)
            return "No sort";

        var direction = sort.Direction == PivotSortDirection.Descending ? "descending" : "ascending";
        if (sort.Target == PivotSortTarget.Value)
            return $"Sorted {direction} by {DataFieldName(dataFields, sort.DataFieldIndex)}";

        return $"Sorted {direction} by labels";
    }

    public static string FormatValueFilter(PivotValueFilterModel filter, IReadOnlyList<PivotDataFieldModel> dataFields)
    {
        var dataField = DataFieldName(dataFields, filter.DataFieldIndex);
        return filter.Kind switch
        {
            PivotValueFilterKind.Top => $"Top {filter.Count} by {dataField}",
            PivotValueFilterKind.Bottom => $"Bottom {filter.Count} by {dataField}",
            PivotValueFilterKind.GreaterThan => $"{dataField} > {FormatNumber(filter.ComparisonValue)}",
            PivotValueFilterKind.GreaterThanOrEqual => $"{dataField} >= {FormatNumber(filter.ComparisonValue)}",
            PivotValueFilterKind.LessThan => $"{dataField} < {FormatNumber(filter.ComparisonValue)}",
            PivotValueFilterKind.LessThanOrEqual => $"{dataField} <= {FormatNumber(filter.ComparisonValue)}",
            PivotValueFilterKind.Equals => $"{dataField} = {FormatNumber(filter.ComparisonValue)}",
            PivotValueFilterKind.DoesNotEqual => $"{dataField} <> {FormatNumber(filter.ComparisonValue)}",
            PivotValueFilterKind.Between => $"{dataField} between {FormatNumber(filter.ComparisonValue)} and {FormatNumber(filter.ComparisonValue2)}",
            PivotValueFilterKind.NotBetween => $"{dataField} not between {FormatNumber(filter.ComparisonValue)} and {FormatNumber(filter.ComparisonValue2)}",
            PivotValueFilterKind.AboveAverage => $"{dataField} above average",
            PivotValueFilterKind.BelowAverage => $"{dataField} below average",
            _ => dataField
        };
    }

    public static string FormatLabelFilter(PivotLabelFilterModel filter) =>
        filter.Kind switch
        {
            PivotLabelFilterKind.Equals => $"equals {Quote(filter.Value)}",
            PivotLabelFilterKind.DoesNotEqual => $"does not equal {Quote(filter.Value)}",
            PivotLabelFilterKind.BeginsWith => $"begins with {Quote(filter.Value)}",
            PivotLabelFilterKind.EndsWith => $"ends with {Quote(filter.Value)}",
            PivotLabelFilterKind.Contains => $"contains {Quote(filter.Value)}",
            PivotLabelFilterKind.DoesNotContain => $"does not contain {Quote(filter.Value)}",
            PivotLabelFilterKind.GreaterThan => $"> {Quote(filter.Value)}",
            PivotLabelFilterKind.GreaterThanOrEqual => $">= {Quote(filter.Value)}",
            PivotLabelFilterKind.LessThan => $"< {Quote(filter.Value)}",
            PivotLabelFilterKind.LessThanOrEqual => $"<= {Quote(filter.Value)}",
            PivotLabelFilterKind.Between => $"between {Quote(filter.Value)} and {Quote(filter.Value2 ?? filter.Value)}",
            _ => Quote(filter.Value)
        };

    private static PivotFieldModel? FindLayoutField(PivotTableModel pivotTable, int sourceFieldIndex) =>
        FindLayoutField(pivotTable.RowFields, sourceFieldIndex) ??
        FindLayoutField(pivotTable.ColumnFields, sourceFieldIndex) ??
        FindLayoutField(pivotTable.PageFields, sourceFieldIndex);

    private static PivotFieldModel? FindLayoutField(IReadOnlyList<PivotFieldModel> fields, int sourceFieldIndex)
    {
        foreach (var field in fields)
        {
            if (field.SourceFieldIndex == sourceFieldIndex)
                return field;
        }

        return null;
    }

    private static IReadOnlyList<string> GetSelectedItems(PivotFieldModel? field)
    {
        if (field?.SelectedItems is { Count: > 0 } selectedItems)
            return selectedItems.ToList();

        return IsExplicitSelection(field?.SelectedItem) ? [field!.SelectedItem!] : [];
    }

    private static string FormatSelectedItemCount(IReadOnlyList<string> selectedItems, int allItemCount)
    {
        var count = selectedItems.Count(IsExplicitSelection);
        return count == 1 ? "1 selected" : $"{Math.Min(count, allItemCount)} selected";
    }

    private static string JoinPreview(IReadOnlyList<string> values)
    {
        var preview = values.Take(3).Select(Quote).ToList();
        if (values.Count > preview.Count)
            preview.Add("...");

        return string.Join(", ", preview);
    }

    private static bool IsExplicitSelection(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !string.Equals(value, "(All)", StringComparison.OrdinalIgnoreCase);

    private static string DataFieldName(IReadOnlyList<PivotDataFieldModel> dataFields, int dataFieldIndex) =>
        dataFieldIndex >= 0 && dataFieldIndex < dataFields.Count
            ? dataFields[dataFieldIndex].Name
            : "Values";

    private static string Quote(string value) => $"\"{value}\"";

    private static string FormatNumber(double? value) =>
        (value ?? 0).ToString("0.########", CultureInfo.CurrentCulture);
}
