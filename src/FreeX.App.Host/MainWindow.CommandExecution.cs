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
        outcome = _commandBus.Execute(_workbook.Id, command);
        RecordDiagnosticEvent("command_invoked", new Dictionary<string, string?>
        {
            ["command"] = title,
            ["status"] = outcome.Success ? "succeeded" : "failed"
        });
        if (outcome.Success)
        {
            if (outcome.IsNoOp)
                return true;

            MarkWorkbookDirty();
            InvalidateNavigationCaches();
            NotifyOtherWindowsOfWorkbookChange();
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
        outcome = _commandBus.ExecuteRepeatable(_workbook.Id, commandFactory);
        RecordDiagnosticEvent("command_invoked", new Dictionary<string, string?>
        {
            ["command"] = title,
            ["status"] = outcome.Success ? "succeeded" : "failed"
        });
        if (outcome.Success)
        {
            if (outcome.IsNoOp)
                return true;

            MarkWorkbookDirty();
            _repeatPostAction = null;
            InvalidateNavigationCaches();
            NotifyOtherWindowsOfWorkbookChange();
            return true;
        }

        ShowCommandError(outcome, title);
        return false;
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
        var targetSheetIds = CurrentGroupedEditSheetIds();
        IWorkbookCommand command = targetSheetIds.Count > 1
            ? new GroupedEditCellsCommand(targetSheetIds, _currentSheetId, edits)
            : new EditCellsCommand(_currentSheetId, edits);
        return TryExecuteCommand(command, title, out outcome);
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

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        if (outcome.Success)
        {
            if (outcome.IsNoOp)
                return true;

            MarkWorkbookDirty();
            _repeatPostAction = null;
            InvalidateNavigationCaches();
            NotifyOtherWindowsOfWorkbookChange();
            return true;
        }

        ShowCommandError(outcome, title);
        return false;
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

        outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateRepeatCommand);
        if (outcome.Success)
        {
            MarkWorkbookDirty();
            _repeatPostAction = null;
            InvalidateNavigationCaches();
            NotifyOtherWindowsOfWorkbookChange();
            return true;
        }

        ShowCommandError(outcome, title);
        return false;
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

        outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateRepeatCommand);
        if (outcome.Success)
        {
            MarkWorkbookDirty();
            _repeatPostAction = null;
            InvalidateNavigationCaches();
            NotifyOtherWindowsOfWorkbookChange();
            return true;
        }

        ShowCommandError(outcome, title);
        return false;
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

        outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateRepeatCommand);
        if (outcome.Success)
        {
            MarkWorkbookDirty();
            _repeatPostAction = null;
            InvalidateNavigationCaches();
            NotifyOtherWindowsOfWorkbookChange();
            return true;
        }

        ShowCommandError(outcome, title);
        return false;
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
        Func<ChartModel, ChartLayoutOptions> optionsFactory)
    {
        IWorkbookCommand CreateCommand()
        {
            var chart = GetFirstChartOnCurrentSheet();
            if (chart is null)
                return new FailedWorkbookCommand(missingMessage);
            if (canApply is not null && !canApply(chart))
                return new FailedWorkbookCommand(unsupportedMessage ?? UiText.Get("MainWindowMessage_UnsupportedChartCommand"));
            return new SetChartLayoutCommand(_currentSheetId, chart.Id, optionsFactory(chart));
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        if (outcome.Success)
        {
            MarkWorkbookDirty();
            _repeatPostAction = null;
            NotifyOtherWindowsOfWorkbookChange();
            return true;
        }

        ShowCommandError(outcome, caption);
        return false;
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
        if (!TryExecuteCommand(new SetChartLayoutCommand(_currentSheetId, chart.Id, options), caption))
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

    private bool ExecuteUndo()
    {
        var outcome = _commandBus.Undo(_workbook.Id);
        if (!outcome.Success)
            return false;

        // After undo, check whether the stack has returned to the save point.
        // If so, restore the clean state; otherwise mark dirty. The version check (in addition
        // to the raw depth) guards against a trim-then-refill aliasing the save-point depth with
        // different entries than were actually on the stack at save time.
        var undoDepthNow = _commandBus.GetUndoStackDepth(_workbook.Id);
        var undoStackVersionNow = _commandBus.GetUndoStackVersion(_workbook.Id);
        if (!_documentState.TryMarkCleanIfAtSavePoint(undoDepthNow, undoStackVersionNow))
            MarkWorkbookDirty();
        else
        {
            // Cleaned via save-point — still update title bar and fan out.
            UpdateTitleBar();
            _windowRegistry?.NotifyDocumentStateChanged(this);
        }

        InvalidateNavigationCaches();
        RecalculateAfterCommandOutcome(outcome);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        NotifyOtherWindowsOfWorkbookChange();
        return true;
    }

    private bool ExecuteRedo()
    {
        var outcome = _commandBus.Redo(_workbook.Id);
        if (!outcome.Success)
            return false;

        // After redo, check whether the stack has returned to the save point.
        // If so, restore the clean state; otherwise mark dirty. The version check (in addition
        // to the raw depth) guards against a trim-then-refill aliasing the save-point depth with
        // different entries than were actually on the stack at save time.
        var undoDepthNow = _commandBus.GetUndoStackDepth(_workbook.Id);
        var undoStackVersionNow = _commandBus.GetUndoStackVersion(_workbook.Id);
        if (!_documentState.TryMarkCleanIfAtSavePoint(undoDepthNow, undoStackVersionNow))
            MarkWorkbookDirty();
        else
        {
            // Cleaned via save-point — still update title bar and fan out.
            UpdateTitleBar();
            _windowRegistry?.NotifyDocumentStateChanged(this);
        }

        InvalidateNavigationCaches();
        RecalculateAfterCommandOutcome(outcome);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        NotifyOtherWindowsOfWorkbookChange();
        return true;
    }

    private void ExecuteRepeatLast()
    {
        var postAction = _repeatPostAction;
        var outcome = _commandBus.RepeatLast(_workbook.Id);
        if (!outcome.Success) return;
        MarkWorkbookDirty();
        InvalidateNavigationCaches();
        postAction?.Invoke(outcome);
        RecalculateAfterCommandOutcome(outcome);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        NotifyOtherWindowsOfWorkbookChange();
    }

    private IWorkbookCommand CreateSingleCellEditCommand(CellAddress address, Cell cell)
    {
        var edits = new List<(CellAddress Address, Cell NewCell)> { (address, cell) };
        var targetSheetIds = CurrentGroupedEditSheetIds();
        return targetSheetIds.Count > 1
            ? new GroupedEditCellsCommand(targetSheetIds, _currentSheetId, edits)
            : new EditCellsCommand(_currentSheetId, edits);
    }

    private void RecalculateAfterCommandOutcome(CommandOutcome outcome)
    {
        if (outcome.AffectedCells is { Count: > 0 } affectedCells)
            RecalculateIfAutomatic(affectedCells);
        else
            RecalculateWorkbook();
    }
}
