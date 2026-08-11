using System.Globalization;
using Free.Shared.Localization;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

public sealed record PivotFieldFilterState(
    string FieldCaption,
    int SourceFieldIndex,
    IReadOnlyList<string> AllItems,
    IReadOnlyList<string> SelectedItems,
    PivotLabelFilterModel? LabelFilter,
    PivotValueFilterModel? ValueFilter,
    IReadOnlyList<PivotDataFieldModel> DataFields,
    bool HasStoredItemSelection,
    string ItemSummary,
    string LabelSummary,
    string ValueSummary)
{
    public PivotFieldFilterState(
        string fieldCaption,
        int sourceFieldIndex,
        IReadOnlyList<string> allItems,
        IReadOnlyList<string> selectedItems,
        PivotLabelFilterModel? labelFilter,
        PivotValueFilterModel? valueFilter,
        IReadOnlyList<PivotDataFieldModel> dataFields)
        : this(
            fieldCaption,
            sourceFieldIndex,
            allItems,
            selectedItems,
            labelFilter,
            valueFilter,
            dataFields,
            selectedItems.Count > 0,
            PivotFieldFilterSummary.FormatItemFilterSummary(selectedItems, allItems.Count, PivotFieldFilterSummary.InvariantText),
            PivotFieldFilterSummary.FormatLabelFilterSummary(labelFilter, PivotFieldFilterSummary.InvariantText),
            PivotFieldFilterSummary.FormatValueFilterSummary(valueFilter, dataFields, PivotFieldFilterSummary.InvariantText))
    {
    }

    public bool HasItemFilter => PivotFieldFilterSummary.HasExplicitItemSelection(SelectedItems, AllItems.Count);
    public bool HasLabelFilter => LabelFilter is not null;
    public bool HasValueFilter => ValueFilter is not null;
    public bool HasAnyFilter => HasItemFilter || HasLabelFilter || HasValueFilter;
    public bool HasStoredFilter => HasStoredItemSelection || HasLabelFilter || HasValueFilter;
    public string OverallSummary => PivotFieldFilterSummary.FormatOverallSummary(this);
}

/// <summary>
/// Builds framework-neutral Pivot field-filter state and display summaries.
/// </summary>
public static class PivotFieldFilterSummary
{
    internal static readonly ResourceKeyTextResolver InvariantText = new(
        key => key switch
        {
            "PivotFieldFilter_NoItemFilter" => "No item filter",
            "PivotFieldFilter_NoLabelFilter" => "No label filter",
            "PivotFieldFilter_NoValueFilter" => "No value filter",
            _ => key
        },
        (key, _) => key);

    public static PivotFieldFilterState CreateState(
        PivotTableModel pivotTable,
        int sourceFieldIndex,
        PivotHeaderArea area,
        string fieldCaption,
        IReadOnlyList<string> allItems,
        ResourceKeyTextResolver text)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        ArgumentNullException.ThrowIfNull(allItems);
        ArgumentNullException.ThrowIfNull(text);

        var selectionState = PivotUiPlanner.CreateFieldSelectionState(pivotTable, area, sourceFieldIndex);
        var selectedItems = selectionState.SelectedItems;
        var labelFilter = FindLabelFilter(pivotTable, sourceFieldIndex);
        var valueFilter = FindValueFilter(pivotTable, sourceFieldIndex);
        var dataFields = pivotTable.DataFields.ToList();

        return new PivotFieldFilterState(
            fieldCaption,
            sourceFieldIndex,
            allItems,
            selectedItems,
            labelFilter,
            valueFilter,
            dataFields,
            selectionState.HasStoredSelection,
            FormatItemFilterSummary(selectedItems, allItems.Count, text),
            FormatLabelFilterSummary(labelFilter, text),
            FormatValueFilterSummary(valueFilter, dataFields, text));
    }

    public static PivotLabelFilterModel? FindLabelFilter(PivotTableModel pivotTable, int sourceFieldIndex) =>
        pivotTable.LabelFilters.LastOrDefault(filter => filter.SourceFieldIndex == sourceFieldIndex);

    public static PivotValueFilterModel? FindValueFilter(PivotTableModel pivotTable, int sourceFieldIndex) =>
        pivotTable.ValueFilters.LastOrDefault(filter =>
            PivotFilterOwnership.BelongsToSourceField(filter, sourceFieldIndex));

    public static bool HasExplicitItemSelection(IReadOnlyList<string> selectedItems, int allItemCount)
    {
        var explicitCount = selectedItems.Count(IsExplicitSelection);
        return explicitCount > 0 && explicitCount < allItemCount;
    }

    public static string FormatItemFilterSummary(
        IReadOnlyList<string> selectedItems,
        int allItemCount,
        ResourceKeyTextResolver text)
    {
        var explicitItems = selectedItems.Where(IsExplicitSelection).ToList();
        if (explicitItems.Count == 0 || explicitItems.Count >= allItemCount)
            return text.Get("PivotFieldFilter_NoItemFilter");

        return explicitItems.Count == 1
            ? $"Item filter: {Quote(explicitItems[0])}"
            : $"Item filter: {explicitItems.Count} items ({JoinPreview(explicitItems)})";
    }

    public static string FormatLabelFilterSummary(
        PivotLabelFilterModel? filter,
        ResourceKeyTextResolver text) =>
        filter is null
            ? text.Get("PivotFieldFilter_NoLabelFilter")
            : $"Label filter: {FormatLabelFilter(filter)}";

    public static string FormatValueFilterSummary(
        PivotValueFilterModel? filter,
        IReadOnlyList<PivotDataFieldModel> dataFields,
        ResourceKeyTextResolver text) =>
        filter is null
            ? text.Get("PivotFieldFilter_NoValueFilter")
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

    public static string FormatSortSummary(
        PivotSortModel? sort,
        IReadOnlyList<PivotDataFieldModel> dataFields)
    {
        if (sort is null)
            return "No sort";

        var direction = sort.Direction == PivotSortDirection.Descending ? "descending" : "ascending";
        return sort.Target == PivotSortTarget.Value
            ? $"Sorted {direction} by {DataFieldName(dataFields, sort.DataFieldIndex)}"
            : $"Sorted {direction} by labels";
    }

    public static string FormatValueFilter(
        PivotValueFilterModel filter,
        IReadOnlyList<PivotDataFieldModel> dataFields)
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

    private static string FormatSelectedItemCount(
        IReadOnlyList<string> selectedItems,
        int allItemCount)
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

    internal static bool IsExplicitSelection(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !string.Equals(value, "(All)", StringComparison.OrdinalIgnoreCase);

    private static string DataFieldName(
        IReadOnlyList<PivotDataFieldModel> dataFields,
        int dataFieldIndex) =>
        dataFieldIndex >= 0 && dataFieldIndex < dataFields.Count
            ? dataFields[dataFieldIndex].Name
            : "Values";

    private static string Quote(string value) => $"\"{value}\"";

    private static string FormatNumber(double? value) =>
        (value ?? 0).ToString("0.########", CultureInfo.CurrentCulture);
}
