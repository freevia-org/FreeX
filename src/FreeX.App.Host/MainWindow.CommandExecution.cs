using System.Collections.Generic;
using System.Linq;
using System.Windows;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void ShowCommandError(CommandOutcome outcome, string title)
    {
        if (outcome.Success) return;

        _messageService.ShowWarning(
            LocalizeCommandErrorMessage(outcome.ErrorMessage),
            UiText.Get("MainWindowMessage_CommandErrorTitle"));
    }

    internal static string LocalizeCommandErrorMessage(string? message)
    {
        var normalizedMessage = CommandFailureMessages.NormalizeForPresentation(message);
        return normalizedMessage switch
        {
            null => UiText.Get("MainWindowMessage_CommandCouldNotBeCompleted"),
            "Picture was not found." => UiText.Get("MainWindowMessage_PictureWasNotFound"),
            "Sheet not found." => UiText.Get("MainWindowMessage_SheetNotFound"),
            _ => normalizedMessage
        };
    }

    private bool TryExecuteCommand(IWorkbookCommand command, string title, out CommandOutcome outcome)
    {
        SynchronizeWorkbookSessionSelection();
        var result = _session.ExecuteCommandPreservingSelection(command);
        outcome = ToCommandOutcome(result);
        RecordDiagnosticEvent("command_invoked", new Dictionary<string, string?>
        {
            ["command"] = title,
            ["status"] = outcome.Success ? "succeeded" : "failed"
        });
        if (outcome.Success)
        {
            if (outcome.IsNoOp)
                return true;

            ApplySuccessfulWorkbookSessionCommand();
            return true;
        }

        ShowCommandError(outcome, title);
        return false;
    }

    private bool TryExecuteCommand(IWorkbookCommand command, string title) =>
        TryExecuteCommand(command, title, out _);

    private bool TryExecuteRepeatableCommand(
        Func<IWorkbookCommand> commandFactory,
        string title,
        out CommandOutcome outcome)
    {
        SynchronizeWorkbookSessionSelection();
        var result = _session.ExecuteRepeatableCommandPreservingSelection(commandFactory);
        outcome = ToCommandOutcome(result);
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
            ApplySuccessfulWorkbookSessionCommand();
            return true;
        }

        ShowCommandError(outcome, title);
        return false;
    }

    private CommandOutcome ExecuteDialogCommandPreservingSelection(IWorkbookCommand command)
    {
        SynchronizeWorkbookSessionSelection();
        var result = _session.ExecuteCommandPreservingSelection(command);
        if (result.Success && !result.IsNoOp)
            ApplySuccessfulWorkbookSessionCommand();
        return ToCommandOutcome(result);
    }

    private CommandOutcome ExecuteCustomViewDialogCommand(IWorkbookCommand command)
    {
        SynchronizeWorkbookSessionSelection();
        var result = _session.ExecuteCustomViewCommand(command);
        if (result.Success && !result.IsNoOp)
            ApplySuccessfulWorkbookSessionCommand();
        return ToCommandOutcome(result);
    }

    private void ApplySuccessfulWorkbookSessionCommand()
    {
        InvalidateNavigationCaches();
        ApplyWorkbookSessionSelectionToRenderer();
        // Commands can mutate view mode/zoom through generic or screenshot-tour paths. Resync
        // this window's view-state cache from the authoritative workbook after every real edit.
        SyncWindowViewState([_currentSheetId]);
        NotifyOtherWindowsOfWorkbookChange();
    }

    private bool TryExecuteWorksheetStructure(
        Func<WorkbookWorksheetStructureResult> execute,
        out WorkbookWorksheetStructureResult result)
    {
        SynchronizeWorkbookSessionSelection();
        result = execute();
        return CompleteWorksheetSessionCommand(result.EditResult, result.CommandTitle);
    }

    private IReadOnlyList<SheetId> CurrentGroupedEditSheetIds()
    {
        var groupedVisibleSheets = _workbook.Sheets
            .Where(sheet => !sheet.IsHidden && _groupedSheetIds.Contains(sheet.Id))
            .Select(sheet => sheet.Id)
            .ToList();

        return groupedVisibleSheets.Count > 1 && groupedVisibleSheets.Contains(_currentSheetId)
            ? groupedVisibleSheets
            : [_currentSheetId];
    }

    private bool TryExecuteEditCells(
        IReadOnlyList<(CellAddress Address, Cell NewCell)> edits,
        string title,
        out CommandOutcome outcome)
    {
        // A formula edit can remain open while the user switches to a referenced worksheet. In
        // that state the displayed sheet is the pointing surface, while the edit addresses still
        // belong to the original formula sheet; route the command by the addresses instead of
        // accidentally writing the formula into the visible target sheet.
        var editSheetId = edits.Count > 0 ? edits[0].Address.Sheet : _currentSheetId;
        var targetSheetIds = CurrentGroupedEditSheetIds();
        IWorkbookCommand command = editSheetId == _currentSheetId && targetSheetIds.Count > 1
            ? new GroupedEditCellsCommand(targetSheetIds, _currentSheetId, edits)
            : new EditCellsCommand(editSheetId, edits);
        var executed = TryExecuteCommand(command, title, out outcome);

        // R54-render-copy-cut-marquee-4-1: Excel cancels an active Copy/Cut marching-ants mode
        // as soon as an ordinary cell edit is committed -- a subsequent Paste must not silently
        // move/copy a source range that the user has since overwritten. TryExecuteCommand itself
        // is a generic low-level executor shared by many unrelated command kinds (charts, styles,
        // print settings, ...), so the cancellation is scoped here, at the specific "committing a
        // normal cell edit" call site, rather than in the generic executor.
        if (executed && !outcome.IsNoOp && (_internalClipboard is not null || SheetGrid.ClipboardRange is not null))
        {
            _internalClipboard = null;
            ClearClipboardVisualState();
        }

        return executed;
    }

    private bool TryExecuteEditCells(
        IReadOnlyList<(CellAddress Address, Cell NewCell)> edits,
        string title) =>
        TryExecuteEditCells(edits, title, out _);

    private bool TryExecuteApplyStyle(GridRange range, StyleDiff diff, string title)
    {
        var command = SelectionStyleCommandPlanner.CreateApplyStyleCommand(
            CurrentGroupedEditSheetIds(),
            [range],
            diff,
            title);
        return TryExecuteCommand(command, title);
    }

    private bool TryExecuteRepeatableApplyStyle(StyleDiff diff, string title)
    {
        IWorkbookCommand CreateCommand()
        {
            var fallbackRange = new GridRange(
                new CellAddress(_currentSheetId, 1, 1),
                new CellAddress(_currentSheetId, 1, 1));
            var ranges = GetCurrentSelectionRanges(fallbackRange);
            return SelectionStyleCommandPlanner.CreateApplyStyleCommand(
                CurrentGroupedEditSheetIds(),
                ranges,
                diff,
                title);
        }

        return TryExecuteRepeatableCommand(CreateCommand, title, out _);
    }

    private IReadOnlyList<GridRange> GetCurrentSelectionRanges(GridRange? fallbackRange = null)
    {
        var ranges = SelectionStyleCommandPlanner.ResolveRanges(SheetGrid.SelectedRange, SheetGrid.SelectedRanges);
        if (ranges.Count > 0)
            return ranges;

        return fallbackRange is { } range ? [range] : [];
    }

    private bool TryExecuteRepeatableGroupedSheetCommand(
        string title,
        Func<SheetId, IWorkbookCommand> createCommand,
        out CommandOutcome outcome)
    {
        IWorkbookCommand CreateRepeatCommand()
        {
            var targetSheetIds = CurrentGroupedEditSheetIds();
            return targetSheetIds.Count > 1
                ? new CompositeWorkbookCommand(title, targetSheetIds.Select(createCommand).ToList())
                : createCommand(_currentSheetId);
        }

        return TryExecuteRepeatableCommand(CreateRepeatCommand, title, out outcome);
    }

    private bool TryExecuteRepeatableGroupedSheetCommand(
        string title,
        Func<SheetId, IWorkbookCommand> createCommand) =>
        TryExecuteRepeatableGroupedSheetCommand(title, createCommand, out _);

    private bool TryExecuteRepeatableCurrentSelectionRangesCommand(
        string title,
        GridRange fallbackRange,
        Func<SheetId, GridRange, IWorkbookCommand> createCommand,
        out CommandOutcome outcome)
    {
        IWorkbookCommand CreateRepeatCommand()
        {
            var ranges = GetCurrentSelectionRanges(fallbackRange);
            return SelectionStyleCommandPlanner.CreateRangeCommand(
                CurrentGroupedEditSheetIds(),
                ranges,
                createCommand,
                title);
        }

        return TryExecuteRepeatableCommand(CreateRepeatCommand, title, out outcome);
    }

    private bool TryExecuteRepeatableCurrentSelectionRangesCommand(
        string title,
        GridRange fallbackRange,
        Func<SheetId, GridRange, IWorkbookCommand> createCommand) =>
        TryExecuteRepeatableCurrentSelectionRangesCommand(title, fallbackRange, createCommand, out _);

    private bool TryExecuteRepeatableCurrentRangeCommand(
        string title,
        GridRange fallbackRange,
        Func<GridRange, IWorkbookCommand> createCommand,
        out CommandOutcome outcome)
    {
        IWorkbookCommand CreateRepeatCommand()
        {
            var range = SheetGrid.SelectedRange ?? fallbackRange;
            return createCommand(range);
        }

        return TryExecuteRepeatableCommand(CreateRepeatCommand, title, out outcome);
    }

    private bool TryExecuteRepeatableCurrentRangeCommand(
        string title,
        GridRange fallbackRange,
        Func<GridRange, IWorkbookCommand> createCommand) =>
        TryExecuteRepeatableCurrentRangeCommand(title, fallbackRange, createCommand, out _);

    private bool TryExecuteRepeatableChartLayout(
        string caption,
        string missingMessage,
        Func<ChartModel, bool>? canApply,
        string? unsupportedMessage,
        Func<ChartModel, ChartLayoutOptions> optionsFactory) =>
        TryExecuteRepeatableChartLayout(
            caption,
            missingMessage,
            unsupportedMessage,
            (sheetId, sheet, selectedChartId) => ChartCommandWorkflowPlanner.PlanLayoutCommand(
                sheetId,
                sheet,
                selectedChartId,
                ChartWorkflowTargetPolicy.SelectedOrFirst,
                optionsFactory,
                canApply));

    private bool TryExecuteRepeatableChartQuickCommand(
        string caption,
        string missingMessage,
        string? unsupportedMessage,
        ChartQuickCommandDescriptor command) =>
        TryExecuteRepeatableChartLayout(
            caption,
            missingMessage,
            unsupportedMessage,
            (sheetId, sheet, selectedChartId) => ChartCommandWorkflowPlanner.PlanQuickCommand(
                sheetId,
                sheet,
                selectedChartId,
                ChartWorkflowTargetPolicy.SelectedOrFirst,
                command));

    private bool TryExecuteRepeatableChartLayout(
        string caption,
        string missingMessage,
        string? unsupportedMessage,
        Func<SheetId, Sheet?, Guid?, ChartLayoutCommandPlan> planFactory)
    {
        IWorkbookCommand CreateCommand()
        {
            var sheet = _workbook.GetSheet(_currentSheetId);
            var plan = planFactory(_currentSheetId, sheet, GetSelectedChartIdOnCurrentSheet());
            if (plan.Command is not null)
                return plan.Command;

            return new FailedWorkbookCommand(
                plan.Issue == ChartLayoutCommandIssue.MissingChart
                    ? missingMessage
                    : unsupportedMessage ?? UiText.Get("MainWindowMessage_UnsupportedChartCommand"));
        }

        return TryExecuteRepeatableCommand(CreateCommand, caption, out _);
    }

    private ChartModel? GetFirstChartOnCurrentSheet()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        return ChartWorkflowTargetPlanner.FindSelectedOrFirstChart(sheet, GetSelectedChartIdOnCurrentSheet());
    }

    private ChartModel? GetSelectedChartOnCurrentSheet()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        return ChartWorkflowTargetPlanner.FindSelectedChart(sheet, GetSelectedChartIdOnCurrentSheet());
    }

    private Guid? GetSelectedChartIdOnCurrentSheet()
    {
        if (SheetGrid.SelectedObjectKind != FreeX.App.UI.ObjectKind.Chart ||
            SheetGrid.SelectedObjectId == Guid.Empty)
            return null;

        return SheetGrid.SelectedObjectId;
    }

    private bool TryGetFirstChartForDialog(string caption, string missingMessage, out ChartModel chart)
    {
        chart = GetFirstChartOnCurrentSheet()!;
        if (chart is not null)
            return true;

        ShowCommandError(new CommandOutcome(false, missingMessage), caption);
        return false;
    }

    private bool ApplyChartLayoutDialogResult(string caption, ChartModel chart, ChartLayoutOptions options)
    {
        if (!TryExecuteCommand(
                ChartCommandWorkflowPlanner.BuildLayoutCommand(_currentSheetId, chart, options),
                caption))
            return false;

        UpdateViewport();
        return true;
    }

    private bool TryExecuteGroupedSheetCommand(
        string title,
        Func<SheetId, IWorkbookCommand> createCommand,
        out CommandOutcome outcome)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        IWorkbookCommand command = targetSheetIds.Count > 1
            ? new CompositeWorkbookCommand(title, targetSheetIds.Select(createCommand).ToList())
            : createCommand(_currentSheetId);
        return TryExecuteCommand(command, title, out outcome);
    }

    private bool TryExecuteGroupedSheetCommand(
        string title,
        Func<SheetId, IWorkbookCommand> createCommand) =>
        TryExecuteGroupedSheetCommand(title, createCommand, out _);

    private static CommandOutcome ToCommandOutcome(WorkbookCellEditResult result) =>
        new(
            result.Success,
            result.ErrorMessage,
            result.AffectedCells,
            result.IsNoOp);

    private bool ExecuteUndo()
        => ApplyWorkbookSessionHistoryResult(_session.UndoLastEdit());

    private bool ExecuteRedo()
        => ApplyWorkbookSessionHistoryResult(_session.RedoLastEdit());

    private void ExecuteRepeatLast()
    {
        // R71-services-undo-redo-4-1: Excel treats F4/Ctrl+Y as REDO whenever a redo is pending
        // (redo takes priority over repeat). Without this gate, F4 after an Undo would re-invoke
        // the stale repeatable factory against whatever is now selected AND destroy the pending
        // redo entry (Execute clears the redo stack), permanently losing the undone change.
        if (_session.CanRedo)
        {
            ExecuteRedo();
            return;
        }

        SynchronizeWorkbookSessionSelection();
        var postAction = _repeatPostAction;
        var result = _session.RepeatLastAction();
        ApplyWorkbookSessionHistoryResult(
            result,
            () => postAction?.Invoke(new CommandOutcome(
                true,
                AffectedCells: result.AffectedCells)));
    }

    private IWorkbookCommand CreateSingleCellEditCommand(CellAddress address, Cell cell)
    {
        var edits = new List<(CellAddress Address, Cell NewCell)> { (address, cell) };
        var targetSheetIds = CurrentGroupedEditSheetIds();
        return targetSheetIds.Count > 1
            ? new GroupedEditCellsCommand(targetSheetIds, _currentSheetId, edits)
            : new EditCellsCommand(_currentSheetId, edits);
    }

    private bool ApplyWorkbookSessionHistoryResult(
        WorkbookCellEditResult result,
        Action? afterSelectionApplied = null)
    {
        if (!result.Success)
            return false;

        if (!result.IsNoOp)
            ClearClipboardMarqueeAfterStructuralEdit();

        ApplyDrawingObjectSelectionHint(result.DrawingObjectSelection);
        UpdateTitleBar();
        _windowRegistry?.NotifyDocumentStateChanged(this);
        if (!_session.IsDirty)
            NotifyAutosaveSaved();

        InvalidateNavigationCaches();
        ApplyWorkbookSessionSelectionToRenderer();
        afterSelectionApplied?.Invoke();
        SyncWindowViewState([_currentSheetId]);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        NotifyOtherWindowsOfWorkbookChange();
        return true;
    }

    private void ApplyDrawingObjectSelectionHint(DrawingObjectSelectionHint? hint)
    {
        if (hint is not { } value)
            return;

        if (value.Exists)
        {
            SheetGrid.SelectedObjectId = value.ObjectId;
            SheetGrid.SelectedObjectKind = ToUiObjectKind(value.Kind);
        }
        else if (SheetGrid.SelectedObjectId == value.ObjectId)
        {
            SheetGrid.SelectedObjectId = Guid.Empty;
            SheetGrid.SelectedObjectKind = FreeX.App.UI.ObjectKind.None;
        }
    }

    private static FreeX.App.UI.ObjectKind ToUiObjectKind(SelectionPaneObjectKind kind) => kind switch
    {
        SelectionPaneObjectKind.Chart => FreeX.App.UI.ObjectKind.Chart,
        SelectionPaneObjectKind.Picture => FreeX.App.UI.ObjectKind.Picture,
        SelectionPaneObjectKind.TextBox => FreeX.App.UI.ObjectKind.TextBox,
        SelectionPaneObjectKind.Shape => FreeX.App.UI.ObjectKind.Shape,
        _ => FreeX.App.UI.ObjectKind.None,
    };

}
