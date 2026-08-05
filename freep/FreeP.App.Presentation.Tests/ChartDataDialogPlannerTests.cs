using System.Globalization;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartDataDialogPlannerTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Fact]
    public void FromChart_DeepCopiesNamesAndPreservesNullValues()
    {
        var chart = MakeChart();

        var planner = ChartDataDialogPlanner.FromChart(chart);
        planner.SetCategory(0, "Updated");
        planner.SetSeriesName(0, "Forecast");
        planner.SetValue(1, 1, 22.0);

        chart.Categories[0].Should().Be("Q1");
        chart.Series[0].Name.Should().Be("Sales");
        chart.Series[1].Values[1].Should().BeNull("the planner should not mutate the live chart before OK");
    }

    [Fact]
    public void FromChart_PadsShortSeriesWithNullsAndTrimsLongSeries()
    {
        var chart = MakeChart();
        chart.Series[0].Values.RemoveAt(2);
        chart.Series[1].Values.Add(99.0);

        var planner = ChartDataDialogPlanner.FromChart(chart);

        planner.ValuesForCommit()[0].Should().Equal(new double?[] { 1.0, 2.0, null });
        planner.ValuesForCommit()[1].Should().Equal(new double?[] { 4.0, null, 6.0 });
    }

    [Fact]
    public void AddSeries_AppendsNamedNullSeries()
    {
        var planner = ChartDataDialogPlanner.FromChart(MakeChart());

        planner.AddSeries();

        planner.SeriesNamesForCommit().Should().Equal("Sales", "Budget", "Series 3");
        planner.ValuesForCommit()[2].Should().Equal(new double?[] { null, null, null });
    }

    [Fact]
    public void MoveSeries_ReordersNamesValuesAndScatterCoordinatesTogether()
    {
        var chart = MakeChart();
        chart.ChartType = ChartType.Scatter;
        chart.Series[0].XValues.AddRange(new double?[] { 10, 20, 30 });
        chart.Series[1].XValues.AddRange(new double?[] { 40, 50, 60 });

        var planner = ChartDataDialogPlanner.FromChart(chart);

        planner.MoveSeries(1, 0).Should().BeTrue();

        planner.SeriesNamesForCommit().Should().Equal("Budget", "Sales");
        planner.ValuesForCommit()[0].Should().Equal(new double?[] { 4.0, null, 6.0 });
        planner.XValuesForCommit()[0].Should().Equal(new double?[] { 40, 50, 60 });
    }

    [Fact]
    public void MoveCategory_ReordersLabelsValuesAndScatterCoordinatesTogether()
    {
        var chart = MakeChart();
        chart.ChartType = ChartType.Scatter;
        chart.Series[0].XValues.AddRange(new double?[] { 10, 20, 30 });
        chart.Series[1].XValues.AddRange(new double?[] { 40, 50, 60 });

        var planner = ChartDataDialogPlanner.FromChart(chart);

        planner.MoveCategory(2, 0).Should().BeTrue();

        planner.CategoriesForCommit().Should().Equal("Q3", "Q1", "Q2");
        planner.ValuesForCommit()[0].Should().Equal(new double?[] { 3.0, 1.0, 2.0 });
        planner.ValuesForCommit()[1].Should().Equal(new double?[] { 6.0, 4.0, null });
        planner.XValuesForCommit()[0].Should().Equal(new double?[] { 30, 10, 20 });
        planner.XValuesForCommit()[1].Should().Equal(new double?[] { 60, 40, 50 });
        planner.MoveCategory(0, 3).Should().BeFalse();
    }

    [Fact]
    public void RemoveIndexedSeriesAndCategory_PreservesScatterCoordinates()
    {
        var chart = MakeChart();
        chart.ChartType = ChartType.Scatter;
        chart.Series[0].XValues.AddRange(new double?[] { 10, 20, 30 });
        chart.Series[1].XValues.AddRange(new double?[] { 40, 50, 60 });

        var planner = ChartDataDialogPlanner.FromChart(chart);

        planner.RemoveSeriesAt(0).Should().BeTrue();
        planner.SeriesNamesForCommit().Should().Equal("Budget");
        planner.ValuesForCommit()[0].Should().Equal(new double?[] { 4.0, null, 6.0 });
        planner.XValuesForCommit()[0].Should().Equal(new double?[] { 40, 50, 60 });

        planner.RemoveCategoryAt(1).Should().BeTrue();
        planner.CategoriesForCommit().Should().Equal("Q1", "Q3");
        planner.ValuesForCommit()[0].Should().Equal(new double?[] { 4.0, 6.0 });
        planner.XValuesForCommit()[0].Should().Equal(new double?[] { 40, 60 });
        planner.RemoveSeriesAt(4).Should().BeFalse();
        planner.RemoveCategoryAt(4).Should().BeFalse();
    }

    [Fact]
    public void AddCategory_AppendsNamedCategoryAndNullValueSlots()
    {
        var planner = ChartDataDialogPlanner.FromChart(MakeChart());

        planner.AddCategory();

        planner.CategoriesForCommit().Should().Equal("Q1", "Q2", "Q3", "Cat 4");
        planner.ValuesForCommit()[0].Should().Equal(new double?[] { 1.0, 2.0, 3.0, null });
        planner.ValuesForCommit()[1].Should().Equal(new double?[] { 4.0, null, 6.0, null });
    }

    [Fact]
    public void SwitchRowsAndColumns_TransposesLabelsAndValuesWhilePreservingGaps()
    {
        var planner = ChartDataDialogPlanner.FromChart(MakeChart());

        planner.SwitchRowsAndColumns();

        planner.CategoriesForCommit().Should().Equal("Sales", "Budget");
        planner.SeriesNamesForCommit().Should().Equal("Q1", "Q2", "Q3");
        planner.ValuesForCommit()[0].Should().Equal(new double?[] { 1.0, 4.0 });
        planner.ValuesForCommit()[1].Should().Equal(new double?[] { 2.0, null });
        planner.ValuesForCommit()[2].Should().Equal(new double?[] { 3.0, 6.0 });
        planner.ValuesForCommit().Should().AllSatisfy(values => values.Count.Should().Be(2));
    }

    [Fact]
    public void RemoveLastSeriesAndCategory_AreNoOpsWhenEmpty()
    {
        var chart = new ChartShape();
        var planner = ChartDataDialogPlanner.FromChart(chart);

        planner.RemoveLastSeries();
        planner.RemoveLastCategory();

        planner.SeriesCount.Should().Be(0);
        planner.CategoryCount.Should().Be(0);
    }

    [Fact]
    public void CommitSnapshots_AreDetachedFromPlanner()
    {
        var planner = ChartDataDialogPlanner.FromChart(MakeChart());

        var categories = planner.CategoriesForCommit();
        var values = planner.ValuesForCommit();
        planner.SetCategory(0, "Changed");
        planner.SetValue(0, 0, 42.0);

        categories[0].Should().Be("Q1");
        values[0][0].Should().Be(1.0);
    }

    [Fact]
    public void BuildTableProjection_ProjectsCategoryRowsAndSeriesColumns()
    {
        var planner = ChartDataDialogPlanner.FromChart(MakeChart());

        var table = planner.BuildTableProjection();

        table.CategoryColumnHeader.Should().Be(ChartDataDialogPlanner.CategoryColumnHeader);
        table.SeriesColumns.Select(column => column.Name).Should().Equal("Sales", "Budget");
        table.SeriesColumns.Select(column => column.SeriesIndex).Should().Equal(0, 1);
        table.SeriesColumns.Select(column => column.ValueIndex).Should().Equal(0, 1);
        table.Rows.Select(row => row.Category).Should().Equal("Q1", "Q2", "Q3");
        table.Rows[1].Values.Select(cell => cell.Value).Should().Equal(new double?[] { 2.0, null });
    }

    [Fact]
    public void BuildTableProjection_ValueCategoryAndSeriesEditsUpdatePlanner()
    {
        var planner = ChartDataDialogPlanner.FromChart(MakeChart());
        var table = planner.BuildTableProjection();

        table.SeriesColumns[1].Name = "Actual";
        table.Rows[1].Category = "Second";
        table.Rows[1].Values[0].Value = 12.5;
        table.Rows[1].Values[1].Value = null;

        planner.SeriesNamesForCommit().Should().Equal("Sales", "Actual");
        planner.CategoriesForCommit().Should().Equal("Q1", "Second", "Q3");
        planner.ValuesForCommit()[0].Should().Equal(new double?[] { 1.0, 12.5, 3.0 });
        planner.ValuesForCommit()[1].Should().Equal(new double?[] { 4.0, null, 6.0 });
    }

    [Fact]
    public void ScatterProjection_ExposesEditableXAndYValues()
    {
        var chart = MakeCoordinateChart(ChartType.Scatter);
        var planner = ChartDataDialogPlanner.FromChart(chart);

        var table = planner.BuildTableProjection();

        table.SeriesColumns.Select(column => column.Kind)
            .Should().Equal(
                ChartDataDialogValueKind.XValue,
                ChartDataDialogValueKind.Value,
                ChartDataDialogValueKind.XValue,
                ChartDataDialogValueKind.Value);
        table.SeriesColumns.Select(column => column.Header)
            .Should().Equal("Sales X", "Sales", "Budget X", "Budget");
        table.Rows[1].Values[0].Value = 2.5;
        table.Rows[1].Values[1].Value = 12.5;

        var commit = planner.BuildCommitPlan();

        commit.XValues[0].Should().Equal(new double?[] { 1.0, 2.5, 3.0 });
        commit.Values[0].Should().Equal(new double?[] { 1.0, 12.5, 3.0 });
    }

    [Fact]
    public void BubbleProjection_ExposesEditableXYSizesAndSeedsNewCoordinates()
    {
        var planner = ChartDataDialogPlanner.FromChart(MakeCoordinateChart(ChartType.Bubble));

        var table = planner.BuildTableProjection();

        table.SeriesColumns.Select(column => column.Kind)
            .Should().Equal(
                ChartDataDialogValueKind.XValue,
                ChartDataDialogValueKind.Value,
                ChartDataDialogValueKind.BubbleSize,
                ChartDataDialogValueKind.XValue,
                ChartDataDialogValueKind.Value,
                ChartDataDialogValueKind.BubbleSize);
        table.Rows[0].Values[2].Value = 8.0;

        var commit = planner.BuildCommitPlan();

        commit.XValues[1].Should().Equal(new double?[] { 1.0, 2.0, 3.0 });
        commit.BubbleSizes[0].Should().Equal(new double?[] { 8.0, 4.0, 5.0 });
    }

    [Fact]
    public void BuildCommitPlan_AppliesCategoryEditsAndReturnsDetachedCommandValues()
    {
        var planner = ChartDataDialogPlanner.FromChart(MakeChart());
        planner.SetSeriesName(1, "Actual");
        planner.SetValue(0, 2, 9.75);

        var commit = planner.BuildCommitPlan(new[]
        {
            new ChartDataDialogCategoryEdit(0, "First"),
            new ChartDataDialogCategoryEdit(1, null),
            new ChartDataDialogCategoryEdit(99, "Ignored")
        });

        commit.Categories.Should().Equal("First", "", "Q3");
        commit.SeriesNames.Should().Equal("Sales", "Actual");
        commit.Values[0].Should().Equal(new double?[] { 1.0, 2.0, 9.75 });
        var commandValues = commit.ValuesForCommand()
            .Select(values => values.ToArray())
            .ToList();
        commandValues[0].Should().Equal(new double?[] { 1.0, 2.0, 9.75 });
        commandValues[1].Should().Equal(new double?[] { 4.0, null, 6.0 });

        planner.SetCategory(0, "Mutated");
        planner.SetValue(0, 2, 42.0);
        commit.Categories[0].Should().Be("First");
        commit.Values[0][2].Should().Be(9.75);
    }

    [Fact]
    public void ChartType_IsWorkingCopyStateAndReturnsInCommitPlan()
    {
        var planner = ChartDataDialogPlanner.FromChart(MakeChart());

        planner.SelectedChartType.Should().Be(ChartType.ColumnClustered);
        planner.SetChartType(ChartType.LineMarkers);

        var commit = planner.BuildCommitPlan();

        commit.ChartType.Should().Be(ChartType.LineMarkers);
        planner.SetChartType(ChartType.Unknown);
        planner.SelectedChartType.Should().Be(ChartType.LineMarkers,
            "Unknown is not an editable chart type");
    }

    [Fact]
    public void SetChartType_BubbleSeedsMissingSizesWhenSomeSizesAlreadyExist()
    {
        var planner = ChartDataDialogPlanner.FromChart(MakeCoordinateChart(ChartType.Scatter));
        planner.SetValue(0, 0, 8.0, ChartDataDialogValueKind.BubbleSize);

        planner.SetChartType(ChartType.Bubble);

        var commit = planner.BuildCommitPlan();
        commit.BubbleSizes[0].Should().Equal(8.0, 1.0, 1.0);
        commit.BubbleSizes[1].Should().Equal(1.0, 1.0, 1.0);
    }

    [Fact]
    public void SetChartType_StockShapesOrdinaryDataAsEditableOhlcSeries()
    {
        var planner = ChartDataDialogPlanner.FromChart(MakeChart());

        planner.SetChartType(ChartType.Stock);

        var table = planner.BuildTableProjection();
        table.SeriesColumns.Select(column => column.Header)
            .Should().Equal("Open", "High", "Low", "Close");
        table.Rows[0].Values.Select(cell => cell.Value)
            .Should().Equal(1.0, 4.0, null, null);

        var commit = planner.BuildCommitPlan();
        commit.ChartType.Should().Be(ChartType.Stock);
        commit.SeriesNames.Should().Equal("Open", "High", "Low", "Close");
        commit.Values.Should().HaveCount(4);
    }

    [Fact]
    public void BuildSurfacePlan_ExposesSharedDialogLabelsAndCommandId()
    {
        var plan = ChartDataDialogPlanner.BuildSurfacePlan();

        plan.CommandId.Should().Be(ChartDataDialogPlanner.EditDataCommandId);
        plan.Title.Should().Be("Edit Chart Data");
        plan.Width.Should().Be(640);
        plan.Height.Should().Be(440);
        plan.AddSeriesLabel.Should().Be("+ Series");
        plan.RemoveSeriesLabel.Should().Be("- Series");
        plan.MoveSeriesUpLabel.Should().Be("Move Series Up");
        plan.MoveSeriesDownLabel.Should().Be("Move Series Down");
        plan.AddCategoryLabel.Should().Be("+ Category");
        plan.RemoveCategoryLabel.Should().Be("- Category");
        plan.MoveCategoryLeftLabel.Should().Be("Move Category Left");
        plan.MoveCategoryRightLabel.Should().Be("Move Category Right");
        plan.SwitchRowsAndColumnsLabel.Should().Be("Switch Row/Column");
        plan.ChartTypeLabel.Should().Be("Chart Type");
        plan.OkLabel.Should().Be("OK");
        plan.CancelLabel.Should().Be("Cancel");
        ChartDataDialogPlanner.ChartTypeOptions.Should().Contain(option =>
            option.Value == ChartType.LineMarkers && option.Label == "Line with Markers");
        ChartDataDialogPlanner.ChartTypeOptions.Should().Contain(option =>
            option.Value == ChartType.Funnel && option.Label == "Funnel");
        ChartDataDialogPlanner.ChartTypeOptions.Should().Contain(option =>
            option.Value == ChartType.Waterfall && option.Label == "Waterfall");
    }

    [Fact]
    public void ApplySeriesNameAndValueEdits_UsesSharedParsingPolicyAndIgnoresOutOfRange()
    {
        var planner = ChartDataDialogPlanner.FromChart(MakeChart());

        planner.ApplySeriesNameEdits(new[]
        {
            new ChartDataDialogSeriesNameEdit(1, "Actual"),
            new ChartDataDialogSeriesNameEdit(99, "Ignored")
        });
        planner.ApplyValueEdits(
            new[]
            {
                new ChartDataDialogValueEdit(0, 1, "12,5"),
                new ChartDataDialogValueEdit(1, 0, "   "),
                new ChartDataDialogValueEdit(1, 2, 8.0),
                new ChartDataDialogValueEdit(99, 99, "123")
            },
            CultureInfo.GetCultureInfo("fr-FR"));

        planner.SeriesNamesForCommit().Should().Equal("Sales", "Actual");
        planner.ValuesForCommit()[0].Should().Equal(new double?[] { 1.0, 12.5, 3.0 });
        planner.ValuesForCommit()[1].Should().Equal(new double?[] { null, null, 8.0 });
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData(1234.5, "1234.5")]
    public void FormatCellValue_UsesG6OrBlank(double? value, string expected)
    {
        ChartDataDialogPlanner.FormatCellValue(value, Invariant).Should().Be(expected);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("abc", null)]
    [InlineData("12.5", 12.5)]
    public void ParseCellValue_ParsesNumericTextAndMapsBlankOrInvalidToNull(
        string text,
        double? expected)
    {
        ChartDataDialogPlanner.ParseCellValue(text, Invariant).Should().Be(expected);
    }

    [Fact]
    public void ParseCellValue_UsesProvidedCulture()
    {
        ChartDataDialogPlanner.ParseCellValue("12,5", CultureInfo.GetCultureInfo("fr-FR"))
            .Should().Be(12.5);
    }

    [Fact]
    public void ChartDisplayOptionsPlanner_UsesWorkingCopyAndBuildsOptions()
    {
        var chart = MakeChart();
        chart.Title = "Existing";
        chart.TitleOverlay = false;
        chart.PlotVisibleOnly = false;
        chart.Legend = LegendPosition.Right;
        chart.DataLabels = new ChartDataLabels { ShowValue = true, Position = DataLabelPosition.Center };
        chart.CategoryAxis.HasMajorGridlines = true;
        chart.ValueAxis.HasMajorGridlines = false;
        chart.ChartType = ChartType.Stock;
        chart.StyleId = 102;

        var planner = ChartDisplayOptionsPlanner.FromChart(chart);
        planner.SetTitle("Revenue");
        planner.SetTitleOverlay(true);
        planner.SetPlotVisibleOnly(true);
        planner.SetRoundedCorners(true);
        planner.SetLegend(LegendPosition.Bottom);
        planner.SetShowValueLabels(false);
        planner.SetShowPercentLabels(true);
        planner.SetShowCategoryLabels(true);
        planner.SetShowSeriesLabels(true);
        planner.SetShowLegendKeys(true);
        planner.SetShowBubbleSize(true);
        planner.SetShowLeaderLines(true);
        planner.SetLabelPosition(DataLabelPosition.OutsideEnd);
        planner.SetLabelNumberFormat("0.0%");
        planner.SetLabelSeparator(" | ");
        planner.SetLabelFontFamily("Aptos");
        planner.SetLabelFontSize(9);
        planner.SetLabelBold(true);
        planner.SetLabelItalic(false);
        planner.SetLabelColor("#2F5496");
        planner.SetCategoryGridlines(false);
        planner.SetValueGridlines(true);
        planner.SetBarGapWidthPercent(40);
        planner.SetBarOverlapPercent(55);
        planner.SetDisplayBlanksAs(ChartDisplayBlanksAs.Span);
        planner.SetShowDataLabelsOverMaximum(true);
        planner.SetVaryColors(true);
        planner.SetLegendOverlay(true);
        planner.SetHighLowLines(false);
        planner.SetStyleId(12);

        var commit = planner.BuildCommitPlan();
        commit.Should().Be(new ChartDisplayOptions(
            "Revenue", LegendPosition.Bottom, false, DataLabelPosition.OutsideEnd, false, true,
            true, true, true, true, "0.0%", " | ", 40, 55, ChartDisplayBlanksAs.Span, true, true, true, false,
            commit.LabelTextStyle, true, 12, true, true, true, true, null, false, false, null));
        commit.RoundedCorners.Should().BeTrue();
        commit.LabelTextStyle.Should().NotBeNull();
        commit.LabelTextStyle!.FontFamily.Should().Be("Aptos");
        commit.LabelTextStyle.FontSizePt.Should().Be(9);
        commit.LabelTextStyle.Bold.Should().BeTrue();
        commit.LabelTextStyle.Italic.Should().BeFalse();
        commit.LabelTextStyle.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x2F5496));
        chart.Title.Should().Be("Existing", "the dialog planner is a working copy");
        ChartDisplayOptionsPlanner.BuildSurfacePlan().CommandId
            .Should().Be(ChartDisplayOptionsPlanner.CommandId);
        planner.AvailableStyleOptions.Should().Contain(option => option.Value == 102);
    }

    [Fact]
    public void ChartDisplayOptionsPlanner_ChartExTitleLayoutUsesSharedCommitPlan()
    {
        var chart = MakeChart();
        chart.IsChartEx = true;

        var planner = ChartDisplayOptionsPlanner.FromChart(chart);
        planner.SetTitlePosition(ChartExTitlePosition.Right);
        planner.SetTitleAlignment(ChartExTitleAlignment.Far);

        var commit = planner.BuildCommitPlan();

        commit.ChartExTitlePosition.Should().Be(ChartExTitlePosition.Right);
        commit.ChartExTitleAlignment.Should().Be(ChartExTitleAlignment.Far);
    }

    [Fact]
    public void ChartDisplayOptionsPlanner_WaterfallConnectorLinesAreScopedToWaterfallCharts()
    {
        var waterfall = MakeChart();
        waterfall.ChartType = ChartType.Waterfall;
        waterfall.ShowWaterfallConnectorLines = true;

        var planner = ChartDisplayOptionsPlanner.FromChart(waterfall);
        planner.SupportsWaterfallConnectorLines.Should().BeTrue();
        planner.WaterfallConnectorLines.Should().BeTrue();
        planner.SetWaterfallConnectorLines(false);
        planner.BuildCommitPlan().ShowWaterfallConnectorLines.Should().BeFalse();

        var column = ChartDisplayOptionsPlanner.FromChart(MakeChart());
        column.SupportsWaterfallConnectorLines.Should().BeFalse();
        column.WaterfallConnectorLines.Should().BeNull();
        column.SetWaterfallConnectorLines(false);
        column.BuildCommitPlan().ShowWaterfallConnectorLines.Should().BeNull();
    }

    [Fact]
    public void ChartDisplayOptionsPlanner_LineDecorationsAreScopedToLineAndStockCharts()
    {
        var line = MakeChart();
        line.ChartType = ChartType.LineMarkers;
        line.ShowDropLines = true;
        line.ShowUpDownBars = false;

        var planner = ChartDisplayOptionsPlanner.FromChart(line);
        planner.SupportsDropLines.Should().BeTrue();
        planner.SupportsUpDownBars.Should().BeTrue();
        planner.DropLines.Should().BeTrue();
        planner.UpDownBars.Should().BeFalse();
        planner.SetDropLines(false);
        planner.SetUpDownBars(true);
        var commit = planner.BuildCommitPlan();
        commit.ShowDropLines.Should().BeFalse();
        commit.ShowUpDownBars.Should().BeTrue();

        var column = ChartDisplayOptionsPlanner.FromChart(MakeChart());
        column.SupportsDropLines.Should().BeFalse();
        column.SupportsUpDownBars.Should().BeFalse();
        column.BuildCommitPlan().ShowDropLines.Should().BeNull();
        column.BuildCommitPlan().ShowUpDownBars.Should().BeNull();
    }

    [Fact]
    public void ChartDisplayOptionsPlanner_SeriesLinesAreScopedToStackedBarFamilies()
    {
        var stacked = MakeChart();
        stacked.ChartType = ChartType.ColumnStacked;
        stacked.SeriesLinesSpecified = true;

        var planner = ChartDisplayOptionsPlanner.FromChart(stacked);
        planner.SupportsSeriesLines.Should().BeTrue();
        planner.SeriesLines.Should().BeTrue();
        planner.SetSeriesLines(false);
        planner.BuildCommitPlan().ShowSeriesLines.Should().BeFalse();

        var line = ChartDisplayOptionsPlanner.FromChart(MakeChart());
        line.SupportsSeriesLines.Should().BeFalse();
        line.BuildCommitPlan().ShowSeriesLines.Should().BeNull();
    }

    [Fact]
    public void ChartDisplayOptionsPlanner_ClampsBarPlotRangesAndPreservesAutomaticValues()
    {
        var planner = ChartDisplayOptionsPlanner.FromChart(MakeChart());

        planner.SetBarGapWidthPercent(700);
        planner.SetBarOverlapPercent(-200);
        planner.BuildCommitPlan().Should().Match<ChartDisplayOptions>(options =>
            options.BarGapWidthPercent == 500 && options.BarOverlapPercent == -100);

        planner.SetBarGapWidthPercent(null);
        planner.SetBarOverlapPercent(null);
        planner.SetDisplayBlanksAs(null);
        planner.BuildCommitPlan().BarGapWidthPercent.Should().BeNull();
        planner.BuildCommitPlan().BarOverlapPercent.Should().BeNull();
        planner.BuildCommitPlan().DisplayBlanksAs.Should().BeNull();
    }

    [Fact]
    public void ChartDataTableOptionsPlanner_UsesWorkingCopyAndBuildsOptions()
    {
        var chart = MakeChart();
        chart.DataTable = new ChartDataTableSettings
        {
            ShowHorizontalBorder = true,
            ShowVerticalBorder = false,
            ShowOutlineBorder = true,
            ShowLegendKeys = false,
        };

        var planner = ChartDataTableOptionsPlanner.FromChart(chart);
        planner.SetShowDataTable(false);
        planner.SetShowHorizontalBorder(false);
        planner.SetShowVerticalBorder(true);
        planner.SetShowOutlineBorder(false);
        planner.SetShowLegendKeys(true);
        planner.SetBackgroundColor("#F2F2F2");
        planner.SetBorderColor("#4472C4");
        planner.SetBorderWidth(1.25);
        planner.SetTextColor("#112233");
        planner.SetFontSize(9);
        planner.SetFontFamily("Aptos");
        planner.SetBold(true);
        planner.SetItalic(false);

        var options = planner.BuildCommitPlan();
        options.Should().Be(new ChartDataTableOptions(false, false, true, false, true,
            "#F2F2F2", "#4472C4", 1.25, "#112233", 9, "Aptos", true, false));
        chart.DataTable.Should().NotBeNull("the planner edits a working copy");
        ChartDataTableOptionsPlanner.BuildSurfacePlan().CommandId
            .Should().Be(ChartDataTableOptionsPlanner.CommandId);
    }

    [Fact]
    public void ChartAxisOptionsPlanner_UsesWorkingCopyAndBuildsScaleOptions()
    {
        var chart = MakeChart();
        chart.ValueAxis.Title = "Amount";
        chart.ValueAxis.Delete = true;
        chart.ValueAxis.Min = 0;
        chart.ValueAxis.Max = 100;
        chart.ValueAxis.MajorUnit = 20;
        chart.ValueAxis.HasMajorGridlines = true;

        var planner = ChartAxisOptionsPlanner.FromChart(chart);
        planner.SetTitle("Revenue");
        planner.SetShowAxis(false);
        planner.SetMinimum(10);
        planner.SetMaximum(90);
        planner.SetMajorUnit(10);
        planner.SetMinorUnit(5);
        planner.SetNumberFormatCode("$#,##0");
        planner.SetDisplayUnit(ChartAxisDisplayUnit.Millions);
        planner.SetMajorGridlines(false);
        planner.SetMinorGridlines(true);
        planner.SetMajorTickMark(ChartTickMark.Out);
        planner.SetMinorTickMark(ChartTickMark.In);
        planner.SetTickLabelPosition(ChartTickLabelPosition.NextTo);
        planner.SetCrosses(ChartAxisCrossing.Min);
        planner.SetCrossesAt(10);
        planner.SetCrossBetween(ChartCrossBetween.MidCat);
        planner.SetLabelAlignment(ChartLabelAlignment.Right);
        planner.SetLabelOffsetPercent(35);
        planner.SetNoMultiLevelLabels(true);
        planner.SetAutoCrossing(false);
        planner.SetReverseOrder(true);

        planner.BuildCommitPlan().Should().Be(new ChartAxisOptions(
            ChartAxisKind.Value, "Revenue", 10, 90, 10, 5, "$#,##0", false,
            ChartTickMark.Out, ChartTickMark.In, ChartTickLabelPosition.NextTo,
            null, 10, false, ChartCrossBetween.MidCat, ChartLabelAlignment.Right,
            35, true, false, true, true, DisplayUnit: ChartAxisDisplayUnit.Millions));
        planner.BuildCommitPlan().DisplayUnit.Should().Be(ChartAxisDisplayUnit.Millions);
        chart.ValueAxis.Title.Should().Be("Amount", "axis dialogs must edit a working copy");
        chart.ValueAxis.Delete.Should().BeTrue("axis dialogs must edit a working copy");
        ChartAxisOptionsPlanner.BuildSurfacePlan().CommandId
            .Should().Be(ChartAxisOptionsPlanner.CommandId);
    }

    [Fact]
    public void ChartAxisOptionsPlanner_EditsSecondaryValueAxisWithoutMutatingChart()
    {
        var chart = MakeChart();
        chart.SecondaryValueAxis = new ChartAxis
        {
            Title = "Margin",
            Min = 0,
            Max = 1,
            MajorUnit = 0.2,
            HasMajorGridlines = false,
        };

        var planner = ChartAxisOptionsPlanner.FromChart(chart);
        planner.SetAxis(ChartAxisKind.SecondaryValue);
        planner.SetTitle("Rate");
        planner.SetMinimum(0);
        planner.SetMaximum(100);
        planner.SetMajorUnit(25);
        planner.SetNumberFormatCode("0%");

        planner.BuildCommitPlan().Should().Be(new ChartAxisOptions(
            ChartAxisKind.SecondaryValue, "Rate", 0, 100, 25, null, "0%", false));
        chart.SecondaryValueAxis.Title.Should().Be("Margin");
        ChartAxisOptionsPlanner.AxisOptions.Should().Contain(option =>
            option.Value == ChartAxisKind.SecondaryValue);
    }

    [Fact]
    public void ChartAxisOptionsPlanner_RoundTripsIndependentTitleStyle()
    {
        var chart = MakeChart();
        chart.ValueAxis.Title = "Amount";
        chart.ValueAxis.TitleStyle = new ChartTextStyle
        {
            FontFamily = "Aptos",
            FontSizePt = 14,
            Bold = true,
            Italic = false,
            Color = new ThemeAwareColor(SrgbColor.FromRgb(0x2F5597)),
        };

        var planner = ChartAxisOptionsPlanner.FromChart(chart);
        planner.SetTitleFontFamily("Aptos Display");
        planner.SetTitleFontSizePt(16);
        planner.SetTitleBold(false);
        planner.SetTitleItalic(true);
        planner.SetTitleColor("#C00000");

        var style = planner.BuildCommitPlan().TitleStyle;
        style.Should().NotBeNull();
        style!.FontFamily.Should().Be("Aptos Display");
        style.FontSizePt.Should().Be(16);
        style.Bold.Should().BeFalse();
        style.Italic.Should().BeTrue();
        style.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0xC00000));
        chart.ValueAxis.TitleStyle!.FontFamily.Should().Be("Aptos");
    }

    [Fact]
    public void ChartSeriesOptionsPlanner_UsesWorkingCopyAndBuildsFormattingOptions()
    {
        var chart = MakeChart();
        chart.Series[0].Name = "Revenue";
        chart.Series[1].Name = "Margin";
        chart.Series[1].SmoothLine = true;
        chart.Series[1].OnSecondaryAxis = true;
        chart.Series[1].InvertIfNegative = true;
        chart.Series[1].LineStyle = new ChartLineStyle { WidthPt = 1.5 };
        chart.Series[1].MarkerStyle = new ChartMarkerStyle
        {
            Symbol = ChartMarkerSymbol.Circle,
            SizePt = 6,
        };

        var planner = ChartSeriesOptionsPlanner.FromChart(chart);
        planner.SetSeriesIndex(1);
        planner.SetSmoothLine(false);
        planner.SetOnSecondaryAxis(false);
        planner.SetInvertIfNegative(false);
        planner.SetOverrideChartType(ChartType.LineMarkers);
        planner.SetLineWidth(2.25);
        planner.SetLineColor("#1F4E79");
        planner.SetLineDash(OutlineDash.DashDot);
        planner.SetNoLine(true);
        planner.SetFillColor("#4472C4");
        planner.SetUseSeriesDataLabels(true);
        planner.SetShowValueLabels(true);
        planner.SetShowCategoryLabels(true);
        planner.SetShowLegendKeys(true);
        planner.SetLabelPosition(DataLabelPosition.InsideEnd);
        planner.SetLabelNumberFormat("0.0%");
        planner.SetLabelSeparator(" | ");
        planner.SetLabelFontFamily("Aptos");
        planner.SetLabelFontSize(9);
        planner.SetLabelBold(true);
        planner.SetLabelItalic(false);
        planner.SetLabelColor("#2F5496");
        planner.SetMarkerSymbol(ChartMarkerSymbol.Diamond);
        planner.SetMarkerSize(8);

        var options = planner.BuildCommitPlan();
        options.SeriesIndex.Should().Be(1);
        options.SmoothLine.Should().BeFalse();
        options.OnSecondaryAxis.Should().BeFalse();
        options.InvertIfNegative.Should().BeFalse();
        options.OverrideChartType.Should().Be(ChartType.LineMarkers);
        options.LineWidthPt.Should().Be(2.25);
        options.LineColor!.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));
        options.LineDash.Should().Be(OutlineDash.DashDot);
        options.NoLine.Should().BeTrue();
        options.MarkerSymbol.Should().Be(ChartMarkerSymbol.Diamond);
        options.MarkerSizePt.Should().Be(8);
        options.FillColor!.Resolved.Should().Be(SrgbColor.FromRgb(0x4472C4));
        options.DataLabels.Should().NotBeNull();
        options.DataLabels!.ShowValue.Should().BeTrue();
        options.DataLabels.ShowCategoryName.Should().BeTrue();
        options.DataLabels.ShowLegendKey.Should().BeTrue();
        options.DataLabels.Position.Should().Be(DataLabelPosition.InsideEnd);
        options.DataLabels.NumberFormat.Should().Be("0.0%");
        options.DataLabels.Separator.Should().Be(" | ");
        options.DataLabels.TextStyle.Should().NotBeNull();
        options.DataLabels.TextStyle!.FontFamily.Should().Be("Aptos");
        options.DataLabels.TextStyle.FontSizePt.Should().Be(9);
        options.DataLabels.TextStyle.Bold.Should().BeTrue();
        options.DataLabels.TextStyle.Italic.Should().BeFalse();
        options.DataLabels.TextStyle.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x2F5496));
        planner.SeriesOptions.Select(option => option.Label)
            .Should().Equal("Revenue", "Margin");
        chart.Series[1].SmoothLine.Should().BeTrue("series dialogs must edit a working copy");
        ChartSeriesOptionsPlanner.BuildSurfacePlan().CommandId
            .Should().Be(ChartSeriesOptionsPlanner.CommandId);
    }

    [Fact]
    public void ChartSeriesOptionsPlanner_PreservesAndValidatesComboTypeOverride()
    {
        var chart = MakeChart();
        chart.Series[0].OverrideChartType = ChartType.Line;

        var planner = ChartSeriesOptionsPlanner.FromChart(chart);
        planner.OverrideChartType.Should().Be(ChartType.Line);
        planner.SetOverrideChartType(null);
        planner.BuildCommitPlan().OverrideChartType.Should().BeNull();
        Action invalid = () => planner.SetOverrideChartType(ChartType.Pie);
        invalid.Should().Throw<ArgumentOutOfRangeException>();
        chart.Series[0].OverrideChartType.Should().Be(ChartType.Line);
    }

    [Fact]
    public void ChartBubbleOptionsPlanner_UsesWorkingCopyAndBuildsSizingOptions()
    {
        var chart = MakeChart();
        chart.ChartType = ChartType.Bubble;
        chart.BubbleScalePercent = 150;
        chart.BubbleSizeRepresents = BubbleSizeRepresentation.Area;
        chart.ShowNegativeBubbles = false;

        var planner = ChartBubbleOptionsPlanner.FromChart(chart);
        planner.SetBubbleScalePercent(225);
        planner.SetSizeRepresents(BubbleSizeRepresentation.Width);
        planner.SetShowNegativeBubbles(true);

        planner.BuildCommitPlan().Should().Be(new ChartBubbleOptions(225, BubbleSizeRepresentation.Width, true));
        chart.BubbleScalePercent.Should().Be(150, "bubble dialogs must edit a working copy");
        chart.BubbleSizeRepresents.Should().Be(BubbleSizeRepresentation.Area);
        chart.ShowNegativeBubbles.Should().BeFalse();
        ChartBubbleOptionsPlanner.BuildSurfacePlan().CommandId
            .Should().Be(ChartBubbleOptionsPlanner.CommandId);
    }

    [Fact]
    public void ChartPointOptionsPlanner_UsesWorkingCopyAndBuildsPointFormattingOptions()
    {
        var chart = MakeChart();
        chart.Series[1].PointColors[1] = new ThemeAwareColor(SrgbColor.FromRgb(0x4472C4));

        var planner = ChartPointOptionsPlanner.FromChart(chart);
        planner.SetSeriesIndex(1);
        planner.SetPointIndex(1);
        planner.SetFillColor("#C00000");
        planner.SetStrokeColor("#1F4E79");
        planner.SetStrokeWidth(1.5);
        planner.SetMarkerSymbol(ChartMarkerSymbol.Diamond);
        planner.SetMarkerSize(7);
        planner.SetExplosionPercent(35);
        planner.SetUsePointDataLabels(true);
        planner.SetShowValueLabels(true);
        planner.SetShowCategoryLabels(true);
        planner.SetShowLegendKeys(true);
        planner.SetShowLeaderLines(true);
        planner.SetLabelPosition(DataLabelPosition.InsideEnd);
        planner.SetLabelNumberFormat("0.0%");
        planner.SetLabelSeparator(" | ");
        planner.SetLabelFontFamily("Aptos");
        planner.SetLabelFontSize(9);
        planner.SetLabelBold(true);
        planner.SetLabelItalic(false);
        planner.SetLabelColor("#2F5496");

        var options = planner.BuildCommitPlan();
        options.SeriesIndex.Should().Be(1);
        options.PointIndex.Should().Be(1);
        options.FillColor!.Resolved.Should().Be(SrgbColor.FromRgb(0xC00000));
        options.StrokeColor!.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));
        options.StrokeWidthPt.Should().Be(1.5);
        options.MarkerSymbol.Should().Be(ChartMarkerSymbol.Diamond);
        options.MarkerSizePt.Should().Be(7);
        options.ExplosionPercent.Should().Be(35);
        options.DataLabels.Should().NotBeNull();
        options.DataLabels!.ShowValue.Should().BeTrue();
        options.DataLabels.ShowCategoryName.Should().BeTrue();
        options.DataLabels.ShowLegendKey.Should().BeTrue();
        options.DataLabels.ShowLeaderLines.Should().BeTrue();
        options.DataLabels.Position.Should().Be(DataLabelPosition.InsideEnd);
        options.DataLabels.NumberFormat.Should().Be("0.0%");
        options.DataLabels.Separator.Should().Be(" | ");
        options.DataLabels.TextStyle.Should().NotBeNull();
        options.DataLabels.TextStyle!.FontFamily.Should().Be("Aptos");
        options.DataLabels.TextStyle.FontSizePt.Should().Be(9);
        options.DataLabels.TextStyle.Bold.Should().BeTrue();
        options.DataLabels.TextStyle.Italic.Should().BeFalse();
        options.DataLabels.TextStyle.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x2F5496));
        planner.PointOptions.Select(option => option.Label).Should().Equal("1: Q1", "2: Q2", "3: Q3");
        chart.Series[1].PointColors[1].Resolved.Should().Be(SrgbColor.FromRgb(0x4472C4));
        ChartPointOptionsPlanner.BuildSurfacePlan().CommandId.Should().Be(ChartPointOptionsPlanner.CommandId);
    }

    [Fact]
    public void ChartLayoutOptionsPlanner_UsesWorkingCopyAndBuildsManualLayoutOptions()
    {
        var chart = MakeChart();
        chart.PlotAreaManualLayout = new ChartManualLayout
        {
            LayoutTarget = "inner",
            X = 0.1,
            Y = 0.2,
            Width = 0.8,
            Height = 0.7,
        };

        var planner = ChartLayoutOptionsPlanner.FromChart(chart);
        planner.SetTarget(ChartLayoutTarget.Legend);
        planner.SetLayoutTarget("outer");
        planner.SetXMode(ChartManualLayoutMode.Edge);
        planner.SetYMode(ChartManualLayoutMode.Edge);
        planner.SetWidthMode(ChartManualLayoutMode.Factor);
        planner.SetHeightMode(ChartManualLayoutMode.Factor);
        planner.SetX(12);
        planner.SetY(18);
        planner.SetWidth(0.25);
        planner.SetHeight(0.4);

        var options = planner.BuildCommitPlan();
        options.Target.Should().Be(ChartLayoutTarget.Legend);
        options.LayoutTarget.Should().Be("outer");
        options.XMode.Should().Be(ChartManualLayoutMode.Edge);
        options.YMode.Should().Be(ChartManualLayoutMode.Edge);
        options.Width.Should().Be(0.25);
        options.Height.Should().Be(0.4);
        chart.LegendManualLayout.Should().BeNull("layout planners must edit a working copy");
        ChartLayoutOptionsPlanner.BuildSurfacePlan().CommandId.Should().Be(ChartLayoutOptionsPlanner.CommandId);
    }

    [Fact]
    public void ChartLayoutOptionsPlanner_ExposesControlledLayoutTargetChoicesAndPreservesUnknownTokens()
    {
        var builtIns = ChartLayoutOptionsPlanner.LayoutTargetOptionsFor(null);
        builtIns.Select(option => option.Value).Should().Equal(null, "inner", "outer");
        builtIns.Select(option => option.Label).Should().Equal("Automatic (outer)", "Inner", "Outer");

        var imported = ChartLayoutOptionsPlanner.LayoutTargetOptionsFor("futureTarget");
        imported.Should().Contain(option => option.Value == "futureTarget" && option.Label == "Imported (futureTarget)");

        var chart = MakeChart();
        chart.PlotAreaManualLayout = new ChartManualLayout { LayoutTarget = "futureTarget" };
        var planner = ChartLayoutOptionsPlanner.FromChart(chart);

        planner.BuildCommitPlan().LayoutTarget.Should().Be("futureTarget");
        planner.SetLayoutTarget(null);
        planner.BuildCommitPlan().LayoutTarget.Should().BeNull();
    }

    [Fact]
    public void ChartPieOptionsPlanner_UsesWorkingCopyAndBuildsRotationAndHoleOptions()
    {
        var chart = MakeChart();
        chart.ChartType = ChartType.Doughnut;
        chart.FirstSliceAngleDegrees = 18;
        chart.DoughnutHolePercent = 45;

        var planner = ChartPieOptionsPlanner.FromChart(chart);
        planner.SetFirstSliceAngleDegrees(225);
        planner.SetDoughnutHolePercent(68);

        planner.BuildCommitPlan().Should().Be(new ChartPieOptions(225, 68));
        chart.FirstSliceAngleDegrees.Should().Be(18);
        chart.DoughnutHolePercent.Should().Be(45);
        ChartPieOptionsPlanner.BuildSurfacePlan().CommandId.Should().Be(ChartPieOptionsPlanner.CommandId);
    }

    [Fact]
    public void ChartPieOptionsPlanner_AuthorsOfPieSplitMembershipWithoutMutatingChart()
    {
        var chart = MakeChart();
        chart.ChartType = ChartType.OfPie;
        chart.OfPieType = OfPieType.Pie;
        chart.OfPieSplitType = OfPieSplitType.Auto;

        var planner = ChartPieOptionsPlanner.FromChart(chart);
        planner.SetOfPieType(OfPieType.Bar);
        planner.SetOfPieSplitType(OfPieSplitType.Custom);
        planner.SetOfPieSplitPosition(2);
        planner.SetOfPieSecondPieSizePercent(75);
        planner.SetOfPieCustomPointIndices(new[] { 0, 2, 2, -1, 99 });
        planner.SetOfPieGapWidthPercent(120);
        planner.SetOfPieSeriesLines(true);

        var options = planner.BuildCommitPlan();
        options.FirstSliceAngleDegrees.Should().Be(chart.FirstSliceAngleDegrees);
        options.DoughnutHolePercent.Should().Be(chart.DoughnutHolePercent);
        options.OfPieType.Should().Be(OfPieType.Bar);
        options.OfPieSplitType.Should().Be(OfPieSplitType.Custom);
        options.OfPieSplitPosition.Should().Be(2);
        options.OfPieSecondPieSizePercent.Should().Be(75);
        options.OfPieCustomPointIndices.Should().Equal(0, 2);
        options.OfPieGapWidthPercent.Should().Be(120);
        options.OfPieSeriesLines.Should().BeTrue();
        chart.OfPieType.Should().Be(OfPieType.Pie);
        chart.OfPieSplitType.Should().Be(OfPieSplitType.Auto);
        chart.OfPieCustomPointIndices.Should().BeEmpty();
    }

    [Fact]
    public void ChartPlotStyleOptionsPlanner_UsesWorkingCopyForScatterAndRadarStyles()
    {
        var chart = MakeChart();
        chart.ChartType = ChartType.Scatter;
        chart.ScatterStyle = ScatterStyle.Marker;
        chart.RadarStyle = RadarStyle.Standard;

        var planner = ChartPlotStyleOptionsPlanner.FromChart(chart);
        planner.SetScatterStyle(ScatterStyle.SmoothMarker);
        planner.SetRadarStyle(RadarStyle.Filled);

        planner.BuildCommitPlan().Should().Be(new ChartPlotStyleOptions(ScatterStyle.SmoothMarker, RadarStyle.Filled));
        chart.ScatterStyle.Should().Be(ScatterStyle.Marker);
        chart.RadarStyle.Should().Be(RadarStyle.Standard);
        ChartPlotStyleOptionsPlanner.BuildSurfacePlan().CommandId.Should().Be(ChartPlotStyleOptionsPlanner.CommandId);
    }

    [Fact]
    public void ChartSeriesOptionsPlanner_UsesWorkingCopyForErrorBars()
    {
        var chart = MakeChart();
        var planner = ChartSeriesOptionsPlanner.FromChart(chart);

        planner.SetErrorBarsEnabled(true);
        planner.SetErrorDirection(ChartErrorDirection.X);
        planner.SetErrorBarType(ChartErrorBarType.Plus);
        planner.SetErrorValueType(ChartErrorValueType.Percentage);
        planner.SetErrorValue(-4);
        planner.SetErrorNoEndCap(true);

        var options = planner.BuildCommitPlan();
        options.ErrorBars.Should().NotBeNull();
        options.ErrorBars!.Direction.Should().Be(ChartErrorDirection.X);
        options.ErrorBars.BarType.Should().Be(ChartErrorBarType.Plus);
        options.ErrorBars.ValueType.Should().Be(ChartErrorValueType.Percentage);
        options.ErrorBars.Value.Should().Be(0);
        options.ErrorBars.NoEndCap.Should().BeTrue();
        chart.Series[0].ErrorBars.Should().BeNull();
    }

    private static ChartShape MakeChart()
    {
        var chart = new ChartShape();
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });

        var sales = new ChartSeries { Name = "Sales" };
        sales.Values.AddRange(new double?[] { 1.0, 2.0, 3.0 });
        chart.Series.Add(sales);

        var budget = new ChartSeries { Name = "Budget" };
        budget.Values.AddRange(new double?[] { 4.0, null, 6.0 });
        chart.Series.Add(budget);

        return chart;
    }

    private static ChartShape MakeCoordinateChart(ChartType type)
    {
        var chart = MakeChart();
        chart.ChartType = type;
        chart.Series[0].XValues.AddRange(new double?[] { 1.0, 2.0, 3.0 });
        chart.Series[1].XValues.AddRange(new double?[] { 1.0, 2.0, 3.0 });
        if (type == ChartType.Bubble)
        {
            chart.Series[0].BubbleSizes.AddRange(new double?[] { 3.0, 4.0, 5.0 });
            chart.Series[1].BubbleSizes.AddRange(new double?[] { 6.0, 7.0, 8.0 });
        }
        return chart;
    }
}
