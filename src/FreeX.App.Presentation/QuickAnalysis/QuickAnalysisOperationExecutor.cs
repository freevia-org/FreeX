using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.QuickAnalysis;

public sealed record QuickAnalysisOperationHandlers(
    Func<QuickAnalysisConditionalFormatCommand, string?, Task>? OpenConditionalFormatDialogAsync,
    Func<ConditionalFormatPreset, Task>? ApplyConditionalFormatAsync,
    Func<Task> ClearConditionalFormattingAsync,
    Func<ChartType, Task> InsertChartAsync,
    Func<Task> OpenChartPickerAsync,
    Func<QuickAnalysisHostOperation, Task> ExecuteTotalAsync,
    Func<Task> CreateTableAsync,
    Func<Task> CreatePivotTableAsync,
    Func<QuickAnalysisHostOperation, Task> InsertSparklineAsync,
    Func<string, Task> ShowDeferredAsync);

/// <summary>
/// Owns the operation-to-effect dispatch after a Quick Analysis item is selected. Renderers provide
/// native dialog, status, and visual-aftermath callbacks; operation classification stays portable.
/// </summary>
public static class QuickAnalysisOperationExecutor
{
    public static Task ExecuteAsync(
        QuickAnalysisHostOperation operation,
        QuickAnalysisOperationHandlers handlers)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(handlers);

        return operation.Kind switch
        {
            QuickAnalysisHostOperationKind.OpenConditionalFormatDialog
                when operation.ConditionalFormat is { } command =>
                RequiredHandler(
                    handlers.OpenConditionalFormatDialogAsync,
                    operation.Kind)(command, operation.ConditionalFormatDialogTitle),

            QuickAnalysisHostOperationKind.ApplyConditionalFormat
                when operation.ConditionalFormatPreset is { } preset =>
                RequiredHandler(handlers.ApplyConditionalFormatAsync, operation.Kind)(preset),

            QuickAnalysisHostOperationKind.ClearConditionalFormatting =>
                handlers.ClearConditionalFormattingAsync(),

            QuickAnalysisHostOperationKind.InsertChart
                when operation.ChartType is { } chartType =>
                handlers.InsertChartAsync(chartType),

            QuickAnalysisHostOperationKind.OpenChartPicker =>
                handlers.OpenChartPickerAsync(),

            QuickAnalysisHostOperationKind.InsertAggregateTotalFormula
                or QuickAnalysisHostOperationKind.InsertPercentTotalFormula
                or QuickAnalysisHostOperationKind.InsertRunningTotalFormula =>
                handlers.ExecuteTotalAsync(operation),

            QuickAnalysisHostOperationKind.CreateTable =>
                handlers.CreateTableAsync(),

            QuickAnalysisHostOperationKind.CreatePivotTable =>
                handlers.CreatePivotTableAsync(),

            QuickAnalysisHostOperationKind.InsertSparkline
                when operation.SparklineKind is not null =>
                handlers.InsertSparklineAsync(operation),

            QuickAnalysisHostOperationKind.Deferred
                when operation.DeferredNote is { } note =>
                handlers.ShowDeferredAsync(note),

            _ => throw new InvalidOperationException(
                $"Quick Analysis operation '{operation.Kind}' does not carry its required payload.")
        };
    }

    private static THandler RequiredHandler<THandler>(
        THandler? handler,
        QuickAnalysisHostOperationKind kind)
        where THandler : Delegate =>
        handler ?? throw new InvalidOperationException(
            $"Quick Analysis operation '{kind}' is not supported by this host.");
}
