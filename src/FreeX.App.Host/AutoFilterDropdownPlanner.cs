using FreeX.App.Presentation;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Commands;
using FreeX.App.Presentation.AutoFilter;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class AutoFilterDropdownPlanner
{
    public static string BlankDisplayText => UiText.Get("AutoFilter_BlankDisplayText");

    public static bool TryGetAutoFilterRange(Sheet sheet, out GridRange range) =>
        AutoFilterRangeResolver.TryGetAutoFilterRange(sheet, out range);

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

    public static IReadOnlyList<AutoFilterChecklistItem> CreateChecklistItems(Sheet sheet, AutoFilterDropdownPlan plan)
    {
        var filterColumn = plan.Range.Start.Col + plan.FilterColumnOffset;
        var seenValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<AutoFilterChecklistItem>();

        for (var row = plan.Range.Start.Row + 1; row <= plan.Range.End.Row; row++)
        {
            // Use the canonical filter text (the single source of truth FilterCommand matches against)
            // so the checklist Value the dropdown sends agrees exactly with what the filter applies.
            var value = FilterValueFormatter.ToText(sheet.GetValue(row, filterColumn));
            if (!seenValues.Add(value))
                continue;

            items.Add(new AutoFilterChecklistItem(
                string.IsNullOrEmpty(value) ? BlankDisplayText : value,
                value));
        }

        items.Sort(CompareChecklistItems);
        return items;
    }

    private static int CompareChecklistItems(AutoFilterChecklistItem left, AutoFilterChecklistItem right)
    {
        var leftKey = CreateChecklistSortKey(left.Value);
        var rightKey = CreateChecklistSortKey(right.Value);
        var rankComparison = leftKey.Rank.CompareTo(rightKey.Rank);
        if (rankComparison != 0)
            return rankComparison;

        var numericComparison = leftKey.Number.CompareTo(rightKey.Number);
        if (numericComparison != 0)
            return numericComparison;

        return string.Compare(left.DisplayText, right.DisplayText, StringComparison.CurrentCultureIgnoreCase);
    }

    private static AutoFilterChecklistSortKey CreateChecklistSortKey(string value)
    {
        if (string.IsNullOrEmpty(value))
            return new AutoFilterChecklistSortKey(5, 0);

        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var number) ||
            double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number))
        {
            return new AutoFilterChecklistSortKey(0, number);
        }

        if (DateTime.TryParse(value, System.Globalization.CultureInfo.CurrentCulture, out var date) ||
            DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out date))
        {
            return new AutoFilterChecklistSortKey(1, date.Ticks);
        }

        if (string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return new AutoFilterChecklistSortKey(3, string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        }

        return value.StartsWith('#')
            ? new AutoFilterChecklistSortKey(4, 0)
            : new AutoFilterChecklistSortKey(2, 0);
    }

    private readonly record struct AutoFilterChecklistSortKey(int Rank, double Number);

    public static AutoFilterMenuPlan CreateMenuPlan(Sheet sheet, AutoFilterDropdownPlan plan)
    {
        return CreateMenuPlan(null, sheet, plan);
    }

    public static AutoFilterMenuPlan CreateMenuPlan(Workbook? workbook, Sheet sheet, AutoFilterDropdownPlan plan)
    {
        var headerText = SpreadsheetDisplayFormatter.FormatCellValue(
            sheet.GetValue(plan.Range.Start.Row, plan.Range.Start.Col + plan.FilterColumnOffset));
        if (string.IsNullOrWhiteSpace(headerText))
            headerText = FormatBlankHeader(plan.Range.Start.Col + plan.FilterColumnOffset);

        var filterKind = DetectFilterKind(sheet, plan);
        var filterEntry = AutoFilterMenuCatalog.CreateFilterFamilyEntry(filterKind);
        var colorOptions = CollectColorOptions(workbook, sheet, plan);

        var hasActiveFilter = HasActiveFilter(sheet, plan.Range);
        var entries = new List<AutoFilterMenuEntry>
        {
            new(UiText.Get("AutoFilter_SortAscending"), AutoFilterMenuEntryKind.SortAscending),
            new(UiText.Get("AutoFilter_SortDescending"), AutoFilterMenuEntryKind.SortDescending),
            new(string.Empty, AutoFilterMenuEntryKind.Separator),
            new(UiText.Format("AutoFilter_ClearFilterFrom", headerText), AutoFilterMenuEntryKind.ClearFilter, isEnabled: hasActiveFilter)
        };
        if (colorOptions.Count > 0)
            entries.Add(new AutoFilterMenuEntry(UiText.Get("AutoFilter_FilterByColor"), AutoFilterMenuEntryKind.FilterByColor));
        entries.Add(filterEntry);
        entries.Add(new AutoFilterMenuEntry(string.Empty, AutoFilterMenuEntryKind.Separator));
        entries.Add(new AutoFilterMenuEntry(UiText.Get("AutoFilter_Search"), AutoFilterMenuEntryKind.Search));
        entries.Add(new AutoFilterMenuEntry(UiText.Get("AutoFilter_SelectAll"), AutoFilterMenuEntryKind.SelectAll));
        entries.Add(new AutoFilterMenuEntry(string.Empty, AutoFilterMenuEntryKind.Separator));

        entries.AddRange(CreateChecklistItems(sheet, plan)
            .Select(item => new AutoFilterMenuEntry(item)));

        return new AutoFilterMenuPlan(
            headerText,
            filterKind,
            entries,
            colorOptions,
            AutoFilterMenuCatalog.CreateSections(entries));
    }

    private static IReadOnlyList<AutoFilterColorOption> CollectColorOptions(
        Workbook? workbook,
        Sheet sheet,
        AutoFilterDropdownPlan plan)
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
            var style = GetCellStyle(workbook, sheet, row, filterColumn);
            if (style.FillColor is { } fillColor)
            {
                if (seenFillColors.Add(fillColor))
                    fillColors.Add(fillColor);
            }
            else
            {
                hasNoFill = true;
            }

            if (!style.FontColor.IsBlack && seenFontColors.Add(style.FontColor))
                fontColors.Add(style.FontColor);
        }

        var options = new List<AutoFilterColorOption>();
        options.AddRange(fillColors.Select(color =>
            new AutoFilterColorOption(ColorInputParser.FormatHexColor(color), AutoFilterColorFilterKind.CellFillColor, color)));
        if (hasNoFill && fillColors.Count > 0)
            options.Add(new AutoFilterColorOption(UiText.Get("AutoFilter_NoFill"), AutoFilterColorFilterKind.NoFill, null));
        options.AddRange(fontColors.Select(color =>
            new AutoFilterColorOption(ColorInputParser.FormatHexColor(color), AutoFilterColorFilterKind.FontColor, color)));
        return options;
    }

    private static CellStyle GetCellStyle(Workbook workbook, Sheet sheet, uint row, uint col)
    {
        var styleId = sheet.GetCell(row, col)?.StyleId ??
            sheet.GetStyleOnly(row, col) ??
            StyleId.Default;
        return workbook.GetStyle(styleId);
    }

    private static bool HasActiveFilter(Sheet sheet, GridRange range)
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

    private static string FormatBlankHeader(uint absoluteColumn) =>
        UiText.Format("AutoFilter_ColumnHeader", CellAddress.NumberToColumnName(absoluteColumn));
}
