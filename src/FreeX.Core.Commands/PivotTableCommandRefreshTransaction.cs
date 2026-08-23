using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class PivotTableCommandRefreshTransaction
{
    internal static CommandOutcome? RefreshGuarded(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable,
        Action restorePivotState,
        bool rescanCacheSharedItems = false) =>
        RefreshGuardedCore(workbook, sheet, pivotTable, restorePivotState, rescanCacheSharedItems);

    internal static CommandOutcome? RefreshGuarded(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable,
        IPivotTableCommandStateSnapshot pivotState,
        bool rescanCacheSharedItems = false) =>
        RefreshGuardedCore(
            workbook,
            sheet,
            pivotTable,
            () => pivotState.Restore(pivotTable),
            rescanCacheSharedItems);

    private static CommandOutcome? RefreshGuardedCore(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable,
        Action restorePivotState,
        bool rescanCacheSharedItems)
    {
        var baseline = PivotTableRefreshService.CaptureGrowthGuardBaseline(sheet, pivotTable);
        var failure = PivotTableRefreshService.RefreshGuarded(
            workbook,
            sheet,
            pivotTable,
            baseline,
            restorePivotState,
            rescanCacheSharedItems);
        if (failure is not null)
            return failure;

        PivotTableRefreshService.UpdateBoundPivotCharts(workbook, sheet, pivotTable);
        return null;
    }

    internal static void Revert(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel? pivotTable,
        IReadOnlyList<(CellAddress Address, Cell? Cell)>? targetSnapshot,
        IPivotTableCommandStateSnapshot? pivotState,
        bool updateBoundPivotCharts = true) =>
        RevertCore(
            workbook,
            sheet,
            pivotTable,
            targetSnapshot,
            pivotState is null ? null : table => pivotState.Restore(table),
            updateBoundPivotCharts);

    internal static void Revert(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel? pivotTable,
        IReadOnlyList<(CellAddress Address, Cell? Cell)>? targetSnapshot,
        Action<PivotTableModel>? restorePivotState,
        bool updateBoundPivotCharts = true) =>
        RevertCore(workbook, sheet, pivotTable, targetSnapshot, restorePivotState, updateBoundPivotCharts);

    private static void RevertCore(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel? pivotTable,
        IReadOnlyList<(CellAddress Address, Cell? Cell)>? targetSnapshot,
        Action<PivotTableModel>? restorePivotState,
        bool updateBoundPivotCharts)
    {
        if (pivotTable is not null && restorePivotState is not null)
        {
            PivotTableRefreshService.ClearRenderedRange(sheet, pivotTable.LastRenderedRange);
            restorePivotState(pivotTable);
        }

        AddPivotTableCommand.Restore(sheet, targetSnapshot);
        if (pivotTable is not null && updateBoundPivotCharts)
            PivotTableRefreshService.UpdateBoundPivotCharts(workbook, sheet, pivotTable);
    }
}
