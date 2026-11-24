using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private async Task ShowHyperlinkForTargetAsync(CellAddress? contextAddress)
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var session = _session;
        var address = contextAddress ?? session.SelectedRange.Start;
        if (address.Sheet != session.ActiveSheet.Id)
            return;
        var sheet = session.ActiveSheet;
        var targetSheetIds = session.GetCurrentGroupedEditSheetIds().ToArray();
        var prefill = HyperlinkDialogPrefill.FromCell(sheet, address);
        var plan = await ShowHyperlinkInputDialogWithPrefillAsync(prefill);
        if (plan is null || _isOpening || _isSaving || !ReferenceEquals(_session, session) ||
            session.ActiveSheet.Id != address.Sheet || session.Workbook.GetSheet(address.Sheet) is null ||
            !targetSheetIds.ToHashSet().SetEquals(session.GetCurrentGroupedEditSheetIds()) ||
            targetSheetIds.Any(id => session.Workbook.GetSheet(id) is null))
        {
            return;
        }

        // Insert/Edit changes one cell per grouped sheet. Removal remains a selection-wide action.
        // Freeze the dialog's address rather than re-reading selection after its asynchronous lifetime.
        var metadata = new HyperlinkMetadata(plan.LinkType, plan.ScreenTip, plan.Bookmark);
        var initialExecution = true;
        IWorkbookCommand CreateCommand()
        {
            if (_isOpening || _isSaving || !ReferenceEquals(_session, session))
                return new FailedWorkbookCommand(UiText.Get("MainLoc_InsertHyperlinkFailed"));

            var target = initialExecution ? address : session.SelectedRange.Start;
            var sheetIds = initialExecution ? targetSheetIds : session.GetCurrentGroupedEditSheetIds();
            initialExecution = false;
            var commands = sheetIds.Select(sheetId => (IWorkbookCommand)new SetHyperlinkCommand(
                sheetId, new CellAddress(sheetId, target.Row, target.Col), plan.Target, plan.DisplayText, metadata)).ToArray();
            return commands.Length == 1 ? commands[0] : new CompositeWorkbookCommand("Insert Hyperlink", commands);
        }

        // WPF preserves every selected area and repeats the accepted link at the then-current
        // primary anchor/grouped sheets. Only the initial execution uses the dialog's frozen target.
        var result = session.ExecuteRepeatableCommandPreservingSelection(CreateCommand);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("MainLoc_InsertHyperlinkFailed"));
            return;
        }

        RefreshShell(UiText.Format("MainLoc_InsertedHyperlinkFor", FormatCellReference(address)));
    }
}
