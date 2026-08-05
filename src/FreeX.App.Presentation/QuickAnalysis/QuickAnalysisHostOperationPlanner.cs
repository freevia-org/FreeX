using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.QuickAnalysis;

/// <summary>
/// Host execution plan for a Quick Analysis menu item. Renderers retain native dialogs and async
/// surfaces; the portable operation executor owns dispatch, and WorkbookSession owns portable mutations.
/// This keeps action payloads and stable command titles out of platform switch statements.
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
    QuickAnalysisConditionalFormatCommand? ConditionalFormat = null,
    ConditionalFormatPreset? ConditionalFormatPreset = null,
    string? ConditionalFormatDialogTitle = null,
    ChartType? ChartType = null,
    string? TotalFunction = null,
    QuickAnalysisTotalFormulaKind? TotalFormulaKind = null,
    string? TotalCommandTitle = null,
    SparklineKind? SparklineKind = null,
    string? SparklineDialogKind = null,
    string? DeferredNote = null);

public sealed record QuickAnalysisConditionalFormatDialogSeed(
    CfRuleType RuleType,
    CfOperator Operator = CfOperator.Equal,
    string? Value1 = null,
    string? Value2 = null,
    string? Text = null,
    string? DateOccurringPeriod = null,
    int TopBottomRank = 10,
    bool TopBottomPercent = false,
    bool IsTop = true);

public static class QuickAnalysisConditionalFormatDialogPlanner
{
    public static QuickAnalysisConditionalFormatDialogSeed Plan(
        QuickAnalysisConditionalFormatCommand command) =>
        command switch
        {
            QuickAnalysisConditionalFormatCommand.DataBar => new(CfRuleType.DataBar),
            QuickAnalysisConditionalFormatCommand.ColorScale => new(CfRuleType.ColorScale),
            QuickAnalysisConditionalFormatCommand.IconSet => new(CfRuleType.IconSet),
            QuickAnalysisConditionalFormatCommand.GreaterThan => new(CfRuleType.CellValue, CfOperator.GreaterThan),
            QuickAnalysisConditionalFormatCommand.LessThan => new(CfRuleType.CellValue, CfOperator.LessThan),
            QuickAnalysisConditionalFormatCommand.Between => new(CfRuleType.CellValue, CfOperator.Between),
            QuickAnalysisConditionalFormatCommand.EqualTo => new(CfRuleType.CellValue, CfOperator.Equal),
            QuickAnalysisConditionalFormatCommand.TextContains => new(CfRuleType.ContainsText, Text: string.Empty),
            QuickAnalysisConditionalFormatCommand.DateOccurring => new(
                CfRuleType.DateOccurring,
                DateOccurringPeriod: "Today"),
            QuickAnalysisConditionalFormatCommand.DuplicateValues => new(CfRuleType.DuplicateValues),
            QuickAnalysisConditionalFormatCommand.Top10Items => new(CfRuleType.Top10),
            QuickAnalysisConditionalFormatCommand.Top10Percent => new(CfRuleType.Top10, TopBottomPercent: true),
            QuickAnalysisConditionalFormatCommand.Bottom10Items => new(CfRuleType.Top10, IsTop: false),
            QuickAnalysisConditionalFormatCommand.Bottom10Percent => new(
                CfRuleType.Top10,
                TopBottomPercent: true,
                IsTop: false),
            QuickAnalysisConditionalFormatCommand.AboveAverage => new(CfRuleType.AboveAverage),
            QuickAnalysisConditionalFormatCommand.BelowAverage => new(CfRuleType.AboveAverage, IsTop: false),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unsupported conditional-format command.")
        };
}

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
                    ConditionalFormat: action.ConditionalFormat,
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

    public static bool TryBuildSparklineCommands(
        QuickAnalysisHostOperation operation,
        Sheet sheet,
        GridRange range,
        out IReadOnlyList<AddSparklineCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(sheet);

        if (operation is not
            {
                Kind: QuickAnalysisHostOperationKind.InsertSparkline,
                SparklineKind: { } sparklineKind
            })
        {
            commands = [];
            return false;
        }

        var description = QuickAnalysisSelectionReader.Describe(sheet, range);
        commands = QuickAnalysisSparklinePlanner.BuildCommands(
            sheet.Id,
            range,
            description.HasHeaderRow,
            sparklineKind);
        return commands.Count > 0;
    }
}
