using FluentAssertions;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.QuickAnalysis;

public sealed class QuickAnalysisCommandRouterTests
{
    [Theory]
    [InlineData(QuickAnalysisFormatKind.DataBars, QuickAnalysisConditionalFormatCommand.DataBar)]
    [InlineData(QuickAnalysisFormatKind.ColorScale, QuickAnalysisConditionalFormatCommand.ColorScale)]
    [InlineData(QuickAnalysisFormatKind.IconSet, QuickAnalysisConditionalFormatCommand.IconSet)]
    [InlineData(QuickAnalysisFormatKind.GreaterThan, QuickAnalysisConditionalFormatCommand.GreaterThan)]
    [InlineData(QuickAnalysisFormatKind.Top10, QuickAnalysisConditionalFormatCommand.Top10Items)]
    public void Route_FormattingSuggestion_MapsToSharedConditionalFormatRoute(
        QuickAnalysisFormatKind formatKind,
        QuickAnalysisConditionalFormatCommand expected)
    {
        var suggestion = FormattingSuggestion(formatKind);

        var route = QuickAnalysisCommandRouter.Route(suggestion);

        route.Kind.Should().Be(QuickAnalysisCommandKind.ConditionalFormat);
        route.ConditionalFormat.Should().Be(expected);
    }

    [Theory]
    [InlineData(QuickAnalysisCommand.DataBar, QuickAnalysisConditionalFormatCommand.DataBar)]
    [InlineData(QuickAnalysisCommand.LessThan, QuickAnalysisConditionalFormatCommand.LessThan)]
    [InlineData(QuickAnalysisCommand.Top10Percent, QuickAnalysisConditionalFormatCommand.Top10Percent)]
    [InlineData(QuickAnalysisCommand.Bottom10, QuickAnalysisConditionalFormatCommand.Bottom10Items)]
    [InlineData(QuickAnalysisCommand.BelowAverage, QuickAnalysisConditionalFormatCommand.BelowAverage)]
    public void Route_WpfOption_MapsFormattingCommandsToSharedConditionalFormatRoute(
        QuickAnalysisCommand command,
        QuickAnalysisConditionalFormatCommand expected)
    {
        var route = QuickAnalysisCommandRouter.Route(Option(command));

        route.Kind.Should().Be(QuickAnalysisCommandKind.ConditionalFormat);
        route.ConditionalFormat.Should().Be(expected);
    }

    [Theory]
    [InlineData(QuickAnalysisCommand.ColumnChart, ChartType.Column)]
    [InlineData(QuickAnalysisCommand.StackedColumnChart, ChartType.StackedColumn)]
    [InlineData(QuickAnalysisCommand.PercentStackedBarChart, ChartType.PercentStackedBar)]
    [InlineData(QuickAnalysisCommand.DoughnutChart, ChartType.Doughnut)]
    [InlineData(QuickAnalysisCommand.StockChart, ChartType.Stock)]
    public void Route_WpfOption_MapsChartCommandsToChartTypes(QuickAnalysisCommand command, ChartType expected)
    {
        var route = QuickAnalysisCommandRouter.Route(Option(command));

        route.Kind.Should().Be(QuickAnalysisCommandKind.InsertChart);
        route.ChartType.Should().Be(expected);
    }

    [Theory]
    [InlineData(QuickAnalysisCommand.Sum, "SUM")]
    [InlineData(QuickAnalysisCommand.Average, "AVERAGE")]
    [InlineData(QuickAnalysisCommand.Count, "COUNT")]
    [InlineData(QuickAnalysisCommand.Max, "MAX")]
    [InlineData(QuickAnalysisCommand.Min, "MIN")]
    public void Route_WpfOption_MapsAggregateTotalsToFunctionNames(QuickAnalysisCommand command, string expected)
    {
        var route = QuickAnalysisCommandRouter.Route(Option(command));

        route.Kind.Should().Be(QuickAnalysisCommandKind.InsertTotalFormula);
        route.TotalFormulaKind.Should().Be(QuickAnalysisTotalFormulaKind.Aggregate);
        route.TotalFunction.Should().Be(expected);
    }

    [Theory]
    [InlineData(QuickAnalysisCommand.PercentTotal, QuickAnalysisTotalFormulaKind.PercentTotal)]
    [InlineData(QuickAnalysisCommand.RunningTotal, QuickAnalysisTotalFormulaKind.RunningTotal)]
    public void Route_WpfOption_MapsSpecialTotalsToFormulaKinds(
        QuickAnalysisCommand command,
        QuickAnalysisTotalFormulaKind expected)
    {
        var route = QuickAnalysisCommandRouter.Route(Option(command));

        route.Kind.Should().Be(QuickAnalysisCommandKind.InsertTotalFormula);
        route.TotalFormulaKind.Should().Be(expected);
    }

    [Theory]
    [InlineData(QuickAnalysisTotalFunction.Sum, "SUM")]
    [InlineData(QuickAnalysisTotalFunction.Average, "AVERAGE")]
    [InlineData(QuickAnalysisTotalFunction.Count, "COUNT")]
    public void Route_TotalSuggestion_MapsAggregateTotalsToFunctionNames(
        QuickAnalysisTotalFunction function,
        string expected)
    {
        var suggestion = QuickAnalysisModelSuggestion(QuickAnalysisGroup.Totals, function);

        var route = QuickAnalysisCommandRouter.Route(suggestion);

        route.Kind.Should().Be(QuickAnalysisCommandKind.InsertTotalFormula);
        route.TotalFormulaKind.Should().Be(QuickAnalysisTotalFormulaKind.Aggregate);
        route.TotalFunction.Should().Be(expected);
    }

    [Theory]
    [InlineData(QuickAnalysisTotalFunction.PercentTotal, QuickAnalysisTotalFormulaKind.PercentTotal)]
    [InlineData(QuickAnalysisTotalFunction.RunningTotal, QuickAnalysisTotalFormulaKind.RunningTotal)]
    public void Route_TotalSuggestion_MapsSpecialTotalsToFormulaKinds(
        QuickAnalysisTotalFunction function,
        QuickAnalysisTotalFormulaKind expected)
    {
        var suggestion = QuickAnalysisModelSuggestion(QuickAnalysisGroup.Totals, function);

        var route = QuickAnalysisCommandRouter.Route(suggestion);

        route.Kind.Should().Be(QuickAnalysisCommandKind.InsertTotalFormula);
        route.TotalFormulaKind.Should().Be(expected);
    }

    [Theory]
    [InlineData(QuickAnalysisSparklineKind.Line, SparklineKind.Line)]
    [InlineData(QuickAnalysisSparklineKind.Column, SparklineKind.Column)]
    [InlineData(QuickAnalysisSparklineKind.WinLoss, SparklineKind.WinLoss)]
    public void Route_SparklineSuggestion_MapsToCoreSparklineKind(
        QuickAnalysisSparklineKind sparklineKind,
        SparklineKind expected)
    {
        var suggestion = FindSuggestion(
            BuildModel(numericColumns: 3, hasHeader: true),
            QuickAnalysisGroup.Sparklines,
            s => s.Sparkline!.SparklineKind == sparklineKind);

        var route = QuickAnalysisCommandRouter.Route(suggestion);

        route.Kind.Should().Be(QuickAnalysisCommandKind.Sparkline);
        route.SparklineKind.Should().Be(expected);
    }

    [Fact]
    public void Route_ChartSuggestion_MapsToInsertChart_CarryingChartType()
    {
        var suggestion = FindSuggestion(
            BuildModel(numericColumns: 2, hasHeader: true),
            QuickAnalysisGroup.Charts,
            _ => true);

        var route = QuickAnalysisCommandRouter.Route(suggestion);

        route.Kind.Should().Be(QuickAnalysisCommandKind.InsertChart);
        route.ChartType.Should().Be(suggestion.Chart!.ChartType);
    }

    [Fact]
    public void Route_TableSuggestion_MapsToTable()
    {
        var suggestion = FindSuggestion(
            BuildModel(numericColumns: 2, hasHeader: true),
            QuickAnalysisGroup.Tables,
            s => s.Table!.TableKind == QuickAnalysisTableKind.Table);

        var route = QuickAnalysisCommandRouter.Route(suggestion);

        route.Kind.Should().Be(QuickAnalysisCommandKind.Table);
    }

    [Fact]
    public void Route_PivotTableSuggestion_PreservesPivotTableIntent()
    {
        var suggestion = FindSuggestion(
            BuildModel(numericColumns: 2, hasHeader: true),
            QuickAnalysisGroup.Tables,
            s => s.Table!.TableKind == QuickAnalysisTableKind.PivotTable);

        var route = QuickAnalysisCommandRouter.Route(suggestion);

        route.Kind.Should().Be(QuickAnalysisCommandKind.PivotTable);
    }

    private static QuickAnalysisSuggestion FormattingSuggestion(QuickAnalysisFormatKind formatKind)
    {
        var model = BuildModel(numericColumns: 1, hasHeader: false);
        return FindSuggestion(model, QuickAnalysisGroup.Formatting, s => s.ConditionalFormat!.FormatKind == formatKind);
    }

    private static QuickAnalysisSuggestion QuickAnalysisModelSuggestion(
        QuickAnalysisGroup group,
        QuickAnalysisTotalFunction function)
    {
        var model = BuildModel(numericColumns: 2, hasHeader: false);
        return FindSuggestion(model, group, s => s.Total!.Function == function);
    }

    private static QuickAnalysisOption Option(QuickAnalysisCommand command) =>
        new(
            QuickAnalysisGroup.Formatting,
            command.ToString(),
            command,
            QuickAnalysisPreviewKind.ConditionalFormat,
            "Preview",
            new QuickAnalysisPreviewVisual(QuickAnalysisPreviewVisualKind.Highlight));

    private static QuickAnalysisModel BuildModel(int numericColumns, bool hasHeader)
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, (uint)numericColumns));
        var columnKinds = Enumerable.Repeat(QuickAnalysisColumnKind.Numeric, numericColumns).ToArray();
        return QuickAnalysisModelBuilder.Build(
            new QuickAnalysisSelectionDescription(range, hasHeader, columnKinds));
    }

    private static QuickAnalysisSuggestion FindSuggestion(
        QuickAnalysisModel model,
        QuickAnalysisGroup group,
        Func<QuickAnalysisSuggestion, bool> predicate) =>
        model.SuggestionsFor(group).First(predicate);
}
