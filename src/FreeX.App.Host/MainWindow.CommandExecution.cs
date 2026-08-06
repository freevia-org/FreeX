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

    /// <summary>
    /// Refreshes any linked/Camera picture (Paste Special &gt; Linked Picture,
    /// <see cref="PictureModel.IsLinkedToSourceRange"/>) whose source range overlaps
    /// <paramref name="affectedCells"/>, rebuilding its cached cell snapshot from the live sheet
    /// (R90-app-camera-picture-link-5-1). Before this, the WPF host never refreshed a linked
    /// picture after the initial paste except via
    /// RowColumnShiftHelpers.RefreshLinkedPictureSnapshot (Core.Commands), which only fires when a
    /// structural row/column insert/delete actually moves the source range's coordinates -- an
    /// ordinary value, fill/border, or dependent-formula-recalculation edit inside the range left
    /// the picture showing stale, paste-time content forever. Mirrors
    /// FreeX.App.Services.WorkbookSession's RefreshLinkedPicturesForEditedCells/
    /// RefreshLinkedPictureCells (the equivalent refresh already performed by the Avalonia shell)
    /// so both shells keep a linked picture's rendered content live. Called from every successful
    /// edit-affecting command outcome in this file (with <see cref="CommandOutcome.AffectedCells"/>)
    /// AND from <see cref="RecalculateIfAutomatic"/> (MainWindow.WorkbookUiState.cs, with the
    /// RecalcEngine's own cascaded <c>RecalcReport.RecalculatedCells</c>) so a formula cell inside a
    /// linked picture's source range that only changes because some other, out-of-range cell it
    /// depends on was edited also keeps the picture live (R91-print-twin-two-tier-synthetic-sweep-3).
    /// </summary>
    private void RefreshLinkedPicturesAffectedBy(IReadOnlyList<CellAddress>? affectedCells)
    {
        if (affectedCells is not { Count: > 0 })
            return;

        foreach (var sheet in _workbook.Sheets)
        {
            if (sheet.Pictures.Count == 0)
                continue;

            foreach (var picture in sheet.Pictures)
            {
                if (!picture.IsLinkedToSourceRange || picture.LinkedSourceRange is not { } sourceRange)
                    continue;

                var sourceSheet = _workbook.GetSheet(sourceRange.Start.Sheet);
                if (sourceSheet is null)
                    continue;

                var touched = false;
                foreach (var edited in affectedCells)
                {
                    if (edited.Sheet.Equals(sourceRange.Start.Sheet) &&
                        edited.Row >= sourceRange.Start.Row && edited.Row <= sourceRange.End.Row &&
                        edited.Col >= sourceRange.Start.Col && edited.Col <= sourceRange.End.Col)
                    {
                        touched = true;
                        break;
                    }
                }
                if (!touched)
                    continue;

                RefreshLinkedPictureCellsFromLiveSheet(picture, sourceSheet, sourceRange);
            }
        }
    }

    /// <summary>Rebuilds a linked picture's cached cell snapshot from the live contents of its source range.</summary>
    private void RefreshLinkedPictureCellsFromLiveSheet(PictureModel picture, Sheet sourceSheet, GridRange sourceRange)
    {
        picture.SourceRowCount = sourceRange.RowCount;
        picture.SourceColumnCount = sourceRange.ColCount;

        picture.Cells.Clear();
        for (var row = sourceRange.Start.Row; row <= sourceRange.End.Row; row++)
        {
            for (var col = sourceRange.Start.Col; col <= sourceRange.End.Col; col++)
            {
                var cell = sourceSheet.GetCell(row, col);
                var styleId = cell?.StyleId ?? sourceSheet.GetStyleOnly(row, col) ?? StyleId.Default;
                var style = _workbook.GetStyle(styleId);
                var value = cell?.Value ?? BlankValue.Instance;

                picture.Cells.Add(new PictureCellSnapshot(
                    row - sourceRange.Start.Row,
                    col - sourceRange.Start.Col,
                    DrawingInputParser.FormatPictureCellText(value),
                    style.Clone(),
                    value is NumberValue or DateTimeValue));
            }
        }
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
            RefreshLinkedPicturesAffectedBy(outcome.AffectedCells);
            // A successful command may have changed the current sheet's view mode/zoom (directly,
            // via SetWorksheetViewModeCommand/SetWorksheetZoomCommand, or via a screenshot-tour
            // helper that constructs those commands itself instead of going through
            // MainWindow.ViewCommands.cs). Resync THIS window's own view-state cache from
            // whatever the current sheet now holds so it can never drift from what this window's
            // own command just applied (R83-app-view-modes-5-1); a no-op for every other command,
            // since their view fields are unchanged. This single choke point covers every
            // TryExecuteCommand/TryExecuteGroupedSheetCommand caller, so the more specific
            // grouped-sheet resyncs elsewhere only need to cover the OTHER grouped sheet ids.
            SyncWindowViewState([_currentSheetId]);
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
            RefreshLinkedPicturesAffectedBy(outcome.AffectedCells);
            // See TryExecuteCommand above (R83-app-view-modes-5-1).
            SyncWindowViewState([_currentSheetId]);
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

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        if (outcome.Success)
        {
            if (outcome.IsNoOp)
                return true;

            MarkWorkbookDirty();
            _repeatPostAction = null;
            InvalidateNavigationCaches();
            RefreshLinkedPicturesAffectedBy(outcome.AffectedCells);
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
            if (outcome.IsNoOp)
                return true;

            MarkWorkbookDirty();
            _repeatPostAction = null;
            InvalidateNavigationCaches();
            RefreshLinkedPicturesAffectedBy(outcome.AffectedCells);
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
            RefreshLinkedPicturesAffectedBy(outcome.AffectedCells);
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

    /// <summary>
    /// R124-cellscmds-multiarea-rowheight-1: non-repeatable counterpart of
    /// TryExecuteRepeatableCurrentSelectionRangesCommand above, for callers (AutoFit Row Height/Column
    /// Width) that have never participated in F4 repeat. Builds one command per disjoint selection
    /// area (SheetGrid.SelectedRanges from a Ctrl+click multi-area header selection, falling back to
    /// the single active range) crossed with every grouped-edit sheet, instead of dropping every area
    /// but the active one the way a plain SheetGrid.SelectedRange read would.
    /// </summary>
    private bool TryExecuteCurrentSelectionRangesCommand(
        string title,
        GridRange fallbackRange,
        Func<SheetId, GridRange, IWorkbookCommand> createCommand,
        out CommandOutcome outcome)
    {
        var ranges = GetCurrentSelectionRanges(fallbackRange);
        var command = SelectionStyleCommandPlanner.CreateRangeCommand(
            CurrentGroupedEditSheetIds(),
            ranges,
            createCommand,
            title);
        return TryExecuteCommand(command, title, out outcome);
    }

    private bool TryExecuteCurrentSelectionRangesCommand(
        string title,
        GridRange fallbackRange,
        Func<SheetId, GridRange, IWorkbookCommand> createCommand) =>
        TryExecuteCurrentSelectionRangesCommand(title, fallbackRange, createCommand, out _);

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
            RefreshLinkedPicturesAffectedBy(outcome.AffectedCells);
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

    /// <summary>
    /// R124-outlinecmds-multiarea-group-1: like TryExecuteRepeatableCurrentRangeCommand above, but
    /// builds one command per disjoint selection area -- SheetGrid.SelectedRanges from a Ctrl+click
    /// multi-area row/column header selection, via the same GetCurrentSelectionRanges/
    /// SelectionStyleCommandPlanner.ResolveRanges choke point the AutoFit Row Height/Column Width
    /// multi-area fix uses (R124-cellscmds-multiarea-rowheight-1) -- instead of dropping every area
    /// but the active one. Used by Group/Ungroup (Data - Outline), which are single-sheet-only
    /// operations with no CurrentGroupedEditSheetIds() fan-out, so this only spans the ranges, not
    /// sheets.
    /// </summary>
    private bool TryExecuteRepeatableCurrentRangesCommand(
        string title,
        GridRange fallbackRange,
        Func<GridRange, IWorkbookCommand> createCommand,
        out CommandOutcome outcome)
    {
        IWorkbookCommand CreateRepeatCommand()
        {
            var ranges = GetCurrentSelectionRanges(fallbackRange);
            var commands = ranges.Select(createCommand).ToList();
            return commands.Count == 1 ? commands[0] : new CompositeWorkbookCommand(title, commands);
        }

        outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateRepeatCommand);
        if (outcome.Success)
        {
            MarkWorkbookDirty();
            _repeatPostAction = null;
            InvalidateNavigationCaches();
            RefreshLinkedPicturesAffectedBy(outcome.AffectedCells);
            NotifyOtherWindowsOfWorkbookChange();
            return true;
        }

        ShowCommandError(outcome, title);
        return false;
    }

    private bool TryExecuteRepeatableCurrentRangesCommand(
        string title,
        GridRange fallbackRange,
        Func<GridRange, IWorkbookCommand> createCommand) =>
        TryExecuteRepeatableCurrentRangesCommand(title, fallbackRange, createCommand, out _);

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

            // The workbook is clean again, but any autosave snapshot written while it was dirty
            // (between the save point and this undo) is now stale — it reflects edits that have
            // been undone away. Delete it so a later crash (even one before any new edit) does
            // not surface stale, already-superseded content as a false "recover unsaved changes?"
            // prompt for a document the user believes was never left dirty (M10).
            NotifyAutosaveSaved();
        }

        InvalidateNavigationCaches();
        RecalculateAfterCommandOutcome(outcome);
        RefreshLinkedPicturesAffectedBy(outcome.AffectedCells);
        // R88-commands-undo-redo-coalescing-5-1: Excel switches the active sheet and reselects the
        // edited range so the user immediately sees what was reverted, even when they had navigated
        // away to a different sheet (or a different part of the current sheet) before pressing
        // Ctrl+Z. Must run before SyncWindowViewState/UpdateViewport below so both act on the
        // now-current sheet.
        RestoreSelectionAfterUndoRedo(outcome);
        // Undo can revert a view-mode/zoom change THIS window itself made; re-adopt the current
        // sheet's now-reverted values into this window's own view-state cache before rendering,
        // or the stale cached override would mask the undo (R83-app-view-modes-5-1).
        SyncWindowViewState([_currentSheetId]);
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

            // Same rationale as ExecuteUndo(): the stale dirty-period autosave snapshot must be
            // deleted now that we are back at the save point, or a later crash offers stale
            // content for a document that was never actually left dirty (M10).
            NotifyAutosaveSaved();
        }

        InvalidateNavigationCaches();
        RecalculateAfterCommandOutcome(outcome);
        RefreshLinkedPicturesAffectedBy(outcome.AffectedCells);
        // See ExecuteUndo() (R88-commands-undo-redo-coalescing-5-1): re-navigate to the
        // affected sheet/range before SyncWindowViewState/UpdateViewport below.
        RestoreSelectionAfterUndoRedo(outcome);
        // Redo can re-apply a view-mode/zoom change THIS window itself made; re-adopt the
        // current sheet's values before rendering (see ExecuteUndo).
        SyncWindowViewState([_currentSheetId]);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        NotifyOtherWindowsOfWorkbookChange();
        return true;
    }

    private void ExecuteRepeatLast()
    {
        // R71-services-undo-redo-4-1: Excel treats F4/Ctrl+Y as REDO whenever a redo is pending
        // (redo takes priority over repeat). Without this gate, F4 after an Undo would re-invoke
        // the stale repeatable factory against whatever is now selected AND destroy the pending
        // redo entry (Execute clears the redo stack), permanently losing the undone change.
        if (_commandBus.CanRedo(_workbook.Id))
        {
            ExecuteRedo();
            return;
        }

        var postAction = _repeatPostAction;
        var outcome = _commandBus.RepeatLast(_workbook.Id);
        if (!outcome.Success) return;
        MarkWorkbookDirty();
        InvalidateNavigationCaches();
        postAction?.Invoke(outcome);
        RecalculateAfterCommandOutcome(outcome);
        RefreshLinkedPicturesAffectedBy(outcome.AffectedCells);
        // See TryExecuteCommand above (R83-app-view-modes-5-1).
        SyncWindowViewState([_currentSheetId]);
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

    /// <summary>
    /// R88-commands-undo-redo-coalescing-5-1: single choke point for both <see cref="ExecuteUndo"/>
    /// and <see cref="ExecuteRedo"/> to switch the active sheet and reselect the affected range,
    /// matching real Excel (which always brings the just-undone/redone edit back into view even when
    /// the user had navigated to a different sheet, or a different part of the current sheet, since
    /// the edit was made). No-ops when the outcome carries no affected cells (nothing to navigate
    /// to), leaving today's behavior unchanged for those outcomes.
    /// </summary>
    private void RestoreSelectionAfterUndoRedo(CommandOutcome outcome)
    {
        // R124-app-drawing-undo-selection-1: Undo of Delete Drawing Object (or Redo of it) carries a
        // hint from CommandBus (see IDrawingObjectDeletionCommand) telling us to sync the host's
        // object-selection fields, not just the plain cell-range selection below. Applied first so
        // the cell-range branch below (which still runs, to land the active cell/viewport on the
        // object's anchor exactly as it did before this fix) doesn't get short-circuited, and so a
        // stale selection is cleared even if AffectedCells is somehow empty.
        ApplyDrawingObjectSelectionHint(outcome.DrawingObjectSelection);

        if (outcome.AffectedCells is not { Count: > 0 } affectedCells)
            return;

        // A single command's affected cells always belong to one sheet, EXCEPT a
        // CompositeWorkbookCommand fanned out across a grouped-sheet edit (Commands.cs); in that
        // case land on the sheet the user was already viewing if it was one of the affected sheets,
        // otherwise fall back to the first affected cell's sheet (mirrors Excel picking the sheet
        // that contains the edit when the previous active sheet had no part in it).
        var targetSheetId = affectedCells[0].Sheet;
        foreach (var candidate in affectedCells)
        {
            if (candidate.Sheet.Equals(_currentSheetId))
            {
                targetSheetId = _currentSheetId;
                break;
            }
        }

        uint minRow = uint.MaxValue, maxRow = uint.MinValue, minCol = uint.MaxValue, maxCol = uint.MinValue;
        foreach (var address in affectedCells)
        {
            if (!address.Sheet.Equals(targetSheetId))
                continue;

            if (address.Row < minRow) minRow = address.Row;
            if (address.Row > maxRow) maxRow = address.Row;
            if (address.Col < minCol) minCol = address.Col;
            if (address.Col > maxCol) maxCol = address.Col;
        }

        if (minRow == uint.MaxValue)
            return;

        var previousSheetId = _currentSheetId;
        _currentSheetId = targetSheetId;
        var start = new CellAddress(targetSheetId, minRow, minCol);
        var end = new CellAddress(targetSheetId, maxRow, maxCol);
        SetSelectionRange(new GridRange(start, end), start);
        EnsureCellVisible(start);

        if (!targetSheetId.Equals(previousSheetId))
            RefreshSheetTabs();
    }

    // R124-app-drawing-undo-selection-1: applies the DrawingObjectSelectionHint CommandBus attaches
    // to the Undo/Redo outcome of a DeleteDrawingObjectCommand (see IDrawingObjectDeletionCommand).
    // hint.Exists is true right after Undo re-inserted the object -- select it, with handles, exactly
    // as Excel does. hint.Exists is false right after Redo re-deleted it -- if the (now stale)
    // selection still names this object, clear it rather than leaving the ribbon's contextual
    // Picture/Shape/Chart Format tab active for an object that no longer exists. The "still names
    // this object" guard matters because between the Undo and the Redo the user could have selected a
    // *different* object or a plain cell range; a blind clear would wrongly wipe that unrelated
    // selection.
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

    private void RecalculateAfterCommandOutcome(CommandOutcome outcome)
    {
        if (outcome.AffectedCells is { Count: > 0 } affectedCells)
            RecalculateIfAutomatic(affectedCells);
        else
            RecalculateWorkbook();
    }
}
