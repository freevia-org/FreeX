using FreeX.Core.Commands;

namespace FreeX.App.Host;

internal static class AutoFilterDialogCriteriaPlanner
{
    public static IReadOnlyList<AutoFilterDialogItem> FilterItems(
        IEnumerable<AutoFilterDialogItem> items,
        string? searchText)
    {
        var needle = searchText?.Trim();
        if (string.IsNullOrEmpty(needle))
            return MaterializeItems(items);

        var filtered = CreateItemList(items);
        foreach (var item in items)
        {
            if (MatchesSearch(item, needle))
                filtered.Add(item);
        }

        return filtered;
    }

    public static IReadOnlyList<AutoFilterDialogItem> SelectAll(IEnumerable<AutoFilterDialogItem> items)
    {
        var selected = CreateItemList(items);
        foreach (var item in items)
            selected.Add(item with { IsSelected = true });

        return selected;
    }

    public static IReadOnlyList<AutoFilterDialogItem> ClearAll(IEnumerable<AutoFilterDialogItem> items)
    {
        var cleared = CreateItemList(items);
        foreach (var item in items)
            cleared.Add(item with { IsSelected = false });

        return cleared;
    }

    public static IReadOnlyList<AutoFilterDialogItem> SetSelectionForSearch(
        IEnumerable<AutoFilterDialogItem> items,
        string? searchText,
        bool isSelected)
    {
        var allItems = MaterializeItems(items);
        var needle = searchText?.Trim();
        if (string.IsNullOrEmpty(needle))
            return SetAllSelections(allItems, isSelected);

        var visibleValues = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in allItems)
        {
            if (MatchesSearch(item, needle))
                visibleValues.Add(item.Value);
        }

        var updated = new List<AutoFilterDialogItem>(allItems.Count);
        foreach (var item in allItems)
        {
            updated.Add(visibleValues.Contains(item.Value)
                ? item with { IsSelected = isSelected }
                : item);
        }

        return updated;
    }

    public static string GetFilterFamilyHeader(AutoFilterMenuFilterKind filterKind) =>
        filterKind switch
        {
            AutoFilterMenuFilterKind.Number => UiText.Get("AutoFilter_FilterFamily_Number"),
            AutoFilterMenuFilterKind.Date => UiText.Get("AutoFilter_FilterFamily_Date"),
            _ => UiText.Get("AutoFilter_FilterFamily_Text")
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
        var selectedValues = new List<string>(resultItems.Count);
        foreach (var item in resultItems)
        {
            if (item.IsSelected)
                selectedValues.Add(item.Value);
        }

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

    public static AutoFilterDialogResult CreateClearFilterResult() =>
        new(
            AutoFilterSortDirection.None,
            [],
            string.Empty,
            string.Empty,
            null,
            AutoFilterDialogAction.ClearFilter);

    public static IReadOnlyList<AutoFilterDialogItem> GetResultItemsForSearchMode(
        IEnumerable<AutoFilterDialogItem> items,
        string? searchText,
        bool addCurrentSelectionToFilter)
    {
        return string.IsNullOrWhiteSpace(searchText) || addCurrentSelectionToFilter
            ? MaterializeItems(items)
            : FilterItems(items, searchText);
    }

    public static IReadOnlyList<string> GetCriteriaSuggestions(AutoFilterMenuPlan menuPlan)
    {
        foreach (var entry in menuPlan.Entries)
        {
            if (entry.Kind != AutoFilterMenuEntryKind.FilterFamily)
                continue;

            var suggestions = new List<string>(entry.CriteriaSuggestions.Count);
            foreach (var suggestion in entry.CriteriaSuggestions)
            {
                if (!string.IsNullOrWhiteSpace(suggestion))
                    suggestions.Add(suggestion);
            }

            return suggestions;
        }

        return [];
    }

    public static IReadOnlyList<AutoFilterCriteriaOption> GetCriteriaOptions(AutoFilterMenuFilterKind filterKind) =>
        filterKind switch
        {
            AutoFilterMenuFilterKind.Number =>
            [
                new(UiText.Get("AutoFilter_Criteria_Equals"), "="),
                new(UiText.Get("AutoFilter_Criteria_DoesNotEqual"), "<>"),
                new(UiText.Get("AutoFilter_Criteria_GreaterThan"), ">"),
                new(UiText.Get("AutoFilter_Criteria_GreaterThanOrEqualTo"), ">="),
                new(UiText.Get("AutoFilter_Criteria_LessThan"), "<"),
                new(UiText.Get("AutoFilter_Criteria_LessThanOrEqualTo"), "<="),
                new(UiText.Get("AutoFilter_Criteria_Between"), "between:"),
                new(UiText.Get("AutoFilter_Criteria_Top10"), "top:"),
                new(UiText.Get("AutoFilter_Criteria_Bottom10"), "bottom:"),
                new(UiText.Get("AutoFilter_Criteria_Top10Percent"), "toppercent:"),
                new(UiText.Get("AutoFilter_Criteria_Bottom10Percent"), "bottompercent:"),
                new(UiText.Get("AutoFilter_Criteria_AboveAverage"), "above average", RequiresValue: false),
                new(UiText.Get("AutoFilter_Criteria_BelowAverage"), "below average", RequiresValue: false),
                new(UiText.Get("AutoFilter_Criteria_Blanks"), "blank", RequiresValue: false),
                new(UiText.Get("AutoFilter_Criteria_NonBlanks"), "nonblank", RequiresValue: false)
            ],
            AutoFilterMenuFilterKind.Date =>
            [
                new(UiText.Get("AutoFilter_Criteria_Equals"), "date="),
                new(UiText.Get("AutoFilter_Criteria_DoesNotEqual"), "date<>"),
                new(UiText.Get("AutoFilter_Criteria_After"), "date>"),
                new(UiText.Get("AutoFilter_Criteria_OnOrAfter"), "date>="),
                new(UiText.Get("AutoFilter_Criteria_Before"), "date<"),
                new(UiText.Get("AutoFilter_Criteria_OnOrBefore"), "date<="),
                new(UiText.Get("AutoFilter_Criteria_Between"), "datebetween:"),
                new(UiText.Get("AutoFilter_Criteria_Blanks"), "blank", RequiresValue: false),
                new(UiText.Get("AutoFilter_Criteria_NonBlanks"), "nonblank", RequiresValue: false)
            ],
            _ =>
            [
                new(UiText.Get("AutoFilter_Criteria_Equals"), "text="),
                new(UiText.Get("AutoFilter_Criteria_DoesNotEqual"), "text<>"),
                new(UiText.Get("AutoFilter_Criteria_Contains"), "contains:"),
                new(UiText.Get("AutoFilter_Criteria_DoesNotContain"), "notcontains:"),
                new(UiText.Get("AutoFilter_Criteria_BeginsWith"), "begins:"),
                new(UiText.Get("AutoFilter_Criteria_EndsWith"), "ends:"),
                new(UiText.Get("AutoFilter_Criteria_Blanks"), "blank", RequiresValue: false),
                new(UiText.Get("AutoFilter_Criteria_NonBlanks"), "nonblank", RequiresValue: false)
            ]
        };

    public static string BuildCriteriaText(AutoFilterCriteriaOption option, string? value) =>
        !option.RequiresValue
            ? option.CriteriaPrefix
            : $"{option.CriteriaPrefix}{value?.Trim() ?? string.Empty}";

    public static string BuildBetweenCriteriaText(AutoFilterCriteriaOption option, string? minimum, string? maximum) =>
        $"{option.CriteriaPrefix}{minimum?.Trim() ?? string.Empty}:{maximum?.Trim() ?? string.Empty}";

    public static string BuildTopBottomCriteriaText(AutoFilterCriteriaOption option, string? count) =>
        $"{option.CriteriaPrefix}{count?.Trim() ?? string.Empty}";

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

    public static bool IsBetweenOption(AutoFilterCriteriaOption option) =>
        option.CriteriaPrefix.Equals("between:", StringComparison.OrdinalIgnoreCase) ||
        option.CriteriaPrefix.Equals("datebetween:", StringComparison.OrdinalIgnoreCase);

    public static bool IsTopBottomOption(AutoFilterCriteriaOption option) =>
        option.CriteriaPrefix.StartsWith("top:", StringComparison.OrdinalIgnoreCase) ||
        option.CriteriaPrefix.StartsWith("bottom:", StringComparison.OrdinalIgnoreCase) ||
        option.CriteriaPrefix.StartsWith("toppercent:", StringComparison.OrdinalIgnoreCase) ||
        option.CriteriaPrefix.StartsWith("bottompercent:", StringComparison.OrdinalIgnoreCase);

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

    private static List<AutoFilterDialogItem> MaterializeItems(IEnumerable<AutoFilterDialogItem> items)
    {
        var materialized = CreateItemList(items);
        foreach (var item in items)
            materialized.Add(item);

        return materialized;
    }

    private static List<AutoFilterDialogItem> CreateItemList(IEnumerable<AutoFilterDialogItem> items) =>
        items.TryGetNonEnumeratedCount(out var count)
            ? new List<AutoFilterDialogItem>(count)
            : [];

    private static IReadOnlyList<AutoFilterDialogItem> SetAllSelections(
        IReadOnlyList<AutoFilterDialogItem> items,
        bool isSelected)
    {
        var updated = new List<AutoFilterDialogItem>(items.Count);
        foreach (var item in items)
            updated.Add(item with { IsSelected = isSelected });

        return updated;
    }

    private static bool MatchesSearch(AutoFilterDialogItem item, string needle) =>
        item.DisplayText.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
        item.Value.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
