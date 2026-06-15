using System.ComponentModel;
using System.Windows;
using FreeX.App.UI;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void MarkWorkbookDirty()
    {
        // Delegates to the shared (singleton) WorkbookDocumentState.
        // MarkDirty() increments DirtyGeneration and sets IsDirty = true in one atomic step.
        _documentState.MarkDirty();
        UpdateTitleBar();
        // Fan out the title-bar refresh to every other window so all windows reflect
        // the dirty indicator without needing a full viewport refresh.
        _windowRegistry?.NotifyDocumentStateChanged();
    }

    private void MarkWorkbookSaved()
    {
        // Delegates to the shared (singleton) WorkbookDocumentState.
        // Record the undo-stack depth at save time so ExecuteUndo/Redo can detect
        // when the stack returns to the save point and clear the dirty flag cleanly.
        var undoDepth = _commandBus.GetUndoStackDepth(_workbook.Id);
        _documentState.MarkSavedAtUndoDepth(undoDepth);
        UpdateTitleBar();
        // Fan out to sibling windows so they also reflect the saved (clean) state.
        _windowRegistry?.NotifyDocumentStateChanged();
        NotifyAutosaveSaved();
    }

    private async Task<SaveChangesConfirmation> ConfirmSaveBeforeDestructiveActionAsync(string message)
    {
        if (!_workbookDirty)
            return SaveChangesConfirmation.Continue;

        var result = ShowOwnedMessage(
            message,
            UiText.Get("MainWindowMessage_SaveChangesTitle"),
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Cancel)
            return SaveChangesConfirmation.Cancel;
        if (result == MessageBoxResult.No)
            return SaveChangesConfirmation.DiscardWithoutSaving;

        if (FileSavePlanner.TryResolveExistingPath(_currentFilePath, _fileAdapters, out var target))
            return await SaveWorkbookToTargetAsync(target!)
                ? SaveChangesConfirmation.Continue
                : SaveChangesConfirmation.Cancel;

        return await SaveWorkbookWithDialogAsync()
            ? SaveChangesConfirmation.Continue
            : SaveChangesConfirmation.Cancel;
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_suppressClosePrompt || !_workbookDirty)
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
    /// True when this is the last window to close.  Must be called AFTER
    /// <c>_windowRegistry.Unregister(this)</c> has run (in
    /// <see cref="PrepareActiveWorkbookForFinalClose"/>), so <c>Count</c> reflects
    /// the remaining windows rather than including this one.
    /// </summary>
    private bool IsFinalWorkbookWindowClose() =>
        _windowRegistry is null || _windowRegistry.Count == 0;

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
        // check so that Count has already been decremented when we decide whether this
        // is the last window.  This closes the concurrent-close race: if two windows
        // close simultaneously, each pre-unregisters first; the window that sees
        // Count<=1 after its own pre-unregister is definitively the last one.
        // Unregister is idempotent — the Closed handler calls it again safely.
        _windowRegistry?.Unregister(this);

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
            SheetGrid.SelectedRanges = null;
            SheetGrid.QuickAnalysisPreviewRange = null;
            SheetGrid.QuickAnalysisPreviewVisual = FreeX.App.UI.GridQuickAnalysisPreviewVisualKind.None;
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
