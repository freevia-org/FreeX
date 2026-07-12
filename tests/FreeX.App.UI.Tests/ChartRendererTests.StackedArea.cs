using FluentAssertions;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

public sealed partial class ChartRendererTests
{
    // Two series (North, South) over two categories. Values chosen so the running stack is easy to
    // verify: North = 10, 20; South = 5, 15.
    private static ViewportModel TwoSeriesTwoCategoryViewport() => new(
        [
            Cell(1, 1, "Category"), Cell(1, 2, "North"), Cell(1, 3, "South"),
            Cell(2, 1, "Q1"), Cell(2, 2, "10"), Cell(2, 3, "5"),
            Cell(3, 1, "Q2"), Cell(3, 2, "20"), Cell(3, 3, "15"),
        ],
        [],
        []);

    private static ChartModel StackedAreaChart(ChartType type, SheetId sheetId) => new()
    {
        Type = type,
        DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)),
        FirstRowIsHeader = true,
        FirstColIsCategories = true,
    };

    [Fact]
    public void StackedAreaRenderer_StacksEachBandOnTheCumulativeBaselineBelow()
    {
        var sheetId = SheetId.New();
        var model = BuildPlotModel(StackedAreaChart(ChartType.StackedArea, sheetId), TwoSeriesTwoCategoryViewport());

        model.Series.Should().HaveCount(2);
        var north = model.Series[0].Should().BeOfType<AreaSeries>().Subject;
        var south = model.Series[1].Should().BeOfType<AreaSeries>().Subject;

        // Bottom band sits on the zero baseline; its top is the raw values.
        north.Points.Select(p => p.Y).Should().Equal(10, 20);
        north.Points2.Select(p => p.Y).Should().Equal(0, 0);

        // Upper band rides on the lower band's cumulative top (10, 20) and stacks to 10+5, 20+15.
        south.Points2.Select(p => p.Y).Should().Equal(10, 20);
        south.Points.Select(p => p.Y).Should().Equal(15, 35);
    }

    [Fact]
    public void PercentStackedAreaRenderer_NormalizesEachCategoryStackTo100Percent()
    {
        var sheetId = SheetId.New();
        var model = BuildPlotModel(StackedAreaChart(ChartType.PercentStackedArea, sheetId), TwoSeriesTwoCategoryViewport());

        model.Series.Should().HaveCount(2);
        var north = model.Series[0].Should().BeOfType<AreaSeries>().Subject;
        var south = model.Series[1].Should().BeOfType<AreaSeries>().Subject;

        // Category 0 total = 15 → North 66.67%, South stacks to 100%. Category 1 total = 35 → North 57.14%.
        north.Points[0].Y.Should().BeApproximately(100.0 * 10 / 15, 0.01);
        north.Points[1].Y.Should().BeApproximately(100.0 * 20 / 35, 0.01);

        // The top of the full stack normalizes to 100% in every category.
        south.Points.Select(p => p.Y).Should().AllSatisfy(y => y.Should().BeApproximately(100, 0.01));

        // The value axis is pinned to the 0..100 percent range.
        var valueAxis = model.Axes.Single(axis => axis.Position == AxisPosition.Left);
        valueAxis.Maximum.Should().Be(100);
        valueAxis.Minimum.Should().Be(0);
    }
}
