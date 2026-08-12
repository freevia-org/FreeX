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

    /// <summary>
    /// R90-io-sheet-view-custom-views-5-2: real Excel disables the entire Custom Views feature
    /// (View &gt; Custom Views is grayed out; attempting it via the object model raises "This command
    /// is not available in a workbook that contains a table") the moment ANY sheet in the workbook
    /// has a structured Table, because a saved view's per-sheet state (hidden rows/cols, filters,
    /// active cell) could conflict with a table's own AutoFilter/banding state on Show. Checked
    /// across every sheet, not just the active one, since Excel's own gate is workbook-wide.
    /// </summary>
    public static CommandOutcome? RejectIfWorkbookHasTable(Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (sheet.StructuredTables.Count > 0)
                return new CommandOutcome(false, "This command is not available in a workbook that contains a table.");
        }

        return null;
    }

    public static List<WorksheetCustomViewState> CaptureWorkbookState(Workbook workbook) =>
        workbook.Sheets.Select(CaptureSheetState).ToList();

    public static int? CaptureActiveSheetIndex(Workbook workbook) =>
        SanitizeActiveSheetIndex(workbook, workbook.ActiveSheetIndex);

    public static int? SanitizeActiveSheetIndex(Workbook workbook, int? index) =>
        index is >= 0 && index < workbook.Sheets.Count ? index.Value : null;

    // N13: this only ever captures the base pane/zoom/gridline fields — it deliberately leaves the
    // hidden-rows/cols/filter and print-setting fields null (i.e. "not captured"). Those fields are
    // gated by the owning WorkbookCustomView's IncludeHiddenRowsColumnsAndFilterSettings /
    // IncludePrintSettings flags, which live above the per-sheet snapshot this method produces (see
    // FreeX.Core.Commands.CustomViewCommands.AugmentCapturedState, N14): that gating relies on this
    // method's result starting out null in those fields for the "flag off" branch to mean anything,
    // so populating them here unconditionally would silently defeat the flags. ApplyState below
    // still knows how to restore them (from a state a caller populated some other way), so the
    // capture/apply pair is complete even though this method's own capture is base-fields-only.
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

        // N13: hidden-rows/cols/filter and print-setting fields. Null means "not captured" (the
        // owning WorkbookCustomView's IncludeHiddenRowsColumnsAndFilterSettings / IncludePrintSettings
        // flag was off, or the state came from an older snapshot that predates these fields) —
        // matching Excel, leave the sheet's current state for that facet untouched in that case.
        if (state.HiddenRows is { } hiddenRows)
        {
            sheet.HiddenRows.Clear();
            foreach (var row in hiddenRows)
                sheet.HiddenRows.Add(row);
        }
        if (state.HiddenCols is { } hiddenCols)
        {
            sheet.HiddenCols.Clear();
            foreach (var col in hiddenCols)
                sheet.HiddenCols.Add(col);
        }
        if (state.FilterHiddenRows is { } filterHiddenRows)
        {
            sheet.FilterHiddenRows.Clear();
            foreach (var row in filterHiddenRows)
                sheet.FilterHiddenRows.Add(row);
        }
        if (state.AutoFilter is not null)
            sheet.AutoFilter = WorksheetAutoFilterCloner.Clone(state.AutoFilter);
        if (state.PrintAreas is { } printAreas)
            sheet.SetPrintAreas(printAreas);
        if (state.PageOrientation is { } pageOrientation)
            sheet.PageOrientation = pageOrientation;
        if (state.PaperSize is { } paperSize)
            sheet.PaperSize = paperSize;
        if (state.PaperSizeCode is { } paperSizeCode)
            sheet.PaperSizeCode = paperSizeCode;
        if (state.PageMargins is { } pageMargins)
            sheet.PageMargins = pageMargins;
        if (state.HeaderMargin is { } headerMargin)
            sheet.HeaderMargin = headerMargin;
        if (state.FooterMargin is { } footerMargin)
            sheet.FooterMargin = footerMargin;
        if (state.PrintGridlines is { } printGridlines)
            sheet.PrintGridlines = printGridlines;
        if (state.PrintHeadings is { } printHeadings)
            sheet.PrintHeadings = printHeadings;
        if (state.ScaleToFit is { } scaleToFit)
            sheet.ScaleToFit = scaleToFit;
        if (state.FitToPage is { } fitToPage)
            sheet.FitToPage = fitToPage;
    }

    private static uint? SanitizeRow(uint? row) =>
        row is >= 1 and <= CellAddress.MaxRow ? row.Value : null;

    private static uint? SanitizeColumn(uint? column) =>
        column is >= 1 and <= CellAddress.MaxCol ? column.Value : null;

}
