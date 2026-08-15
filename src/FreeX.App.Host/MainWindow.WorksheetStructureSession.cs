using System.Collections.Generic;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private bool TryExecuteWorksheetLayout(
        Func<WorkbookCellEditResult> execute,
        string title,
        out WorkbookCellEditResult result)
    {
        SynchronizeWorkbookSessionSelection();
        result = execute();
        return CompleteWorksheetSessionCommand(result, title);
    }

    private bool CompleteWorksheetSessionCommand(
        WorkbookCellEditResult result,
        string title,
        IReadOnlyList<SheetId>? viewStateSheetIds = null)
    {
        var outcome = ToCommandOutcome(result);
        RecordDiagnosticEvent("command_invoked", new Dictionary<string, string?>
        {
            ["command"] = title,
            ["status"] = outcome.Success ? "succeeded" : "failed"
        });
        if (outcome.Success)
        {
            if (outcome.IsNoOp)
                return true;

            _repeatPostAction = null;
            InvalidateNavigationCaches();
            ApplyWorkbookSessionSelectionToRenderer();
            SyncWindowViewState(viewStateSheetIds ?? [_currentSheetId]);
            NotifyOtherWindowsOfWorkbookChange();
            return true;
        }

        ShowCommandError(outcome, title);
        return false;
    }

    private bool TryExecuteWorksheetLayout(Func<WorkbookCellEditResult> execute, string title) =>
        TryExecuteWorksheetLayout(execute, title, out _);
}
