using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Small parity wires for ribbon buttons that map to existing capabilities.

    /// <summary>
    /// Review - Delete Comment: remove only the threaded comment (with all its replies) at the
    /// active cell, leaving any coexisting legacy note untouched. Mirrors WPF's
    /// ReviewDeleteThreadedCommentBtn_Click (MainWindow.ReviewCommands.cs), which runs
    /// DeleteThreadedCommentCommand rather than the broad ClearCommentsCommand.
    /// </summary>
    private void DeleteActiveCellThreadedComment()
    {
        var result = ReviewSessionController.DeleteThreadedComment();
        if (!result.Success)
        {
            RefreshShell(UiText.Get("InsertLoc_CouldNotDeleteComment"));
            return;
        }

        ApplyReviewRefreshPlan(result.RefreshPlan, UiText.Get("InsertLoc_ClearedCommentsAndNotes"));
    }

    /// <summary>
    /// Review - Delete Note: remove only the legacy note at the active cell, leaving any
    /// coexisting threaded comment (and its replies) untouched. Mirrors WPF's
    /// ReviewDeleteCommentBtn_Click, which runs DeleteCommentCommand rather than the broad
    /// ClearCommentsCommand.
    /// </summary>
    private void DeleteActiveCellNote()
    {
        var result = ReviewSessionController.DeleteNote();
        if (!result.Success)
        {
            RefreshShell(UiText.Get("InsertLoc_CouldNotDeleteComment"));
            return;
        }

        ApplyReviewRefreshPlan(result.RefreshPlan, UiText.Get("InsertLoc_ClearedCommentsAndNotes"));
    }

    /// <summary>View - Normal: leave Page Break Preview.</summary>
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

    /// <summary>
    /// View - Split: toggles the window split, matching the ribbon's IconToggle("Split", ...) semantics
    /// and WPF's SplitViewBtn_Click (MainWindow.ViewCommands.cs). If the active sheet is already split,
    /// clear it (splitRow/splitColumn both null); otherwise split at the active cell -- falling back to
    /// the viewport midpoint when the active cell is A1 through the shared worksheet-structure policy.
    /// </summary>
    private void SplitPanesAtActiveCell()
    {
        var wasSplit = _session.GetEffectiveSplitRow() is not null ||
            _session.GetEffectiveSplitCol() is not null;
        var result = _session.ToggleSplitPanesAtActiveCell();
        RefreshShell(result.Success
            ? (wasSplit
                ? UiText.Get("InsertLoc_RemovedWindowSplit")
                : UiText.Format("InsertLoc_SplitWindowAt", FormatCellReference(_session.ActiveCell)))
            : result.ErrorMessage ?? UiText.Get("InsertLoc_CouldNotSplitWindow"));
    }
}
