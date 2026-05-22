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
                QuickAnalysisCommand.LessThan,
                QuickAnalysisCommand.Between,
                QuickAnalysisCommand.EqualTo,
                QuickAnalysisCommand.TextContains,
                QuickAnalysisCommand.DateOccurring,
                QuickAnalysisCommand.DuplicateValues,
                QuickAnalysisCommand.IconSet,
                QuickAnalysisCommand.Top10,
                QuickAnalysisCommand.Top10Percent,
                QuickAnalysisCommand.Bottom10,
                QuickAnalysisCommand.Bottom10Percent,
                QuickAnalysisCommand.AboveAverage,
                QuickAnalysisCommand.BelowAverage,
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
                QuickAnalysisCommand.PercentTotal,
                QuickAnalysisCommand.RunningTotal,
                QuickAnalysisCommand.Max,
                QuickAnalysisCommand.Min,
                QuickAnalysisCommand.FormatAsTable,
                QuickAnalysisCommand.LineSparkline
            ]);

        options.Where(option => option.Group == "Formatting")
            .Select(option => option.Label)
            .Should()
            .Equal(
                "Data Bars",
                "Color Scale",
                "Icon Set",
                "Greater Than...",
                "Less Than...",
                "Between...",
                "Equal To...",
                "Text that Contains...",
                "A Date Occurring...",
                "Duplicate Values...",
                "Top 10...",
                "Top 10%",
                "Bottom 10...",
                "Bottom 10%",
                "Above Average",
                "Below Average",
                "Clear Conditional Formatting");

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
                "Stock",
                "More Charts...");

        options.Single(option => option.Label == "More Charts...")
            .Command.Should().Be(QuickAnalysisCommand.MoreCharts);
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
        options.Single(option => option.Command == QuickAnalysisCommand.PercentTotal)
            .PreviewKind.Should().Be(QuickAnalysisPreviewKind.Total);
        options.Single(option => option.Command == QuickAnalysisCommand.FormatAsTable)
            .PreviewKind.Should().Be(QuickAnalysisPreviewKind.Table);
        options.Single(option => option.Command == QuickAnalysisCommand.LineSparkline)
            .PreviewKind.Should().Be(QuickAnalysisPreviewKind.Sparkline);
    }

    [Fact]
    public void BuildOptions_AttachesVisualPreviewDescriptorToEachOption()
    {
        var sheetId = SheetId.New();
        var selection = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 4));

        var options = QuickAnalysisPlanner.BuildOptions(selection);

        options.Should().OnlyContain(option => option.PreviewVisual.Kind != QuickAnalysisPreviewVisualKind.None);
        options.Single(option => option.Command == QuickAnalysisCommand.DataBar)
            .PreviewVisual.Kind.Should().Be(QuickAnalysisPreviewVisualKind.DataBars);
        options.Single(option => option.Command == QuickAnalysisCommand.ColumnChart)
            .PreviewVisual.Kind.Should().Be(QuickAnalysisPreviewVisualKind.ColumnChart);
        options.Single(option => option.Command == QuickAnalysisCommand.Sum)
            .PreviewVisual.Kind.Should().Be(QuickAnalysisPreviewVisualKind.TotalFormula);
        options.Single(option => option.Command == QuickAnalysisCommand.LineSparkline)
            .PreviewVisual.Kind.Should().Be(QuickAnalysisPreviewVisualKind.LineSparkline);
    }

    [Fact]
    public void BuildHoverPreview_UsesSelectionForFormattingChartsAndTables()
    {
        var sheetId = SheetId.New();
        var selection = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 6, 5));
        var chart = QuickAnalysisPlanner.BuildOptions(selection)
            .Single(option => option.Command == QuickAnalysisCommand.ColumnChart);
        var table = QuickAnalysisPlanner.BuildOptions(selection)
            .Single(option => option.Command == QuickAnalysisCommand.FormatAsTable);

        QuickAnalysisPlanner.BuildHoverPreview(selection, chart).Should().Be(
            new QuickAnalysisHoverPreview(selection, QuickAnalysisPreviewKind.Chart, "Column", "Preview a clustered column chart from the selected range."));
        QuickAnalysisPlanner.BuildHoverPreview(selection, table).Should().Be(
            new QuickAnalysisHoverPreview(selection, QuickAnalysisPreviewKind.Table, "Format as Table", "Preview formatting the selection as a table."));
    }

    [Fact]
    public void BuildHoverPreview_PlacesTotalsAndSparklinesBesideSelection()
    {
        var sheetId = SheetId.New();
        var selection = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 6, 5));
        var sum = QuickAnalysisPlanner.BuildOptions(selection)
            .Single(option => option.Command == QuickAnalysisCommand.Sum);
        var percentTotal = QuickAnalysisPlanner.BuildOptions(selection)
            .Single(option => option.Command == QuickAnalysisCommand.PercentTotal);
        var sparkline = QuickAnalysisPlanner.BuildOptions(selection)
            .Single(option => option.Command == QuickAnalysisCommand.LineSparkline);

        QuickAnalysisPlanner.BuildHoverPreview(selection, sum)!.Range.Should().Be(
            new GridRange(new CellAddress(sheetId, 2, 6), new CellAddress(sheetId, 6, 6)));
        QuickAnalysisPlanner.BuildHoverPreview(selection, percentTotal)!.Range.Should().Be(
            new GridRange(new CellAddress(sheetId, 2, 6), new CellAddress(sheetId, 6, 6)));
        QuickAnalysisPlanner.BuildHoverPreview(selection, sparkline)!.Range.Should().Be(
            new GridRange(new CellAddress(sheetId, 2, 6), new CellAddress(sheetId, 6, 6)));
    }
}
