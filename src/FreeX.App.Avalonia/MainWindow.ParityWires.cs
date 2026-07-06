using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Small parity wires for ribbon buttons that map to existing capabilities.

    /// <summary>Review - Delete Comment: clear comments/notes on the selection.</summary>
    private void DeleteActiveCellComment()
    {
        var result = _session.ClearSelectedRangeComments();
        RefreshShell(result.Success
            ? UiText.Get("InsertLoc_ClearedCommentsAndNotes")
            : result.ErrorMessage ?? UiText.Get("InsertLoc_CouldNotDeleteComment"));
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
    /// clear it (splitRow/splitColumn both null); otherwise split at the active cell.
    /// </summary>
    private void SplitPanesAtActiveCell()
    {
        var sheet = _session.ActiveSheet;
        uint? splitRow = null;
        uint? splitColumn = null;
        var wasSplit = sheet.SplitRow is not null || sheet.SplitColumn is not null;

        if (!wasSplit)
        {
            var cell = _session.ActiveCell;
            splitRow = cell.Row > 1 ? cell.Row : null;
            splitColumn = cell.Col > 1 ? cell.Col : null;
        }

        var result = _session.ExecuteReviewCommand(
            new SetSplitPanesCommand(sheet.Id, splitRow, splitColumn));
        RefreshShell(result.Success
            ? (wasSplit
                ? UiText.Get("InsertLoc_RemovedWindowSplit")
                : UiText.Format("InsertLoc_SplitWindowAt", FormatCellReference(_session.ActiveCell)))
            : result.ErrorMessage ?? UiText.Get("InsertLoc_CouldNotSplitWindow"));
    }
}
