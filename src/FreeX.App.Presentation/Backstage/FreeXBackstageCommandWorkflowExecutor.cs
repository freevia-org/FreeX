namespace FreeX.App.Presentation.Backstage;

public sealed record FreeXBackstageCommandHandlers(
    Func<Task> NewWorkbookAsync,
    Func<Task> OpenWorkbookAsync,
    Func<Task> ShareWorkbookAsync,
    Func<Task> SaveWorkbookAsync,
    Func<Task> SaveWorkbookAsAsync,
    Func<Task> ExportWorkbookAsync,
    Func<Task> CloseWorkbookAsync,
    Func<Task> AccountAsync,
    Func<Task> OptionsAsync);

/// <summary>
/// Shared dispatcher for FreeX Backstage command workflows. Platform shells provide concrete effects;
/// the presentation layer owns the workflow-to-handler contract.
/// </summary>
public static class FreeXBackstageCommandWorkflowExecutor
{
    public static Task ExecuteAsync(
        FreeXBackstageCommandId command,
        FreeXBackstageCommandHandlers handlers) =>
        ExecuteAsync(FreeXBackstageFlowPlanner.BuildCommandWorkflow(command), handlers);

    public static Task ExecuteAsync(
        FreeXBackstageCommandWorkflowPlan plan,
        FreeXBackstageCommandHandlers handlers)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(handlers);

        var handler = ResolveHandler(plan.Workflow, handlers)
            ?? throw new InvalidOperationException($"Backstage command workflow '{plan.Workflow}' is missing a handler.");

        return handler();
    }

    private static Func<Task>? ResolveHandler(
        FreeXBackstageCommandWorkflowKind workflow,
        FreeXBackstageCommandHandlers handlers) =>
        workflow switch
        {
            FreeXBackstageCommandWorkflowKind.NewWorkbook => handlers.NewWorkbookAsync,
            FreeXBackstageCommandWorkflowKind.OpenWorkbook => handlers.OpenWorkbookAsync,
            FreeXBackstageCommandWorkflowKind.ShareWorkbook => handlers.ShareWorkbookAsync,
            FreeXBackstageCommandWorkflowKind.SaveWorkbook => handlers.SaveWorkbookAsync,
            FreeXBackstageCommandWorkflowKind.SaveWorkbookAs => handlers.SaveWorkbookAsAsync,
            FreeXBackstageCommandWorkflowKind.ExportWorkbook => handlers.ExportWorkbookAsync,
            FreeXBackstageCommandWorkflowKind.CloseWorkbook => handlers.CloseWorkbookAsync,
            FreeXBackstageCommandWorkflowKind.Account => handlers.AccountAsync,
            FreeXBackstageCommandWorkflowKind.Options => handlers.OptionsAsync,
            _ => throw new InvalidOperationException($"Unsupported Backstage command workflow '{workflow}'.")
        };
}
