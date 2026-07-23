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

        for (var row = range.Start.Row + 1; row <= range.End.Row; row++)
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

        for (var row = range.Start.Row + 1; row <= range.End.Row; row++)
        {
            var value = sheet.GetValue(row, col);
            var normalized = ToFilterText(value);
            if (!seen.Add(normalized))
                continue;

            var displayText = string.IsNullOrEmpty(normalized)
                ? blankDisplayText
                : FormatDisplayText(workbook, sheet, row, col, value, normalized);

            items.Add(new AutoFilterChecklistItem(displayText, normalized));
        }

        items.Sort(CompareChecklistItems);
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

        items.Sort(CompareChecklistItems);
        return items;
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

    private static int CompareChecklistItems(AutoFilterChecklistItem left, AutoFilterChecklistItem right)
    {
        var leftKey = CreateSortKey(left.Value);
        var rightKey = CreateSortKey(right.Value);
        var rankComparison = leftKey.Rank.CompareTo(rightKey.Rank);
        if (rankComparison != 0)
            return rankComparison;

        var numericComparison = leftKey.Number.CompareTo(rightKey.Number);
        if (numericComparison != 0)
            return numericComparison;

        return string.Compare(left.DisplayText, right.DisplayText, StringComparison.CurrentCultureIgnoreCase);
    }

    private static SortKey CreateSortKey(string value)
    {
        if (string.IsNullOrEmpty(value))
            return new SortKey(5, 0);

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
