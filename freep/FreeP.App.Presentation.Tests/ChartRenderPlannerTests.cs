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

    [Fact]
    public void BuildFramePlan_BarChart_ReservesLeftCategoryAndBottomValueAxisBands()
    {
        var chart = new ChartShape { ChartType = ChartType.BarClustered };

        var plan = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        plan.Family.Should().Be(ChartRenderFamily.HorizontalBar);
        plan.Plot.Should().Be(new ChartPlanRect(52, 8, 340, 244));
    }

    [Fact]
    public void BuildColumnPrimitives_MatchesPowerPointClusterGeometry()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);

        var primitives = ChartRenderPlanner.BuildColumnPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        primitives.Should().ContainEquivalentOf(new ChartRectPrimitive(
            SeriesIndex: 0,
            CategoryIndex: 0,
            Bounds: new ChartPlanRect(30, 80, 19, 20)));
        primitives.Should().ContainEquivalentOf(new ChartRectPrimitive(
            SeriesIndex: 1,
            CategoryIndex: 0,
            Bounds: new ChartPlanRect(50, 40, 19, 60)));
    }

    [Fact]
    public void BuildBarPrimitives_ReversesCategoryAndClusterSeriesOrder()
    {
        var chart = MakeTwoSeriesChart(ChartType.BarClustered);

        var primitives = ChartRenderPlanner.BuildBarPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        primitives.Should().ContainEquivalentOf(new ChartRectPrimitive(
            SeriesIndex: 0,
            CategoryIndex: 0,
            Bounds: new ChartPlanRect(0, 75, 40, 9)));
        primitives.Should().ContainEquivalentOf(new ChartRectPrimitive(
            SeriesIndex: 1,
            CategoryIndex: 0,
            Bounds: new ChartPlanRect(0, 65, 120, 9)));
    }

    [Fact]
    public void BuildLineSeriesPrimitives_PreservesGapsBetweenSegments()
    {
        var series = new ChartSeries { Name = "Line" };
        series.Values.AddRange(new double?[] { 10, null, 30 });
        var chart = new ChartShape { ChartType = ChartType.LineMarkers };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });
        chart.Series.Add(series);

        var primitive = ChartRenderPlanner.BuildLineSeriesPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100),
            withMarkers: true).Single();

        primitive.WithMarkers.Should().BeTrue();
        primitive.Points[0].Should().Be(new ChartPlanPoint(0, 75));
        primitive.Points[1].Should().BeNull();
        primitive.Points[2].Should().Be(new ChartPlanPoint(200, 25));
    }

    [Fact]
    public void BuildAreaSeriesPrimitives_PlansBackToFrontFilledPolygons()
    {
        var chart = new ChartShape { ChartType = ChartType.Area };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });

        var first = new ChartSeries { Name = "Actual" };
        first.Values.AddRange(new double?[] { 0, 40, 80 });
        chart.Series.Add(first);

        var second = new ChartSeries { Name = "Forecast" };
        second.Values.AddRange(new double?[] { 10, 20, 30 });
        chart.Series.Add(second);

        var seriesColors = new[]
        {
            new SrgbColor(0x10, 0x20, 0x30),
            new SrgbColor(0x40, 0x50, 0x60)
        };

        var primitives = ChartRenderPlanner.BuildAreaSeriesPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100),
            seriesColors);

        primitives.Should().HaveCount(2);
        primitives[0].SeriesIndex.Should().Be(1);
        primitives[1].SeriesIndex.Should().Be(0);
        primitives[0].BaselineStart.Should().Be(new ChartPlanPoint(0, 100));
        primitives[0].BaselineEnd.Should().Be(new ChartPlanPoint(200, 100));
        primitives[0].Points.Should().Equal(
            new ChartPlanPoint(0, 90),
            new ChartPlanPoint(100, 80),
            new ChartPlanPoint(200, 70));
        primitives[0].Fill.Should().Be(new ChartFillPlan(seriesColors[1], ChartRenderPlanner.AreaFillAlpha));
        primitives[0].AreaPath.IsClosed.Should().BeTrue();
        primitives[0].AreaPath.Fill.Should().Be(primitives[0].Fill);
        primitives[0].AreaPath.Points.Should().Equal(
            new ChartPlanPoint(0, 100),
            new ChartPlanPoint(0, 90),
            new ChartPlanPoint(100, 80),
            new ChartPlanPoint(200, 70),
            new ChartPlanPoint(200, 100));
    }

    [Fact]
    public void BuildScatterPrimitivePlan_PlansAxesAndPreservesSeriesGaps()
    {
        var series = new ChartSeries { Name = "XY" };
        series.XValues.AddRange(new double?[] { 0, 50, 100 });
        series.Values.AddRange(new double?[] { 10, null, 30 });

        var chart = new ChartShape
        {
            ChartType = ChartType.Scatter,
            ScatterStyle = ScatterStyle.LineMarker
        };
        chart.Series.Add(series);

        var plan = ChartRenderPlanner.BuildScatterPrimitivePlan(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        plan.GridLines.Should().HaveCount(11);
        plan.XAxisLabels.Should().HaveCount(6);
        plan.YAxisLabels.Should().HaveCount(5);
        plan.Series.Should().ContainSingle();
        plan.Series[0].DrawLines.Should().BeTrue();
        plan.Series[0].DrawMarkers.Should().BeTrue();
        plan.Series[0].Points[0].Should().Be(new ChartPlanPoint(0, 75));
        plan.Series[0].Points[1].Should().BeNull();
        plan.Series[0].Points[2]!.Value.X.Should().BeApproximately(160, 0.0001);
        plan.Series[0].Points[2]!.Value.Y.Should().BeApproximately(25, 0.0001);
    }

    [Fact]
    public void BuildBubblePrimitivePlan_NormalizesBubbleRadiiAndAxisLabels()
    {
        var series = new ChartSeries { Name = "Bubble" };
        series.XValues.AddRange(new double?[] { 0, 100 });
        series.Values.AddRange(new double?[] { 0, 40 });
        series.BubbleSizes.AddRange(new double?[] { 25, 100 });

        var chart = new ChartShape { ChartType = ChartType.Bubble };
        chart.Series.Add(series);

        var plan = ChartRenderPlanner.BuildBubblePrimitivePlan(
            chart,
            new ChartPlanRect(0, 0, 160, 80));

        plan.GridLines.Should().HaveCount(12);
        plan.XAxisLabels.Should().HaveCount(6);
        plan.YAxisLabels.Should().HaveCount(6);
        plan.Bubbles.Should().HaveCount(2);
        plan.Bubbles[0].Radius.Should().BeApproximately(5, 0.0001);
        plan.Bubbles[0].Center.Should().Be(new ChartPlanPoint(0, 80));
        plan.Bubbles[1].Radius.Should().BeApproximately(10, 0.0001);
        plan.Bubbles[1].Center.X.Should().BeApproximately(128, 0.0001);
        plan.Bubbles[1].Center.Y.Should().BeApproximately(16, 0.0001);
    }

    [Fact]
    public void BuildRadarPrimitivePlan_PlansRingsSpokesLabelsAndSeries()
    {
        var series = new ChartSeries { Name = "Radar" };
        series.Values.AddRange(new double?[] { 1, 2, 3, 4 });

        var chart = new ChartShape
        {
            ChartType = ChartType.Radar,
            RadarStyle = RadarStyle.Filled
        };
        chart.Categories.AddRange(new[] { "North", "East", "South", "West" });
        chart.Series.Add(series);

        var plan = ChartRenderPlanner.BuildRadarPrimitivePlan(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        plan.Rings.Should().HaveCount(4);
        plan.Rings[0].Points.Should().HaveCount(4);
        plan.Spokes.Should().HaveCount(4);
        plan.CategoryLabels.Should().HaveCount(4);
        plan.Series.Should().ContainSingle();
        plan.Series[0].IsFilled.Should().BeTrue();
        plan.Series[0].WithMarkers.Should().BeFalse();
        plan.Series[0].Points[0].X.Should().BeApproximately(100, 0.0001);
        plan.Series[0].Points[0].Y.Should().BeApproximately(40.625, 0.0001);
    }

    [Fact]
    public void BuildPieSlicePrimitives_ComputesClockwiseWedgesFromTop()
    {
        var series = new ChartSeries { Name = "Share" };
        series.Values.AddRange(new double?[] { 1, 3 });
        var chart = new ChartShape { ChartType = ChartType.Pie };
        chart.Series.Add(series);

        var slices = ChartRenderPlanner.BuildPieSlicePrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        slices.Should().HaveCount(2);
        var first = slices[0];
        first.SeriesIndex.Should().Be(0);
        first.PointIndex.Should().Be(0);
        first.Center.Should().Be(new ChartPlanPoint(100, 50));
        first.InnerRadius.Should().Be(0);
        first.OuterRadius.Should().BeApproximately(42.5, 0.0001);
        first.StartAngle.Should().BeApproximately(-Math.PI / 2, 0.0001);
        first.EndAngle.Should().BeApproximately(0, 0.0001);
        first.IsLargeArc.Should().BeFalse();
        first.OuterStart.X.Should().BeApproximately(100, 0.0001);
        first.OuterStart.Y.Should().BeApproximately(7.5, 0.0001);
        first.OuterEnd.X.Should().BeApproximately(142.5, 0.0001);
        first.OuterEnd.Y.Should().BeApproximately(50, 0.0001);
    }

    [Fact]
    public void BuildDoughnutSlicePrimitives_PlansSeriesZeroAsInnermostRing()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Doughnut,
            DoughnutHolePercent = 50
        };

        var first = new ChartSeries { Name = "Inner" };
        first.Values.AddRange(new double?[] { 1, 1 });
        chart.Series.Add(first);

        var second = new ChartSeries { Name = "Outer" };
        second.Values.AddRange(new double?[] { 1, 1 });
        chart.Series.Add(second);

        var slices = ChartRenderPlanner.BuildDoughnutSlicePrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        slices.Should().HaveCount(4);
        slices[0].SeriesIndex.Should().Be(0);
        slices[0].PointIndex.Should().Be(0);
        slices[0].InnerRadius.Should().BeApproximately(21.25, 0.0001);
        slices[0].OuterRadius.Should().BeApproximately(31.025, 0.0001);
        slices[2].SeriesIndex.Should().Be(1);
        slices[2].PointIndex.Should().Be(0);
        slices[2].InnerRadius.Should().BeApproximately(32.725, 0.0001);
        slices[2].OuterRadius.Should().BeApproximately(42.5, 0.0001);
    }

    [Fact]
    public void BuildDataLabelPlans_StackedColumnPercentUsesCategoryTotal()
    {
        var labels = new ChartDataLabels
        {
            ShowValue = true,
            ShowPercent = true,
            Position = DataLabelPosition.Center
        };
        var chart = new ChartShape
        {
            ChartType = ChartType.ColumnStacked,
            DataLabels = labels
        };
        chart.Categories.Add("Q1");
        var first = new ChartSeries { Name = "Actual" };
        first.Values.Add(2);
        var second = new ChartSeries { Name = "Forecast" };
        second.Values.Add(6);
        chart.Series.Add(first);
        chart.Series.Add(second);

        var planned = ChartRenderPlanner.BuildDataLabelPlans(
            chart,
            new ChartPlanRect(0, 0, 100, 100));

        planned.Should().Contain(label =>
            label.SeriesIndex == 1 &&
            label.CategoryIndex == 0 &&
            label.Text == "6 75%" &&
            label.Bounds == new ChartPlanRect(30, 32, 40, 11));
    }

    private static ChartShape MakeTwoSeriesChart(ChartType chartType)
    {
        var chart = new ChartShape { ChartType = chartType };
        chart.Categories.AddRange(new[] { "Q1", "Q2" });

        var first = new ChartSeries { Name = "Actual" };
        first.Values.AddRange(new double?[] { 10, 20 });
        chart.Series.Add(first);

        var second = new ChartSeries { Name = "Forecast" };
        second.Values.AddRange(new double?[] { 30, 40 });
        chart.Series.Add(second);

        return chart;
    }
}
