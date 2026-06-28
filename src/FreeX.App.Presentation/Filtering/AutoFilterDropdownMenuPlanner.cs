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
        AutoFilterChecklistPlanner.CreateItems(sheet, plan, blankDisplayText);

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
        var filterEntry = AutoFilterMenuCatalog.CreateFilterFamilyEntry(filterKind, textProvider);
        var colorOptions = CollectColorOptions(workbook, sheet, plan, textProvider);

        var hasActiveFilter = HasActiveFilter(sheet, plan.Range);
        var entries = new List<AutoFilterMenuEntry>
        {
            new(textProvider.Get("AutoFilter_SortAscending"), AutoFilterMenuEntryKind.SortAscending),
            new(textProvider.Get("AutoFilter_SortDescending"), AutoFilterMenuEntryKind.SortDescending),
            new(string.Empty, AutoFilterMenuEntryKind.Separator),
            new(textProvider.Format("AutoFilter_ClearFilterFrom", headerText), AutoFilterMenuEntryKind.ClearFilter, isEnabled: hasActiveFilter)
        };
        if (colorOptions.Count > 0)
            entries.Add(new AutoFilterMenuEntry(textProvider.Get("AutoFilter_FilterByColor"), AutoFilterMenuEntryKind.FilterByColor));
        entries.Add(filterEntry);
        entries.Add(new AutoFilterMenuEntry(string.Empty, AutoFilterMenuEntryKind.Separator));
        entries.Add(new AutoFilterMenuEntry(textProvider.Get("AutoFilter_Search"), AutoFilterMenuEntryKind.Search));
        entries.Add(new AutoFilterMenuEntry(textProvider.Get("AutoFilter_SelectAll"), AutoFilterMenuEntryKind.SelectAll));
        entries.Add(new AutoFilterMenuEntry(string.Empty, AutoFilterMenuEntryKind.Separator));

        entries.AddRange(CreateChecklistItems(sheet, plan, blankDisplayText)
            .Select(item => new AutoFilterMenuEntry(item)));

        return new AutoFilterMenuPlan(
            headerText,
            filterKind,
            entries,
            colorOptions,
            AutoFilterMenuCatalog.CreateSections(entries, textProvider));
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
            new AutoFilterColorOption(FormatHexColor(color), AutoFilterColorFilterKind.CellFillColor, color)));
        if (hasNoFill && fillColors.Count > 0)
            options.Add(new AutoFilterColorOption(textProvider.Get("AutoFilter_NoFill"), AutoFilterColorFilterKind.NoFill, null));
        options.AddRange(fontColors.Select(color =>
            new AutoFilterColorOption(FormatHexColor(color), AutoFilterColorFilterKind.FontColor, color)));
        return options;
    }

    private static CellStyle GetCellStyle(Workbook workbook, Sheet sheet, uint row, uint col)
    {
        var styleId = sheet.GetCell(row, col)?.StyleId ??
            sheet.GetStyleOnly(row, col) ??
            StyleId.Default;
        return workbook.GetStyle(styleId);
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
