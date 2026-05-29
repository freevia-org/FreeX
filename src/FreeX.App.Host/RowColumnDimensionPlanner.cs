using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

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
        IReadOnlyList<AutoFitSizePlan> plans) =>
        CreateAutoFitCommand(
            plans,
            "Auto Row Height",
            plan => new SetRowHeightCommand(sheetId, plan.Index, plan.Index, plan.Size));

    public static IWorkbookCommand CreateAutoFitColumnWidthCommand(
        SheetId sheetId,
        IReadOnlyList<AutoFitSizePlan> plans) =>
        CreateAutoFitCommand(
            plans,
            "Auto Column Width",
            plan => new SetColumnWidthCommand(sheetId, plan.Index, plan.Index, plan.Size));

    private static IWorkbookCommand CreateAutoFitCommand(
        IReadOnlyList<AutoFitSizePlan> plans,
        string compositeName,
        Func<AutoFitSizePlan, IWorkbookCommand> createCommand)
    {
        if (plans.Count == 1)
            return createCommand(plans[0]);

        return new CompositeWorkbookCommand(
            compositeName,
            plans.Select(createCommand).ToList());
    }
}
