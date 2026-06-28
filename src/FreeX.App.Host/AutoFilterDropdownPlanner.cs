using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class AutoFilterDropdownPlanner
{
    private static readonly IAutoFilterMenuTextProvider TextProvider = new UiAutoFilterMenuTextProvider();

    public static string BlankDisplayText => UiText.Get("AutoFilter_BlankDisplayText");

    public static bool TryGetAutoFilterRange(Sheet sheet, out GridRange range) =>
        AutoFilterDropdownMenuPlanner.TryGetAutoFilterRange(sheet, out range);

    public static bool TryPlan(GridRange currentRegion, CellAddress activeCell, out AutoFilterDropdownPlan plan)
        => AutoFilterDropdownMenuPlanner.TryPlan(currentRegion, activeCell, out plan);

    public static IReadOnlyList<AutoFilterChecklistItem> CreateChecklistItems(Sheet sheet, AutoFilterDropdownPlan plan)
        => AutoFilterDropdownMenuPlanner.CreateChecklistItems(sheet, plan, BlankDisplayText);

    public static AutoFilterMenuPlan CreateMenuPlan(Sheet sheet, AutoFilterDropdownPlan plan)
    {
        return CreateMenuPlan(null, sheet, plan);
    }

    public static AutoFilterMenuPlan CreateMenuPlan(Workbook? workbook, Sheet sheet, AutoFilterDropdownPlan plan)
        => AutoFilterDropdownMenuPlanner.CreateMenuPlan(workbook, sheet, plan, TextProvider, BlankDisplayText);

    private sealed class UiAutoFilterMenuTextProvider : IAutoFilterMenuTextProvider
    {
        public string Get(string resourceKey) => UiText.Get(resourceKey);

        public string Format(string resourceKey, string value) => UiText.Format(resourceKey, value);
    }
}
