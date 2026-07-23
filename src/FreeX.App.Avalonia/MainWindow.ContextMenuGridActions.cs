using FreeX.Core.Commands;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    /// <summary>
    /// Executes the row/column insertion commands used by the WPF worksheet context menu. The
    /// cell-shift dialog is reserved for Insert Cells; Insert Row/Column Above/Below/Left/Right
    /// are structural edits at the active cell boundary.
    /// </summary>
    private void InsertContextRow(uint beforeRow)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var result = _session.ExecuteReviewCommand(
            new InsertRowsCommand(_session.ActiveSheet.Id, beforeRow));
        if (result.Success)
        {
            ClearFormulaTraceArrowsAfterStructuralEdit();
            SetClipboardMarquee(null, isCut: false);
            ShiftScrollOriginForRowEdit(beforeRow, 1);
            _session.RecalculateWorkbook();
        }

        RefreshShell(result.Success
            ? UiText.Get("RibbonWire_InsertedSheetRows")
            : result.ErrorMessage ?? UiText.Get("RibbonWire_InsertSheetRowsFailed"));
    }

    private void InsertContextColumn(uint beforeColumn)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var result = _session.ExecuteReviewCommand(
            new InsertColumnsCommand(_session.ActiveSheet.Id, beforeColumn));
        if (result.Success)
        {
            ClearFormulaTraceArrowsAfterStructuralEdit();
            SetClipboardMarquee(null, isCut: false);
            ShiftScrollOriginForColEdit(beforeColumn, 1);
            _session.RecalculateWorkbook();
        }

        RefreshShell(result.Success
            ? UiText.Get("RibbonWire_InsertedSheetColumns")
            : result.ErrorMessage ?? UiText.Get("RibbonWire_InsertSheetColumnsFailed"));
    }
}
