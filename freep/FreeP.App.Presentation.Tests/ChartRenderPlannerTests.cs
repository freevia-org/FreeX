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
    public void BuildFramePlan_ColumnAxisTitles_ReservesSharedTitleBands()
    {
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.ValueAxis.Title = "Revenue";
        chart.CategoryAxis.Title = "Quarter";

        var plan = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        plan.Plot.Should().Be(new ChartPlanRect(62, 8, 330, 254));
    }

    [Fact]
    public void BuildAxisTitlePlans_ColumnChart_PlansVerticalValueAndHorizontalCategoryTitles()
    {
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.ValueAxis.Title = "Revenue";
        chart.CategoryAxis.Title = "Quarter";
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var titles = ChartRenderPlanner.BuildAxisTitlePlans(chart, frame);

        titles.Should().HaveCount(2);
        titles[0].Label.Text.Should().Be("Revenue");
        titles[0].Label.Bounds.Should().Be(new ChartPlanRect(8, 8, 14, 254));
        titles[0].Label.FontSize.Should().Be(ChartRenderPlanner.AxisTitleFontSize);
        titles[0].Orientation.Should().Be(ChartAxisTitleOrientation.VerticalCounterclockwise);
        titles[1].Label.Text.Should().Be("Quarter");
        titles[1].Label.Bounds.Should().Be(new ChartPlanRect(62, 280, 330, 14));
        titles[1].Orientation.Should().Be(ChartAxisTitleOrientation.Horizontal);
    }

    [Fact]
    public void BuildAxisTitlePlans_BarChart_SwapsTitleOrientationsWithAxes()
    {
        var chart = new ChartShape { ChartType = ChartType.BarClustered };
        chart.ValueAxis.Title = "Revenue";
        chart.CategoryAxis.Title = "Quarter";
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var titles = ChartRenderPlanner.BuildAxisTitlePlans(chart, frame);

        frame.Plot.Should().Be(new ChartPlanRect(66, 8, 326, 230));
        titles.Should().HaveCount(2);
        titles[0].Label.Text.Should().Be("Revenue");
        titles[0].Label.Bounds.Should().Be(new ChartPlanRect(66, 256, 326, 14));
        titles[0].Orientation.Should().Be(ChartAxisTitleOrientation.Horizontal);
        titles[1].Label.Text.Should().Be("Quarter");
        titles[1].Label.Bounds.Should().Be(new ChartPlanRect(8, 8, 14, 230));
        titles[1].Orientation.Should().Be(ChartAxisTitleOrientation.VerticalCounterclockwise);
    }

    [Fact]
    public void BuildAxisTitlePlans_DeletedAxes_DoNotReserveOrRenderTitles()
    {
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.ValueAxis.Title = "Revenue";
        chart.ValueAxis.Delete = true;
        chart.CategoryAxis.Title = "Quarter";
        chart.CategoryAxis.Delete = true;

        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));
        var titles = ChartRenderPlanner.BuildAxisTitlePlans(chart, frame);

        frame.Plot.Should().Be(new ChartPlanRect(48, 8, 344, 268));
        titles.Should().BeEmpty();
    }

    [Fact]
    public void BuildMajorGridLinePrimitivePlan_PlansSharedStrokeWithCartesianGridlines()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var plan = ChartRenderPlanner.BuildMajorGridLinePrimitivePlan(chart, frame);

        plan.GridLines.Should().HaveCount(6);
        plan.GridLines[0].Start.Should().Be(new ChartPlanPoint(frame.Plot.X, frame.Plot.Bottom));
        plan.GridLines[0].End.Should().Be(new ChartPlanPoint(frame.Plot.Right, frame.Plot.Bottom));
        plan.GridLines[^1].Start.Should().Be(new ChartPlanPoint(frame.Plot.X, frame.Plot.Y));
        plan.GridLines[^1].End.Should().Be(new ChartPlanPoint(frame.Plot.Right, frame.Plot.Y));
        plan.Stroke.Should().Be(new ChartStrokePlan(
            new SrgbColor(0xD9, 0xD9, 0xD9),
            Alpha: 255,
            Thickness: 0.5));
    }

    [Fact]
    public void BuildMajorGridLinePrimitivePlan_DisabledGridlinesReturnEmptyGeometryButKeepSharedStroke()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.ValueAxis.HasMajorGridlines = false;
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var plan = ChartRenderPlanner.BuildMajorGridLinePrimitivePlan(chart, frame);

        plan.GridLines.Should().BeEmpty();
        plan.Stroke.Should().Be(new ChartStrokePlan(
            new SrgbColor(0xD9, 0xD9, 0xD9),
            Alpha: 255,
            Thickness: 0.5));
    }

    [Fact]
    public void BuildMajorAxisTickPrimitivePlan_ColumnChart_PlansCategoryAndValueTicks()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var plan = ChartRenderPlanner.BuildMajorAxisTickPrimitivePlan(chart, frame);

        plan.CategoryTicks.Should().HaveCount(2);
        plan.CategoryTicks[0].Start.Should().Be(new ChartPlanPoint(134, frame.Plot.Bottom));
        plan.CategoryTicks[0].End.Should().Be(new ChartPlanPoint(134, frame.Plot.Bottom + ChartRenderPlanner.AxisMajorTickLength));
        plan.CategoryTicks[1].Start.Should().Be(new ChartPlanPoint(306, frame.Plot.Bottom));
        plan.ValueTicks.Should().HaveCount(6);
        plan.ValueTicks[0].Start.Should().Be(new ChartPlanPoint(frame.Plot.X - ChartRenderPlanner.AxisMajorTickLength, frame.Plot.Bottom));
        plan.ValueTicks[0].End.Should().Be(new ChartPlanPoint(frame.Plot.X, frame.Plot.Bottom));
        plan.ValueTicks[^1].Start.Should().Be(new ChartPlanPoint(frame.Plot.X - ChartRenderPlanner.AxisMajorTickLength, frame.Plot.Y));
        plan.ValueTicks[^1].End.Should().Be(new ChartPlanPoint(frame.Plot.X, frame.Plot.Y));
        plan.Stroke.Should().Be(new ChartStrokePlan(
            new SrgbColor(0x7F, 0x7F, 0x7F),
            Alpha: 255,
            Thickness: 0.75));
    }

    [Fact]
    public void BuildMajorAxisTickPrimitivePlan_BarChart_SwapsCategoryAndValueTickEdges()
    {
        var chart = MakeTwoSeriesChart(ChartType.BarClustered);
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var plan = ChartRenderPlanner.BuildMajorAxisTickPrimitivePlan(chart, frame);

        plan.CategoryTicks.Should().HaveCount(2);
        plan.CategoryTicks[0].Start.Should().Be(new ChartPlanPoint(frame.Plot.X - ChartRenderPlanner.AxisMajorTickLength, 191));
        plan.CategoryTicks[0].End.Should().Be(new ChartPlanPoint(frame.Plot.X, 191));
        plan.CategoryTicks[1].Start.Should().Be(new ChartPlanPoint(frame.Plot.X - ChartRenderPlanner.AxisMajorTickLength, 69));
        plan.ValueTicks.Should().HaveCount(6);
        plan.ValueTicks[0].Start.Should().Be(new ChartPlanPoint(frame.Plot.X, frame.Plot.Bottom));
        plan.ValueTicks[0].End.Should().Be(new ChartPlanPoint(frame.Plot.X, frame.Plot.Bottom + ChartRenderPlanner.AxisMajorTickLength));
        plan.ValueTicks[^1].Start.Should().Be(new ChartPlanPoint(frame.Plot.Right, frame.Plot.Bottom));
        plan.ValueTicks[^1].End.Should().Be(new ChartPlanPoint(frame.Plot.Right, frame.Plot.Bottom + ChartRenderPlanner.AxisMajorTickLength));
    }

    [Fact]
    public void BuildMajorAxisTickPrimitivePlan_DeletedAxesAndNoPlot_ReturnEmptyGeometry()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.CategoryAxis.Delete = true;
        chart.ValueAxis.Delete = true;
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var deletedAxisPlan = ChartRenderPlanner.BuildMajorAxisTickPrimitivePlan(chart, frame);
        var noPlotPlan = ChartRenderPlanner.BuildMajorAxisTickPrimitivePlan(
            chart,
            frame with { Plot = new ChartPlanRect(frame.Plot.X, frame.Plot.Y, 0, frame.Plot.Height) });

        deletedAxisPlan.CategoryTicks.Should().BeEmpty();
        deletedAxisPlan.ValueTicks.Should().BeEmpty();
        noPlotPlan.CategoryTicks.Should().BeEmpty();
        noPlotPlan.ValueTicks.Should().BeEmpty();
    }

    [Fact]
    public void BuildLegendItemPlans_BottomLegend_PlansOneItemPerSeries()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.Legend = LegendPosition.Bottom;
        var colors = new[]
        {
            new SrgbColor(0x11, 0x22, 0x33),
            new SrgbColor(0x44, 0x55, 0x66)
        };
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var items = ChartRenderPlanner.BuildLegendItemPlans(chart, frame, colors);

        items.Should().HaveCount(2);
        items[0].SwatchBounds.Should().Be(new ChartPlanRect(48, 277, 8, 8));
        items[0].Label.Text.Should().Be("Actual");
        items[0].Label.Bounds.Should().Be(new ChartPlanRect(58, 274, 70, ChartRenderPlanner.LegendHeight));
        items[0].Label.FontSize.Should().Be(7.0);
        items[0].Label.Alignment.Should().Be(ChartPlanTextAlignment.Left);
        items[0].Fill.Should().Be(new ChartFillPlan(colors[0], Alpha: 255));
        items[1].SwatchBounds.Should().Be(new ChartPlanRect(128, 277, 8, 8));
        items[1].Label.Text.Should().Be("Forecast");
        items[1].Fill.Should().Be(new ChartFillPlan(colors[1], Alpha: 255));
    }

    [Fact]
    public void BuildLegendItemPlans_RightPieLegend_PlansPointItemsAndFallbackColor()
    {
        var series = new ChartSeries { Name = "Share" };
        series.Values.AddRange(new double?[] { 1, 2 });
        var chart = new ChartShape
        {
            ChartType = ChartType.Pie,
            Legend = LegendPosition.Right
        };
        chart.Series.Add(series);
        var suppliedColor = new SrgbColor(0x10, 0x20, 0x30);
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var items = ChartRenderPlanner.BuildLegendItemPlans(chart, frame, new[] { suppliedColor });

        items.Should().HaveCount(2);
        items[0].SwatchBounds.Should().Be(new ChartPlanRect(316, 11, 8, 8));
        items[0].Label.Text.Should().Be("Point 1");
        items[0].Label.Bounds.Should().Be(new ChartPlanRect(326, 8, 66, ChartRenderPlanner.LegendHeight));
        items[0].Fill.Should().Be(new ChartFillPlan(suppliedColor, Alpha: 255));
        items[1].SwatchBounds.Should().Be(new ChartPlanRect(316, 25, 8, 8));
        items[1].Label.Text.Should().Be("Point 2");
        items[1].Fill.Should().Be(new ChartFillPlan(new SrgbColor(0x4F, 0x81, 0xBD), Alpha: 255));
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
            Bounds: new ChartPlanRect(30, 80, 19, 20),
            Fill: new ChartFillPlan(new SrgbColor(0x4F, 0x81, 0xBD), ChartRenderPlanner.RectSeriesFillAlpha),
            Stroke: null));
        primitives.Should().ContainEquivalentOf(new ChartRectPrimitive(
            SeriesIndex: 1,
            CategoryIndex: 0,
            Bounds: new ChartPlanRect(50, 40, 19, 60),
            Fill: new ChartFillPlan(new SrgbColor(0xC0, 0x50, 0x4D), ChartRenderPlanner.RectSeriesFillAlpha),
            Stroke: null));
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
            Bounds: new ChartPlanRect(0, 75, 40, 9),
            Fill: new ChartFillPlan(new SrgbColor(0x4F, 0x81, 0xBD), ChartRenderPlanner.RectSeriesFillAlpha),
            Stroke: null));
        primitives.Should().ContainEquivalentOf(new ChartRectPrimitive(
            SeriesIndex: 1,
            CategoryIndex: 0,
            Bounds: new ChartPlanRect(0, 65, 120, 9),
            Fill: new ChartFillPlan(new SrgbColor(0xC0, 0x50, 0x4D), ChartRenderPlanner.RectSeriesFillAlpha),
            Stroke: null));
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
        primitive.LineSegments.Should().BeEmpty();
        primitive.Markers.Should().HaveCount(2);
        primitive.Markers[0].Center.Should().Be(new ChartPlanPoint(0, 75));
        primitive.Markers[0].Radius.Should().Be(ChartRenderPlanner.LineMarkerRadius);
        primitive.MarkerFill.Should().Be(new ChartFillPlan(
            new SrgbColor(0x4F, 0x81, 0xBD),
            ChartRenderPlanner.RectSeriesFillAlpha));
        primitive.MarkerStroke.Should().Be(new ChartStrokePlan(
            new SrgbColor(0x4F, 0x81, 0xBD),
            Alpha: 255,
            Thickness: ChartRenderPlanner.LineMarkerStrokeThickness));
    }

    [Fact]
    public void BuildLineSeriesPrimitives_PlansSharedStrokeSegmentsAndMarkerStyle()
    {
        var series = new ChartSeries { Name = "Line" };
        series.Values.AddRange(new double?[] { 10, 20, 30 });
        var chart = new ChartShape { ChartType = ChartType.LineMarkers };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });
        chart.Series.Add(series);
        var seriesColor = new SrgbColor(0x20, 0x40, 0x60);

        var primitive = ChartRenderPlanner.BuildLineSeriesPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100),
            withMarkers: true,
            seriesColors: new[] { seriesColor }).Single();

        primitive.Stroke.Should().Be(new ChartStrokePlan(
            seriesColor,
            Alpha: 255,
            Thickness: ChartRenderPlanner.LineSeriesStrokeThickness));
        primitive.LineSegments.Should().HaveCount(2);
        primitive.LineSegments[0].Start.Should().Be(new ChartPlanPoint(0, 75));
        primitive.LineSegments[0].End.Should().Be(new ChartPlanPoint(100, 50));
        primitive.LineSegments[0].Stroke.Should().Be(primitive.Stroke);
        primitive.Markers.Should().HaveCount(3);
        primitive.Markers[0].Fill.Should().Be(new ChartFillPlan(
            seriesColor,
            ChartRenderPlanner.RectSeriesFillAlpha));
        primitive.Markers[0].Stroke.Should().Be(new ChartStrokePlan(
            seriesColor,
            Alpha: 255,
            Thickness: ChartRenderPlanner.LineMarkerStrokeThickness));
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
            new ChartPlanRect(0, 0, 200, 100),
            new[] { new SrgbColor(0x12, 0x34, 0x56) });

        plan.GridLines.Should().HaveCount(11);
        plan.GridLineStroke.Should().Be(new ChartStrokePlan(
            new SrgbColor(0xD9, 0xD9, 0xD9),
            Alpha: 255,
            Thickness: 0.5));
        plan.XAxisLabels.Should().HaveCount(6);
        plan.YAxisLabels.Should().HaveCount(5);
        plan.Series.Should().ContainSingle();
        plan.Series[0].DrawLines.Should().BeTrue();
        plan.Series[0].DrawMarkers.Should().BeTrue();
        plan.Series[0].Points[0].Should().Be(new ChartPlanPoint(0, 75));
        plan.Series[0].Points[1].Should().BeNull();
        plan.Series[0].Points[2]!.Value.X.Should().BeApproximately(160, 0.0001);
        plan.Series[0].Points[2]!.Value.Y.Should().BeApproximately(25, 0.0001);
        plan.Series[0].LineSegments.Should().BeEmpty();
        plan.Series[0].Markers.Should().HaveCount(2);
        plan.Series[0].Markers[0].Fill.Should().Be(new ChartFillPlan(
            new SrgbColor(0x12, 0x34, 0x56),
            Alpha: 255));
        plan.Series[0].Markers[0].Radius.Should().Be(ChartRenderPlanner.ScatterMarkerRadius);
        plan.DataLabels.Should().BeEmpty();
    }

    [Fact]
    public void BuildScatterPrimitivePlan_PlansRendererNeutralSegmentsMarkersAndLabels()
    {
        var series = new ChartSeries { Name = "XY" };
        series.XValues.AddRange(new double?[] { 0, 50 });
        series.Values.AddRange(new double?[] { 10, 20 });

        var chart = new ChartShape
        {
            ChartType = ChartType.Scatter,
            ScatterStyle = ScatterStyle.LineMarker,
            DataLabels = new ChartDataLabels
            {
                ShowValue = true,
                Position = DataLabelPosition.Right
            }
        };
        chart.Series.Add(series);

        var plan = ChartRenderPlanner.BuildScatterPrimitivePlan(
            chart,
            new ChartPlanRect(0, 0, 100, 100),
            new[] { new SrgbColor(0x20, 0x40, 0x60) });

        var primitive = plan.Series.Single();
        primitive.LineSegments.Should().ContainSingle();
        primitive.LineSegments[0].Start.Should().Be(new ChartPlanPoint(0, 60));
        primitive.LineSegments[0].End.X.Should().BeApproximately(83.3333, 0.0001);
        primitive.LineSegments[0].End.Y.Should().BeApproximately(20, 0.0001);
        primitive.LineSegments[0].Stroke.Should().Be(new ChartStrokePlan(
            new SrgbColor(0x20, 0x40, 0x60),
            Alpha: 255,
            Thickness: ChartRenderPlanner.ScatterLineThickness));
        primitive.Markers.Should().HaveCount(2);
        primitive.Markers[0].Center.Should().Be(new ChartPlanPoint(0, 60));
        primitive.Markers[0].Radius.Should().Be(ChartRenderPlanner.ScatterMarkerRadius);
        primitive.Markers[0].Fill.Should().Be(new ChartFillPlan(
            new SrgbColor(0x20, 0x40, 0x60),
            Alpha: 255));

        plan.DataLabels.Should().HaveCount(2);
        plan.DataLabels[0].Text.Should().Be("10");
        plan.DataLabels[0].Bounds.Should().Be(new ChartPlanRect(
            3,
            54.5,
            ChartRenderPlanner.ScatterDataLabelWidth,
            ChartRenderPlanner.ScatterDataLabelHeight));
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
        var seriesColor = new SrgbColor(0x12, 0x34, 0x56);

        var plan = ChartRenderPlanner.BuildBubblePrimitivePlan(
            chart,
            new ChartPlanRect(0, 0, 160, 80),
            new[] { seriesColor });

        plan.GridLines.Should().HaveCount(12);
        plan.GridLineStroke.Should().Be(new ChartStrokePlan(
            new SrgbColor(0xD9, 0xD9, 0xD9),
            Alpha: 255,
            Thickness: 0.5));
        plan.XAxisLabels.Should().HaveCount(6);
        plan.YAxisLabels.Should().HaveCount(6);
        plan.Bubbles.Should().HaveCount(2);
        plan.Bubbles[0].Radius.Should().BeApproximately(5, 0.0001);
        plan.Bubbles[0].Center.Should().Be(new ChartPlanPoint(0, 80));
        plan.Bubbles[0].Fill.Should().Be(new ChartFillPlan(seriesColor, ChartRenderPlanner.BubbleFillAlpha));
        plan.Bubbles[0].Stroke.Should().Be(new ChartStrokePlan(
            seriesColor,
            Alpha: 255,
            Thickness: ChartRenderPlanner.BubbleStrokeThickness));
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
        var seriesColor = new SrgbColor(0xAA, 0xBB, 0xCC);

        var plan = ChartRenderPlanner.BuildRadarPrimitivePlan(
            chart,
            new ChartPlanRect(0, 0, 200, 100),
            new[] { seriesColor });

        plan.Rings.Should().HaveCount(4);
        plan.Rings[0].Points.Should().HaveCount(4);
        plan.Rings[0].Path.Points.Should().Equal(plan.Rings[0].Points);
        plan.Rings[0].Path.IsClosed.Should().BeTrue();
        plan.Rings[0].Path.Fill.Should().BeNull();
        plan.Rings[0].Stroke.Should().Be(new ChartStrokePlan(
            new SrgbColor(0xD9, 0xD9, 0xD9),
            Alpha: 255,
            Thickness: 0.5));
        plan.Spokes.Should().HaveCount(4);
        plan.SpokeStroke.Should().Be(new ChartStrokePlan(
            new SrgbColor(0xC0, 0xC0, 0xC0),
            Alpha: 255,
            Thickness: 0.5));
        plan.CategoryLabels.Should().HaveCount(4);
        plan.Series.Should().ContainSingle();
        plan.Series[0].IsFilled.Should().BeTrue();
        plan.Series[0].WithMarkers.Should().BeFalse();
        plan.Series[0].Path.IsClosed.Should().BeTrue();
        plan.Series[0].Path.Points.Should().Equal(plan.Series[0].Points);
        plan.Series[0].Path.Fill.Should().Be(new ChartFillPlan(seriesColor, ChartRenderPlanner.RadarFillAlpha));
        plan.Series[0].Stroke.Should().Be(new ChartStrokePlan(
            seriesColor,
            Alpha: 255,
            Thickness: ChartRenderPlanner.RadarSeriesStrokeThickness));
        plan.Series[0].Markers.Should().BeEmpty();
        plan.Series[0].Points[0].X.Should().BeApproximately(100, 0.0001);
        plan.Series[0].Points[0].Y.Should().BeApproximately(40.625, 0.0001);
    }

    [Fact]
    public void BuildRadarPrimitivePlan_PlansMarkerCirclesFromSeriesColor()
    {
        var series = new ChartSeries { Name = "Radar" };
        series.Values.AddRange(new double?[] { 1, 2, 3 });

        var chart = new ChartShape
        {
            ChartType = ChartType.Radar,
            RadarStyle = RadarStyle.Marker
        };
        chart.Categories.AddRange(new[] { "North", "East", "South" });
        chart.Series.Add(series);
        var seriesColor = new SrgbColor(0x22, 0x66, 0xAA);

        var plan = ChartRenderPlanner.BuildRadarPrimitivePlan(
            chart,
            new ChartPlanRect(0, 0, 120, 120),
            new[] { seriesColor });

        var primitive = plan.Series.Should().ContainSingle().Subject;
        primitive.WithMarkers.Should().BeTrue();
        primitive.Path.Fill.Should().BeNull();
        primitive.Markers.Should().HaveCount(3);
        primitive.Markers[0].Radius.Should().Be(ChartRenderPlanner.RadarMarkerRadius);
        primitive.Markers[0].Fill.Should().Be(new ChartFillPlan(seriesColor, Alpha: 255));
        primitive.Markers[0].Stroke.Should().BeNull();
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
