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

        ApplyWorksheetStructureResult(
            _session.InsertRows(beforeRow),
            UiText.Get("RibbonWire_InsertedSheetRows"),
            UiText.Get("RibbonWire_InsertSheetRowsFailed"),
            recalculateWorkbook: true);
    }

    private void InsertContextColumn(uint beforeColumn)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        ApplyWorksheetStructureResult(
            _session.InsertColumns(beforeColumn),
            UiText.Get("RibbonWire_InsertedSheetColumns"),
            UiText.Get("RibbonWire_InsertSheetColumnsFailed"),
            recalculateWorkbook: true);
    }
}
