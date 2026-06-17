namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    /// <summary>
    /// Converts the current selection into a structured table through the shared session command path,
    /// reusing the Core <see cref="FreeX.Core.Commands.CreateStructuredTableCommand"/>. Header detection
    /// reuses the shell's <see cref="QuickAnalysisSelectionReader"/> heuristic so the menu and (future)
    /// Quick Analysis agree on whether the first row is a header; the Avalonia grid paints the table styling
    /// on the next refresh. Surfaces the Core guard message (e.g. range must include a header row and a data
    /// row) on failure rather than silently no-opping.
    /// </summary>
    private void InsertTableFromSelection()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var range = _session.SelectedRange;
        var hasHeaderRow = QuickAnalysisSelectionReader.Describe(_session.ActiveSheet, range).HasHeaderRow;
        var command = InsertTableCommandFactory.Build(_session.ActiveSheet.Id, range, hasHeaderRow);
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            RefreshShell(result.ErrorMessage ?? "Insert Table failed.");
            return;
        }

        ClearSelectedDrawingObject();
        RefreshShell($"Created table from {FormatRangeReference(range)}");
    }
}
