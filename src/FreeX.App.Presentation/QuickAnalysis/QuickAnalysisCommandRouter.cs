using FreeX.Core.Model;

namespace FreeX.App.Presentation.QuickAnalysis;

/// <summary>How a host should execute a chosen Quick Analysis item.</summary>
public enum QuickAnalysisCommandKind
{
    /// <summary>Apply or open a conditional-format command.</summary>
    ConditionalFormat,

    /// <summary>Clear conditional formats from the selected range.</summary>
    ClearConditionalFormatting,

    /// <summary>Insert a chart over the selected range.</summary>
    InsertChart,

    /// <summary>Open the host's full chart picker.</summary>
    MoreCharts,

    /// <summary>Insert total formulas beside the selected range.</summary>
    InsertTotalFormula,

    /// <summary>Convert the selection into a structured table.</summary>
    Table,

    /// <summary>Create a PivotTable from the selection.</summary>
    PivotTable,

    /// <summary>Insert sparklines beside the selection.</summary>
    Sparkline,

    /// <summary>The host may surface the item but does not have an execution path for it yet.</summary>
    Deferred,
}

/// <summary>The conditional-format intent behind a Quick Analysis formatting command.</summary>
public enum QuickAnalysisConditionalFormatCommand
{
    DataBar,
    ColorScale,
    IconSet,
    GreaterThan,
    LessThan,
    Between,
    EqualTo,
    TextContains,
    DateOccurring,
    DuplicateValues,
    Top10Items,
    Top10Percent,
    Bottom10Items,
    Bottom10Percent,
    AboveAverage,
    BelowAverage,
}

/// <summary>The shared total-formula families Quick Analysis can insert.</summary>
public enum QuickAnalysisTotalFormulaKind
{
    Aggregate,
    PercentTotal,
    RunningTotal,
}

/// <summary>
/// The neutral execution route for a Quick Analysis option or suggestion. Hosts map this route onto their
/// existing command handlers; no renderer-specific types appear in the shared contract.
/// </summary>
public sealed record QuickAnalysisCommandRoute(
    QuickAnalysisCommandKind Kind,
    QuickAnalysisConditionalFormatCommand? ConditionalFormat = null,
    ChartType? ChartType = null,
    QuickAnalysisTotalFormulaKind? TotalFormulaKind = null,
    string? TotalFunction = null,
    SparklineKind? SparklineKind = null,
    string? DeferredNote = null);

/// <summary>Maps shared Quick Analysis models onto the neutral execution route hosts consume.</summary>
public static class QuickAnalysisCommandRouter
{
    public static QuickAnalysisCommandRoute Route(QuickAnalysisSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        return suggestion.ActionKind switch
        {
            QuickAnalysisActionKind.ConditionalFormat => RouteFormatting(suggestion),
            QuickAnalysisActionKind.InsertTotals => RouteTotals(suggestion),
            QuickAnalysisActionKind.InsertSparklines => RouteSparkline(suggestion),
            QuickAnalysisActionKind.InsertChart => RouteChart(suggestion),
            QuickAnalysisActionKind.Table => RouteTable(suggestion),
            _ => Deferred("This Quick Analysis suggestion is not available."),
        };
    }

    public static QuickAnalysisCommandRoute Route(QuickAnalysisOption option)
    {
        ArgumentNullException.ThrowIfNull(option);

        return QuickAnalysisCatalog.Route(option.Command);
    }

    private static QuickAnalysisCommandRoute RouteFormatting(QuickAnalysisSuggestion suggestion)
    {
        var action = suggestion.ConditionalFormat
            ?? throw new ArgumentException("Formatting suggestion has no conditional-format action.", nameof(suggestion));

        return ConditionalFormat(action.FormatKind switch
        {
            QuickAnalysisFormatKind.DataBars => QuickAnalysisConditionalFormatCommand.DataBar,
            QuickAnalysisFormatKind.ColorScale => QuickAnalysisConditionalFormatCommand.ColorScale,
            QuickAnalysisFormatKind.IconSet => QuickAnalysisConditionalFormatCommand.IconSet,
            QuickAnalysisFormatKind.GreaterThan => QuickAnalysisConditionalFormatCommand.GreaterThan,
            QuickAnalysisFormatKind.LessThan => QuickAnalysisConditionalFormatCommand.LessThan,
            QuickAnalysisFormatKind.Between => QuickAnalysisConditionalFormatCommand.Between,
            QuickAnalysisFormatKind.EqualTo => QuickAnalysisConditionalFormatCommand.EqualTo,
            QuickAnalysisFormatKind.TextContains => QuickAnalysisConditionalFormatCommand.TextContains,
            QuickAnalysisFormatKind.DateOccurring => QuickAnalysisConditionalFormatCommand.DateOccurring,
            QuickAnalysisFormatKind.DuplicateValues => QuickAnalysisConditionalFormatCommand.DuplicateValues,
            QuickAnalysisFormatKind.Top10 => QuickAnalysisConditionalFormatCommand.Top10Items,
            QuickAnalysisFormatKind.Top10Percent => QuickAnalysisConditionalFormatCommand.Top10Percent,
            QuickAnalysisFormatKind.Bottom10 => QuickAnalysisConditionalFormatCommand.Bottom10Items,
            QuickAnalysisFormatKind.Bottom10Percent => QuickAnalysisConditionalFormatCommand.Bottom10Percent,
            QuickAnalysisFormatKind.AboveAverage => QuickAnalysisConditionalFormatCommand.AboveAverage,
            QuickAnalysisFormatKind.BelowAverage => QuickAnalysisConditionalFormatCommand.BelowAverage,
            _ => QuickAnalysisConditionalFormatCommand.DataBar,
        });
    }

    private static QuickAnalysisCommandRoute RouteTotals(QuickAnalysisSuggestion suggestion)
    {
        var action = suggestion.Total
            ?? throw new ArgumentException("Totals suggestion has no total action.", nameof(suggestion));

        return action.Function switch
        {
            QuickAnalysisTotalFunction.Sum => Aggregate("SUM"),
            QuickAnalysisTotalFunction.Average => Aggregate("AVERAGE"),
            QuickAnalysisTotalFunction.Count => Aggregate("COUNT"),
            QuickAnalysisTotalFunction.PercentTotal => new(QuickAnalysisCommandKind.InsertTotalFormula, TotalFormulaKind: QuickAnalysisTotalFormulaKind.PercentTotal),
            QuickAnalysisTotalFunction.RunningTotal => new(QuickAnalysisCommandKind.InsertTotalFormula, TotalFormulaKind: QuickAnalysisTotalFormulaKind.RunningTotal),
            QuickAnalysisTotalFunction.Max => Aggregate("MAX"),
            QuickAnalysisTotalFunction.Min => Aggregate("MIN"),
            _ => Deferred("This total is not available."),
        };
    }

    private static QuickAnalysisCommandRoute RouteSparkline(QuickAnalysisSuggestion suggestion)
    {
        var action = suggestion.Sparkline
            ?? throw new ArgumentException("Sparkline suggestion has no sparkline action.", nameof(suggestion));

        return Sparkline(action.SparklineKind switch
        {
            QuickAnalysisSparklineKind.Line => SparklineKind.Line,
            QuickAnalysisSparklineKind.Column => SparklineKind.Column,
            QuickAnalysisSparklineKind.WinLoss => SparklineKind.WinLoss,
            _ => SparklineKind.Line,
        });
    }

    private static QuickAnalysisCommandRoute RouteChart(QuickAnalysisSuggestion suggestion)
    {
        var action = suggestion.Chart
            ?? throw new ArgumentException("Chart suggestion has no chart action.", nameof(suggestion));

        return Chart(action.ChartType);
    }

    private static QuickAnalysisCommandRoute RouteTable(QuickAnalysisSuggestion suggestion)
    {
        var action = suggestion.Table
            ?? throw new ArgumentException("Table suggestion has no table action.", nameof(suggestion));

        return action.TableKind == QuickAnalysisTableKind.Table
            ? new QuickAnalysisCommandRoute(QuickAnalysisCommandKind.Table)
            : new QuickAnalysisCommandRoute(QuickAnalysisCommandKind.PivotTable);
    }

    private static QuickAnalysisCommandRoute ConditionalFormat(QuickAnalysisConditionalFormatCommand command) =>
        new(QuickAnalysisCommandKind.ConditionalFormat, ConditionalFormat: command);

    private static QuickAnalysisCommandRoute Chart(ChartType chartType) =>
        new(QuickAnalysisCommandKind.InsertChart, ChartType: chartType);

    private static QuickAnalysisCommandRoute Aggregate(string function) =>
        new(
            QuickAnalysisCommandKind.InsertTotalFormula,
            TotalFormulaKind: QuickAnalysisTotalFormulaKind.Aggregate,
            TotalFunction: function);

    private static QuickAnalysisCommandRoute Sparkline(SparklineKind kind) =>
        new(QuickAnalysisCommandKind.Sparkline, SparklineKind: kind);

    private static QuickAnalysisCommandRoute Deferred(string note) =>
        new(QuickAnalysisCommandKind.Deferred, DeferredNote: note);
}
