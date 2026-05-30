using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record AutoFilterDropdownPlan(GridRange Range, uint FilterColumnOffset);

public sealed record AutoFilterChecklistItem(string DisplayText, string Value);

public sealed record AutoFilterMenuPlan(
    string HeaderText,
    AutoFilterMenuFilterKind FilterKind,
    IReadOnlyList<AutoFilterMenuEntry> Entries,
    IReadOnlyList<AutoFilterColorOption>? ColorOptions = null,
    IReadOnlyList<AutoFilterMenuSection>? Sections = null)
{
    public IReadOnlyList<AutoFilterMenuSection> Sections { get; init; } = Sections ?? [];
}

public sealed record AutoFilterMenuSection(
    AutoFilterMenuSectionKind Kind,
    string Label,
    IReadOnlyList<AutoFilterMenuEntry> Entries);

public enum AutoFilterMenuSectionKind
{
    Sort,
    FilterCommands,
    Search,
    Checklist
}

public enum AutoFilterColorFilterKind
{
    None,
    CellFillColor,
    NoFill,
    FontColor
}

public sealed record AutoFilterColorOption(
    string Label,
    AutoFilterColorFilterKind Kind,
    CellColor? Color);

public sealed record AutoFilterMenuEntry(
    string Header,
    AutoFilterMenuEntryKind Kind,
    IReadOnlyList<string> CriteriaSuggestions,
    string Value,
    IReadOnlyList<AutoFilterMenuEntry> Children)
{
    public AutoFilterMenuEntry(string header, AutoFilterMenuEntryKind kind)
        : this(header, kind, [], header, [])
    {
    }

    public AutoFilterMenuEntry(string header, AutoFilterMenuEntryKind kind, IReadOnlyList<string> criteriaSuggestions)
        : this(header, kind, criteriaSuggestions, header, [])
    {
    }

    public AutoFilterMenuEntry(
        string header,
        AutoFilterMenuEntryKind kind,
        IReadOnlyList<string> criteriaSuggestions,
        string value)
        : this(header, kind, criteriaSuggestions, value, [])
    {
    }

    public AutoFilterMenuEntry(AutoFilterChecklistItem item)
        : this(item.DisplayText, AutoFilterMenuEntryKind.ChecklistItem, [], item.Value, [])
    {
    }
}

public enum AutoFilterMenuFilterKind
{
    Text,
    Number,
    Date
}

public enum AutoFilterMenuEntryKind
{
    SortAscending,
    SortDescending,
    Separator,
    ClearFilter,
    FilterByColor,
    FilterFamily,
    FilterFamilyCommand,
    Search,
    SelectAll,
    ChecklistItem
}

public static class AutoFilterDropdownPlanner
{
    public static string BlankDisplayText => UiText.Get("AutoFilter_BlankDisplayText");

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
            var value = SpreadsheetDisplayFormatter.FormatCellValue(sheet.GetValue(row, filterColumn));
            if (!seenValues.Add(value))
                continue;

            items.Add(new AutoFilterChecklistItem(
                string.IsNullOrEmpty(value) ? BlankDisplayText : value,
                value));
        }

        return items;
    }

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

        var entries = new List<AutoFilterMenuEntry>
        {
            new(UiText.Get("AutoFilter_SortAscending"), AutoFilterMenuEntryKind.SortAscending),
            new(UiText.Get("AutoFilter_SortDescending"), AutoFilterMenuEntryKind.SortDescending),
            new(string.Empty, AutoFilterMenuEntryKind.Separator),
            new(UiText.Format("AutoFilter_ClearFilterFrom", headerText), AutoFilterMenuEntryKind.ClearFilter),
            new(UiText.Get("AutoFilter_FilterByColor"), AutoFilterMenuEntryKind.FilterByColor),
            filterEntry,
            new(string.Empty, AutoFilterMenuEntryKind.Separator),
            new(UiText.Get("AutoFilter_Search"), AutoFilterMenuEntryKind.Search),
            new(UiText.Get("AutoFilter_SelectAll"), AutoFilterMenuEntryKind.SelectAll),
            new(string.Empty, AutoFilterMenuEntryKind.Separator)
        };

        entries.AddRange(CreateChecklistItems(sheet, plan)
            .Select(item => new AutoFilterMenuEntry(item)));

        return new AutoFilterMenuPlan(
            headerText,
            filterKind,
            entries,
            CollectColorOptions(workbook, sheet, plan),
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
