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
        _session.MarkDirtyFromHost();
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
        _session.MarkSavedFromHost();
        UpdateTitleBar();
        // Fan out to this document's sibling views so they also reflect the saved (clean) state.
        _windowRegistry?.NotifyDocumentStateChanged(this);
        NotifyAutosaveSaved();
    }

    /// <summary>
    /// Replaces this window's document owner while preserving its WPF command/recalc adapters.
    /// An outgoing shared session stays alive until its remaining sibling views close.
    /// </summary>
    private void ReplaceWorkbookSession(StartupWorkbookLoadResult source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var previousSession = _session;
        _workbookRef.Current = source.Workbook;
        _session = _sessionFactory.CreateHostOwned(
            source,
            _commandBus,
            _recalcEngine,
            _viewportService,
            _fileAdapters,
            new Free.Shared.AppServices.WorkbookDocumentState(),
            viewportHeight: Math.Max(1, SheetGrid?.ActualHeight ?? 1),
            viewportWidth: Math.Max(1, SheetGrid?.ActualWidth ?? 1),
            includeObjects: true);
        _currentSheetId = _session.ActiveSheet.Id;
        ConfigureWorkbookSessionRendererAdapters();
        previousSession.Dispose();
    }

    private async Task<SaveChangesConfirmation> ConfirmSaveBeforeDestructiveActionAsync(string message)
    {
        return await _fileWorkflow.ConfirmBeforeDestructiveActionAsync(
            _workbookDirty,
            () => Task.FromResult(PromptSaveChangesBeforeDestructiveAction(message)),
            SaveResolvedAsync);
    }

    private Task<bool> CanProceedAfterSaveBeforeDestructiveActionAsync(string message) =>
        _fileWorkflow.CanProceedAfterDirtyGateWithCleanSaveAsync(
            _workbookDirty,
            () => Task.FromResult(PromptSaveChangesBeforeDestructiveAction(message)),
            SaveResolvedAsync,
            () => _workbookDirty);

    /// <summary>
    /// Runs a Save, resolving Save-vs-Save-As through the shared <see cref="FileLifecyclePlanner.PlanSave"/>
    /// decision: an existing usable path saves directly to it; otherwise the Save-As dialog is shown.
    /// The concrete <see cref="FileSaveTarget"/> (path + adapter) is produced by FreeX's adapter-resolving
    /// <see cref="FileSavePlanner.TryResolveExistingPath"/> -- unless the session was marked read-only
    /// by <see cref="ApplyReadOnlyRecommendedPromptIfNeeded"/>, in which case
    /// <see cref="ResolveExistingSaveTarget"/> withholds the existing path so this falls through to the
    /// Save-As dialog instead of silently overwriting the original file (Excel parity: Ctrl+S on a
    /// Read-Only-Recommended/write-reservation workbook is always forced through Save-As). Shared
    /// between the dirty-gate's "Save then proceed" branch and <c>SaveButton_Click</c>.
    /// </summary>
    private async Task<bool> SaveResolvedAsync()
    {
        return await _fileWorkflow.SaveResolvedAsync(
            _workbookDirty,
            _currentFilePath,
            ResolveExistingSaveTarget,
            SaveWorkbookToTargetAsync,
            SaveWorkbookWithDialogAsync);
    }

    /// <summary>
    /// The existing-path save target, or <c>null</c> if there is none usable OR this session was
    /// marked read-only by <see cref="ApplyReadOnlyRecommendedPromptIfNeeded"/> -- see
    /// <see cref="SaveResolvedAsync"/> for why a read-only session must never resolve back to its
    /// original path.
    /// </summary>
    private FileSaveTarget? ResolveExistingSaveTarget() =>
        !_isWorkbookReadOnly
            ? _fileWorkflow.ResolveExistingSaveTarget(_currentFilePath)
            : null;

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
        // A Save/Save-As or File>Open may still be running on a background thread while the
        // workbook happens to read as clean at this instant (a brand-new Book1, or a workbook
        // that was already saved and hasn't been re-edited since) -- MarkWorkbookSaved() (open)
        // and the save-completion dirty handling in SaveWorkbookToTargetAsync only run deep
        // inside the awaited body, AFTER the write/read completes. Without this guard the
        // dirty-gate fast path below would let PrepareActiveWorkbookForFinalClose() run and the
        // window close immediately while that save/open Task is still in flight -- and under the
        // default WPF ShutdownMode.OnLastWindowClose, closing the last window shuts the whole
        // process down mid-I/O. Mirrors the Avalonia shell's own
        // `if (_isOpening || _isSaving) { e.Cancel = true; ... }` guard in its MainWindow_Closing
        // (R120-app-host-close-during-save-open).
        if (_isSavingFile || _isOpeningFile)
        {
            e.Cancel = true;
            ShowOwnedMessage(
                UiText.Get("MainWindowMessage_FinishSaveOrOpenBeforeClosing"),
                UiText.Get("MainWindowMessage_SaveChangesTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

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
        _worksheetViewStates.Clear();
        _groupedSheetIds.Clear();
        _formulaTraceArrows.Clear();
        _splitPaneViewportOffsets.Clear();
        _statusBarStatsCache.Clear();
        _statusBarDisplayStateCache.Clear();
        _sparklineValueCache.Clear();
        _toolbarVisualStateCache.Clear();

    }

    private void ReleaseWorkbookUiStateForClose()
    {
        // Stop the resize-debounce timer, mirroring the autosave timer's stop-on-close. Its Tick
        // closure captures this window, so a pending tick both kept the closed window (and its grid)
        // rooted for the life of the process and re-entered UpdateViewport on torn-down state.
        CancelPendingViewportResizeRefresh();
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
