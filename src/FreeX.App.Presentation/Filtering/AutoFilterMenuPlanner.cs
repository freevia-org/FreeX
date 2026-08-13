using Free.Shared.Ribbon;

namespace FreeX.App.Presentation.Filtering;

public enum AutoFilterMenuItemKind
{
    SortAscending,
    SortDescending,
    Separator,
    ClearFilter,
    FilterByColor,
    SortByColor,
    FilterFamily,
    FilterFamilyCommand,
    Search,
    SelectAll,
    ChecklistItem,
}

public sealed record AutoFilterMenuItem(
    AutoFilterMenuItemKind Kind,
    string Label,
    string Value = "",
    bool IsEnabled = true,
    bool? IsChecked = null,
    RibbonCommandIconKind IconKind = RibbonCommandIconKind.Generic,
    AutoFilterMenuEntryFocusRole FocusRole = AutoFilterMenuEntryFocusRole.None,
    bool ShowsContinuation = false,
    bool ParticipatesInSearch = false);

public sealed record AutoFilterMenuModel(
    string Header,
    AutoFilterMenuFilterKind FilterKind,
    IReadOnlyList<AutoFilterMenuItem> Items,
    IReadOnlyList<AutoFilterCriteriaOption> CriteriaOptions,
    IReadOnlyList<string> CriteriaSuggestions,
    IReadOnlyList<AutoFilterColorOption> ColorOptions);

public sealed record AutoFilterChecklistState(
    IReadOnlyList<AutoFilterDialogItem> VisibleItems,
    bool IsChecklistEnabled,
    bool? SelectAllState,
    bool IsAddCurrentSelectionVisible,
    bool IsAddCurrentSelectionEnabled,
    bool ShouldClearAddCurrentSelection);

/// <summary>
/// Projects the canonical AutoFilter menu into renderer-neutral interaction rows and owns the
/// checklist/criteria result decisions used by both desktop shells.
/// </summary>
public static class AutoFilterMenuPlanner
{
    public static AutoFilterMenuModel Build(AutoFilterMenuPlan plan) =>
        Build(plan, InvariantAutoFilterMenuTextProvider.Instance);

    public static AutoFilterMenuModel Build(
        AutoFilterMenuPlan plan,
        IAutoFilterMenuTextProvider textProvider)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(textProvider);

        return new AutoFilterMenuModel(
            plan.HeaderText,
            plan.FilterKind,
            plan.Entries.Select(ToMenuItem).ToList(),
            CreateCriteriaOptions(plan.FilterKind, textProvider),
            AutoFilterDialogCriteriaPlanner.GetCriteriaSuggestions(plan),
            plan.ColorOptions ?? []);
    }

    public static IReadOnlyList<AutoFilterDialogItem> CreateDialogItems(AutoFilterMenuPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Entries
            .Where(entry => entry.Kind == AutoFilterMenuEntryKind.ChecklistItem)
            .Select(entry => new AutoFilterDialogItem(entry.Header, entry.Value, entry.IsChecked ?? true))
            .ToList();
    }

    public static IReadOnlyList<AutoFilterDialogItem> CreateDialogItems(AutoFilterMenuModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Items
            .Where(item => item.Kind == AutoFilterMenuItemKind.ChecklistItem)
            .Select(item => new AutoFilterDialogItem(item.Label, item.Value, item.IsChecked ?? true))
            .ToList();
    }

    public static string GetFilterFamilyHeader(
        AutoFilterMenuFilterKind filterKind,
        IAutoFilterMenuTextProvider textProvider)
    {
        ArgumentNullException.ThrowIfNull(textProvider);
        return textProvider.Get(AutoFilterMenuCatalog.GetFilterFamilyDescriptor(filterKind).ResourceKey);
    }

    public static IReadOnlyList<AutoFilterCriteriaOption> CreateCriteriaOptions(
        AutoFilterMenuFilterKind filterKind,
        IAutoFilterMenuTextProvider textProvider)
    {
        ArgumentNullException.ThrowIfNull(textProvider);

        var descriptors = AutoFilterMenuCatalog.GetCriteriaDescriptors(filterKind);
        var options = new List<AutoFilterCriteriaOption>(descriptors.Count);
        foreach (var descriptor in descriptors)
        {
            options.Add(new AutoFilterCriteriaOption(
                textProvider.Get(descriptor.ResourceKey),
                descriptor.CriteriaPrefix,
                descriptor.RequiresValue));
        }

        return options;
    }

    public static IReadOnlyList<AutoFilterDialogItem> FilterItems(
        IEnumerable<AutoFilterDialogItem> items,
        string? searchText) =>
        AutoFilterDialogCriteriaPlanner.FilterItems(items, searchText);

    public static IReadOnlyList<AutoFilterDialogItem> SetSelectionForSearch(
        IEnumerable<AutoFilterDialogItem> items,
        string? searchText,
        bool isSelected) =>
        AutoFilterDialogCriteriaPlanner.SetSelectionForSearch(items, searchText, isSelected);

    public static bool? SelectAllState(IEnumerable<AutoFilterDialogItem> items)
    {
        var materialized = items.ToList();
        if (materialized.Count == 0)
            return false;

        var selectedCount = materialized.Count(item => item.IsSelected);
        return selectedCount == materialized.Count
            ? true
            : selectedCount == 0
                ? false
                : null;
    }

    public static AutoFilterChecklistState PlanChecklistState(
        IEnumerable<AutoFilterDialogItem> items,
        string? searchText)
    {
        var visibleItems = FilterItems(items, searchText);
        var hasVisibleItems = visibleItems.Count > 0;
        var hasSearchText = !string.IsNullOrWhiteSpace(searchText);
        return new AutoFilterChecklistState(
            visibleItems,
            hasVisibleItems,
            SelectAllState(visibleItems),
            hasSearchText,
            hasSearchText && hasVisibleItems,
            ShouldClearAddCurrentSelection: !hasSearchText);
    }

    public static AutoFilterDialogResult BuildResult(
        IEnumerable<AutoFilterDialogItem> items,
        string? searchText,
        string? criteriaText,
        AutoFilterColorFilter? colorFilter = null,
        bool addCurrentSelectionToFilter = false) =>
        AutoFilterDialogCriteriaPlanner.BuildResult(
            AutoFilterSortDirection.None,
            items,
            searchText,
            criteriaText,
            colorFilter,
            addCurrentSelectionToFilter);

    public static string BuildCriteriaText(AutoFilterCriteriaOption option, string? value) =>
        AutoFilterDialogCriteriaPlanner.BuildCriteriaText(option, value);

    public static string BuildCompletedCriteriaText(
        AutoFilterCriteriaOption option,
        string? value,
        string? secondValue = null)
    {
        if (option.RequiresValue && string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (RequiresSecondCriteriaValue(option))
        {
            return string.IsNullOrWhiteSpace(secondValue)
                ? string.Empty
                : AutoFilterDialogCriteriaPlanner.BuildBetweenCriteriaText(option, value, secondValue);
        }

        return RequiresCountCriteriaValue(option)
            ? AutoFilterDialogCriteriaPlanner.BuildTopBottomCriteriaText(option, value)
            : BuildCriteriaText(option, value);
    }

    public static bool RequiresSecondCriteriaValue(AutoFilterCriteriaOption option) =>
        AutoFilterDialogCriteriaPlanner.IsBetweenOption(option);

    public static bool RequiresCountCriteriaValue(AutoFilterCriteriaOption option) =>
        AutoFilterDialogCriteriaPlanner.IsTopBottomOption(option);

    private static AutoFilterMenuItem ToMenuItem(AutoFilterMenuEntry entry) =>
        new(
            entry.Kind switch
            {
                AutoFilterMenuEntryKind.SortAscending => AutoFilterMenuItemKind.SortAscending,
                AutoFilterMenuEntryKind.SortDescending => AutoFilterMenuItemKind.SortDescending,
                AutoFilterMenuEntryKind.ClearFilter => AutoFilterMenuItemKind.ClearFilter,
                AutoFilterMenuEntryKind.FilterByColor => AutoFilterMenuItemKind.FilterByColor,
                AutoFilterMenuEntryKind.SortByColor => AutoFilterMenuItemKind.SortByColor,
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
            entry.IsChecked,
            entry.Presentation.IconKind,
            entry.Presentation.FocusRole,
            entry.Presentation.ShowsContinuation,
            entry.Presentation.ParticipatesInSearch);
}
