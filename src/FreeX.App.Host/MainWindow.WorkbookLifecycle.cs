using System.ComponentModel;
using System.Windows;
using FreeX.Core.IO;

namespace FreeX.App.Host;

public partial class MainWindow
{
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

    private async Task<bool> ConfirmSaveBeforeDestructiveActionAsync(string message)
    {
        if (!_workbookDirty)
            return true;

        var result = ShowOwnedMessage(
            message,
            UiText.Get("MainWindowMessage_SaveChangesTitle"),
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Cancel)
            return false;
        if (result == MessageBoxResult.No)
            return true;

        if (FileSavePlanner.TryResolveExistingPath(_currentFilePath, _fileAdapters, out var target))
            return await SaveWorkbookToTargetAsync(target!);

        return await SaveWorkbookWithDialogAsync();
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_suppressClosePrompt || !_workbookDirty)
            return;

        e.Cancel = true;
        if (_closeAfterSaveInProgress)
            return;

        _closeAfterSaveInProgress = true;
        var canClose = await ConfirmSaveBeforeDestructiveActionAsync(UiText.Get("MainWindowMessage_SaveChangesBeforeClosingWorkbook"));
        _closeAfterSaveInProgress = false;

        if (!canClose)
            return;

        _suppressClosePrompt = true;
        Close();
    }
}
