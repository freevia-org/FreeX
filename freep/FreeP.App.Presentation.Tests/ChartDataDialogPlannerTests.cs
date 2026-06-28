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
