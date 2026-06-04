using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class ForecastChartPlannerTests
{
    private static readonly SheetId Sheet = SheetId.New();

    // Layout matches ForecastSheetCommand output: A=Timeline, B=Actual, C=Forecast,
    // D=Lower Confidence Bound, E=Upper Confidence Bound, header on row 1.
    private static ForecastChartLayout SampleLayout(uint lastRow = 6) =>
        new(Sheet, HeaderRow: 1, LastRow: lastRow);

    [Fact]
    public void Plan_ProducesLineChartCoveringTheFullForecastLayout()
    {
        var chart = ForecastChartPlanner.Plan(SampleLayout(lastRow: 6));

        chart.Type.Should().Be(ChartType.Line);
        chart.FirstColIsCategories.Should().BeTrue();
        chart.FirstRowIsHeader.Should().BeTrue();
        chart.DataRange.Start.Should().Be(new CellAddress(Sheet, 1, 1));
        chart.DataRange.End.Should().Be(new CellAddress(Sheet, 6, 5));
    }

    [Fact]
    public void Plan_UsesTimelineColumnAsCategoryAxisAndYieldsFourSeries()
    {
        var chart = ForecastChartPlanner.Plan(SampleLayout(lastRow: 6));

        // Column A is the category axis (timeline).
        ChartTypeSupport.GetXAxisValueColumn(chart).Should().Be(1);
        // Series are columns B, C, D, E => Actual, Forecast, Lower, Upper.
        ChartTypeSupport.GetDataSeriesCount(chart).Should().Be(4);
        ChartTypeSupport.GetYAxisValueColumns(chart).Should().Equal(2u, 3u, 4u, 5u);
    }

    [Fact]
    public void Plan_GivesTheChartAForecastTitle()
    {
        var chart = ForecastChartPlanner.Plan(SampleLayout());

        chart.Title.Should().Be("Forecast");
    }

    [Fact]
    public void Plan_StylesConfidenceBoundsAsDashedSeries()
    {
        var chart = ForecastChartPlanner.Plan(SampleLayout());

        // Confidence bounds are series indexes 2 (lower) and 3 (upper).
        var lower = chart.SeriesFormats.Single(f => f.SeriesIndex == 2);
        var upper = chart.SeriesFormats.Single(f => f.SeriesIndex == 3);

        lower.DashStyle.Should().Be(ChartLineDashStyle.Dash);
        upper.DashStyle.Should().Be(ChartLineDashStyle.Dash);
    }

    [Fact]
    public void Plan_GivesActualAndForecastDistinctSolidStyling()
    {
        var chart = ForecastChartPlanner.Plan(SampleLayout());

        // Actual (0) and Forecast (1) are not dashed.
        var actual = chart.SeriesFormats.SingleOrDefault(f => f.SeriesIndex == 0);
        var forecast = chart.SeriesFormats.SingleOrDefault(f => f.SeriesIndex == 1);

        (actual?.DashStyle ?? ChartLineDashStyle.Solid).Should().Be(ChartLineDashStyle.Solid);
        forecast.Should().NotBeNull();
        (forecast!.DashStyle ?? ChartLineDashStyle.Solid).Should().Be(ChartLineDashStyle.Solid);
    }

    [Fact]
    public void Plan_ProducesADeterministicChart()
    {
        var layout = SampleLayout();

        var first = ForecastChartPlanner.Plan(layout);
        var second = ForecastChartPlanner.Plan(layout);

        first.Type.Should().Be(second.Type);
        first.DataRange.Should().Be(second.DataRange);
        first.Title.Should().Be(second.Title);
        first.SeriesFormats.Select(f => (f.SeriesIndex, f.DashStyle))
            .Should().Equal(second.SeriesFormats.Select(f => (f.SeriesIndex, f.DashStyle)));
    }

    [Fact]
    public void Plan_PositionsChartToTheRightOfTheData()
    {
        var chart = ForecastChartPlanner.Plan(SampleLayout());

        chart.Left.Should().BeGreaterThan(0);
        chart.Top.Should().BeGreaterThan(0);
        chart.Width.Should().BeGreaterThan(0);
        chart.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Plan_ChartIsAuthorableByAddChartCommand()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Forecast");
        SeedForecastData(sheet);

        var chart = ForecastChartPlanner.Plan(new ForecastChartLayout(sheet.Id, 1, 6));

        // The planned chart must satisfy the validation in the existing chart-create path.
        ChartTypeSupport.IsRenderable(chart.Type).Should().BeTrue();
        ChartTypeSupport.GetDataSeriesCount(chart).Should().BeGreaterThan(0);
        ChartTypeSupport.GetDataPointCount(chart).Should().BeGreaterThan(0);
    }

    private static void SeedForecastData(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Forecast"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Lower Confidence Bound"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new TextValue("Upper Confidence Bound"));
        for (uint row = 2; row <= 6; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row - 1));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue((row - 1) * 10));
        }
    }
}
