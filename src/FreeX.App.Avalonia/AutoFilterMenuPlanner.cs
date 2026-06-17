using System.Globalization;

namespace FreeX.App.Avalonia;

/// <summary>The kind of entry in an AutoFilter dropdown menu.</summary>
internal enum AutoFilterMenuItemKind
{
    SortAscending,
    SortDescending,
    Separator,
    ClearFilter,
    SelectAll,
    ChecklistItem,
}

/// <summary>One entry in the AutoFilter dropdown menu.</summary>
internal sealed record AutoFilterMenuItem(
    AutoFilterMenuItemKind Kind,
    string Label,
    string Value = "",
    bool IsEnabled = true);

/// <summary>The resolved AutoFilter dropdown menu for a column: header plus ordered entries.</summary>
internal sealed record AutoFilterMenuModel(string Header, IReadOnlyList<AutoFilterMenuItem> Items);

/// <summary>
/// UI-free planner that builds the AutoFilter dropdown menu model for a column from its distinct display
/// values (the shell supplies them from the viewport, since value formatting is not portable). Mirrors the
/// desktop host's menu shape — Sort A-Z / Sort Z-A, Clear Filter (enabled only when a filter is active),
/// Select All, then a value checklist sorted numbers-then-dates-then-text — and the same checklist ordering
/// so the macOS dropdown matches Windows. Pure, so the menu shape and ordering are unit testable.
/// </summary>
internal static class AutoFilterMenuPlanner
{
    internal const string BlankDisplayText = "(Blanks)";

    /// <summary>
    /// Builds the menu for <paramref name="header"/> over <paramref name="distinctValues"/> (raw display
    /// strings, deduplicated here). <paramref name="hasActiveFilter"/> enables the Clear Filter entry.
    /// </summary>
    public static AutoFilterMenuModel Build(
        string header,
        IReadOnlyList<string> distinctValues,
        bool hasActiveFilter)
    {
        ArgumentNullException.ThrowIfNull(distinctValues);

        var items = new List<AutoFilterMenuItem>
        {
            new(AutoFilterMenuItemKind.SortAscending, "Sort A to Z"),
            new(AutoFilterMenuItemKind.SortDescending, "Sort Z to A"),
            new(AutoFilterMenuItemKind.Separator, string.Empty),
            new(AutoFilterMenuItemKind.ClearFilter, $"Clear Filter from \"{header}\"", IsEnabled: hasActiveFilter),
            new(AutoFilterMenuItemKind.Separator, string.Empty),
            new(AutoFilterMenuItemKind.SelectAll, "(Select All)"),
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var checklist = new List<string>();
        foreach (var value in distinctValues)
        {
            var normalized = value ?? string.Empty;
            if (seen.Add(normalized))
                checklist.Add(normalized);
        }

        checklist.Sort(CompareChecklistValues);
        foreach (var value in checklist)
        {
            items.Add(new AutoFilterMenuItem(
                AutoFilterMenuItemKind.ChecklistItem,
                string.IsNullOrEmpty(value) ? BlankDisplayText : value,
                value));
        }

        return new AutoFilterMenuModel(header, items);
    }

    private static int CompareChecklistValues(string left, string right)
    {
        var leftKey = CreateSortKey(left);
        var rightKey = CreateSortKey(right);
        var rankComparison = leftKey.Rank.CompareTo(rightKey.Rank);
        if (rankComparison != 0)
            return rankComparison;

        var numericComparison = leftKey.Number.CompareTo(rightKey.Number);
        if (numericComparison != 0)
            return numericComparison;

        return string.Compare(left, right, StringComparison.CurrentCultureIgnoreCase);
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
