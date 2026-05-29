namespace FreeX.App.Host;

public sealed partial class AutoFilterDialog
{
    public static IReadOnlyList<AutoFilterDialogItem> FilterItems(
        IEnumerable<AutoFilterDialogItem> items,
        string? searchText) =>
        AutoFilterDialogCriteriaPlanner.FilterItems(items, searchText);

    public static IReadOnlyList<AutoFilterDialogItem> SelectAll(IEnumerable<AutoFilterDialogItem> items) =>
        AutoFilterDialogCriteriaPlanner.SelectAll(items);

    public static IReadOnlyList<AutoFilterDialogItem> ClearAll(IEnumerable<AutoFilterDialogItem> items) =>
        AutoFilterDialogCriteriaPlanner.ClearAll(items);

    public static IReadOnlyList<AutoFilterDialogItem> SetSelectionForSearch(
        IEnumerable<AutoFilterDialogItem> items,
        string? searchText,
        bool isSelected) =>
        AutoFilterDialogCriteriaPlanner.SetSelectionForSearch(items, searchText, isSelected);

    public static string GetFilterFamilyHeader(AutoFilterMenuFilterKind filterKind) =>
        AutoFilterDialogCriteriaPlanner.GetFilterFamilyHeader(filterKind);

    public static AutoFilterDialogResult BuildResult(
        AutoFilterSortDirection sortDirection,
        IEnumerable<AutoFilterDialogItem> items,
        string? searchText,
        string? criteriaText,
        AutoFilterColorFilter? colorFilter = null,
        bool addCurrentSelectionToFilter = false) =>
        AutoFilterDialogCriteriaPlanner.BuildResult(
            sortDirection,
            items,
            searchText,
            criteriaText,
            colorFilter,
            addCurrentSelectionToFilter);

    public static AutoFilterDialogResult CreateClearFilterResult() =>
        AutoFilterDialogCriteriaPlanner.CreateClearFilterResult();

    public static IReadOnlyList<AutoFilterDialogItem> GetResultItemsForSearchMode(
        IEnumerable<AutoFilterDialogItem> items,
        string? searchText,
        bool addCurrentSelectionToFilter) =>
        AutoFilterDialogCriteriaPlanner.GetResultItemsForSearchMode(items, searchText, addCurrentSelectionToFilter);

    public static IReadOnlyList<string> GetCriteriaSuggestions(AutoFilterMenuPlan menuPlan) =>
        AutoFilterDialogCriteriaPlanner.GetCriteriaSuggestions(menuPlan);

    public static IReadOnlyList<AutoFilterCriteriaOption> GetCriteriaOptions(AutoFilterMenuFilterKind filterKind) =>
        AutoFilterDialogCriteriaPlanner.GetCriteriaOptions(filterKind);

    public static string BuildCriteriaText(AutoFilterCriteriaOption option, string? value) =>
        AutoFilterDialogCriteriaPlanner.BuildCriteriaText(option, value);

    public static string BuildBetweenCriteriaText(AutoFilterCriteriaOption option, string? minimum, string? maximum) =>
        AutoFilterDialogCriteriaPlanner.BuildBetweenCriteriaText(option, minimum, maximum);

    public static string BuildTopBottomCriteriaText(AutoFilterCriteriaOption option, string? count) =>
        AutoFilterDialogCriteriaPlanner.BuildTopBottomCriteriaText(option, count);

    public static string BuildDatePresetCriteriaText(string preset, DateTime today) =>
        AutoFilterDialogCriteriaPlanner.BuildDatePresetCriteriaText(preset, today);

    public static string BuildCompositeCriteriaText(string? firstCriteria, string? connector, string? secondCriteria) =>
        AutoFilterDialogCriteriaPlanner.BuildCompositeCriteriaText(firstCriteria, connector, secondCriteria);

    public static bool HasFilterByColorEntry(AutoFilterMenuPlan menuPlan) =>
        AutoFilterDialogCriteriaPlanner.HasFilterByColorEntry(menuPlan);
}
