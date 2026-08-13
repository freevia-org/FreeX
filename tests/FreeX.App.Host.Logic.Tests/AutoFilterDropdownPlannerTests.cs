using FreeX.App.Presentation;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class AutoFilterDropdownMenuPlannerHostResourceTests
{
    private static readonly SheetId SheetId = SheetId.New();
    private static readonly FreeXPlannerTextResources TextResources = new(UiText.Get, UiText.Format);

    private static bool TryGetAutoFilterRange(Sheet sheet, out GridRange range) =>
        AutoFilterDropdownMenuPlanner.TryGetAutoFilterRange(sheet, out range);

    private static bool TryPlan(GridRange currentRegion, CellAddress activeCell, out AutoFilterDropdownPlan plan) =>
        AutoFilterDropdownMenuPlanner.TryPlan(currentRegion, activeCell, out plan);

    private static IReadOnlyList<AutoFilterChecklistItem> CreateChecklistItems(
        Sheet sheet,
        AutoFilterDropdownPlan plan) =>
        AutoFilterDropdownMenuPlanner.CreateChecklistItems(
            sheet,
            plan,
            TextResources.AutoFilter.BlankDisplayText);

    private static AutoFilterMenuPlan CreateMenuPlan(Sheet sheet, AutoFilterDropdownPlan plan) =>
        AutoFilterDropdownMenuPlanner.CreateMenuPlan(
            sheet,
            plan,
            TextResources.AutoFilter,
            TextResources.AutoFilter.BlankDisplayText);

    private static AutoFilterMenuPlan CreateMenuPlan(
        Workbook workbook,
        Sheet sheet,
        AutoFilterDropdownPlan plan) =>
        AutoFilterDropdownMenuPlanner.CreateMenuPlan(
            workbook,
            sheet,
            plan,
            TextResources.AutoFilter,
            TextResources.AutoFilter.BlankDisplayText);
}
