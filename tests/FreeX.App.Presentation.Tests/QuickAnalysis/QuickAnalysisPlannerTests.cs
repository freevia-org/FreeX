using FluentAssertions;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.QuickAnalysis;

public sealed class QuickAnalysisPlannerTests
{
    [Fact]
    public void BuildOptions_ReturnsNoOptionsForSingleCellSelection()
    {
        var sheetId = SheetId.New();
        var selection = new GridRange(new CellAddress(sheetId, 4, 2), new CellAddress(sheetId, 4, 2));

        QuickAnalysisPlanner.BuildOptions(selection).Should().BeEmpty();
        QuickAnalysisPlanner.BuildDisplayModel(selection).IsEmpty.Should().BeTrue();
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
            .Equal(
                QuickAnalysisGroup.Formatting,
                QuickAnalysisGroup.Charts,
                QuickAnalysisGroup.Totals,
                QuickAnalysisGroup.Tables,
                QuickAnalysisGroup.Sparklines);
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

        options.Where(option => option.Group == QuickAnalysisGroup.Formatting)
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

        options.Where(option => option.Group == QuickAnalysisGroup.Charts)
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
    public void BuildOptions_GroupsThroughSharedShellPlanner()
    {
        var sheetId = SheetId.New();
        var selection = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 4));

        var groups = QuickAnalysisShellPlanner.GroupOptions(QuickAnalysisPlanner.BuildOptions(selection));

        groups.Select(group => group.Group)
            .Should()
            .Equal(
                QuickAnalysisGroup.Formatting,
                QuickAnalysisGroup.Charts,
                QuickAnalysisGroup.Totals,
                QuickAnalysisGroup.Tables,
                QuickAnalysisGroup.Sparklines);
        groups[0].Options.Select(option => option.Label)
            .Should()
            .StartWith(["Data Bars", "Color Scale", "Icon Set"]);
    }

    [Fact]
    public void BuildDisplayModel_ReturnsRendererFacingItemsWithRoutesAndPreviewMetadata()
    {
        var sheetId = SheetId.New();
        var selection = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 4));

        var model = QuickAnalysisPlanner.BuildDisplayModel(selection);

        model.Groups.Select(group => group.Group)
            .Should()
            .Equal(
                QuickAnalysisGroup.Formatting,
                QuickAnalysisGroup.Charts,
                QuickAnalysisGroup.Totals,
                QuickAnalysisGroup.Tables,
                QuickAnalysisGroup.Sparklines);

        var dataBars = model.AllItems().Single(item => item.Id == "format.databars");
        dataBars.Label.Should().Be("Data Bars");
        dataBars.Command.Should().Be(QuickAnalysisCommand.DataBar);
        dataBars.Route.Kind.Should().Be(QuickAnalysisCommandKind.ConditionalFormat);
        dataBars.Route.ConditionalFormat.Should().Be(QuickAnalysisConditionalFormatCommand.DataBar);
        dataBars.PreviewVisual.Kind.Should().Be(QuickAnalysisPreviewVisualKind.DataBars);

        var percentTotal = model.AllItems().Single(item => item.Id == "total.percenttotal");
        percentTotal.Route.TotalFormulaKind.Should().Be(QuickAnalysisTotalFormulaKind.PercentTotal);
        percentTotal.PreviewKind.Should().Be(QuickAnalysisPreviewKind.Total);
    }

    [Theory]
    [InlineData(QuickAnalysisGroup.Formatting, "TableLoc_QaGroupFormatting", "Formatting")]
    [InlineData(QuickAnalysisGroup.Charts, "TableLoc_QaGroupCharts", "Charts")]
    [InlineData(QuickAnalysisGroup.Totals, "TableLoc_QaGroupTotals", "Totals")]
    [InlineData(QuickAnalysisGroup.Tables, "TableLoc_QaGroupTables", "Tables")]
    [InlineData(QuickAnalysisGroup.Sparklines, "TableLoc_QaGroupSparklines", "Sparklines")]
    public void ShellPlanner_CentralizesGroupTitles(
        QuickAnalysisGroup group,
        string expectedResourceKey,
        string expectedFallback)
    {
        QuickAnalysisShellPlanner.GroupTitleResourceKey(group).Should().Be(expectedResourceKey);
        QuickAnalysisShellPlanner.GroupTitleFallback(group).Should().Be(expectedFallback);
    }

    [Fact]
    public void ShellPlanner_BuildMenuPlan_MaterializesTitlesActionsAutomationIdsAndHoverPreviews()
    {
        var sheetId = SheetId.New();
        var selection = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 6, 5));
        var displayModel = QuickAnalysisPlanner.BuildDisplayModel(selection);

        var plan = QuickAnalysisShellPlanner.BuildMenuPlan(
            displayModel,
            QuickAnalysisShellCapabilities.DialogBacked,
            selection);

        plan.Groups.Select(group => group.TitleResourceKey)
            .Should()
            .Equal(
                "TableLoc_QaGroupFormatting",
                "TableLoc_QaGroupCharts",
                "TableLoc_QaGroupTotals",
                "TableLoc_QaGroupTables",
                "TableLoc_QaGroupSparklines");
        plan.Groups.Select(group => group.TitleFallback)
            .Should()
            .Equal("Formatting", "Charts", "Totals", "Tables", "Sparklines");

        var dataBars = plan.AllItems().Single(item => item.Id == "format.databars");
        dataBars.Label.Should().Be("Data Bars");
        dataBars.ToolTip.Should().Be("Preview data bars across the selected values.");
        dataBars.AutomationId.Should().Be("QuickAnalysis_format.databars");
        dataBars.HoverPreview.PreviewVisual.Kind.Should().Be(QuickAnalysisPreviewVisualKind.DataBars);
        dataBars.PreviewIcon.Glyph.Should().Be(QuickAnalysisPreviewIconGlyph.HorizontalBars);
        dataBars.Action.Kind.Should().Be(QuickAnalysisShellActionKind.OpenConditionalFormatDialog);
        dataBars.Action.ConditionalFormatDialogTitle.Should().Be("Data Bar");
        dataBars.HoverPreview.Range.Should().Be(selection);
        dataBars.HoverPreview.StatusText.Should().Be(dataBars.ToolTip);

        var sparkline = plan.AllItems().Single(item => item.Id == "sparkline.line");
        sparkline.Action.Kind.Should().Be(QuickAnalysisShellActionKind.InsertSparkline);
        sparkline.HoverPreview.Range.Should().Be(
            new GridRange(new CellAddress(sheetId, 2, 6), new CellAddress(sheetId, 6, 6)));
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
        options.Single(option => option.Command == QuickAnalysisCommand.ColumnSparkline)
            .PreviewVisual.Kind.Should().Be(QuickAnalysisPreviewVisualKind.ColumnSparkline);
        options.Single(option => option.Command == QuickAnalysisCommand.WinLossSparkline)
            .PreviewVisual.Kind.Should().Be(QuickAnalysisPreviewVisualKind.WinLossSparkline);
    }

    [Fact]
    public void BuildHoverPreview_CarriesVisualKindForDataBars()
    {
        var sheetId = SheetId.New();
        var selection = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 4));
        var dataBars = QuickAnalysisPlanner.BuildOptions(selection)
            .Single(option => option.Command == QuickAnalysisCommand.DataBar);

        var preview = QuickAnalysisPlanner.BuildHoverPreview(selection, dataBars);

        preview.PreviewVisual.Kind.Should().Be(QuickAnalysisPreviewVisualKind.DataBars);
        preview.Range.Should().Be(selection);
    }

    [Fact]
    public void BuildHoverPreview_FromDisplayItem_CarriesRouteAndVisualKind()
    {
        var sheetId = SheetId.New();
        var selection = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 4));
        var dataBars = QuickAnalysisPlanner.BuildDisplayModel(selection)
            .AllItems()
            .Single(item => item.Id == "format.databars");

        var preview = QuickAnalysisPlanner.BuildHoverPreview(selection, dataBars);

        preview.PreviewVisual.Kind.Should().Be(QuickAnalysisPreviewVisualKind.DataBars);
        preview.Route.Should().Be(dataBars.Route);
        preview.Range.Should().Be(selection);
        preview.StatusText.Should().Be(dataBars.PreviewText);
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
            new QuickAnalysisHoverPreview(
                selection,
                QuickAnalysisPreviewKind.Chart,
                "Column",
                "Preview a clustered column chart from the selected range.",
                QuickAnalysisCommand.ColumnChart,
                new QuickAnalysisPreviewVisual(QuickAnalysisPreviewVisualKind.ColumnChart)));
        QuickAnalysisPlanner.BuildHoverPreview(selection, table).Should().Be(
            new QuickAnalysisHoverPreview(
                selection,
                QuickAnalysisPreviewKind.Table,
                "Format as Table",
                "Preview formatting the selection as a table.",
                QuickAnalysisCommand.FormatAsTable,
                new QuickAnalysisPreviewVisual(QuickAnalysisPreviewVisualKind.Table)));
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

    [Fact]
    public void BuildHoverPreview_StaysInsideSheetAtLastColumn()
    {
        var sheetId = SheetId.New();
        var selection = new GridRange(
            new CellAddress(sheetId, 2, CellAddress.MaxCol),
            new CellAddress(sheetId, 6, CellAddress.MaxCol));
        var sum = QuickAnalysisPlanner.BuildOptions(selection)
            .Single(option => option.Command == QuickAnalysisCommand.Sum);

        QuickAnalysisPlanner.BuildHoverPreview(selection, sum).Range.Should().Be(selection);
    }

    [Theory]
    [InlineData(QuickAnalysisCommand.DataBar, QuickAnalysisPreviewVisualKind.DataBars)]
    [InlineData(QuickAnalysisCommand.Sum, QuickAnalysisPreviewVisualKind.TotalFormula)]
    [InlineData(QuickAnalysisCommand.LineSparkline, QuickAnalysisPreviewVisualKind.LineSparkline)]
    [InlineData(QuickAnalysisCommand.ColumnSparkline, QuickAnalysisPreviewVisualKind.ColumnSparkline)]
    [InlineData(QuickAnalysisCommand.WinLossSparkline, QuickAnalysisPreviewVisualKind.WinLossSparkline)]
    public void BuildHoverPreview_PreservesCommandAndVisualDescriptor(
        QuickAnalysisCommand command,
        QuickAnalysisPreviewVisualKind visualKind)
    {
        var sheetId = SheetId.New();
        var selection = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 6, 5));
        var option = QuickAnalysisPlanner.BuildOptions(selection)
            .Single(option => option.Command == command);

        var preview = QuickAnalysisPlanner.BuildHoverPreview(selection, option);

        preview.Command.Should().Be(command);
        preview.PreviewVisual.Should().Be(new QuickAnalysisPreviewVisual(visualKind));
    }
}
