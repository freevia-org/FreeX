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
            sheet.ShowFormulas));

    public static WorksheetCustomViewState SanitizePaneState(WorksheetCustomViewState state)
    {
        if (state.FrozenRows == 0 && state.FrozenCols == 0)
            return state;

        return state with
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
    }
}
