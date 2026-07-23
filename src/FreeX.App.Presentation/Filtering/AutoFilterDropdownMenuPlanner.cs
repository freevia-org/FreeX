using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Filtering;

public static class AutoFilterDropdownMenuPlanner
{
    public static bool TryGetAutoFilterRange(Sheet sheet, out GridRange range) =>
        AutoFilterRangeResolver.TryGetEffectiveAutoFilterRange(sheet, out range);

    public static bool TryPlan(GridRange currentRegion, CellAddress activeCell, out AutoFilterDropdownPlan plan)
    {
        plan = default!;
        if (activeCell.Sheet != currentRegion.Start.Sheet ||
            activeCell.Row != currentRegion.Start.Row ||
            activeCell.Col < currentRegion.Start.Col ||
            activeCell.Col > currentRegion.End.Col)
        {
            return false;
        }

        plan = new AutoFilterDropdownPlan(currentRegion, activeCell.Col - currentRegion.Start.Col);
        return true;
    }

    public static IReadOnlyList<AutoFilterChecklistItem> CreateChecklistItems(
        Sheet sheet,
        AutoFilterDropdownPlan plan,
        string blankDisplayText) =>
        AutoFilterChecklistPlanner.CreateItems(null, sheet, plan, blankDisplayText);

    public static IReadOnlyList<AutoFilterChecklistItem> CreateChecklistItems(
        Workbook? workbook,
        Sheet sheet,
        AutoFilterDropdownPlan plan,
        string blankDisplayText) =>
        AutoFilterChecklistPlanner.CreateItems(workbook, sheet, plan, blankDisplayText);

    public static AutoFilterMenuPlan CreateMenuPlan(
        Sheet sheet,
        AutoFilterDropdownPlan plan,
        IAutoFilterMenuTextProvider textProvider,
        string blankDisplayText) =>
        CreateMenuPlan(null, sheet, plan, textProvider, blankDisplayText);

    public static AutoFilterMenuPlan CreateMenuPlan(
        Workbook? workbook,
        Sheet sheet,
        AutoFilterDropdownPlan plan,
        IAutoFilterMenuTextProvider textProvider,
        string blankDisplayText)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(textProvider);
        ArgumentNullException.ThrowIfNull(blankDisplayText);

        var headerText = SpreadsheetDisplayFormatter.FormatCellValue(
            sheet.GetValue(plan.Range.Start.Row, plan.Range.Start.Col + plan.FilterColumnOffset));
        if (string.IsNullOrWhiteSpace(headerText))
            headerText = textProvider.Format("AutoFilter_ColumnHeader", CellAddress.NumberToColumnName(plan.Range.Start.Col + plan.FilterColumnOffset));

        var filterKind = DetectFilterKind(sheet, plan);
        var sortLabels = GetSortLabels(filterKind, textProvider);
        var filterEntry = AutoFilterMenuCatalog.CreateFilterFamilyEntry(filterKind, textProvider);
        var colorOptions = CollectColorOptions(workbook, sheet, plan, textProvider);

        var hasActiveFilter = HasActiveFilter(sheet, plan.Range);
        var entries = new List<AutoFilterMenuEntry>
        {
            new(sortLabels.Ascending, AutoFilterMenuEntryKind.SortAscending),
            new(sortLabels.Descending, AutoFilterMenuEntryKind.SortDescending),
        };
        // R76-render-autofilter-dropdown-4-2: Excel offers "Sort by Color" right alongside the
        // A-Z/Z-A sort entries whenever the column has fill/font colors to sort by -- it sits next
        // to the sort entries, not down in the filter section where "Filter by Color" lives.
        if (colorOptions.Count > 0)
            entries.Add(new AutoFilterMenuEntry(textProvider.Get("AutoFilter_SortByColor"), AutoFilterMenuEntryKind.SortByColor));
        entries.Add(CreateSeparator());
        entries.Add(new(textProvider.Format("AutoFilter_ClearFilterFrom", headerText), AutoFilterMenuEntryKind.ClearFilter, isEnabled: hasActiveFilter));
        if (colorOptions.Count > 0)
            entries.Add(new AutoFilterMenuEntry(textProvider.Get("AutoFilter_FilterByColor"), AutoFilterMenuEntryKind.FilterByColor));
        entries.Add(filterEntry);
        entries.Add(CreateSeparator());
        entries.Add(new AutoFilterMenuEntry(textProvider.Get("AutoFilter_Search"), AutoFilterMenuEntryKind.Search));
        var checklistEntries = CreateChecklistEntries(workbook, sheet, plan, blankDisplayText);
        entries.Add(new AutoFilterMenuEntry(
            textProvider.Get("AutoFilter_SelectAll"),
            AutoFilterMenuEntryKind.SelectAll,
            isChecked: ComputeSelectAllState(checklistEntries)));
        entries.Add(CreateSeparator());

        entries.AddRange(checklistEntries);

        return new AutoFilterMenuPlan(
            headerText,
            filterKind,
            entries,
            colorOptions,
            AutoFilterMenuCatalog.CreateSections(entries, textProvider));
    }

    private static AutoFilterMenuEntry CreateSeparator() =>
        new(string.Empty, AutoFilterMenuEntryKind.Separator, isEnabled: false);

    private static (string Ascending, string Descending) GetSortLabels(
        AutoFilterMenuFilterKind filterKind,
        IAutoFilterMenuTextProvider textProvider) =>
        filterKind switch
        {
            AutoFilterMenuFilterKind.Number => (
                textProvider.Get("AutoFilter_SortSmallestToLargest"),
                textProvider.Get("AutoFilter_SortLargestToSmallest")),
            AutoFilterMenuFilterKind.Date => (
                textProvider.Get("AutoFilter_SortOldestToNewest"),
                textProvider.Get("AutoFilter_SortNewestToOldest")),
            _ => (
                textProvider.Get("AutoFilter_SortAToZ"),
                textProvider.Get("AutoFilter_SortZToA"))
        };

    private static IReadOnlyList<AutoFilterMenuEntry> CreateChecklistEntries(
        Workbook? workbook,
        Sheet sheet,
        AutoFilterDropdownPlan plan,
        string blankDisplayText)
    {
        var items = CreateChecklistItems(workbook, sheet, plan, blankDisplayText);
        if (items.Count == 0)
            return items.Select(item => new AutoFilterMenuEntry(item)).ToList();

        var filterColumn = plan.Range.Start.Col + plan.FilterColumnOffset;

        // R45-commands-autofilter-topbottom-3-1: reopening a column's dropdown must reflect THIS
        // column's own persisted filter selection, never rows an unrelated column's filter happens
        // to be hiding. Excel ANDs criteria across columns via sheet.FilterHiddenRows (the recomputed
        // union), but each column's own checklist stays scoped to its own mechanism -- see
        // sheet.ActiveValueFilterColumns / sheet.ColumnFilterOwnedRows for the per-column state.
        if (sheet.ActiveValueFilterColumns.TryGetValue(filterColumn, out var allowedValues))
        {
            var allowedSet = new HashSet<string>(allowedValues, StringComparer.Ordinal);
            return items
                .Select(item => new AutoFilterMenuEntry(item with { IsChecked = allowedSet.Contains(item.Value) }))
                .ToList();
        }

        if (sheet.ColumnFilterOwnedRows.TryGetValue(filterColumn, out var ownedHiddenRows) && ownedHiddenRows.Count > 0)
        {
            var visibleValues = CollectValuesNotOwnedHidden(sheet, plan, filterColumn, ownedHiddenRows);
            return items
                .Select(item => new AutoFilterMenuEntry(item with { IsChecked = visibleValues.Contains(item.Value) }))
                .ToList();
        }

        // No filter mechanism is owned by this column: every value stays checked, regardless of
        // whether some OTHER column's filter is currently hiding rows in the range.
        return items.Select(item => new AutoFilterMenuEntry(item with { IsChecked = true })).ToList();
    }

    private static HashSet<string> CollectValuesNotOwnedHidden(
        Sheet sheet,
        AutoFilterDropdownPlan plan,
        uint filterColumn,
        HashSet<uint> ownedHiddenRows)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        for (var row = plan.Range.Start.Row + 1; row <= plan.Range.End.Row; row++)
        {
            if (ownedHiddenRows.Contains(row))
                continue;

            values.Add(AutoFilterChecklistPlanner.ToFilterText(sheet.GetValue(row, filterColumn)));
        }

        return values;
    }

    private static bool? ComputeSelectAllState(IReadOnlyList<AutoFilterMenuEntry> checklistEntries)
    {
        if (checklistEntries.Count == 0)
            return false;

        var checkedCount = checklistEntries.Count(entry => entry.IsChecked == true);
        return checkedCount == checklistEntries.Count
            ? true
            : checkedCount == 0
                ? false
                : null;
    }

    private static IReadOnlyList<AutoFilterColorOption> CollectColorOptions(
        Workbook? workbook,
        Sheet sheet,
        AutoFilterDropdownPlan plan,
        IAutoFilterMenuTextProvider textProvider)
    {
        if (workbook is null)
            return [];

        var filterColumn = plan.Range.Start.Col + plan.FilterColumnOffset;
        var fillColors = new List<CellColor>();
        var fontColors = new List<CellColor>();
        var seenFillColors = new HashSet<CellColor>();
        var seenFontColors = new HashSet<CellColor>();
        var hasNoFill = false;

        for (var row = plan.Range.Start.Row + 1; row <= plan.Range.End.Row; row++)
        {
            // filter-by-color-cf: offer the color Excel would actually display for this cell —
            // including any conditional-formatting-driven fill/font color — not just the cell's
            // static stored style, so a CF-red cell shows up under its CF color rather than under
            // "No Fill".
            var cell = sheet.GetCell(row, filterColumn);
            var address = new CellAddress(sheet.Id, row, filterColumn);
            var fillColor = SortCommand.GetEffectiveColor(workbook, sheet, address, cell, wantFill: true);
            if (fillColor is { } fill)
            {
                if (seenFillColors.Add(fill))
                    fillColors.Add(fill);
            }
            else
            {
                hasNoFill = true;
            }

            var fontColor = SortCommand.GetEffectiveColor(workbook, sheet, address, cell, wantFill: false) ?? CellColor.Black;
            if (!fontColor.IsBlack && seenFontColors.Add(fontColor))
                fontColors.Add(fontColor);
        }

        var options = new List<AutoFilterColorOption>();
        options.AddRange(fillColors.Select(color =>
            new AutoFilterColorOption(FormatHexColor(color), AutoFilterColorFilterKind.CellFillColor, color)));
        if (hasNoFill && fillColors.Count > 0)
            options.Add(new AutoFilterColorOption(textProvider.Get("AutoFilter_NoFill"), AutoFilterColorFilterKind.NoFill, null));
        options.AddRange(fontColors.Select(color =>
            new AutoFilterColorOption(FormatHexColor(color), AutoFilterColorFilterKind.FontColor, color)));
        return options;
    }

    /// <summary>
    /// Builds the <see cref="SortCommand"/> a shell runs when the user picks one of the
    /// <see cref="AutoFilterMenuPlan.ColorOptions"/> swatches under the Sort-by-Color entry: rows
    /// whose effective cell/font color matches <paramref name="option"/> move to the top (mirroring
    /// Excel's "Sort by Color"), using <see cref="SortOn.CellColor"/>/<see cref="SortOn.FontColor"/>
    /// with that color as the <see cref="SortKey.TargetColor"/>. "No Fill" has no single target
    /// color to sort toward, so it is not offered as a Sort-by-Color choice (only as Filter by
    /// Color) -- callers should only invoke this for options with a non-null <see cref="AutoFilterColorOption.Color"/>.
    /// </summary>
    public static SortCommand CreateSortByColorCommand(
        SheetId sheetId,
        GridRange range,
        uint columnOffset,
        AutoFilterColorOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (option.Color is not { } color)
            throw new ArgumentException("Sort by Color requires a specific color, not No Fill.", nameof(option));

        var sortOn = option.Kind == AutoFilterColorFilterKind.FontColor ? SortOn.FontColor : SortOn.CellColor;
        return new SortCommand(sheetId, range, [new SortKey(columnOffset, Ascending: true, sortOn, color)]);
    }

    public static bool HasActiveFilter(Sheet sheet, GridRange range)
    {
        var firstDataRow = range.Start.Row + 1;
        var lastDataRow = range.End.Row;
        if (sheet.FilterHiddenRows.Count == 0 || firstDataRow > lastDataRow)
            return false;

        if ((uint)sheet.FilterHiddenRows.Count < range.RowCount)
        {
            foreach (var row in sheet.FilterHiddenRows)
            {
                if (row >= firstDataRow && row <= lastDataRow)
                    return true;
            }

            return false;
        }

        for (var row = firstDataRow; row <= lastDataRow; row++)
        {
            if (sheet.FilterHiddenRows.Contains(row))
                return true;
        }

        return false;
    }

    private static AutoFilterMenuFilterKind DetectFilterKind(Sheet sheet, AutoFilterDropdownPlan plan)
    {
        var filterColumn = plan.Range.Start.Col + plan.FilterColumnOffset;
        var hasTypedValues = false;
        var allNumbers = true;
        var allDates = true;

        for (var row = plan.Range.Start.Row + 1; row <= plan.Range.End.Row; row++)
        {
            var value = sheet.GetValue(row, filterColumn);
            if (value is BlankValue)
                continue;

            hasTypedValues = true;
            allNumbers &= value is NumberValue;
            allDates &= value is DateTimeValue;
        }

        if (hasTypedValues && allDates)
            return AutoFilterMenuFilterKind.Date;
        if (hasTypedValues && allNumbers)
            return AutoFilterMenuFilterKind.Number;
        return AutoFilterMenuFilterKind.Text;
    }

    private static string FormatHexColor(CellColor color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
