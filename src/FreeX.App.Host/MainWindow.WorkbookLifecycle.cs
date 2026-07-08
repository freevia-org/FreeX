using System.ComponentModel;
using System.Windows;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.App.Services;
using FreeX.App.UI;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void MarkWorkbookDirty()
    {
        // Delegates to this document's WorkbookDocumentState (shared by its "New Window" views).
        // MarkDirty() increments DirtyGeneration and sets IsDirty = true in one atomic step.
        _documentState.MarkDirty();
        UpdateTitleBar();
        // Fan out the title-bar refresh to this document's other views so they reflect
        // the dirty indicator without needing a full viewport refresh.
        _windowRegistry?.NotifyDocumentStateChanged(this);
    }

    private void MarkWorkbookSaved()
    {
        // Delegates to this document's WorkbookDocumentState (shared by its "New Window" views).
        // Record the undo-stack depth AND its monotonic version at save time so ExecuteUndo/Redo
        // can detect when the stack returns to the save point and clear the dirty flag cleanly.
        // The version (not just the depth) guards against the stack having been trimmed and
        // refilled to the same depth with different entries — see TryMarkCleanIfAtSavePoint.
        var undoDepth = _commandBus.GetUndoStackDepth(_workbook.Id);
        var undoStackVersion = _commandBus.GetUndoStackVersion(_workbook.Id);
        _documentState.MarkSavedAtUndoDepth(undoDepth, undoStackVersion);
        UpdateTitleBar();
        // Fan out to this document's sibling views so they also reflect the saved (clean) state.
        _windowRegistry?.NotifyDocumentStateChanged(this);
        NotifyAutosaveSaved();
    }

    private async Task<SaveChangesConfirmation> ConfirmSaveBeforeDestructiveActionAsync(string message)
    {
        return await WorkbookFileLifecycleCoordinator.ConfirmBeforeDestructiveActionAsync(
            _workbookDirty,
            () => Task.FromResult(PromptSaveChangesBeforeDestructiveAction(message)),
            SaveResolvedAsync);
    }

    private Task<bool> CanProceedAfterSaveBeforeDestructiveActionAsync(string message) =>
        WorkbookFileLifecycleCoordinator.CanProceedAfterDirtyGateWithCleanSaveAsync(
            _workbookDirty,
            () => Task.FromResult(PromptSaveChangesBeforeDestructiveAction(message)),
            SaveResolvedAsync,
            () => _workbookDirty);

    /// <summary>
    /// Runs a Save, resolving Save-vs-Save-As through the shared <see cref="FileLifecyclePlanner.PlanSave"/>
    /// decision: an existing usable path saves directly to it; otherwise the Save-As dialog is shown.
    /// The concrete <see cref="FileSaveTarget"/> (path + adapter) is produced by FreeX's adapter-resolving
    /// <see cref="FileSavePlanner.TryResolveExistingPath"/>. Shared between the dirty-gate's
    /// "Save then proceed" branch and <c>SaveButton_Click</c>.
    /// </summary>
    private async Task<bool> SaveResolvedAsync()
    {
        return await WorkbookFileLifecycleCoordinator.SaveResolvedAsync(
            _workbookDirty,
            _currentFilePath,
            _fileAdapters,
            SaveWorkbookToTargetAsync,
            SaveWorkbookWithDialogAsync);
    }

    private SaveChangesPrompt PromptSaveChangesBeforeDestructiveAction(string message)
    {
        var result = ShowOwnedMessage(
            message,
            UiText.Get("MainWindowMessage_SaveChangesTitle"),
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        return result switch
        {
            MessageBoxResult.Cancel => SaveChangesPrompt.Cancel,
            MessageBoxResult.No => SaveChangesPrompt.DontSave,
            _ => SaveChangesPrompt.Save
        };
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        // A sibling "New Window" view keeps the document (and its dirty state) alive, so closing
        // this view must not prompt to save — only the document's last view prompts (Excel parity).
        if (_suppressClosePrompt || !_workbookDirty || DocumentSharedWithOtherWindows())
        {
            PrepareActiveWorkbookForFinalClose();
            return;
        }

        e.Cancel = true;
        if (_closeAfterSaveInProgress)
            return;

        _closeAfterSaveInProgress = true;
        SaveChangesConfirmation confirmation;
        try
        {
            confirmation = await ConfirmSaveBeforeDestructiveActionAsync(UiText.Get("MainWindowMessage_SaveChangesBeforeClosingWorkbook"));
        }
        finally
        {
            _closeAfterSaveInProgress = false;
        }

        // Delegate the post-prompt decision to the pure planner so the logic is
        // independently unit-testable.  The dirty re-check is only applied when
        // confirmation == Continue (a save ran and edits may have arrived mid-save);
        // DiscardWithoutSaving proceeds to close unconditionally regardless of the
        // current dirty flag (the discard path never calls MarkWorkbookSaved).
        if (WindowCloseDecisionPlanner.Decide(confirmation, _workbookDirty) == WindowCloseAction.StayOpen)
            return;

        _suppressClosePrompt = true;
        PrepareActiveWorkbookForFinalClose();
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }

    /// <summary>
    /// True when this window is the last remaining view of ITS document — windows over other
    /// documents do not keep this one alive.  Must be called AFTER
    /// <c>_windowRegistry.Unregister(this)</c> has run (in
    /// <see cref="PrepareActiveWorkbookForFinalClose"/>), so the registry reflects
    /// the remaining windows rather than including this one.
    /// </summary>
    private bool IsFinalWorkbookWindowClose() =>
        _windowRegistry is null || !_windowRegistry.HasWindowForDocument(_workbook.Id);

    /// <summary>
    /// Bypasses the save-changes prompt on the next Close() call.
    /// Used by test infrastructure to cleanly tear down windows without triggering the dialog.
    /// Prefer this method over reflection-based field access.
    /// </summary>
    internal void SuppressNextClosePrompt() => _suppressClosePrompt = true;

    private void PrepareActiveWorkbookForFinalClose()
    {
        ReleaseWorkbookUiStateForClose();

        // Pre-unregister from the registry *before* the IsFinalWorkbookWindowClose()
        // check so this window is already excluded when we decide whether it is the last
        // view of its document.  This closes the concurrent-close race: if two views
        // close simultaneously, each pre-unregisters first; the view that sees no other
        // registered window for the document after its own pre-unregister is definitively
        // the last one.  Unregister is idempotent — the Closed handler calls it again safely.
        _windowRegistry?.Unregister(this);

        // A surviving "New Window" sibling still views this document: leave the workbook,
        // its loaded-package snapshot, and the shared document state untouched.
        if (!IsFinalWorkbookWindowClose())
            return;

        XlsxFileAdapter.ForgetLoadedPackageSnapshot(_workbook);
        _currentXlsxFeatureReport = null;
        _worksheetSelections.Clear();
        _groupedSheetIds.Clear();
        _formulaTraceArrows.Clear();
        _splitPaneViewportOffsets.Clear();
        _statusBarStatsCache.Clear();
        _statusBarDisplayStateCache.Clear();
        _sparklineValueCache.Clear();
        _toolbarVisualStateCache.Clear();

        var replacement = NewWorkbookFactory.Create(_options);
        _workbook = replacement;
        _workbookRef.Current = replacement;
        _currentSheetId = replacement.Sheets[0].Id;
        // If there are still sibling windows (unusual for a final-close path but
        // possible if IsFinalWorkbookWindowClose() was incorrect), notify them.
        NotifyOtherWindowsOfWorkbookChange();
    }

    private void ReleaseWorkbookUiStateForClose()
    {
        ClearFormulaReferenceHighlights();
        ClearClipboardVisualState();
        _internalClipboard = null;

        if (_validationDropdown is not null)
        {
            _validationDropdown.IsDropDownOpen = false;
            _validationDropdown.ItemsSource = null;
            _validationDropdown.Visibility = Visibility.Collapsed;
        }

        if (SheetGrid is not null)
        {
            SheetGrid.Viewport = null;
            SheetGrid.HiddenRows = null;
            SheetGrid.HiddenColumns = null;
            SheetGrid.SelectedRange = null;
            SheetGrid.ActiveCell = null;
            SheetGrid.SelectedRanges = null;
            SheetGrid.QuickAnalysisPreviewRange = null;
            SheetGrid.QuickAnalysisPreviewVisual = QuickAnalysisPreviewVisualKind.None;
            SheetGrid.EditingCell = null;
            SheetGrid.FormulaTraceArrows = null;
            SheetGrid.FormulaTraceSheetId = default;
            SheetGrid.HyperlinkCells = null;
            SheetGrid.Charts = null;
            SheetGrid.TextBoxes = null;
            SheetGrid.DrawingShapes = null;
            SheetGrid.WorkbookTheme = WorkbookTheme.Office;
            SheetGrid.Pictures = null;
            SheetGrid.DrawingObjectZOrder = null;
            SheetGrid.NativeSlicers = null;
            SheetGrid.NativeTimelines = null;
            SheetGrid.FormControls = null;
            SheetGrid.WorksheetBackground = null;
            SheetGrid.PivotHeaderDropdowns = null;
            SheetGrid.Sparklines = null;
            SheetGrid.SparklineValues = null;
            SheetGrid.MergedRegions = null;
            SheetGrid.RowPageBreaks = null;
            SheetGrid.ColumnPageBreaks = null;
            SheetGrid.PrintArea = null;
            SheetGrid.SplitRow = null;
            SheetGrid.SplitColumn = null;
            SheetGrid.SelectedObjectId = Guid.Empty;
            SheetGrid.SelectedObjectKind = ObjectKind.None;
            SheetGrid.ContextMenu = null;
        }

        _sheetTabs.Clear();
        if (SheetTabsControl is not null)
            SheetTabsControl.ItemsSource = null;

        _pendingPivotLayout = null;
        _pivotFieldListAvailableItems = [];
        _pivotHeaderDropdownTargets = new Dictionary<(uint Row, uint Col), PivotHeaderDropdownTarget>();
        if (PivotFieldListPane is not null)
            PivotFieldListPane.Visibility = Visibility.Collapsed;
        if (PivotAvailableFieldsList is not null)
            PivotAvailableFieldsList.ItemsSource = null;
        if (PivotRowsList is not null)
            PivotRowsList.ItemsSource = null;
        if (PivotColumnsList is not null)
            PivotColumnsList.ItemsSource = null;
        if (PivotFiltersList is not null)
            PivotFiltersList.ItemsSource = null;
        if (PivotValuesList is not null)
            PivotValuesList.ItemsSource = null;

        _slicerTimelinePaneDismissed = false;
        if (SlicerTimelinePane is not null)
            SlicerTimelinePane.Visibility = Visibility.Collapsed;
        if (SlicerItemsControl is not null)
            SlicerItemsControl.ItemsSource = null;
        if (TimelineItemsControl is not null)
            TimelineItemsControl.ItemsSource = null;

        _lastViewportTableContextRefreshKey = null;
        _lastViewportChartContextRefreshKey = null;
        _lastViewportDrawingObjectContextRefreshKey = null;
        _lastViewportPivotFieldListRefreshKey = null;
        _lastViewportSlicerTimelineRefreshKey = null;
    }
}
