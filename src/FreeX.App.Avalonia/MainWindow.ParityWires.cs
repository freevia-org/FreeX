using FreeX.Core.Commands;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Small parity wires for ribbon buttons that map to existing capabilities.

    /// <summary>Review ▸ Delete Comment — clear comments/notes on the selection.</summary>
    private void DeleteActiveCellComment()
    {
        var result = _session.ClearSelectedRangeComments();
        RefreshShell(result.Success
            ? "Cleared comments and notes."
            : result.ErrorMessage ?? "Could not delete the comment.");
    }

    /// <summary>Formulas ▸ Error Checking — select formula cells that evaluate to an error.</summary>
    private void CheckFormulaErrors()
    {
        var result = _session.GoToSpecial(
            GoToSpecialKind.Formulas,
            new GoToSpecialOptions(GoToSpecialValueTypes.Errors));
        RefreshShell(result.Success && result.MatchCount > 0
            ? $"Error checking: selected {result.MatchCount} cell(s) with formula errors."
            : "Error checking: no formula errors found.");
    }

    /// <summary>View ▸ Normal — leave Page Break Preview.</summary>
    private void SetNormalView()
    {
        if (_isPageBreakPreviewActive)
            _isPageBreakPreviewActive = false;
        RefreshShell("Normal view");
    }

    /// <summary>View ▸ Split — split the window at the active cell.</summary>
    private void SplitPanesAtActiveCell()
    {
        var cell = _session.ActiveCell;
        var result = _session.ExecuteReviewCommand(
            new SetSplitPanesCommand(_session.ActiveSheet.Id, cell.Row, cell.Col));
        RefreshShell(result.Success
            ? $"Split window at {FormatCellReference(cell)}"
            : result.ErrorMessage ?? "Could not split the window.");
    }
}
