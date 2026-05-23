namespace Freexcel.App.Host;

public sealed partial class AutoFilterDialog
{
public static IReadOnlyList<AutoFilterDialogItem> FilterItems(
        IEnumerable<AutoFilterDialogItem> items,
        string? searchText)
    {
        var needle = searchText?.Trim();
        if (string.IsNullOrEmpty(needle))
            return items.ToList();

        return items
            .Where(item =>
                item.DisplayText.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                item.Value.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static IReadOnlyList<AutoFilterDialogItem> SelectAll(IEnumerable<AutoFilterDialogItem> items)
    {
        return items.Select(item => item with { IsSelected = true }).ToList();
    }

    public static IReadOnlyList<AutoFilterDialogItem> ClearAll(IEnumerable<AutoFilterDialogItem> items)
    {
        return items.Select(item => item with { IsSelected = false }).ToList();
    }

    public static IReadOnlyList<AutoFilterDialogItem> SetSelectionForSearch(
        IEnumerable<AutoFilterDialogItem> items,
        string? searchText,
        bool isSelected)
    {
        var visibleValues = FilterItems(items, searchText)
            .Select(item => item.Value)
            .ToHashSet(StringComparer.Ordinal);

        return items
            .Select(item => visibleValues.Contains(item.Value)
                ? item with { IsSelected = isSelected }
                : item)
            .ToList();
    }

    public static string GetFilterFamilyHeader(AutoFilterMenuFilterKind filterKind) =>
        filterKind switch
        {
            AutoFilterMenuFilterKind.Number => "Number Filters",
            AutoFilterMenuFilterKind.Date => "Date Filters",
            _ => "Text Filters"
        };

    public static AutoFilterDialogResult BuildResult(
        AutoFilterSortDirection sortDirection,
        IEnumerable<AutoFilterDialogItem> items,
        string? searchText,
        string? criteriaText,
        AutoFilterColorFilter? colorFilter = null,
        bool addCurrentSelectionToFilter = false)
    {
        var resultItems = GetResultItemsForSearchMode(items, searchText, addCurrentSelectionToFilter);
        var selectedValues = resultItems
            .Where(item => item.IsSelected)
            .Select(item => item.Value)
            .ToList();
        var normalizedCriteria = string.IsNullOrWhiteSpace(criteriaText)
            ? string.Join(", ", selectedValues)
            : criteriaText.Trim();

        return new AutoFilterDialogResult(
            sortDirection,
            selectedValues,
            searchText?.Trim() ?? string.Empty,
            normalizedCriteria,
            colorFilter);
    }

    public static IReadOnlyList<AutoFilterDialogItem> GetResultItemsForSearchMode(
        IEnumerable<AutoFilterDialogItem> items,
        string? searchText,
        bool addCurrentSelectionToFilter)
    {
        var allItems = items.ToList();
        return string.IsNullOrWhiteSpace(searchText) || addCurrentSelectionToFilter
            ? allItems
            : FilterItems(allItems, searchText);
    }

    public static IReadOnlyList<string> GetCriteriaSuggestions(AutoFilterMenuPlan menuPlan) =>
        menuPlan.Entries
            .FirstOrDefault(entry => entry.Kind == AutoFilterMenuEntryKind.FilterFamily)
            ?.CriteriaSuggestions
            .Where(suggestion => !string.IsNullOrWhiteSpace(suggestion))
            .ToList() ?? [];

    public static IReadOnlyList<AutoFilterCriteriaOption> GetCriteriaOptions(AutoFilterMenuFilterKind filterKind) =>
        filterKind switch
        {
            AutoFilterMenuFilterKind.Number =>
            [
                new("Equals", "="),
                new("Does Not Equal", "<>"),
                new("Greater Than", ">"),
                new("Greater Than Or Equal To", ">="),
                new("Less Than", "<"),
                new("Less Than Or Equal To", "<="),
                new("Between", "between:"),
                new("Top 10", "top:"),
                new("Bottom 10", "bottom:"),
                new("Top 10 Percent", "toppercent:"),
                new("Bottom 10 Percent", "bottompercent:"),
                new("Above Average", "above average", RequiresValue: false),
                new("Below Average", "below average", RequiresValue: false),
                new("Blanks", "blank", RequiresValue: false),
                new("Non-Blanks", "nonblank", RequiresValue: false)
            ],
            AutoFilterMenuFilterKind.Date =>
            [
                new("Equals", "date="),
                new("Does Not Equal", "date<>"),
                new("After", "date>"),
                new("On Or After", "date>="),
                new("Before", "date<"),
                new("On Or Before", "date<="),
                new("Between", "datebetween:"),
                new("Blanks", "blank", RequiresValue: false),
                new("Non-Blanks", "nonblank", RequiresValue: false)
            ],
            _ =>
            [
                new("Equals", "text="),
                new("Does Not Equal", "text<>"),
                new("Contains", "contains:"),
                new("Does Not Contain", "notcontains:"),
                new("Begins With", "begins:"),
                new("Ends With", "ends:"),
                new("Blanks", "blank", RequiresValue: false),
                new("Non-Blanks", "nonblank", RequiresValue: false)
            ]
        };

    public static string BuildCriteriaText(AutoFilterCriteriaOption option, string? value)
    {
        if (!option.RequiresValue)
            return option.CriteriaPrefix;

        return $"{option.CriteriaPrefix}{value?.Trim() ?? string.Empty}";
    }

    public static string BuildBetweenCriteriaText(AutoFilterCriteriaOption option, string? minimum, string? maximum)
    {
        return $"{option.CriteriaPrefix}{minimum?.Trim() ?? string.Empty}:{maximum?.Trim() ?? string.Empty}";
    }

    public static string BuildTopBottomCriteriaText(AutoFilterCriteriaOption option, string? count)
    {
        return $"{option.CriteriaPrefix}{count?.Trim() ?? string.Empty}";
    }

    public static string BuildDatePresetCriteriaText(string preset, DateTime today)
    {
        var date = today.Date;
        return preset switch
        {
            "Today" => $"date={date:yyyy-MM-dd}",
            "Yesterday" => $"date={date.AddDays(-1):yyyy-MM-dd}",
            "Tomorrow" => $"date={date.AddDays(1):yyyy-MM-dd}",
            "This Week" => BuildDateBetweenCriteria(StartOfWeek(date)),
            "Last Week" => BuildDateBetweenCriteria(StartOfWeek(date).AddDays(-7), days: 7),
            "Next Week" => BuildDateBetweenCriteria(StartOfWeek(date).AddDays(7), days: 7),
            "This Month" => BuildMonthCriteria(new DateTime(date.Year, date.Month, 1)),
            "Last Month" => BuildMonthCriteria(new DateTime(date.Year, date.Month, 1).AddMonths(-1)),
            "Next Month" => BuildMonthCriteria(new DateTime(date.Year, date.Month, 1).AddMonths(1)),
            "This Year" => BuildYearCriteria(date.Year),
            "Last Year" => BuildYearCriteria(date.Year - 1),
            "Next Year" => BuildYearCriteria(date.Year + 1),
            _ => string.Empty
        };
    }

    private static string BuildMonthCriteria(DateTime firstDayOfMonth)
    {
        var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);
        return BuildDateBetweenCriteria(firstDayOfMonth, lastDayOfMonth);
    }

    private static string BuildYearCriteria(int year) =>
        BuildDateBetweenCriteria(new DateTime(year, 1, 1), new DateTime(year, 12, 31));

    private static string BuildDateBetweenCriteria(DateTime firstDay, int days = 7) =>
        BuildDateBetweenCriteria(firstDay, firstDay.AddDays(days - 1));

    private static string BuildDateBetweenCriteria(DateTime firstDay, DateTime lastDay) =>
        $"datebetween:{firstDay:yyyy-MM-dd}:{lastDay:yyyy-MM-dd}";

    private static DateTime StartOfWeek(DateTime date) =>
        date.AddDays(-(int)date.DayOfWeek);

    public static string BuildCompositeCriteriaText(string? firstCriteria, string? connector, string? secondCriteria)
    {
        var first = firstCriteria?.Trim() ?? string.Empty;
        var second = secondCriteria?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(first))
            return second;

        if (string.IsNullOrWhiteSpace(second))
            return first;

        var prefix = string.Equals(connector, "Or", StringComparison.OrdinalIgnoreCase)
            ? "or"
            : "and";
        return $"{prefix}:{first}|{second}";
    }

    public static bool HasFilterByColorEntry(AutoFilterMenuPlan menuPlan) =>
        menuPlan.Entries.Any(entry => entry.Kind == AutoFilterMenuEntryKind.FilterByColor);
}
