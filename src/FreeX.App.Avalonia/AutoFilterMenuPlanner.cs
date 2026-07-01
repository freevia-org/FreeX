using FreeX.App.Presentation.Filtering;

namespace FreeX.App.Avalonia;

/// <summary>The kind of entry in an AutoFilter dropdown menu.</summary>
internal enum AutoFilterMenuItemKind
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
    ChecklistItem,
}

/// <summary>One entry in the AutoFilter dropdown menu.</summary>
internal sealed record AutoFilterMenuItem(
    AutoFilterMenuItemKind Kind,
    string Label,
    string Value = "",
    bool IsEnabled = true,
    bool? IsChecked = null);

/// <summary>The resolved AutoFilter dropdown menu for a column: header plus ordered entries.</summary>
internal sealed record AutoFilterMenuModel(string Header, IReadOnlyList<AutoFilterMenuItem> Items);

/// <summary>
/// UI-free planner that builds the AutoFilter dropdown menu model for a column from canonical filter
/// values. The menu chrome remains Avalonia-specific; checklist values and ordering come from the shared
/// presentation planner so the macOS dropdown matches Windows.
/// </summary>
internal static class AutoFilterMenuPlanner
{
    public static AutoFilterMenuModel Build(AutoFilterMenuPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new AutoFilterMenuModel(
            plan.HeaderText,
            plan.Entries.Select(ToMenuItem).ToList());
    }

    private static AutoFilterMenuItem ToMenuItem(AutoFilterMenuEntry entry) =>
        new(
            entry.Kind switch
            {
                AutoFilterMenuEntryKind.SortAscending => AutoFilterMenuItemKind.SortAscending,
                AutoFilterMenuEntryKind.SortDescending => AutoFilterMenuItemKind.SortDescending,
                AutoFilterMenuEntryKind.ClearFilter => AutoFilterMenuItemKind.ClearFilter,
                AutoFilterMenuEntryKind.FilterByColor => AutoFilterMenuItemKind.FilterByColor,
                AutoFilterMenuEntryKind.FilterFamily => AutoFilterMenuItemKind.FilterFamily,
                AutoFilterMenuEntryKind.FilterFamilyCommand => AutoFilterMenuItemKind.FilterFamilyCommand,
                AutoFilterMenuEntryKind.Search => AutoFilterMenuItemKind.Search,
                AutoFilterMenuEntryKind.SelectAll => AutoFilterMenuItemKind.SelectAll,
                AutoFilterMenuEntryKind.ChecklistItem => AutoFilterMenuItemKind.ChecklistItem,
                _ => AutoFilterMenuItemKind.Separator
            },
            entry.Header,
            entry.Value,
            entry.IsEnabled,
            entry.IsChecked);
}
