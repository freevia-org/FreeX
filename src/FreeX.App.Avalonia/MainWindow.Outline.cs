using FreeX.Core.Commands;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Data ▸ Outline ▸ Group / Ungroup (parity gap: the ribbon buttons were no-ops). Groups the
    // selected rows at outline level 1; Ungroup clears the worksheet outline. Routed through the
    // generic review-command executor so both get undo/redo. Kept in the Avalonia shell (no
    // WorkbookSession change) to avoid churn with the concurrently-active FreeW/macOS sessions.

    private void GroupSelectedRows()
    {
        var range = _session.SelectedRange;
        var result = _session.ExecuteReviewCommand(
            new GroupRowsCommand(_session.ActiveSheet.Id, range.Start.Row, range.End.Row, level: 1));
        RefreshShell(result.Success
            ? $"Grouped rows {range.Start.Row}–{range.End.Row}"
            : result.ErrorMessage ?? "Could not group rows.");
    }

    private void ClearWorksheetOutline()
    {
        var result = _session.ExecuteReviewCommand(new ClearWorksheetOutlineCommand(_session.ActiveSheet.Id));
        RefreshShell(result.Success
            ? "Cleared the worksheet outline."
            : result.ErrorMessage ?? "Could not clear the outline.");
    }
}
