using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

/// <summary>
/// Builds portable header-dropdown targets and menus from a <see cref="PivotTableModel"/>. The "active"
/// classification (whether a field has an explicit item selection, a label/value filter, or a sort) is
/// ported from the header-target planning logic in the desktop hosts. Item generation per area
/// (sort, label/value filters, move, settings, remove) is the menu content the desktop header dropdowns
/// present. Unsupported expand/collapse entries are intentionally omitted because the WPF host does not
/// expose an equivalent command or persisted state.
/// </summary>
public static class PivotHeaderDropdownMenuBuilder
{
    /// <summary>
    /// Builds the dropdown targets for every drop-down-bearing field of a pivot (page, then row, then
    /// column). Skips fields whose <c>ShowDropDowns</c> is explicitly false or whose source index is out of
    /// range. Mirrors the ordering of the desktop header-target planner without computing cell coordinates.
    /// </summary>
    public static IReadOnlyList<PivotHeaderDropdownTargetModel> BuildTargets(
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        ArgumentNullException.ThrowIfNull(headers);

        var targets = new List<PivotHeaderDropdownTargetModel>();
        if (!pivotTable.ShowFieldHeaders)
            return targets;

        AddAreaTargets(pivotTable, headers, pivotTable.PageFields, PivotHeaderArea.Page, targets);
        AddAreaTargets(pivotTable, headers, pivotTable.RowFields, PivotHeaderArea.Row, targets);
        AddAreaTargets(pivotTable, headers, pivotTable.ColumnFields, PivotHeaderArea.Column, targets);

        return targets;
    }

    /// <summary>Builds the menu for a single header-dropdown target.</summary>
    public static PivotHeaderDropdownMenuModel BuildMenu(
        PivotTableModel pivotTable,
        PivotHeaderDropdownTargetModel target)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        ArgumentNullException.ThrowIfNull(target);

        var items = new List<PivotHeaderMenuItemModel>();
        AddSortItems(pivotTable, target, items);
        items.Add(PivotHeaderMenuItemModel.Separator);
        AddFilterItems(pivotTable, target, items);
        items.Add(PivotHeaderMenuItemModel.Separator);

        AddMoveItems(target, items);
        items.Add(PivotHeaderMenuItemModel.Separator);
        AddSettingsAndRemoveItems(target, items);

        return new PivotHeaderDropdownMenuModel(target, items);
    }

    private static void AddAreaTargets(
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyList<PivotFieldModel> fields,
        PivotHeaderArea area,
        List<PivotHeaderDropdownTargetModel> targets)
    {
        foreach (var field in fields)
        {
            if (field.ShowDropDowns == false ||
                field.SourceFieldIndex < 0 ||
                field.SourceFieldIndex >= headers.Count)
            {
                continue;
            }

            targets.Add(new PivotHeaderDropdownTargetModel(
                pivotTable.Name,
                PivotFieldListPaneBuilder.FieldCaption(headers, field.SourceFieldIndex),
                field.SourceFieldIndex,
                area,
                IsFieldActive(pivotTable, field)));
        }
    }

    private static void AddSortItems(
        PivotTableModel pivotTable,
        PivotHeaderDropdownTargetModel target,
        List<PivotHeaderMenuItemModel> items)
    {
        var sort = FindSort(pivotTable, target.SourceFieldIndex);
        var ascending = sort is { Direction: PivotSortDirection.Ascending };
        var descending = sort is { Direction: PivotSortDirection.Descending };

        items.Add(new PivotHeaderMenuItemModel(
            PivotHeaderMenuAction.SortAscending, "Sort A to Z", IsChecked: ascending));
        items.Add(new PivotHeaderMenuItemModel(
            PivotHeaderMenuAction.SortDescending, "Sort Z to A", IsChecked: descending));
        items.Add(new PivotHeaderMenuItemModel(
            PivotHeaderMenuAction.MoreSortOptions, "More Sort Options..."));
        items.Add(new PivotHeaderMenuItemModel(
            PivotHeaderMenuAction.ClearSort, "Clear Sort", IsEnabled: sort is not null));
    }

    private static void AddFilterItems(
        PivotTableModel pivotTable,
        PivotHeaderDropdownTargetModel target,
        List<PivotHeaderMenuItemModel> items)
    {
        var hasFilter = HasActiveFilter(pivotTable, target.SourceFieldIndex);
        items.Add(new PivotHeaderMenuItemModel(PivotHeaderMenuAction.LabelFilter, "Label Filters..."));
        items.Add(new PivotHeaderMenuItemModel(PivotHeaderMenuAction.ValueFilter, "Value Filters..."));
        items.Add(new PivotHeaderMenuItemModel(
            PivotHeaderMenuAction.ClearFilter, "Clear Filter", IsEnabled: hasFilter));
    }

    private static void AddMoveItems(
        PivotHeaderDropdownTargetModel target,
        List<PivotHeaderMenuItemModel> items)
    {
        items.Add(new PivotHeaderMenuItemModel(
            PivotHeaderMenuAction.MoveUp, "Move Up"));
        items.Add(new PivotHeaderMenuItemModel(
            PivotHeaderMenuAction.MoveDown, "Move Down"));
        items.Add(new PivotHeaderMenuItemModel(
            PivotHeaderMenuAction.MoveToRows, "Move to Rows",
            IsEnabled: target.Area != PivotHeaderArea.Row));
        items.Add(new PivotHeaderMenuItemModel(
            PivotHeaderMenuAction.MoveToColumns, "Move to Columns",
            IsEnabled: target.Area != PivotHeaderArea.Column));
        items.Add(new PivotHeaderMenuItemModel(
            PivotHeaderMenuAction.MoveToFilters, "Move to Filters",
            IsEnabled: target.Area != PivotHeaderArea.Page));
        items.Add(new PivotHeaderMenuItemModel(
            PivotHeaderMenuAction.MoveToValues, "Move to Values",
            IsEnabled: target.Area != PivotHeaderArea.Value));
    }

    private static void AddSettingsAndRemoveItems(
        PivotHeaderDropdownTargetModel target,
        List<PivotHeaderMenuItemModel> items)
    {
        if (target.Area == PivotHeaderArea.Value)
            items.Add(new PivotHeaderMenuItemModel(
                PivotHeaderMenuAction.ValueFieldSettings, "Value Field Settings..."));
        else
            items.Add(new PivotHeaderMenuItemModel(
                PivotHeaderMenuAction.FieldSettings, "Field Settings..."));

        items.Add(new PivotHeaderMenuItemModel(PivotHeaderMenuAction.RemoveField, "Remove Field"));
    }

    private static bool IsFieldActive(PivotTableModel pivotTable, PivotFieldModel field) =>
        HasExplicitSelection(field) || HasActiveFilter(pivotTable, field.SourceFieldIndex) ||
        FindSort(pivotTable, field.SourceFieldIndex) is not null;

    private static bool HasActiveFilter(PivotTableModel pivotTable, int sourceFieldIndex) =>
        pivotTable.LabelFilters.Any(filter => filter.SourceFieldIndex == sourceFieldIndex) ||
        pivotTable.ValueFilters.Any(filter => filter.SourceFieldIndex == sourceFieldIndex);

    private static PivotSortModel? FindSort(PivotTableModel pivotTable, int sourceFieldIndex) =>
        pivotTable.Sorts.FirstOrDefault(sort => sort.FieldIndex == sourceFieldIndex);

    private static bool HasExplicitSelection(PivotFieldModel field)
    {
        if (field.SelectedItems is { Count: > 0 } selectedItems)
            return selectedItems.Any(IsExplicitSelection);

        return IsExplicitSelection(field.SelectedItem);
    }

    private static bool IsExplicitSelection(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !string.Equals(value, "(All)", StringComparison.OrdinalIgnoreCase);
}
