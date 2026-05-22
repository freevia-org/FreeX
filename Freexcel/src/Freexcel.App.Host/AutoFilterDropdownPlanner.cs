using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record AutoFilterDropdownPlan(GridRange Range, uint FilterColumnOffset);

public sealed record AutoFilterChecklistItem(string DisplayText, string Value);

public sealed record AutoFilterMenuPlan(
    string HeaderText,
    AutoFilterMenuFilterKind FilterKind,
    IReadOnlyList<AutoFilterMenuEntry> Entries);

public sealed record AutoFilterMenuEntry(
    string Header,
    AutoFilterMenuEntryKind Kind,
    IReadOnlyList<string> CriteriaSuggestions,
    string Value)
{
    public AutoFilterMenuEntry(string header, AutoFilterMenuEntryKind kind)
        : this(header, kind, [], header)
    {
    }

    public AutoFilterMenuEntry(string header, AutoFilterMenuEntryKind kind, IReadOnlyList<string> criteriaSuggestions)
        : this(header, kind, criteriaSuggestions, header)
    {
    }

    public AutoFilterMenuEntry(AutoFilterChecklistItem item)
        : this(item.DisplayText, AutoFilterMenuEntryKind.ChecklistItem, [], item.Value)
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
    Search,
    SelectAll,
    ChecklistItem
}

public static class AutoFilterDropdownPlanner
{
    public const string BlankDisplayText = "(Blanks)";

    private static readonly string[] TextFilterCriteria =
    [
        "equals:",
        "text<>",
        "contains:",
        "notcontains:",
        "begins:",
        "ends:",
        "blank",
        "nonblank"
    ];

    private static readonly string[] NumberFilterCriteria =
    [
        "=",
        "<>",
        ">",
        ">=",
        "<",
        "<=",
        "between:",
        "top:",
        "bottom:",
        "toppercent:",
        "bottompercent:",
        "above average",
        "below average",
        "blank",
        "nonblank"
    ];

    private static readonly string[] DateFilterCriteria =
    [
        "date=",
        "date<>",
        "date>",
        "date>=",
        "date<",
        "date<=",
        "datebetween:",
        "blank",
        "nonblank"
    ];

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
        var headerText = SpreadsheetDisplayFormatter.FormatCellValue(
            sheet.GetValue(plan.Range.Start.Row, plan.Range.Start.Col + plan.FilterColumnOffset));
        if (string.IsNullOrWhiteSpace(headerText))
            headerText = FormatColumnHeader(plan.FilterColumnOffset);

        var filterKind = DetectFilterKind(sheet, plan);
        var filterEntry = filterKind switch
        {
            AutoFilterMenuFilterKind.Number => new AutoFilterMenuEntry("Number Filters", AutoFilterMenuEntryKind.FilterFamily, NumberFilterCriteria),
            AutoFilterMenuFilterKind.Date => new AutoFilterMenuEntry("Date Filters", AutoFilterMenuEntryKind.FilterFamily, DateFilterCriteria),
            _ => new AutoFilterMenuEntry("Text Filters", AutoFilterMenuEntryKind.FilterFamily, TextFilterCriteria)
        };

        var entries = new List<AutoFilterMenuEntry>
        {
            new("Sort A to Z", AutoFilterMenuEntryKind.SortAscending),
            new("Sort Z to A", AutoFilterMenuEntryKind.SortDescending),
            new(string.Empty, AutoFilterMenuEntryKind.Separator),
            new($"Clear Filter From \"{headerText}\"", AutoFilterMenuEntryKind.ClearFilter),
            new("Filter by Color", AutoFilterMenuEntryKind.FilterByColor),
            filterEntry,
            new(string.Empty, AutoFilterMenuEntryKind.Separator),
            new("Search", AutoFilterMenuEntryKind.Search),
            new("(Select All)", AutoFilterMenuEntryKind.SelectAll),
            new(string.Empty, AutoFilterMenuEntryKind.Separator)
        };

        entries.AddRange(CreateChecklistItems(sheet, plan)
            .Select(item => new AutoFilterMenuEntry(item)));

        return new AutoFilterMenuPlan(headerText, filterKind, entries);
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

    private static string FormatColumnHeader(uint offset)
    {
        var index = offset + 1;
        var chars = new Stack<char>();
        while (index > 0)
        {
            index--;
            chars.Push((char)('A' + index % 26));
            index /= 26;
        }

        return new string(chars.ToArray());
    }
}
