using FreeX.App.Presentation.Backstage;
using FreeX.App.Presentation.Shell;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private Task ExecuteOwnedNativeFileMenuItemAsync(NativeFileMenuItemId item) =>
        item switch
        {
            NativeFileMenuItemId.BackstageInfo => ExecuteOwnedNativeFileAction(ShowBackstageInfo),
            NativeFileMenuItemId.BackstageExport => ExecuteBackstageCommandWorkflowAsync(FreeXBackstageCommandId.Export),
            NativeFileMenuItemId.BackstageAccount => ExecuteBackstageCommandWorkflowAsync(FreeXBackstageCommandId.Account),
            NativeFileMenuItemId.Options => ExecuteBackstageCommandWorkflowAsync(FreeXBackstageCommandId.Options),
            NativeFileMenuItemId.WorkbookStatistics => ShowWorkbookStatisticsDialogAsync(),
            NativeFileMenuItemId.PageSetup => ShowPageSetupDialogAsync(),
            NativeFileMenuItemId.PrintPreview => ShowPrintPreviewDialogAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(item), item, "Native file menu item is not an owned validation route."),
        };

    private static Task ExecuteOwnedNativeFileAction(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private Task ExecuteBackstageCommandWorkflowAsync(FreeXBackstageCommandId command) =>
        FreeXBackstageCommandWorkflowExecutor.ExecuteAsync(
            command,
            CreateBackstageCommandHandlers());

    private FreeXBackstageCommandHandlers CreateBackstageCommandHandlers() =>
        new(
            NewWorkbookAsync: CreateNewWorkbookAsync,
            OpenWorkbookAsync: OpenWorkbookAsync,
            ShareWorkbookAsync: ShareWorkbookAsync,
            SaveWorkbookAsync: async () => await SaveCurrentWorkbookAsync(),
            SaveWorkbookAsAsync: async () => await SaveWorkbookAsAsync(),
            ExportWorkbookAsync: ShowBackstageExportDialogAsync,
            CloseWorkbookAsync: CloseWorkbookAsync,
            AccountAsync: ShowBackstageAccountDialogAsync,
            OptionsAsync: () =>
            {
                ShowOptions();
                return Task.CompletedTask;
            });
}
