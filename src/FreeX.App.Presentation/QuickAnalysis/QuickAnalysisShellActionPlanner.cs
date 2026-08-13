using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.QuickAnalysis;

/// <summary>
/// Host-facing Quick Analysis action plan. Renderers still own controls and native effects; shared
/// sessions dispatch the action and keep route, label, and kind decisions in portable code.
/// </summary>
public enum QuickAnalysisShellActionKind
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

public sealed record QuickAnalysisShellAction(
    QuickAnalysisShellActionKind Kind,
    QuickAnalysisCommandRoute Route,
    ConditionalFormatPreset? ConditionalFormatPreset = null,
    QuickAnalysisConditionalFormatDialogPlan? ConditionalFormatDialog = null,
    ChartType? ChartType = null,
    string? TotalFunction = null,
    string? TotalCommandTitle = null,
    SparklineKind? SparklineKind = null,
    string? SparklineDialogKind = null,
    string? DeferredNote = null);

public sealed record QuickAnalysisShellCapabilities(
    bool OpensConditionalFormatDialogs,
    bool SupportsClearConditionalFormatting,
    bool SupportsChartPicker,
    bool SupportsPercentTotalFormulas,
    bool SupportsRunningTotalFormulas,
    bool SupportsPivotTables,
    string DeferredPlatformName)
{
    public static QuickAnalysisShellCapabilities DialogBacked { get; } =
        new(
            OpensConditionalFormatDialogs: true,
            SupportsClearConditionalFormatting: true,
            SupportsChartPicker: true,
            SupportsPercentTotalFormulas: true,
            SupportsRunningTotalFormulas: true,
            SupportsPivotTables: true,
            DeferredPlatformName: "Windows");

    public static QuickAnalysisShellCapabilities DirectApplyLimited { get; } =
        new(
            OpensConditionalFormatDialogs: false,
            SupportsClearConditionalFormatting: false,
            SupportsChartPicker: false,
            SupportsPercentTotalFormulas: false,
            SupportsRunningTotalFormulas: false,
            SupportsPivotTables: false,
            DeferredPlatformName: "macOS");
}

public static class QuickAnalysisShellActionPlanner
{
    public static QuickAnalysisShellAction Plan(
        QuickAnalysisDisplayItem item,
        QuickAnalysisShellCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(capabilities);

        return Plan(QuickAnalysisCommandRouter.Route(item), capabilities);
    }

    public static QuickAnalysisShellAction Plan(
        QuickAnalysisCommandRoute route,
        QuickAnalysisShellCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(capabilities);

        return route.Kind switch
        {
            QuickAnalysisCommandKind.ConditionalFormat
                when route.ConditionalFormat is { } command &&
                     capabilities.OpensConditionalFormatDialogs =>
                new QuickAnalysisShellAction(
                    QuickAnalysisShellActionKind.OpenConditionalFormatDialog,
                    route,
                    ConditionalFormatDialog: QuickAnalysisConditionalFormatDialogPlanner.PlanDialog(command)),

            QuickAnalysisCommandKind.ConditionalFormat
                when route.ConditionalFormat is { } command =>
                new QuickAnalysisShellAction(
                    QuickAnalysisShellActionKind.ApplyConditionalFormat,
                    route,
                    ConditionalFormatPreset: QuickAnalysisConditionalFormatPresetPlanner.TryResolve(command, out var preset)
                        ? preset
                        : null),

            QuickAnalysisCommandKind.ClearConditionalFormatting
                when capabilities.SupportsClearConditionalFormatting =>
                new QuickAnalysisShellAction(QuickAnalysisShellActionKind.ClearConditionalFormatting, route),

            QuickAnalysisCommandKind.InsertChart
                when route.ChartType is { } chartType =>
                new QuickAnalysisShellAction(
                    QuickAnalysisShellActionKind.InsertChart,
                    route,
                    ChartType: chartType),

            QuickAnalysisCommandKind.MoreCharts
                when capabilities.SupportsChartPicker =>
                new QuickAnalysisShellAction(QuickAnalysisShellActionKind.OpenChartPicker, route),

            QuickAnalysisCommandKind.InsertTotalFormula
                when route.TotalFormulaKind == QuickAnalysisTotalFormulaKind.Aggregate &&
                     IsAutoSumAggregate(route.TotalFunction) =>
                new QuickAnalysisShellAction(
                    QuickAnalysisShellActionKind.InsertAggregateTotalFormula,
                    route,
                    TotalFunction: route.TotalFunction,
                    TotalCommandTitle: AggregateTotalCommandTitle(route.TotalFunction)),

            QuickAnalysisCommandKind.InsertTotalFormula
                when route.TotalFormulaKind == QuickAnalysisTotalFormulaKind.PercentTotal &&
                     capabilities.SupportsPercentTotalFormulas =>
                new QuickAnalysisShellAction(
                    QuickAnalysisShellActionKind.InsertPercentTotalFormula,
                    route,
                    TotalCommandTitle: "Quick Analysis % Total"),

            QuickAnalysisCommandKind.InsertTotalFormula
                when route.TotalFormulaKind == QuickAnalysisTotalFormulaKind.RunningTotal &&
                     capabilities.SupportsRunningTotalFormulas =>
                new QuickAnalysisShellAction(
                    QuickAnalysisShellActionKind.InsertRunningTotalFormula,
                    route,
                    TotalCommandTitle: "Quick Analysis Running Total"),

            QuickAnalysisCommandKind.Table =>
                new QuickAnalysisShellAction(QuickAnalysisShellActionKind.CreateTable, route),

            QuickAnalysisCommandKind.PivotTable
                when capabilities.SupportsPivotTables =>
                new QuickAnalysisShellAction(QuickAnalysisShellActionKind.CreatePivotTable, route),

            QuickAnalysisCommandKind.Sparkline
                when route.SparklineKind is { } sparklineKind =>
                new QuickAnalysisShellAction(
                    QuickAnalysisShellActionKind.InsertSparkline,
                    route,
                    SparklineKind: sparklineKind,
                    SparklineDialogKind: SparklineDialogKind(sparklineKind)),

            _ => Deferred(route, capabilities)
        };
    }

    public static string SparklineDialogKind(SparklineKind kind) =>
        kind switch
        {
            SparklineKind.Column => "column",
            SparklineKind.WinLoss => "winloss",
            _ => "line"
        };

    private static bool IsAutoSumAggregate(string? function) =>
        string.Equals(function, "SUM", StringComparison.Ordinal) ||
        string.Equals(function, "AVERAGE", StringComparison.Ordinal) ||
        string.Equals(function, "COUNT", StringComparison.Ordinal) ||
        string.Equals(function, "MAX", StringComparison.Ordinal) ||
        string.Equals(function, "MIN", StringComparison.Ordinal);

    private static string? AggregateTotalCommandTitle(string? function) =>
        function switch
        {
            "SUM" => "Quick Analysis Sum",
            "AVERAGE" => "Quick Analysis Average",
            "COUNT" => "Quick Analysis Count",
            "MAX" => "Quick Analysis Max",
            "MIN" => "Quick Analysis Min",
            _ => null
        };

    private static QuickAnalysisShellAction Deferred(
        QuickAnalysisCommandRoute route,
        QuickAnalysisShellCapabilities capabilities) =>
        new(
            QuickAnalysisShellActionKind.Deferred,
            route,
            DeferredNote: route.Kind switch
            {
                QuickAnalysisCommandKind.PivotTable =>
                    $"Converting to a PivotTable is not yet available on {capabilities.DeferredPlatformName}.",
                QuickAnalysisCommandKind.InsertTotalFormula =>
                    $"This total is not yet available on {capabilities.DeferredPlatformName}.",
                _ => route.DeferredNote
            });
}
