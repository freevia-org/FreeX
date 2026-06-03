using System.ComponentModel;
using System.Windows;
using FreeX.Core.IO;

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
}
