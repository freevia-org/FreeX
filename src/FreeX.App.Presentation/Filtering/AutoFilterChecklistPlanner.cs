using System.Globalization;

using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Filtering;

public static class AutoFilterChecklistPlanner
{
    public static string ToFilterText(ScalarValue value) => FilterValueFormatter.ToText(value);

    public static IReadOnlyList<string> DistinctColumnValues(Sheet sheet, GridRange range, uint columnOffset)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var col = range.Start.Col + columnOffset;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var values = new List<string>();

        // R104-app-presentation-autofilter-totalsrow-1: when range is a structured table's raw
        // Range and its Totals Row is shown, range.End.Row IS the Totals Row -- exclude it from the
        // distinct-values scan the same way the interactive filter-apply commands already do.
        var lastRow = AutoFilterRangeResolver.GetFilterableLastRow(sheet, range);
        for (var row = range.Start.Row + 1; row <= lastRow; row++)
        {
            var text = ToFilterText(sheet.GetValue(row, col));
            if (seen.Add(text))
                values.Add(text);
        }

        return values;
    }

    public static IReadOnlyList<AutoFilterChecklistItem> CreateItems(
        Sheet sheet,
        AutoFilterDropdownPlan plan,
        string blankDisplayText) =>
        CreateItems(null, sheet, plan.Range, plan.FilterColumnOffset, blankDisplayText);

    /// <summary>
    /// Workbook-aware overload (finding R76-render-autofilter-dropdown-4-3): the checklist's
    /// <see cref="AutoFilterChecklistItem.DisplayText"/> is rendered through the same
    /// <see cref="NumberFormatter"/> the grid uses for each row's own cell number format (e.g. a
    /// Currency column shows "$1,500.00", a Date column shows the cell's own date format), while
    /// <see cref="AutoFilterChecklistItem.Value"/> stays the raw invariant filter-match text so
    /// selecting/matching rows is unaffected.
    /// </summary>
    public static IReadOnlyList<AutoFilterChecklistItem> CreateItems(
        Workbook? workbook,
        Sheet sheet,
        AutoFilterDropdownPlan plan,
        string blankDisplayText) =>
        CreateItems(workbook, sheet, plan.Range, plan.FilterColumnOffset, blankDisplayText);

    public static IReadOnlyList<AutoFilterChecklistItem> CreateItems(
        Sheet sheet,
        GridRange range,
        uint columnOffset,
        string blankDisplayText) =>
        CreateItems(null, sheet, range, columnOffset, blankDisplayText);

    public static IReadOnlyList<AutoFilterChecklistItem> CreateItems(
        Workbook? workbook,
        Sheet sheet,
        GridRange range,
        uint columnOffset,
        string blankDisplayText)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var col = range.Start.Col + columnOffset;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<AutoFilterChecklistItem>();

        // R103-app-presentation-autofilter-1-1: a formula-computed date (DATE()/EDATE()/date
        // arithmetic such as `=PrevDate+7`) round-trips through the formula engine as a plain
        // NumberValue, not DateTimeValue -- CreateSortKey's text-based Rank-0 (numeric)/Rank-1
        // (date) split would then bucket it ahead of any literally-typed date in the SAME column
        // regardless of chronological order, since a raw invariant number string ("45292") parses
        // as Rank 0 while a literal "yyyy-MM-dd" string parses as Rank 1. When the cell's resolved
        // number format says the column is a date column, record each such value's actual date (as
        // OADate ticks, matching the units the literal-date branch below already sorts by) here so
        // the comparer puts it in the same date-ordered bucket as literally-typed dates.
        var dateSortOverrides = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        // R104-app-presentation-autofilter-totalsrow-1: when range is a structured table's raw
        // Range and its Totals Row is shown, range.End.Row IS the Totals Row (a SUBTOTAL aggregate
        // or custom formula result, not a data row) -- exclude it from the checklist exactly like
        // the interactive filter-apply commands (FilterCommand/TopBottomFilterCommand/
        // AverageFilterCommand/FilterConditionCommand) already do via GetFilterableLastRow.
        var lastRow = AutoFilterRangeResolver.GetFilterableLastRow(sheet, range);
        for (var row = range.Start.Row + 1; row <= lastRow; row++)
        {
            var value = sheet.GetValue(row, col);
            var normalized = ToFilterText(value);
            if (!seen.Add(normalized))
            {
                continue;
            }

            var displayText = string.IsNullOrEmpty(normalized)
                ? blankDisplayText
                : FormatDisplayText(workbook, sheet, row, col, value, normalized);

            items.Add(new AutoFilterChecklistItem(displayText, normalized));

            // TryToDateTime, not ToDateTime: a date-formatted cell can hold a number outside
            // DateTime's representable range (huge/negative value typed into a date-formatted
            // cell, or a formula result) -- that must not crash opening the filter dropdown.
            // When the serial can't be converted, leave it out of dateSortOverrides so it falls
            // back to the checklist's ordinary numeric-text sort instead of throwing.
            if (value is NumberValue number && workbook is not null && IsDateFormattedCell(workbook, sheet, row, col)
                && new DateTimeValue(number.Value).TryToDateTime(out var overrideDate))
                dateSortOverrides[normalized] = overrideDate.Ticks;
        }

        items.Sort((left, right) => CompareChecklistItems(left, right, dateSortOverrides));
        return items;
    }

    public static IReadOnlyList<AutoFilterChecklistItem> CreateItems(
        IEnumerable<string?> values,
        string blankDisplayText)
    {
        ArgumentNullException.ThrowIfNull(values);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<AutoFilterChecklistItem>();

        foreach (var value in values)
        {
            var normalized = value ?? string.Empty;
            if (!seen.Add(normalized))
                continue;

            items.Add(new AutoFilterChecklistItem(
                string.IsNullOrEmpty(normalized) ? blankDisplayText : normalized,
                normalized));
        }

        items.Sort((left, right) => CompareChecklistItems(left, right, EmptyDateSortOverrides));
        return items;
    }

    private static readonly IReadOnlyDictionary<string, double> EmptyDateSortOverrides =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True when the cell at (<paramref name="row"/>, <paramref name="col"/>) carries a date/time
    /// number format -- Excel's own rule for "this is a date", independent of whether the value is
    /// a literal entry or a formula result (Excel has no separate date value type distinct from a
    /// formatted double). Mirrors the style resolution <see cref="FormatDisplayText"/> uses.
    /// </summary>
    private static bool IsDateFormattedCell(Workbook workbook, Sheet sheet, uint row, uint col)
    {
        var cell = sheet.GetCell(row, col);
        var styleId = cell is not null && cell.StyleId != StyleId.Default
            ? cell.StyleId
            : sheet.GetStyleOnly(row, col) ?? StyleId.Default;

        var numberFormat = workbook.GetStyle(styleId).NumberFormat;
        return !string.IsNullOrEmpty(numberFormat) && NumberFormatter.IsDateTimeNumberFormat(numberFormat);
    }

    /// <summary>
    /// Renders <paramref name="value"/> through the cell's own number format (currency/date/custom),
    /// matching what the grid displays, falling back to <paramref name="fallbackText"/> (the raw
    /// invariant filter text) when there is no workbook to resolve a style from, the cell's format is
    /// the default "General", or the value isn't a number/date (text, bool, error already render the
    /// same either way).
    /// </summary>
    private static string FormatDisplayText(
        Workbook? workbook,
        Sheet sheet,
        uint row,
        uint col,
        ScalarValue value,
        string fallbackText)
    {
        if (workbook is null || value is not (NumberValue or DateTimeValue))
            return fallbackText;

        var cell = sheet.GetCell(row, col);
        var styleId = cell is not null && cell.StyleId != StyleId.Default
            ? cell.StyleId
            : sheet.GetStyleOnly(row, col) ?? StyleId.Default;

        var numberFormat = workbook.GetStyle(styleId).NumberFormat;
        if (string.IsNullOrEmpty(numberFormat) || numberFormat == "General")
            return fallbackText;

        return NumberFormatter.Format(value, numberFormat, workbook.Uses1904DateSystem);
    }

    private static int CompareChecklistItems(
        AutoFilterChecklistItem left,
        AutoFilterChecklistItem right,
        IReadOnlyDictionary<string, double> dateSortOverrides)
    {
        var leftKey = CreateSortKey(left.Value, dateSortOverrides);
        var rightKey = CreateSortKey(right.Value, dateSortOverrides);
        var rankComparison = leftKey.Rank.CompareTo(rightKey.Rank);
        if (rankComparison != 0)
            return rankComparison;

        var numericComparison = leftKey.Number.CompareTo(rightKey.Number);
        if (numericComparison != 0)
            return numericComparison;

        return string.Compare(left.DisplayText, right.DisplayText, StringComparison.CurrentCultureIgnoreCase);
    }

    private static SortKey CreateSortKey(string value, IReadOnlyDictionary<string, double> dateSortOverrides)
    {
        if (string.IsNullOrEmpty(value))
            return new SortKey(5, 0);

        // A formula-computed date's raw filter text is an invariant number string (its OADate
        // serial), which would otherwise fall into the Rank-0 numeric branch below. When the
        // caller determined (from the cell's own number format) that this value is really a date,
        // route it into the same Rank-1 date bucket -- and same tick units -- as literally-typed
        // dates so a mixed literal/computed date column sorts chronologically.
        if (dateSortOverrides.TryGetValue(value, out var overrideTicks))
            return new SortKey(1, overrideTicks);

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) ||
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return new SortKey(0, number);
        }

        if (DateTime.TryParse(value, CultureInfo.CurrentCulture, out var date) ||
            DateTime.TryParse(value, CultureInfo.InvariantCulture, out date))
        {
            return new SortKey(1, date.Ticks);
        }

        if (string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return new SortKey(3, string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        }

        return value.StartsWith('#')
            ? new SortKey(4, 0)
            : new SortKey(2, 0);
    }

    private readonly record struct SortKey(int Rank, double Number);
}
