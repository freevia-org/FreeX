using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static class CustomViewStatePlanner
{
    public static int FindViewIndex(Workbook workbook, string name)
    {
        for (var i = 0; i < workbook.CustomViews.Count; i++)
            if (string.Equals(workbook.CustomViews[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    public static List<WorksheetCustomViewState> CaptureWorkbookState(Workbook workbook) =>
        workbook.Sheets.Select(CaptureSheetState).ToList();

    public static int? CaptureActiveSheetIndex(Workbook workbook) =>
        SanitizeActiveSheetIndex(workbook, workbook.ActiveSheetIndex);

    public static int? SanitizeActiveSheetIndex(Workbook workbook, int? index) =>
        index is >= 0 && index < workbook.Sheets.Count ? index.Value : null;

    public static WorksheetCustomViewState CaptureSheetState(Sheet sheet) =>
        SanitizePaneState(new WorksheetCustomViewState(
            sheet.Name,
            sheet.ViewMode,
            sheet.FrozenRows,
            sheet.FrozenCols,
            sheet.SplitRow,
            sheet.SplitColumn,
            sheet.ShowGridlines,
            sheet.ShowHeadings,
            sheet.ShowRulers,
            sheet.ZoomPercent,
            sheet.ShowFormulas,
            ActiveRow: SanitizeRow(sheet.ActiveRow) ?? 1,
            ActiveCol: SanitizeColumn(sheet.ActiveCol) ?? 1,
            ViewTopRow: SanitizeRow(sheet.ViewTopRow) ?? 1,
            ViewLeftCol: SanitizeColumn(sheet.ViewLeftCol) ?? 1));

    public static WorksheetCustomViewState SanitizePaneState(WorksheetCustomViewState state)
    {
        var sanitized = state with
        {
            ActiveRow = SanitizeRow(state.ActiveRow),
            ActiveCol = SanitizeColumn(state.ActiveCol),
            ViewTopRow = SanitizeRow(state.ViewTopRow),
            ViewLeftCol = SanitizeColumn(state.ViewLeftCol)
        };

        if (state.FrozenRows == 0 && state.FrozenCols == 0)
            return sanitized;

        return sanitized with
        {
            SplitRow = null,
            SplitColumn = null
        };
    }

    public static void ApplyState(Sheet sheet, WorksheetCustomViewState state)
    {
        state = SanitizePaneState(state);
        sheet.ViewMode = state.ViewMode;
        sheet.FrozenRows = state.FrozenRows;
        sheet.FrozenCols = state.FrozenCols;
        sheet.SplitRow = state.SplitRow;
        sheet.SplitColumn = state.SplitColumn;
        sheet.ShowGridlines = state.ShowGridlines;
        sheet.ShowHeadings = state.ShowHeadings;
        sheet.ShowRulers = state.ShowRulers;
        sheet.ZoomPercent = state.ZoomPercent;
        sheet.ShowFormulas = state.ShowFormulas;
        if (state.ActiveRow is { } activeRow)
            sheet.ActiveRow = activeRow;
        if (state.ActiveCol is { } activeCol)
            sheet.ActiveCol = activeCol;
        if (state.ViewTopRow is { } viewTopRow)
            sheet.ViewTopRow = viewTopRow;
        if (state.ViewLeftCol is { } viewLeftCol)
            sheet.ViewLeftCol = viewLeftCol;
    }

    private static uint? SanitizeRow(uint? row) =>
        row is >= 1 and <= CellAddress.MaxRow ? row.Value : null;

    private static uint? SanitizeColumn(uint? column) =>
        column is >= 1 and <= CellAddress.MaxCol ? column.Value : null;
}
