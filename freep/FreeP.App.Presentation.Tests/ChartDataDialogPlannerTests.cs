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
    public void BuildSurfacePlan_ExposesSharedDialogLabelsAndCommandId()
    {
        var plan = ChartDataDialogPlanner.BuildSurfacePlan();

        plan.CommandId.Should().Be(ChartDataDialogPlanner.EditDataCommandId);
        plan.Title.Should().Be("Edit Chart Data");
        plan.Width.Should().Be(640);
        plan.Height.Should().Be(440);
        plan.AddSeriesLabel.Should().Be("+ Series");
        plan.RemoveSeriesLabel.Should().Be("- Series");
        plan.AddCategoryLabel.Should().Be("+ Category");
        plan.RemoveCategoryLabel.Should().Be("- Category");
        plan.SwitchRowsAndColumnsLabel.Should().Be("Switch Row/Column");
        plan.ChartTypeLabel.Should().Be("Chart Type");
        plan.OkLabel.Should().Be("OK");
        plan.CancelLabel.Should().Be("Cancel");
        ChartDataDialogPlanner.ChartTypeOptions.Should().Contain(option =>
            option.Value == ChartType.LineMarkers && option.Label == "Line with Markers");
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
        chart.Legend = LegendPosition.Right;
        chart.DataLabels = new ChartDataLabels { ShowValue = true, Position = DataLabelPosition.Center };
        chart.CategoryAxis.HasMajorGridlines = true;
        chart.ValueAxis.HasMajorGridlines = false;

        var planner = ChartDisplayOptionsPlanner.FromChart(chart);
        planner.SetTitle("Revenue");
        planner.SetLegend(LegendPosition.Bottom);
        planner.SetShowValueLabels(false);
        planner.SetLabelPosition(DataLabelPosition.OutsideEnd);
        planner.SetCategoryGridlines(false);
        planner.SetValueGridlines(true);

        var commit = planner.BuildCommitPlan();
        commit.Should().Be(new ChartDisplayOptions(
            "Revenue", LegendPosition.Bottom, false, DataLabelPosition.OutsideEnd, false, true));
        chart.Title.Should().Be("Existing", "the dialog planner is a working copy");
        ChartDisplayOptionsPlanner.BuildSurfacePlan().CommandId
            .Should().Be(ChartDisplayOptionsPlanner.CommandId);
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
}
