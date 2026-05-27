using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class RowColumnDimensionPlanner
{
    public static double GetRowHeightDialogValue(Sheet? sheet, GridRange range, double fallbackHeight = 20)
    {
        if (sheet is null)
            return fallbackHeight;

        var (startRow, _) = SelectionRangeService.GetRowSpan(range);
        return sheet.RowHeights.TryGetValue(startRow, out var height) ? height : sheet.DefaultRowHeight;
    }

    public static double GetColumnWidthDialogValue(Sheet? sheet, GridRange range, double fallbackWidth = 8.43)
    {
        if (sheet is null)
            return fallbackWidth;

        var (startCol, _) = SelectionRangeService.GetColumnSpan(range);
        return sheet.ColumnWidths.TryGetValue(startCol, out var width) ? width : sheet.DefaultColumnWidth;
    }

    public static IWorkbookCommand CreateRowHeightCommand(SheetId sheetId, GridRange range, double height)
    {
        var (startRow, endRow) = SelectionRangeService.GetRowSpan(range);
        return new SetRowHeightCommand(sheetId, startRow, endRow, height);
    }

    public static IWorkbookCommand CreateColumnWidthCommand(SheetId sheetId, GridRange range, double width)
    {
        var (startCol, endCol) = SelectionRangeService.GetColumnSpan(range);
        return new SetColumnWidthCommand(sheetId, startCol, endCol, width);
    }

    public static IWorkbookCommand CreateRowsHiddenCommand(SheetId sheetId, GridRange range, bool hidden)
    {
        var (startRow, endRow) = SelectionRangeService.GetRowSpan(range);
        return new SetRowsHiddenCommand(sheetId, startRow, endRow, hidden);
    }

    public static IWorkbookCommand CreateColumnsHiddenCommand(SheetId sheetId, GridRange range, bool hidden)
    {
        var (startCol, endCol) = SelectionRangeService.GetColumnSpan(range);
        return new SetColumnsHiddenCommand(sheetId, startCol, endCol, hidden);
    }

    public static IWorkbookCommand CreateAutoFitRowHeightCommand(
        SheetId sheetId,
        IReadOnlyList<AutoFitSizePlan> plans)
    {
        if (plans.Count == 1)
            return new SetRowHeightCommand(sheetId, plans[0].Index, plans[0].Index, plans[0].Size);

        return new CompositeWorkbookCommand(
            "Auto Row Height",
            plans.Select(plan => (IWorkbookCommand)new SetRowHeightCommand(sheetId, plan.Index, plan.Index, plan.Size)).ToList());
    }

    public static IWorkbookCommand CreateAutoFitColumnWidthCommand(
        SheetId sheetId,
        IReadOnlyList<AutoFitSizePlan> plans)
    {
        if (plans.Count == 1)
            return new SetColumnWidthCommand(sheetId, plans[0].Index, plans[0].Index, plans[0].Size);

        return new CompositeWorkbookCommand(
            "Auto Column Width",
            plans.Select(plan => (IWorkbookCommand)new SetColumnWidthCommand(sheetId, plan.Index, plan.Index, plan.Size)).ToList());
    }
}
