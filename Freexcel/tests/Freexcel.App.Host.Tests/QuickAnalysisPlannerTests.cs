using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class QuickAnalysisPlannerTests
{
    [Fact]
    public void BuildOptions_ReturnsNoOptionsForSingleCellSelection()
    {
        var sheetId = SheetId.New();
        var selection = new GridRange(new CellAddress(sheetId, 4, 2), new CellAddress(sheetId, 4, 2));

        QuickAnalysisPlanner.BuildOptions(selection).Should().BeEmpty();
    }

    [Fact]
    public void BuildOptions_ReturnsExcelLikeGroupsForMultiCellSelection()
    {
        var sheetId = SheetId.New();
        var selection = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 4));

        var options = QuickAnalysisPlanner.BuildOptions(selection);

        options.Select(option => option.Group)
            .Distinct()
            .Should()
            .Equal("Formatting", "Charts", "Totals", "Tables", "Sparklines");
        options.Select(option => option.Command)
            .Should()
            .Contain([
                QuickAnalysisCommand.DataBar,
                QuickAnalysisCommand.IconSet,
                QuickAnalysisCommand.Top10,
                QuickAnalysisCommand.ClearConditionalFormatting,
                QuickAnalysisCommand.ColumnChart,
                QuickAnalysisCommand.StackedColumnChart,
                QuickAnalysisCommand.PercentStackedColumnChart,
                QuickAnalysisCommand.BarChart,
                QuickAnalysisCommand.StackedBarChart,
                QuickAnalysisCommand.PercentStackedBarChart,
                QuickAnalysisCommand.DoughnutChart,
                QuickAnalysisCommand.AreaChart,
                QuickAnalysisCommand.ScatterChart,
                QuickAnalysisCommand.BubbleChart,
                QuickAnalysisCommand.RadarChart,
                QuickAnalysisCommand.StockChart,
                QuickAnalysisCommand.Sum,
                QuickAnalysisCommand.Max,
                QuickAnalysisCommand.Min,
                QuickAnalysisCommand.FormatAsTable,
                QuickAnalysisCommand.LineSparkline
            ]);

        options.Where(option => option.Group == "Charts")
            .Select(option => option.Label)
            .Should()
            .Equal(
                "Column",
                "Stacked Column",
                "100% Stacked Column",
                "Line",
                "Pie",
                "Doughnut",
                "Bar",
                "Stacked Bar",
                "100% Stacked Bar",
                "Area",
                "Scatter",
                "Bubble",
                "Radar",
                "Stock");
    }

    [Fact]
    public void BuildOptions_AttachesHoverPreviewMetadataToEachOption()
    {
        var sheetId = SheetId.New();
        var selection = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 4));

        var options = QuickAnalysisPlanner.BuildOptions(selection);

        options.Should().OnlyContain(option => !string.IsNullOrWhiteSpace(option.PreviewText));
        options.Single(option => option.Command == QuickAnalysisCommand.DataBar)
            .PreviewKind.Should().Be(QuickAnalysisPreviewKind.ConditionalFormat);
        options.Single(option => option.Command == QuickAnalysisCommand.ColumnChart)
            .PreviewKind.Should().Be(QuickAnalysisPreviewKind.Chart);
        options.Single(option => option.Command == QuickAnalysisCommand.Sum)
            .PreviewKind.Should().Be(QuickAnalysisPreviewKind.Total);
        options.Single(option => option.Command == QuickAnalysisCommand.FormatAsTable)
            .PreviewKind.Should().Be(QuickAnalysisPreviewKind.Table);
        options.Single(option => option.Command == QuickAnalysisCommand.LineSparkline)
            .PreviewKind.Should().Be(QuickAnalysisPreviewKind.Sparkline);
    }
}
