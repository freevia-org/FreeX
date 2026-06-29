using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.QuickAnalysis;

/// <summary>
/// Host execution plan for a Quick Analysis menu item. Renderers still own the native command calls,
/// dialogs, and async surfaces; this keeps the action payload and stable command titles out of WPF and
/// platform switch statements.
/// </summary>
public enum QuickAnalysisHostOperationKind
{
    OpenConditionalFormatDialog,
    ApplyConditionalFormat,
    ClearConditionalFormatting,
    InsertChart,
    OpenChartPicker,
    InsertAggregateTotalFormula,
    InsertPercentTotalFormula,
    InsertRunningTotalFormula,
    CreateTable,
    CreatePivotTable,
    InsertSparkline,
    Deferred
}

public sealed record QuickAnalysisHostOperation(
    QuickAnalysisHostOperationKind Kind,
    QuickAnalysisCommandRoute Route,
    ConditionalFormatPreset? ConditionalFormatPreset = null,
    string? ConditionalFormatDialogTitle = null,
    ChartType? ChartType = null,
    string? TotalFunction = null,
    QuickAnalysisTotalFormulaKind? TotalFormulaKind = null,
    string? TotalCommandTitle = null,
    SparklineKind? SparklineKind = null,
    string? SparklineDialogKind = null,
    string? DeferredNote = null);

public static class QuickAnalysisHostOperationPlanner
{
    public static QuickAnalysisHostOperation Plan(QuickAnalysisShellItemPlan item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var action = item.Action;
        return action.Kind switch
        {
            QuickAnalysisShellActionKind.OpenConditionalFormatDialog =>
                new QuickAnalysisHostOperation(
                    QuickAnalysisHostOperationKind.OpenConditionalFormatDialog,
                    action.Route,
                    ConditionalFormatDialogTitle: action.ConditionalFormatDialogTitle),

            QuickAnalysisShellActionKind.ApplyConditionalFormat =>
                new QuickAnalysisHostOperation(
                    QuickAnalysisHostOperationKind.ApplyConditionalFormat,
                    action.Route,
                    ConditionalFormatPreset: action.ConditionalFormatPreset),

            QuickAnalysisShellActionKind.ClearConditionalFormatting =>
                new QuickAnalysisHostOperation(
                    QuickAnalysisHostOperationKind.ClearConditionalFormatting,
                    action.Route),

            QuickAnalysisShellActionKind.InsertChart =>
                new QuickAnalysisHostOperation(
                    QuickAnalysisHostOperationKind.InsertChart,
                    action.Route,
                    ChartType: action.ChartType),

            QuickAnalysisShellActionKind.OpenChartPicker =>
                new QuickAnalysisHostOperation(
                    QuickAnalysisHostOperationKind.OpenChartPicker,
                    action.Route),

            QuickAnalysisShellActionKind.InsertAggregateTotalFormula =>
                new QuickAnalysisHostOperation(
                    QuickAnalysisHostOperationKind.InsertAggregateTotalFormula,
                    action.Route,
                    TotalFunction: action.TotalFunction,
                    TotalFormulaKind: QuickAnalysisTotalFormulaKind.Aggregate,
                    TotalCommandTitle: action.TotalCommandTitle),

            QuickAnalysisShellActionKind.InsertPercentTotalFormula =>
                new QuickAnalysisHostOperation(
                    QuickAnalysisHostOperationKind.InsertPercentTotalFormula,
                    action.Route,
                    TotalFormulaKind: QuickAnalysisTotalFormulaKind.PercentTotal,
                    TotalCommandTitle: action.TotalCommandTitle),

            QuickAnalysisShellActionKind.InsertRunningTotalFormula =>
                new QuickAnalysisHostOperation(
                    QuickAnalysisHostOperationKind.InsertRunningTotalFormula,
                    action.Route,
                    TotalFormulaKind: QuickAnalysisTotalFormulaKind.RunningTotal,
                    TotalCommandTitle: action.TotalCommandTitle),

            QuickAnalysisShellActionKind.CreateTable =>
                new QuickAnalysisHostOperation(
                    QuickAnalysisHostOperationKind.CreateTable,
                    action.Route),

            QuickAnalysisShellActionKind.CreatePivotTable =>
                new QuickAnalysisHostOperation(
                    QuickAnalysisHostOperationKind.CreatePivotTable,
                    action.Route),

            QuickAnalysisShellActionKind.InsertSparkline =>
                new QuickAnalysisHostOperation(
                    QuickAnalysisHostOperationKind.InsertSparkline,
                    action.Route,
                    SparklineKind: action.SparklineKind,
                    SparklineDialogKind: action.SparklineDialogKind),

            _ =>
                new QuickAnalysisHostOperation(
                    QuickAnalysisHostOperationKind.Deferred,
                    action.Route,
                    DeferredNote: action.DeferredNote)
        };
    }

    public static bool TryBuildTotalFormulaEdits(
        QuickAnalysisHostOperation operation,
        GridRange range,
        out IReadOnlyList<(CellAddress Address, Cell NewCell)> edits)
    {
        ArgumentNullException.ThrowIfNull(operation);

        edits = operation.TotalFormulaKind switch
        {
            QuickAnalysisTotalFormulaKind.Aggregate when !string.IsNullOrWhiteSpace(operation.TotalFunction) =>
                QuickAnalysisTotalsPlanner.BuildAggregateEdits(range, operation.TotalFunction),
            QuickAnalysisTotalFormulaKind.PercentTotal =>
                QuickAnalysisTotalsPlanner.BuildPercentTotalEdits(range),
            QuickAnalysisTotalFormulaKind.RunningTotal =>
                QuickAnalysisTotalsPlanner.BuildRunningTotalEdits(range),
            _ => []
        };

        return edits.Count > 0;
    }
}
