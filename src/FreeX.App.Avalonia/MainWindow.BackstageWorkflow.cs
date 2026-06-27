using FreeX.App.Presentation.Backstage;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private async Task ExecuteBackstageCommandWorkflowAsync(FreeXBackstageCommandId command)
    {
        var plan = FreeXBackstageFlowPlanner.BuildCommandWorkflow(command);
        switch (plan.Workflow)
        {
            case FreeXBackstageCommandWorkflowKind.NewWorkbook:
                await CreateNewWorkbookAsync();
                break;

            case FreeXBackstageCommandWorkflowKind.OpenWorkbook:
                await OpenWorkbookAsync();
                break;

            case FreeXBackstageCommandWorkflowKind.ShareWorkbook:
                await ShareWorkbookAsync();
                break;

            case FreeXBackstageCommandWorkflowKind.SaveWorkbook:
                await SaveCurrentWorkbookAsync();
                break;

            case FreeXBackstageCommandWorkflowKind.SaveWorkbookAs:
                await SaveWorkbookAsAsync();
                break;

            case FreeXBackstageCommandWorkflowKind.ExportWorkbook:
                await ShowBackstageExportDialogAsync();
                break;

            case FreeXBackstageCommandWorkflowKind.CloseWorkbook:
                await CloseWorkbookAsync();
                break;

            case FreeXBackstageCommandWorkflowKind.Account:
                await ShowBackstageAccountDialogAsync();
                break;

            case FreeXBackstageCommandWorkflowKind.Options:
                ShowOptions();
                break;

            default:
                throw new InvalidOperationException($"Unsupported Backstage command workflow '{plan.Workflow}'.");
        }
    }
}
