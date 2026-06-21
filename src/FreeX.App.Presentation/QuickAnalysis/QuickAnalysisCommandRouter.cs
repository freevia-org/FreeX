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

        return option.Command switch
        {
            QuickAnalysisCommand.DataBar => ConditionalFormat(QuickAnalysisConditionalFormatCommand.DataBar),
            QuickAnalysisCommand.ColorScale => ConditionalFormat(QuickAnalysisConditionalFormatCommand.ColorScale),
            QuickAnalysisCommand.IconSet => ConditionalFormat(QuickAnalysisConditionalFormatCommand.IconSet),
            QuickAnalysisCommand.GreaterThan => ConditionalFormat(QuickAnalysisConditionalFormatCommand.GreaterThan),
            QuickAnalysisCommand.LessThan => ConditionalFormat(QuickAnalysisConditionalFormatCommand.LessThan),
            QuickAnalysisCommand.Between => ConditionalFormat(QuickAnalysisConditionalFormatCommand.Between),
            QuickAnalysisCommand.EqualTo => ConditionalFormat(QuickAnalysisConditionalFormatCommand.EqualTo),
            QuickAnalysisCommand.TextContains => ConditionalFormat(QuickAnalysisConditionalFormatCommand.TextContains),
            QuickAnalysisCommand.DateOccurring => ConditionalFormat(QuickAnalysisConditionalFormatCommand.DateOccurring),
            QuickAnalysisCommand.DuplicateValues => ConditionalFormat(QuickAnalysisConditionalFormatCommand.DuplicateValues),
            QuickAnalysisCommand.Top10 => ConditionalFormat(QuickAnalysisConditionalFormatCommand.Top10Items),
            QuickAnalysisCommand.Top10Percent => ConditionalFormat(QuickAnalysisConditionalFormatCommand.Top10Percent),
            QuickAnalysisCommand.Bottom10 => ConditionalFormat(QuickAnalysisConditionalFormatCommand.Bottom10Items),
            QuickAnalysisCommand.Bottom10Percent => ConditionalFormat(QuickAnalysisConditionalFormatCommand.Bottom10Percent),
            QuickAnalysisCommand.AboveAverage => ConditionalFormat(QuickAnalysisConditionalFormatCommand.AboveAverage),
            QuickAnalysisCommand.BelowAverage => ConditionalFormat(QuickAnalysisConditionalFormatCommand.BelowAverage),
            QuickAnalysisCommand.ClearConditionalFormatting => new(QuickAnalysisCommandKind.ClearConditionalFormatting),

            QuickAnalysisCommand.ColumnChart => Chart(ChartType.Column),
            QuickAnalysisCommand.StackedColumnChart => Chart(ChartType.StackedColumn),
            QuickAnalysisCommand.PercentStackedColumnChart => Chart(ChartType.PercentStackedColumn),
            QuickAnalysisCommand.LineChart => Chart(ChartType.Line),
            QuickAnalysisCommand.PieChart => Chart(ChartType.Pie),
            QuickAnalysisCommand.DoughnutChart => Chart(ChartType.Doughnut),
            QuickAnalysisCommand.BarChart => Chart(ChartType.Bar),
            QuickAnalysisCommand.StackedBarChart => Chart(ChartType.StackedBar),
            QuickAnalysisCommand.PercentStackedBarChart => Chart(ChartType.PercentStackedBar),
            QuickAnalysisCommand.AreaChart => Chart(ChartType.Area),
            QuickAnalysisCommand.ScatterChart => Chart(ChartType.Scatter),
            QuickAnalysisCommand.BubbleChart => Chart(ChartType.Bubble),
            QuickAnalysisCommand.RadarChart => Chart(ChartType.Radar),
            QuickAnalysisCommand.StockChart => Chart(ChartType.Stock),
            QuickAnalysisCommand.MoreCharts => new(QuickAnalysisCommandKind.MoreCharts),

            QuickAnalysisCommand.Sum => Aggregate("SUM"),
            QuickAnalysisCommand.Average => Aggregate("AVERAGE"),
            QuickAnalysisCommand.Count => Aggregate("COUNT"),
            QuickAnalysisCommand.PercentTotal => new(QuickAnalysisCommandKind.InsertTotalFormula, TotalFormulaKind: QuickAnalysisTotalFormulaKind.PercentTotal),
            QuickAnalysisCommand.RunningTotal => new(QuickAnalysisCommandKind.InsertTotalFormula, TotalFormulaKind: QuickAnalysisTotalFormulaKind.RunningTotal),
            QuickAnalysisCommand.Max => Aggregate("MAX"),
            QuickAnalysisCommand.Min => Aggregate("MIN"),

            QuickAnalysisCommand.FormatAsTable => new(QuickAnalysisCommandKind.Table),
            QuickAnalysisCommand.PivotTable => new(QuickAnalysisCommandKind.PivotTable),
            QuickAnalysisCommand.LineSparkline => Sparkline(SparklineKind.Line),
            QuickAnalysisCommand.ColumnSparkline => Sparkline(SparklineKind.Column),
            QuickAnalysisCommand.WinLossSparkline => Sparkline(SparklineKind.WinLoss),
            _ => Deferred("This Quick Analysis command is not available."),
        };
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
            QuickAnalysisFormatKind.Top10 => QuickAnalysisConditionalFormatCommand.Top10Items,
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
