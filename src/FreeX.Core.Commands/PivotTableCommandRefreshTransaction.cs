using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class PivotTableCommandRefreshTransaction
{
    internal static CommandOutcome? RefreshGuarded(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable,
        Action restorePivotState,
        bool rescanCacheSharedItems = false)
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
        Action<PivotTableModel>? restorePivotState)
    {
        if (pivotTable is not null && restorePivotState is not null)
        {
            PivotTableRefreshService.ClearRenderedRange(sheet, pivotTable.LastRenderedRange);
            restorePivotState(pivotTable);
        }

        AddPivotTableCommand.Restore(sheet, targetSnapshot);
        if (pivotTable is not null)
            PivotTableRefreshService.UpdateBoundPivotCharts(workbook, sheet, pivotTable);
    }
}
