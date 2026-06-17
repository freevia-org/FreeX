namespace FreeW.Core.Model.Tests;

/// <summary>
/// Unit coverage for the <see cref="Chart"/> / <see cref="ChartSeries"/> / <see cref="Run.Chart"/> model
/// (roadmap item W3): the inline-run-mark API and the convenience factories.
/// </summary>
public class ChartsTests
{
    [Fact]
    public void Create_BuildsSingleSeriesChartWithCategoriesValuesNameAndTitle()
    {
        var chart = Chart.Create(
            ChartKind.Column,
            categories: ["A", "B", "C"],
            values: [1.0, 2.0, 3.0],
            seriesName: "Sales",
            title: "Annual");

        chart.Kind.Should().Be(ChartKind.Column);
        chart.Title.Should().Be("Annual");
        chart.Categories.Should().Equal("A", "B", "C");
        var series = chart.Series.Should().ContainSingle().Subject;
        series.Name.Should().Be("Sales");
        series.Values.Should().Equal(1.0, 2.0, 3.0);
    }

    [Fact]
    public void Create_TitleAndSeriesName_DefaultToNull()
    {
        var chart = Chart.Create(ChartKind.Bar, ["A"], [1.0]);

        chart.Title.Should().BeNull();
        chart.Series.Single().Name.Should().BeNull();
    }

    [Fact]
    public void Chart_DefaultsToColumnWithWordTypicalSize()
    {
        var chart = new Chart();

        chart.Kind.Should().Be(ChartKind.Column);
        chart.WidthPt.Should().Be(360);
        chart.HeightPt.Should().Be(216);
        chart.Categories.Should().BeEmpty();
        chart.Series.Should().BeEmpty();
    }

    [Fact]
    public void ChartSeries_Constructor_CopiesNameAndValues()
    {
        var series = new ChartSeries("North", [4.0, 5.0]);

        series.Name.Should().Be("North");
        series.Values.Should().Equal(4.0, 5.0);
    }

    [Fact]
    public void FromChart_ProducesTextlessRunCarryingTheChart()
    {
        var chart = Chart.Create(ChartKind.Pie, ["X"], [1.0]);

        var run = Run.FromChart(chart);

        run.Chart.Should().BeSameAs(chart);
        run.Text.Should().BeEmpty();
        run.Image.Should().BeNull();
        run.Equation.Should().BeNull();
    }

    [Fact]
    public void Chart_SupportsMultipleSeries()
    {
        var chart = new Chart { Kind = ChartKind.Line };
        chart.Categories.AddRange(["Jan", "Feb"]);
        chart.Series.Add(new ChartSeries("A", [1.0, 2.0]));
        chart.Series.Add(new ChartSeries("B", [3.0, 4.0]));

        chart.Series.Should().HaveCount(2);
        chart.Series[1].Values.Should().Equal(3.0, 4.0);
    }

    [Theory]
    [InlineData(ChartKind.Scatter)]
    [InlineData(ChartKind.Area)]
    [InlineData(ChartKind.Doughnut)]
    public void Create_SupportsRicherChartKinds(ChartKind kind)
    {
        var chart = Chart.Create(kind, ["A", "B"], [1.0, 2.0]);

        chart.Kind.Should().Be(kind);
        chart.Series.Single().Values.Should().Equal(1.0, 2.0);
    }

    [Fact]
    public void Chart_LegendAndAxisTitles_DefaultToOff()
    {
        var chart = new Chart();

        chart.ShowLegend.Should().BeFalse();
        chart.CategoryAxisTitle.Should().BeNull();
        chart.ValueAxisTitle.Should().BeNull();
    }

    [Fact]
    public void Chart_LegendAndAxisTitles_AreSettable()
    {
        var chart = new Chart
        {
            ShowLegend = true,
            CategoryAxisTitle = "Quarter",
            ValueAxisTitle = "USD",
        };

        chart.ShowLegend.Should().BeTrue();
        chart.CategoryAxisTitle.Should().Be("Quarter");
        chart.ValueAxisTitle.Should().Be("USD");
    }
}
