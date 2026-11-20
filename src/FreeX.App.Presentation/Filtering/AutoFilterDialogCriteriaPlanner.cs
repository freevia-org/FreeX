using System.Globalization;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Filtering;

public sealed record AutoFilterSavedCondition(string CriteriaPrefix, string Value);

public sealed record AutoFilterSavedCriteria(
    AutoFilterSavedCondition? First,
    string Connector,
    AutoFilterSavedCondition? Second,
    string CriteriaText);

/// <summary>
/// Portable, UI-free planning for the AutoFilter dialog's value checklist and custom-criteria text: search
/// filtering and select-all/clear of the value list, building the dialog result from the current selection and
/// search mode, and composing the criteria strings the filter command parses (typed operators, Between,
/// Top/Bottom, the date-preset and composite And/Or rows). Pure decision/format logic single-sourced here so the
/// desktop host and the macOS port share identical behavior; the host keeps only the widget construction and
/// supplies the localized operator labels from the shared AutoFilter menu catalog.
/// </summary>
public static class AutoFilterDialogCriteriaPlanner
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

        var normalizedCriteria = criteriaText?.Trim() ?? string.Empty;

        return new AutoFilterDialogResult(
            sortDirection,
            selectedValues,
            searchText?.Trim() ?? string.Empty,
            normalizedCriteria,
            colorFilter);
    }

    /// <summary>
    /// Builds the result a Sort-by-Color swatch click commits: unlike <see cref="BuildResult"/>'s
    /// <c>colorFilter</c> (which filters the column down to that color), this leaves the checklist
    /// selection untouched and only carries <paramref name="colorFilter"/> in
    /// <see cref="AutoFilterDialogResult.SortByColorFilter"/> for the host to turn into a Sort.
    /// </summary>
    public static AutoFilterDialogResult BuildSortByColorResult(AutoFilterColorFilter colorFilter) =>
        new(
            AutoFilterSortDirection.None,
            [],
            string.Empty,
            string.Empty,
            ColorFilter: null,
            Action: AutoFilterDialogAction.Apply,
            SortByColorFilter: colorFilter);

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
            "Today" => $"date={FormatIsoDate(date)}",
            "Yesterday" => $"date={FormatIsoDate(date.AddDays(-1))}",
            "Tomorrow" => $"date={FormatIsoDate(date.AddDays(1))}",
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

    public static bool HasSortByColorEntry(AutoFilterMenuPlan menuPlan) =>
        menuPlan.Entries.Any(entry => entry.Kind == AutoFilterMenuEntryKind.SortByColor);

    public static bool IsBetweenOption(AutoFilterCriteriaOption option) =>
        AutoFilterMenuCatalog.IsBetweenCriteriaPrefix(option.CriteriaPrefix);

    public static bool IsTopBottomOption(AutoFilterCriteriaOption option) =>
        AutoFilterMenuCatalog.IsTopBottomCriteriaPrefix(option.CriteriaPrefix);

    public static bool IsAverageOption(AutoFilterCriteriaOption option) =>
        string.Equals(option.CriteriaPrefix, "above average", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(option.CriteriaPrefix, "below average", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Excel's Custom AutoFilter dialog never offers Between, Top 10 (count or percent), or Above/Below
    /// Average as a per-row operator combinable with a second And/Or criterion -- those are always
    /// separate, single-shot filter actions with their own dedicated inputs (min/max boxes, an item
    /// count, or no input at all). The dialog's row-2 operator dropdown otherwise reuses row 1's full
    /// per-family criteria list, but row 2 has only a single plain value textbox: offering Between/Top-N
    /// there lets a user "choose" a second criterion the UI can never actually collect (no min/max or
    /// count controls exist for row 2), so it silently drops out of the composed criteria text, and
    /// offering Above/Below Average there builds a composite "and:.../or:..." string that the downstream
    /// FilterCriterionInputParser rejects outright. Excluding them keeps row 2 to operators it can
    /// genuinely combine with row 1.
    /// </summary>
    public static IReadOnlyList<AutoFilterCriteriaOption> GetSecondRowCriteriaOptions(
        IReadOnlyList<AutoFilterCriteriaOption> criteriaOptions)
    {
        var filtered = new List<AutoFilterCriteriaOption>(criteriaOptions.Count);
        foreach (var option in criteriaOptions)
        {
            if (IsBetweenOption(option) || IsTopBottomOption(option) || IsAverageOption(option))
                continue;

            filtered.Add(option);
        }

        return filtered;
    }

    /// <summary>
    /// Reads the active column's custom conditions from worksheet or table metadata. A saved filter
    /// without an editable representation still returns a draft so an untouched dialog preserves it.
    /// </summary>
    public static AutoFilterSavedCriteria? GetSavedCriteria(
        Sheet sheet,
        AutoFilterDropdownPlan plan,
        AutoFilterMenuFilterKind filterKind)
    {
        if (AutoFilterRangeResolver.TryGetWorksheetAutoFilterRange(sheet, out var range) &&
            range == plan.Range)
        {
            var column = sheet.AutoFilter?.FilterColumns.FirstOrDefault(
                column => column.ColumnId == plan.FilterColumnOffset);
            return column is null
                ? null
                : ProjectSavedCriteria(column.CustomFilters, column.CustomFiltersAnd, filterKind);
        }

        var table = sheet.StructuredTables.FirstOrDefault(table => table.HasAutoFilter && table.Range == plan.Range);
        var tableColumn = table?.FilterColumns.FirstOrDefault(column => column.ColumnId == plan.FilterColumnOffset);
        return tableColumn is null
            ? null
            : ProjectSavedCriteria(
                tableColumn.CustomFilters.Select(filter =>
                    new WorksheetAutoFilterCustomFilterModel(filter.Operator, filter.Value)).ToArray(),
                tableColumn.CustomFiltersAnd,
                filterKind);
    }

    private static AutoFilterSavedCriteria? ProjectSavedCriteria(
        IReadOnlyList<WorksheetAutoFilterCustomFilterModel> filters,
        bool useAnd,
        AutoFilterMenuFilterKind filterKind)
    {
        if (filters.Count == 0)
            return null;

        var connector = useAnd ? "And" : "Or";
        var first = filters.Count <= 2 ? ProjectSavedCondition(filters[0], filterKind) : null;
        var second = filters.Count == 2 ? ProjectSavedCondition(filters[1], filterKind) : null;
        if (first is null ||
            (filters.Count == 2 && (second is null || first.Value.Contains('|') || second.Value.Contains('|'))))
            return new AutoFilterSavedCriteria(null, connector, null, string.Empty);

        var criteriaText = BuildCompositeCriteriaText(
            first.CriteriaPrefix + first.Value,
            connector,
            second is null ? null : second.CriteriaPrefix + second.Value);
        return new AutoFilterSavedCriteria(first, connector, second, criteriaText);
    }

    private static AutoFilterSavedCondition? ProjectSavedCondition(
        WorksheetAutoFilterCustomFilterModel filter,
        AutoFilterMenuFilterKind filterKind)
    {
        var comparison = filter.Operator switch
        {
            null or "equal" => "=",
            "notEqual" => "<>",
            "greaterThan" => ">",
            "greaterThanOrEqual" => ">=",
            "lessThan" => "<",
            "lessThanOrEqual" => "<=",
            _ => null
        };
        if (comparison is null || filter.Value is not { Length: > 0 } value)
            return null;

        var isNumber = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
            double.IsFinite(number);
        if (filterKind == AutoFilterMenuFilterKind.Date && isNumber)
        {
            if (!new DateTimeValue(number).TryToDateTime(out var date) ||
                DateTimeValue.FromDateTime(date.Date).Value != number)
                return null;

            return new AutoFilterSavedCondition("date" + comparison, FormatIsoDate(date));
        }

        if (isNumber && (filterKind == AutoFilterMenuFilterKind.Number || comparison is not ("=" or "<>")))
            return new AutoFilterSavedCondition(comparison, number.ToString(CultureInfo.CurrentCulture));

        // Equality with Excel wildcards is the exact editable form of the saved pattern.
        // Keep the wildcards and escapes, since the metadata has no original text operator.
        if (comparison is "=" or "<>" && value == value.Trim())
            return new AutoFilterSavedCondition("text" + comparison, value);

        return null;
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
        $"datebetween:{FormatIsoDate(firstDay)}:{FormatIsoDate(lastDay)}";

    private static string FormatIsoDate(DateTime date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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
