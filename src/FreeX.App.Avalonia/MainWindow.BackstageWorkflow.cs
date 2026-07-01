using FreeX.App.Presentation.Backstage;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
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
