using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Small parity wires for ribbon buttons that map to existing capabilities.

    /// <summary>Review ▸ Delete Comment — clear comments/notes on the selection.</summary>
    private void DeleteActiveCellComment()
    {
        var result = _session.ClearSelectedRangeComments();
        RefreshShell(result.Success
            ? UiText.Get("InsertLoc_ClearedCommentsAndNotes")
            : result.ErrorMessage ?? UiText.Get("InsertLoc_CouldNotDeleteComment"));
    }

    /// <summary>Formulas ▸ Error Checking — select formula cells that evaluate to an error.</summary>
    private void CheckFormulaErrors()
    {
        var result = _session.GoToSpecial(
            GoToSpecialKind.Formulas,
            new GoToSpecialOptions(GoToSpecialValueTypes.Errors));
        RefreshShell(result.Success && result.MatchCount > 0
            ? UiText.Format("InsertLoc_ErrorCheckingSelected", result.MatchCount)
            : UiText.Get("InsertLoc_ErrorCheckingNone"));
    }

    /// <summary>View ▸ Normal — leave Page Break Preview.</summary>
    private void SetNormalView()
    {
        var result = _session.SetWorksheetViewMode(WorksheetViewMode.Normal);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("InsertLoc_NormalView"));
            return;
        }

        RefreshShell(UiText.Get("InsertLoc_NormalView"));
    }

    /// <summary>View ▸ Split — split the window at the active cell.</summary>
    private void SplitPanesAtActiveCell()
    {
        var cell = _session.ActiveCell;
        var result = _session.ExecuteReviewCommand(
            new SetSplitPanesCommand(_session.ActiveSheet.Id, cell.Row, cell.Col));
        RefreshShell(result.Success
            ? UiText.Format("InsertLoc_SplitWindowAt", FormatCellReference(cell))
            : result.ErrorMessage ?? UiText.Get("InsertLoc_CouldNotSplitWindow"));
    }
}
