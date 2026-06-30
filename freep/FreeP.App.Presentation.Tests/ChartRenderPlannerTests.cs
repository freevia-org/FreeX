using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartRenderPlannerTests
{
    [Fact]
    public void ComputePrimaryValueAxisRange_ExcludesSecondarySeries()
    {
        var primary = new ChartSeries { Name = "Bars", OnSecondaryAxis = false };
        primary.Values.AddRange(new double?[] { 20, 50, 100 });

        var secondary = new ChartSeries { Name = "Line", OnSecondaryAxis = true };
        secondary.Values.AddRange(new double?[] { 200_000, 600_000, 1_000_000 });

        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Series.Add(primary);
        chart.Series.Add(secondary);

        var (min, max, unit) = ChartRenderPlanner.ComputePrimaryValueAxisRange(chart);

        min.Should().Be(0);
        max.Should().BeLessThan(10_000);
        max.Should().BeGreaterThanOrEqualTo(100);
        unit.Should().BePositive();
    }

    [Fact]
    public void ComputeSecondaryValueAxisRange_UsesOnlySecondarySeries()
    {
        var primary = new ChartSeries { Name = "Bars", OnSecondaryAxis = false };
        primary.Values.AddRange(new double?[] { 20, 50, 100 });

        var secondary = new ChartSeries { Name = "Line", OnSecondaryAxis = true };
        secondary.Values.AddRange(new double?[] { 200_000, 600_000, 1_000_000 });

        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Series.Add(primary);
        chart.Series.Add(secondary);

        var (min, max, unit) = ChartRenderPlanner.ComputeSecondaryValueAxisRange(chart);

        min.Should().Be(0);
        max.Should().BeGreaterThanOrEqualTo(1_000_000);
        unit.Should().BePositive();
    }

    [Fact]
    public void ComputeSecondaryValueAxisRange_NoSecondarySeries_ReturnsFallback()
    {
        var series = new ChartSeries { Name = "Bars" };
        series.Values.AddRange(new double?[] { 10, 20, 30 });

        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Series.Add(series);

        ChartRenderPlanner.ComputeSecondaryValueAxisRange(chart)
            .Should().Be((0, 1, 1));
    }

    [Fact]
    public void ComputeScatterAxisRange_UsesXValuesWhenRequested()
    {
        var series = new ChartSeries { Name = "Scatter" };
        series.XValues.AddRange(new double?[] { -5, 10, 80 });
        series.Values.AddRange(new double?[] { 1, 2, 3 });

        var chart = new ChartShape { ChartType = ChartType.Scatter };
        chart.Series.Add(series);

        var (min, max, unit) = ChartRenderPlanner.ComputeScatterAxisRange(chart, useX: true);

        min.Should().BeLessThanOrEqualTo(-5);
        max.Should().BeGreaterThanOrEqualTo(80);
        unit.Should().BePositive();
    }

    [Fact]
    public void ResolveEffectiveLabels_SeriesOverrideWins()
    {
        var chartLabels = new ChartDataLabels { ShowValue = true };
        var seriesLabels = new ChartDataLabels { ShowSeriesName = true };
        var series = new ChartSeries
        {
            Name = "Series",
            DataLabels = seriesLabels
        };

        var chart = new ChartShape
        {
            ChartType = ChartType.ColumnClustered,
            DataLabels = chartLabels
        };
        chart.Series.Add(series);

        ChartRenderPlanner.ResolveEffectiveLabels(chart, 0)
            .Should().BeSameAs(seriesLabels);
    }

    [Theory]
    [InlineData(1200, "1.2K")]
    [InlineData(42, "42")]
    [InlineData(1.2345, "1.23")]
    public void FormatAxisValue_MatchesRendererContract(double value, string expected)
    {
        ChartRenderPlanner.FormatAxisValue(value).Should().Be(expected);
    }

    [Fact]
    public void FormatDataLabel_ComposesConfiguredParts()
    {
        var labels = new ChartDataLabels
        {
            ShowSeriesName = true,
            ShowCategoryName = true,
            ShowValue = true,
            ShowPercent = true,
            NumberFormat = "0.0%"
        };

        ChartRenderPlanner.FormatDataLabel(labels, 0.25, 1.0, "Q1", "Sales")
            .Should().Be("Sales Q1 25.0% 25%");
    }
}
