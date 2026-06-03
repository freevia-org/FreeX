using System.ComponentModel;
using System.Windows;
using FreeX.App.UI;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private enum SaveChangesConfirmation
    {
        Cancel,
        Continue,
        DiscardWithoutSaving
    }

    private void MarkWorkbookDirty()
    {
        _workbookDirty = true;
        UpdateTitleBar();
    }

    private void MarkWorkbookSaved()
    {
        _workbookDirty = false;
        UpdateTitleBar();
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

        if (confirmation == SaveChangesConfirmation.Cancel)
            return;

        _suppressClosePrompt = true;
        PrepareActiveWorkbookForFinalClose();
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }

    private bool IsFinalWorkbookWindowClose() =>
        _windowRegistry is null || _windowRegistry.Count <= 1;

    private void PrepareActiveWorkbookForFinalClose()
    {
        ReleaseWorkbookUiStateForClose();

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
            SheetGrid.SelectedRange = null;
            SheetGrid.SelectedRanges = null;
            SheetGrid.QuickAnalysisPreviewRange = null;
            SheetGrid.QuickAnalysisPreviewVisual = FreeX.App.UI.GridQuickAnalysisPreviewVisualKind.None;
            SheetGrid.EditingCell = null;
            SheetGrid.FormulaTraceArrows = null;
            SheetGrid.FormulaTraceSheetId = default;
            SheetGrid.Charts = null;
            SheetGrid.TextBoxes = null;
            SheetGrid.DrawingShapes = null;
            SheetGrid.WorkbookTheme = WorkbookTheme.Office;
            SheetGrid.Pictures = null;
            SheetGrid.DrawingObjectZOrder = null;
            SheetGrid.NativeSlicers = null;
            SheetGrid.NativeTimelines = null;
            SheetGrid.WorksheetBackground = null;
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
        _lastViewportPivotFieldListRefreshKey = null;
        _lastViewportSlicerTimelineRefreshKey = null;
    }
}
