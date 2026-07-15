using System.Collections.Generic;
using System.Linq;
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
    public void ComputePrimaryValueAxisRange_HundredPercentStackedDefaultsToPercentAxis()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnStacked100);

        ChartRenderPlanner.ComputePrimaryValueAxisRange(chart)
            .Should().Be((0, 1, 0.25));
    }

    [Fact]
    public void ComputePrimaryValueAxisRange_HundredPercentStackedRespectsAuthoredAxisBounds()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnStacked100);
        chart.ValueAxis.Min = 0;
        chart.ValueAxis.Max = 2;

        ChartRenderPlanner.ComputePrimaryValueAxisRange(chart)
            .Should().Be((0, 2.5, 0.5));
    }

    [Fact]
    public void ComputePrimaryValueAxisRange_StackedAreaUsesCategoryTotals()
    {
        var chart = new ChartShape { ChartType = ChartType.AreaStacked };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });

        var first = new ChartSeries { Name = "Actual" };
        first.Values.AddRange(new double?[] { 30, 50, 10 });
        chart.Series.Add(first);

        var second = new ChartSeries { Name = "Forecast" };
        second.Values.AddRange(new double?[] { 20, 80, 15 });
        chart.Series.Add(second);

        ChartRenderPlanner.ComputePrimaryValueAxisRange(chart)
            .Should().Be((0, 150, 25));
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

    [Theory]
    [InlineData(0.25, "0.0%", "25.0%")]
    [InlineData(1234.5, "#,##0.0", "1,234.5")]
    [InlineData(1234.5, "$#,##0.00", "$1,234.50")]
    [InlineData(-1234.5, "#,##0.0;(#,##0.0)", "(1,234.5)")]
    [InlineData(12.34, "#,##0.0 \"kg\"", "12.3 kg")]
    [InlineData(42, "[Red]#,##0", "42")]
    [InlineData(1234.5, "#,##0.0,", "1.2")]
    [InlineData(1234567, "0.0,,\"M\"", "1.2M")]
    [InlineData(1234, "[>=1000000]0.0,,\"M\";[>=1000]0.0,\"K\";0", "1.2K")]
    [InlineData(1234567, "[>=1000000]0.0,,\"M\";[>=1000]0.0,\"K\";0", "1.2M")]
    [InlineData(42, "[>=1000000]0.0,,\"M\";[>=1000]0.0,\"K\";0", "42")]
    [InlineData(1.0625, "[h]:mm:ss", "25:30:00")]
    [InlineData(0.0625, "[m]:ss", "90:00")]
    [InlineData(0.00001736111111111111, "[s].0", "1.5")]
    [InlineData(1.25, "# ?/?", "1 1/4")]
    [InlineData(0.3333333333333333, "# ??/??", "1/3")]
    [InlineData(0.125, "?/??", "1/8")]
    [InlineData(1.2, "# ?/??", "1 1/5")]
    public void FormatWithCode_UsesPowerPointStyleNumericCodes(double value, string code, string expected)
    {
        ChartRenderPlanner.FormatWithCode(value, code).Should().Be(expected);
    }

    [Fact]
    public void AxisLabelPlans_FormatElapsedTimeNumberFormats()
    {
        var series = new ChartSeries { Name = "Duration" };
        series.Values.AddRange(new double?[] { 0, 1 });
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Series.Add(series);
        chart.ValueAxis.Min = 0;
        chart.ValueAxis.Max = 1;
        chart.ValueAxis.NumberFormatCode = "[h]:mm:ss";
        chart.ValueAxis.NumberFormatSourceLinked = false;
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var valueLabels = ChartRenderPlanner.BuildValueAxisLabelPlans(chart, frame);

        valueLabels.Select(label => label.Text)
            .Should().StartWith(new[] { "0:00:00", "6:00:00", "12:00:00", "18:00:00", "24:00:00" });
        valueLabels.Should().OnlyContain(label =>
            label.AxisLabelFormat == new ChartAxisLabelFormatPlan("[h]:mm:ss", false));
    }

    [Fact]
    public void AxisLabelPlans_ApplyConditionalScaledCustomFormatSections()
    {
        var series = new ChartSeries { Name = "Revenue" };
        series.Values.AddRange(new double?[] { 250000, 1250000 });
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Series.Add(series);
        chart.ValueAxis.NumberFormatCode = "[>=1000000]0.0,,\"M\";[>=1000]0.0,\"K\";0";
        chart.ValueAxis.NumberFormatSourceLinked = false;
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var valueLabels = ChartRenderPlanner.BuildValueAxisLabelPlans(chart, frame);

        valueLabels.Select(label => label.Text)
            .Should().Equal("0", "250.0K", "500.0K", "750.0K", "1.0M", "1.3M", "1.5M");
        valueLabels.Should().OnlyContain(label =>
            label.AxisLabelFormat == new ChartAxisLabelFormatPlan(
                "[>=1000000]0.0,,\"M\";[>=1000]0.0,\"K\";0",
                false));
    }

    [Fact]
    public void FormatWithCode_UnsupportedFormatFallsBackWithoutThrowing()
    {
        var act = () => ChartRenderPlanner.FormatWithCode(1200, "unsupported text");

        act.Should().NotThrow().Which.Should().Be("1.2K");
    }

    [Fact]
    public void AxisLabelPlans_FormatTextAndCarryNumberFormatMetadata()
    {
        var series = new ChartSeries { Name = "Sales" };
        series.Values.AddRange(new double?[] { 1000, 2000 });
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(new[] { "2026-01-01", "2026-02-01" });
        chart.Series.Add(series);
        chart.CategoryAxis.NumberFormatCode = "m/d/yy";
        chart.CategoryAxis.NumberFormatSourceLinked = true;
        chart.ValueAxis.NumberFormatCode = "#,##0.0";
        chart.ValueAxis.NumberFormatSourceLinked = false;
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var categoryLabels = ChartRenderPlanner.BuildCategoryAxisLabelPlans(chart, frame);
        var valueLabels = ChartRenderPlanner.BuildValueAxisLabelPlans(chart, frame);

        categoryLabels[0].Text.Should().Be("1/1/26");
        categoryLabels[0].AxisLabelFormat.Should().Be(new ChartAxisLabelFormatPlan("m/d/yy", true));
        valueLabels[0].Text.Should().Be("0.0");
        valueLabels[0].AxisLabelFormat.Should().Be(new ChartAxisLabelFormatPlan("#,##0.0", false));
    }

    [Fact]
    public void CategoryAxisLabelPlans_FormatIsoAndSerialDateLabels()
    {
        var series = new ChartSeries { Name = "Sales" };
        series.Values.AddRange(new double?[] { 10, 20, 30 });
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(new[] { "2026-01-01", "46024", "Not a date" });
        chart.Series.Add(series);
        chart.CategoryAxis.NumberFormatCode = "[$-409]d\\-mmm;@";
        chart.CategoryAxis.NumberFormatSourceLinked = false;
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var categoryLabels = ChartRenderPlanner.BuildCategoryAxisLabelPlans(chart, frame);

        categoryLabels.Select(label => label.Text).Should().Equal("1-Jan", "2-Jan", "Not a date");
        categoryLabels.Should().OnlyContain(label =>
            label.AxisLabelFormat == new ChartAxisLabelFormatPlan("[$-409]d\\-mmm;@", false));
    }

    [Fact]
    public void SecondaryValueAxisPlan_FormatsTextAndCarriesNumberFormatMetadata()
    {
        var chart = MakeSecondaryAxisChart();
        chart.SecondaryValueAxis!.NumberFormatCode = "0.00%";
        chart.SecondaryValueAxis.NumberFormatSourceLinked = false;
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var plan = ChartRenderPlanner.BuildSecondaryValueAxisPrimitivePlan(chart, frame);

        plan.Labels[0].Text.Should().Be("0.00%");
        plan.Labels.Should().OnlyContain(label =>
            label.AxisLabelFormat == new ChartAxisLabelFormatPlan("0.00%", false));
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
    public void FormatDataLabel_UsesConditionalScaledNumberFormat()
    {
        var labels = new ChartDataLabels
        {
            ShowValue = true,
            NumberFormat = "[>=1000000]0.0,,\"M\";[>=1000]0.0,\"K\";0"
        };

        ChartRenderPlanner.FormatDataLabel(labels, 1250000, 1250000, "Q1", "Sales")
            .Should().Be("1.3M");
    }

    [Fact]
    public void FormatDataLabel_UsesBoundedFractionNumberFormat()
    {
        var labels = new ChartDataLabels
        {
            ShowCategoryName = true,
            ShowValue = true,
            NumberFormat = "# ??/??"
        };

        ChartRenderPlanner.FormatDataLabel(labels, 1.125, 1.125, "Elapsed", "Series")
            .Should().Be("Elapsed 1 1/8");
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
    public void BuildFramePlan_ImportedChartTextStyle_UsesPowerPointSizedSpacing()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.ColumnClustered,
            Legend = LegendPosition.Right,
            TextStyle = new ChartTextStyle { FontSizePt = 18.0 }
        };

        var plan = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(80, 73, 1146, 613));

        plan.Plot.Should().Be(new ChartPlanRect(148, 93, 938, 541));
        ChartRenderPlanner.ResolveTextFontSize(chart, 6.5).Should().Be(18.0);
    }

    [Theory]
    [InlineData(ChartType.Stock)]
    [InlineData(ChartType.Surface)]
    [InlineData(ChartType.Surface3D)]
    public void BuildFramePlan_StockAndSurfaceFamilies_UseExplicitCartesianFallback(ChartType chartType)
    {
        var chart = MakeTwoSeriesChart(chartType);

        var plan = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        plan.Family.Should().Be(ChartRenderFamily.Cartesian);
        plan.Plot.HasPositiveArea.Should().BeTrue(
            "stock/surface imports should render through the cartesian fallback, not unknown placeholders");
    }

    [Fact]
    public void BuildStockPrimitivePlan_UsesHighLowStemsAndOpenCloseTicks()
    {
        var chart = MakeStockChart();
        var plot = new ChartPlanRect(0, 0, 300, 200);

        var plan = ChartRenderPlanner.BuildStockPrimitivePlan(chart, plot);

        plan.HighLowLines.Should().HaveCount(3);
        plan.OpenTicks.Should().HaveCount(3);
        plan.CloseTicks.Should().HaveCount(3);
        plan.HighLowLines.Should().OnlyContain(line => line.Start.X == line.End.X);
        plan.HighLowLines[0].Start.Y.Should().BeGreaterThan(plan.HighLowLines[0].End.Y,
            "low values should sit below high values in the stock stem");
        plan.OpenTicks[0].Segment.Start.X.Should().BeLessThan(plan.OpenTicks[0].Segment.End.X);
        plan.OpenTicks[0].Segment.End.X.Should().Be(plan.HighLowLines[0].Start.X);
        plan.CloseTicks[0].Segment.Start.X.Should().Be(plan.HighLowLines[0].Start.X);
        plan.CloseTicks[0].Segment.End.X.Should().BeGreaterThan(plan.CloseTicks[0].Segment.Start.X);
    }

    [Fact]
    public void GradientColorInterpolation_UsesLinearLightRatherThanDirectSrgbMidpoint()
    {
        var midpoint = GradientColorInterpolation.InterpolateLinearLight(
            new SrgbColor(0xA0, 0xD0, 0xFF),
            new SrgbColor(0xC0, 0x70, 0x00),
            0.5);

        midpoint.Should().Be(new SrgbColor(0xB1, 0xA8, 0xBA));
    }

    [Fact]
    public void BuildStockPrimitivePlan_ClassifiesOpenClosePriceMovesForSharedRendering()
    {
        var chart = MakeStockChart();
        chart.Series[0].Values.Clear();
        chart.Series[0].Values.AddRange(new double?[] { 10, 12, 11, 10 });
        chart.Series[1].Values.Add(14);
        chart.Series[2].Values.Add(8);
        chart.Series[3].Values.Clear();
        chart.Series[3].Values.AddRange(new double?[] { 13, 11, 11, null });

        var plan = ChartRenderPlanner.BuildStockPrimitivePlan(
            chart,
            new ChartPlanRect(0, 0, 400, 200));

        plan.CloseTicks.Select(tick => tick.PriceMove)
            .Should().Equal(
                ChartStockPriceMove.Rising,
                ChartStockPriceMove.Falling,
                ChartStockPriceMove.Unchanged);
        plan.OpenTicks.Select(tick => tick.PriceMove)
            .Should().Equal(
                ChartStockPriceMove.Rising,
                ChartStockPriceMove.Falling,
                ChartStockPriceMove.Unchanged,
                ChartStockPriceMove.Unknown);
        plan.CloseTicks[0].Segment.Stroke.Color.Should().Be(new SrgbColor(0x2E, 0x7D, 0x32));
        plan.CloseTicks[1].Segment.Stroke.Color.Should().Be(new SrgbColor(0xC6, 0x28, 0x28));
        plan.CloseTicks[2].Segment.Stroke.Color.Should().Be(new SrgbColor(0x44, 0x44, 0x44));
    }

    [Fact]
    public void BuildStockVolumePrimitives_UsesBottomBandVolumeColumnsForSharedRendering()
    {
        var chart = MakeStockVolumeChart();
        var plot = new ChartPlanRect(0, 0, 300, 200);

        var ohlcPlan = ChartRenderPlanner.BuildStockPrimitivePlan(chart, plot);
        var volumeBars = ChartRenderPlanner.BuildStockVolumePrimitives(chart, plot);

        ohlcPlan.HighLowLines.Should().HaveCount(3);
        ohlcPlan.OpenTicks.Should().HaveCount(3);
        ohlcPlan.CloseTicks.Should().HaveCount(3);
        volumeBars.Should().HaveCount(3);
        volumeBars.Should().OnlyContain(bar => bar.SeriesIndex == 0);
        volumeBars[0].Bounds.Width.Should().BeApproximately(55, 0.0001);
        volumeBars[0].Bounds.Bottom.Should().Be(plot.Bottom);
        volumeBars[1].Bounds.Height.Should().BeApproximately(
            plot.Height * ChartRenderPlanner.StockVolumeBandHeightFraction,
            0.0001);
        volumeBars[2].Bounds.Height.Should().BeLessThan(volumeBars[1].Bounds.Height);
        volumeBars[0].Fill.Color.Should().Be(ChartRenderPlanner.ResolveSeriesColor(0, null));
    }

    [Theory]
    [InlineData(ChartType.Surface)]
    [InlineData(ChartType.Surface3D)]
    public void BuildSurfaceCellPrimitives_MapsSeriesAndCategoriesToValueGrid(ChartType chartType)
    {
        var chart = MakeSurfaceChart(chartType);
        var plot = new ChartPlanRect(0, 0, 300, 120);

        var cells = ChartRenderPlanner.BuildSurfaceCellPrimitives(chart, plot);

        cells.Should().HaveCount(6);
        cells[0].Bounds.Should().Be(new ChartPlanRect(0, 0, 100, 60));
        cells[^1].Bounds.Should().Be(new ChartPlanRect(200, 60, 100, 60));
        cells.Min(cell => cell.NormalizedValue).Should().Be(0);
        cells.Max(cell => cell.NormalizedValue).Should().Be(1);
        cells.Select(cell => cell.Fill.Color).Distinct().Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void BuildSurfaceCellPrimitives_SkipsBlankCellsWithoutReflowingGrid()
    {
        var chart = MakeSurfaceChart(ChartType.Surface);
        chart.Series[0].Values[1] = null;

        var cells = ChartRenderPlanner.BuildSurfaceCellPrimitives(
            chart,
            new ChartPlanRect(0, 0, 300, 120));

        cells.Should().HaveCount(5);
        cells.Should().NotContain(cell => cell.SeriesIndex == 0 && cell.CategoryIndex == 1);
        cells.Single(cell => cell.SeriesIndex == 0 && cell.CategoryIndex == 2)
            .Bounds.Should().Be(new ChartPlanRect(200, 0, 100, 60));
        cells.Single(cell => cell.SeriesIndex == 1 && cell.CategoryIndex == 0)
            .Bounds.Should().Be(new ChartPlanRect(0, 60, 100, 60));
    }

    [Fact]
    public void BuildSurfaceGeometryPlan_Surface3DPlansProjectedFacetsAndWireframe()
    {
        var chart = MakeSurfaceChart(ChartType.Surface3D);
        chart.Series[0].Values[2] = chart.Series[0].Values[0];

        var plan = ChartRenderPlanner.BuildSurfaceGeometryPlan(
            chart,
            new ChartPlanRect(0, 0, 300, 120));

        plan.Cells.Should().HaveCount(6);
        plan.Points.Should().HaveCount(6);
        plan.Facets.Should().HaveCount(2);
        plan.WireframeSegments.Should().HaveCount(7);
        plan.ContourSegments.Should().NotBeEmpty();
        plan.Facets.Should().OnlyContain(facet => facet.Points.Count == 4);
        plan.Facets.Select(facet => facet.AverageNormalizedValue)
            .Should()
            .OnlyContain(value => value > 0 && value < 1);

        var lowest = plan.Points.Single(point => point.SeriesIndex == 0 && point.CategoryIndex == 0);
        var highest = plan.Points.Single(point => point.SeriesIndex == 1 && point.CategoryIndex == 2);
        highest.Point.Y.Should().BeLessThan(lowest.Point.Y,
            "3-D surface projection should raise higher values instead of drawing a flat fallback grid");
        highest.Point.X.Should().BeGreaterThan(lowest.Point.X,
            "3-D surface projection should include depth offset across the series axis");

        var south = plan.Points.Single(point => point.SeriesIndex == 0 && point.CategoryIndex == 2);
        south.Point.Y.Should().BeGreaterThan(lowest.Point.Y,
            "the category axis should recede downwards to the right in PowerPoint's 3-D surface view");
    }

    [Fact]
    public void BuildSurfaceGeometryPlan_Surface3DPreservesIncompleteCellsAsTriangles()
    {
        var chart = MakeSurfaceChart(ChartType.Surface3D);
        chart.VaryColors = true;
        chart.Series[0].Values[1] = null;

        var plan = ChartRenderPlanner.BuildSurfaceGeometryPlan(
            chart,
            new ChartPlanRect(0, 0, 300, 120));

        plan.Facets.Should().HaveCount(2);
        plan.Facets.Should().OnlyContain(facet => facet.Points.Count == 3);
        plan.Facets.Select(facet => facet.Fill.Color).Distinct().Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void BuildSurfaceGeometryPlan_SurfacePlansContourSegmentsInPlotBounds()
    {
        var chart = MakeSurfaceChart(ChartType.Surface);

        var plan = ChartRenderPlanner.BuildSurfaceGeometryPlan(
            chart,
            new ChartPlanRect(10, 20, 300, 120));

        plan.Facets.Should().HaveCount(2);
        plan.ContourSegments.Should().NotBeEmpty();
        plan.ContourSegments.Should().OnlyContain(segment =>
            segment.Start.X >= 10 &&
            segment.Start.X <= 310 &&
            segment.End.X >= 10 &&
            segment.End.X <= 310 &&
            segment.Start.Y >= 20 &&
            segment.Start.Y <= 140 &&
            segment.End.Y >= 20 &&
            segment.End.Y <= 140);
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
    public void BuildFramePlan_SecondaryValueAxis_ReservesRightAxisLabelAndTitleBands()
    {
        var chart = MakeSecondaryAxisChart();
        chart.SecondaryValueAxis!.Title = "Margin";

        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        frame.Plot.Should().Be(new ChartPlanRect(48, 8, 288, 268));
    }

    [Fact]
    public void BuildFramePlan_DataTable_ReservesBottomTableBand()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.DataTable = new ChartDataTableSettings();

        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        // The plot's left edge is inset by the data table's row-header column width (72) so the
        // plot's category band and the table's category columns share one left origin/width.
        frame.Plot.Should().Be(new ChartPlanRect(120, 8, 272, 241));
        ChartRenderPlanner.BuildCategoryAxisLabelPlans(chart, frame).Should().BeEmpty();
    }

    [Fact]
    public void BuildFramePlan_DataTableColumnsAlignWithPlotCategoryBand()
    {
        var withTable = MakeTwoSeriesChart(ChartType.ColumnClustered);
        withTable.DataTable = new ChartDataTableSettings();
        var frameWithTable = ChartRenderPlanner.BuildFramePlan(withTable, new ChartPlanRect(0, 0, 400, 300));
        var tablePlan = ChartRenderPlanner.BuildDataTablePrimitivePlan(withTable, frameWithTable);

        // The plot's category band (bars/points) starts at plot.X and spans plot.Width/categoryCount
        // per category. The data table's first category column (ColumnIndex 1, i.e. Q1) must start
        // at the exact same X and have the exact same width, so it sits directly under category 0's
        // bar - matching PowerPoint, where each data-table column sits under its category.
        double plot = frameWithTable.Plot.X;
        double categoryStep = frameWithTable.Plot.Width / withTable.Categories.Count;
        var firstCategoryColumn = tablePlan.Cells.Single(cell => cell.RowIndex == 0 && cell.ColumnIndex == 1);

        firstCategoryColumn.CellBounds.X.Should().Be(plot);
        firstCategoryColumn.CellBounds.Width.Should().Be(categoryStep);

        // Without a data table, the plot layout is unchanged (no regression for the common case).
        var withoutTable = MakeTwoSeriesChart(ChartType.ColumnClustered);
        var frameWithoutTable = ChartRenderPlanner.BuildFramePlan(withoutTable, new ChartPlanRect(0, 0, 400, 300));
        frameWithoutTable.Plot.Should().Be(new ChartPlanRect(48, 8, 344, 268));
    }

    [Fact]
    public void BuildDataTablePrimitivePlan_PlansHeadersSeriesRowsKeysAndBorders()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.DataTable = new ChartDataTableSettings { ShowLegendKeys = true };
        var colors = new[]
        {
            new SrgbColor(0x11, 0x22, 0x33),
            new SrgbColor(0x44, 0x55, 0x66)
        };
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var plan = ChartRenderPlanner.BuildDataTablePrimitivePlan(chart, frame, colors);

        plan.Bounds.Should().Be(new ChartPlanRect(48, 253, 344, 39));
        plan.Cells.Should().HaveCount(9);
        plan.Cells.Should().Contain(cell =>
            cell.RowIndex == 0 &&
            cell.ColumnIndex == 1 &&
            cell.Text == "Q1" &&
            cell.CellBounds == new ChartPlanRect(120, 253, 136, 13) &&
            cell.Bounds == new ChartPlanRect(122, 253, 132, 13) &&
            cell.IsHeader);
        plan.Cells.Should().Contain(cell =>
            cell.RowIndex == 1 &&
            cell.ColumnIndex == 0 &&
            cell.Text == "Actual" &&
            cell.CellBounds == new ChartPlanRect(48, 266, 72, 13) &&
            cell.Bounds == new ChartPlanRect(58, 266, 60, 13) &&
            cell.LegendKeyBounds == new ChartPlanRect(50, 269.5, 6, 6) &&
            cell.LegendKeyFill == new ChartFillPlan(colors[0], Alpha: 255));
        plan.Cells.Should().Contain(cell =>
            cell.RowIndex == 2 &&
            cell.ColumnIndex == 2 &&
            cell.Text == "40" &&
            cell.CellBounds == new ChartPlanRect(256, 279, 136, 13) &&
            cell.Bounds == new ChartPlanRect(258, 279, 132, 13));
        plan.HorizontalBorders.Should().HaveCount(4);
        plan.VerticalBorders.Should().HaveCount(4);
        plan.OutlineBorders.Should().HaveCount(4);
        plan.BorderStroke.Should().Be(new ChartStrokePlan(
            new SrgbColor(0xB7, 0xB7, 0xB7),
            Alpha: 255,
            Thickness: 0.5));
    }

    [Fact]
    public void BuildDataTablePrimitivePlan_UsesModeledPowerPointBorderStroke()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.DataTable = new ChartDataTableSettings
        {
            BorderOutline = new ShapeOutline.Visible(
                new SrgbColor(0x12, 0x34, 0x56),
                widthPt: 1.25,
                dash: OutlineDash.DashDot)
        };
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var plan = ChartRenderPlanner.BuildDataTablePrimitivePlan(chart, frame);

        plan.BorderStroke.Should().Be(new ChartStrokePlan(
            new SrgbColor(0x12, 0x34, 0x56),
            Alpha: 255,
            Thickness: 1.25,
            Dash: OutlineDash.DashDot));
    }

    [Fact]
    public void BuildDataTablePrimitivePlan_UsesModeledPowerPointGradientBorderStroke()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.DataTable = new ChartDataTableSettings
        {
            BorderOutline = new ShapeOutline.GradientVisible(
                new ShapeFill.Gradient(
                    new[]
                    {
                        new GradientStop(0.0, new ThemeAwareColor(new SrgbColor(0x10, 0x20, 0x30))),
                        new GradientStop(0.5, new ThemeAwareColor(new SrgbColor(0x44, 0x55, 0x66))),
                        new GradientStop(1.0, new ThemeAwareColor(new SrgbColor(0xD0, 0xE0, 0xF0)))
                    },
                    GradientKind.Linear,
                    angleDegrees: 35.0),
                widthPt: 1.75,
                dash: OutlineDash.LongDashDot)
        };
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var plan = ChartRenderPlanner.BuildDataTablePrimitivePlan(chart, frame);

        plan.BorderStroke.Color.Should().Be(new SrgbColor(0x10, 0x20, 0x30));
        plan.BorderStroke.Alpha.Should().Be(255);
        plan.BorderStroke.Thickness.Should().Be(1.75);
        plan.BorderStroke.Dash.Should().Be(OutlineDash.LongDashDot);
        var gradient = plan.BorderStroke.Fill.Should().BeOfType<ResolvedFill.Gradient>().Subject;
        gradient.Kind.Should().Be(GradientKind.Linear);
        gradient.AngleDegrees.Should().Be(35.0);
        gradient.Stops.Select(stop => (stop.Position, stop.Color)).Should().Equal(
            (0.0, new SrgbColor(0x10, 0x20, 0x30)),
            (0.5, new SrgbColor(0x44, 0x55, 0x66)),
            (1.0, new SrgbColor(0xD0, 0xE0, 0xF0)));
    }

    [Fact]
    public void BuildDataTablePrimitivePlan_UsesModeledPowerPointBackgroundFill()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.DataTable = new ChartDataTableSettings
        {
            BackgroundFill = new ShapeFill.Solid(new SrgbColor(0xFA, 0xF1, 0xD2))
        };
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var plan = ChartRenderPlanner.BuildDataTablePrimitivePlan(chart, frame);

        plan.BackgroundFill.Should().Be(new ChartFillPlan(new SrgbColor(0xFA, 0xF1, 0xD2), Alpha: 255));
    }

    [Fact]
    public void BuildDataTablePrimitivePlan_UsesModeledPowerPointTextStyle()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.DataTable = new ChartDataTableSettings
        {
            TextStyle = new ChartTextStyle
            {
                FontSizePt = 8.75,
                Bold = true,
                Italic = true,
                Color = new ThemeAwareColor(new SrgbColor(0x22, 0x44, 0x66))
            }
        };
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var plan = ChartRenderPlanner.BuildDataTablePrimitivePlan(chart, frame);

        plan.Cells.Should().NotBeEmpty();
        plan.Cells.Should().OnlyContain(cell =>
            cell.FontSize == 8.75 &&
            cell.IsBold &&
            cell.IsItalic &&
            cell.TextColor == new SrgbColor(0x22, 0x44, 0x66));
    }

    [Fact]
    public void BuildDataTablePrimitivePlan_UsesModeledFontFamily()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.DataTable = new ChartDataTableSettings
        {
            TextStyle = new ChartTextStyle
            {
                FontFamily = "Georgia"
            }
        };
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var plan = ChartRenderPlanner.BuildDataTablePrimitivePlan(chart, frame);

        plan.Cells.Should().NotBeEmpty();
        plan.Cells.Should().OnlyContain(cell => cell.FontFamily == "Georgia");
    }

    [Fact]
    public void BuildDataTablePrimitivePlan_NoFontFamilySet_CellsHaveNullFontFamily()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.DataTable = new ChartDataTableSettings();
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var plan = ChartRenderPlanner.BuildDataTablePrimitivePlan(chart, frame);

        plan.Cells.Should().NotBeEmpty();
        plan.Cells.Should().OnlyContain(cell => cell.FontFamily == null);
    }

    [Fact]
    public void BuildDataTablePrimitivePlan_UnsupportedFamiliesAndDisabledBordersReturnExpectedPlan()
    {
        var pie = MakeTwoSeriesChart(ChartType.Pie);
        pie.DataTable = new ChartDataTableSettings();
        var pieFrame = ChartRenderPlanner.BuildFramePlan(pie, new ChartPlanRect(0, 0, 400, 300));

        ChartRenderPlanner.BuildDataTablePrimitivePlan(pie, pieFrame).Cells.Should().BeEmpty();

        var column = MakeTwoSeriesChart(ChartType.ColumnClustered);
        column.DataTable = new ChartDataTableSettings
        {
            ShowHorizontalBorder = false,
            ShowVerticalBorder = false,
            ShowOutlineBorder = false,
        };
        var columnFrame = ChartRenderPlanner.BuildFramePlan(column, new ChartPlanRect(0, 0, 400, 300));

        var plan = ChartRenderPlanner.BuildDataTablePrimitivePlan(column, columnFrame);

        plan.Cells.Should().NotBeEmpty();
        plan.HorizontalBorders.Should().BeEmpty();
        plan.VerticalBorders.Should().BeEmpty();
        plan.OutlineBorders.Should().BeEmpty();
    }

    [Fact]
    public void BuildSecondaryValueAxisPrimitivePlan_PlansRightTicksLabelsAndClockwiseTitle()
    {
        var chart = MakeSecondaryAxisChart();
        chart.SecondaryValueAxis!.Title = "Margin";
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var plan = ChartRenderPlanner.BuildSecondaryValueAxisPrimitivePlan(chart, frame);

        plan.Labels.Should().HaveCount(6);
        plan.Ticks.Should().HaveCount(6);
        plan.Labels[0].Text.Should().Be("0");
        plan.Labels[0].Bounds.Should().Be(new ChartPlanRect(342, 270, 34, 12));
        plan.Labels[4].Text.Should().Be("1000K");
        plan.Ticks[0].Start.Should().Be(new ChartPlanPoint(frame.Plot.Right, frame.Plot.Bottom));
        plan.Ticks[0].End.Should().Be(new ChartPlanPoint(frame.Plot.Right + ChartRenderPlanner.AxisMajorTickLength, frame.Plot.Bottom));
        plan.Ticks[^1].Start.Should().Be(new ChartPlanPoint(frame.Plot.Right, frame.Plot.Y));
        plan.TickStroke.Should().Be(new ChartStrokePlan(
            new SrgbColor(0x7F, 0x7F, 0x7F),
            Alpha: 255,
            Thickness: 0.75));
        plan.Title.Should().NotBeNull();
        plan.Title!.Value.Label.Text.Should().Be("Margin");
        plan.Title.Value.Label.Bounds.Should().Be(new ChartPlanRect(378, 8, 14, 268));
        plan.Title.Value.Orientation.Should().Be(ChartAxisTitleOrientation.VerticalClockwise);
    }

    [Fact]
    public void BuildSecondaryValueAxisPrimitivePlan_DeletedAxisAndUnsupportedFamiliesReturnEmptyGeometry()
    {
        var chart = MakeSecondaryAxisChart();
        chart.SecondaryValueAxis!.Delete = true;
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var deletedPlan = ChartRenderPlanner.BuildSecondaryValueAxisPrimitivePlan(chart, frame);
        var barChart = MakeSecondaryAxisChart();
        barChart.ChartType = ChartType.BarClustered;
        var barPlan = ChartRenderPlanner.BuildSecondaryValueAxisPrimitivePlan(
            barChart,
            ChartRenderPlanner.BuildFramePlan(barChart, new ChartPlanRect(0, 0, 400, 300)));

        deletedPlan.Labels.Should().BeEmpty();
        deletedPlan.Ticks.Should().BeEmpty();
        deletedPlan.Title.Should().BeNull();
        barPlan.Labels.Should().BeEmpty();
        barPlan.Ticks.Should().BeEmpty();
        barPlan.Title.Should().BeNull();
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
    public void BuildLegendItemPlans_VaryColorPie_UsesPerPointFillPlans()
    {
        var series = new ChartSeries { Name = "Share" };
        series.Values.AddRange(new double?[] { 1, 2 });
        var chart = new ChartShape
        {
            ChartType = ChartType.Pie,
            Legend = LegendPosition.Right,
            VaryColors = true
        };
        chart.Series.Add(series);
        var first = new SrgbColor(0x10, 0x20, 0x30);
        var second = new SrgbColor(0x40, 0x50, 0x60);
        var fills = new ChartFillPlanSet
        {
            PointFills = new Dictionary<ChartFillKey, ChartFillPlan>
            {
                [new ChartFillKey(0, 0)] = new ChartFillPlan(first, 255),
                [new ChartFillKey(0, 1)] = new ChartFillPlan(second, 255)
            }
        };
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var items = ChartRenderPlanner.BuildLegendItemPlans(chart, frame, new[] { first, second }, fills);

        items.Select(item => item.Fill.Color).Should().Equal(first, second);
    }

    [Fact]
    public void BuildFramePlan_ManualPlotLayout_UsesBoundedFactorRectangle()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.PlotAreaManualLayout = new ChartManualLayout
        {
            LayoutTarget = "inner",
            X = 0.20,
            Y = 0.15,
            Width = 0.50,
            Height = 0.40
        };

        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        frame.Plot.Should().Be(new ChartPlanRect(80, 45, 200, 120));
    }

    [Fact]
    public void BuildFramePlan_ManualPlotLayout_UsesEdgeRightAndBottomCoordinates()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.PlotAreaManualLayout = new ChartManualLayout
        {
            LayoutTarget = "inner",
            XMode = ChartManualLayoutMode.Edge,
            YMode = ChartManualLayoutMode.Edge,
            WidthMode = ChartManualLayoutMode.Edge,
            HeightMode = ChartManualLayoutMode.Edge,
            X = 0.10,
            Y = 0.20,
            Width = 0.90,
            Height = 0.75
        };

        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        frame.Plot.Should().Be(new ChartPlanRect(40, 60, 320, 165));
    }

    [Fact]
    public void BuildFramePlan_ManualPlotLayout_MixesFactorSizesWithEdgeBounds()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.PlotAreaManualLayout = new ChartManualLayout
        {
            XMode = ChartManualLayoutMode.Edge,
            YMode = ChartManualLayoutMode.Factor,
            WidthMode = ChartManualLayoutMode.Factor,
            HeightMode = ChartManualLayoutMode.Edge,
            X = 0.10,
            Y = 0.20,
            Width = 0.50,
            Height = 0.80
        };

        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        frame.Plot.Should().Be(new ChartPlanRect(40, 60, 200, 180));
    }

    [Fact]
    public void BuildFramePlan_ManualPlotLayout_ClampsEdgeCoordinatesToParent()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.PlotAreaManualLayout = new ChartManualLayout
        {
            XMode = ChartManualLayoutMode.Edge,
            YMode = ChartManualLayoutMode.Edge,
            WidthMode = ChartManualLayoutMode.Edge,
            HeightMode = ChartManualLayoutMode.Edge,
            X = -0.25,
            Y = 0.10,
            Width = 1.25,
            Height = 0.60
        };

        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        frame.Plot.Should().Be(new ChartPlanRect(0, 30, 400, 150));
    }

    [Fact]
    public void BuildFramePlan_LegendOverlay_DoesNotReservePlotArea()
    {
        var reserving = MakeTwoSeriesChart(ChartType.ColumnClustered);
        reserving.Legend = LegendPosition.Right;
        var overlay = MakeTwoSeriesChart(ChartType.ColumnClustered);
        overlay.Legend = LegendPosition.Right;
        overlay.LegendOverlay = true;

        var reservingFrame = ChartRenderPlanner.BuildFramePlan(reserving, new ChartPlanRect(0, 0, 400, 300));
        var overlayFrame = ChartRenderPlanner.BuildFramePlan(overlay, new ChartPlanRect(0, 0, 400, 300));

        reservingFrame.Plot.Should().Be(new ChartPlanRect(48, 8, 264, 268));
        overlayFrame.LegendAreaWidth.Should().Be(0);
        overlayFrame.Plot.Should().Be(new ChartPlanRect(48, 8, 344, 268));
    }

    [Fact]
    public void BuildLegendItemPlans_ManualLegendLayout_DrivesItemPlacement()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.Legend = LegendPosition.Right;
        chart.LegendOverlay = true;
        chart.LegendManualLayout = new ChartManualLayout
        {
            X = 0.55,
            Y = 0.20,
            Width = 0.30,
            Height = 0.25
        };
        var colors = new[]
        {
            new SrgbColor(0x11, 0x22, 0x33),
            new SrgbColor(0x44, 0x55, 0x66)
        };
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var items = ChartRenderPlanner.BuildLegendItemPlans(chart, frame, colors);

        items.Should().HaveCount(2);
        items[0].SwatchBounds.X.Should().BeApproximately(220, 0.0001);
        items[0].SwatchBounds.Y.Should().BeApproximately(63, 0.0001);
        items[0].SwatchBounds.Width.Should().Be(8);
        items[0].SwatchBounds.Height.Should().Be(8);
        items[0].Label.Bounds.X.Should().BeApproximately(230, 0.0001);
        items[0].Label.Bounds.Y.Should().BeApproximately(60, 0.0001);
        items[0].Label.Bounds.Width.Should().BeApproximately(110, 0.0001);
        items[0].Label.Bounds.Height.Should().Be(ChartRenderPlanner.LegendHeight);
        items[1].SwatchBounds.X.Should().BeApproximately(220, 0.0001);
        items[1].SwatchBounds.Y.Should().BeApproximately(77, 0.0001);
        items[1].Label.Text.Should().Be("Forecast");
    }

    [Fact]
    public void BuildLegendItemPlans_ManualLegendEdgeLayout_DrivesItemPlacement()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.Legend = LegendPosition.Right;
        chart.LegendOverlay = true;
        chart.LegendManualLayout = new ChartManualLayout
        {
            XMode = ChartManualLayoutMode.Edge,
            YMode = ChartManualLayoutMode.Edge,
            WidthMode = ChartManualLayoutMode.Edge,
            HeightMode = ChartManualLayoutMode.Edge,
            X = 0.70,
            Y = 0.10,
            Width = 0.95,
            Height = 0.45
        };
        var colors = new[]
        {
            new SrgbColor(0x11, 0x22, 0x33),
            new SrgbColor(0x44, 0x55, 0x66)
        };
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var items = ChartRenderPlanner.BuildLegendItemPlans(chart, frame, colors);

        items.Should().HaveCount(2);
        items[0].SwatchBounds.Should().Be(new ChartPlanRect(280, 33, 8, 8));
        items[0].Label.Bounds.Should().Be(new ChartPlanRect(290, 30, 90, ChartRenderPlanner.LegendHeight));
        items[1].SwatchBounds.Should().Be(new ChartPlanRect(280, 47, 8, 8));
        items[1].Label.Bounds.Should().Be(new ChartPlanRect(290, 44, 90, ChartRenderPlanner.LegendHeight));
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
    public void ResolveBarClusterSpacing_DefaultMatchesExistingPowerPointClusterGeometry()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);

        var slot = ChartRenderPlanner.ResolveBarClusterSpacing(
            chart,
            categorySize: 100,
            seriesCount: 2,
            stacked: false);

        slot.CategoryStart.Should().BeApproximately(30, 0.0001);
        slot.ClusterSize.Should().BeApproximately(40, 0.0001);
        slot.SeriesSize.Should().BeApproximately(20, 0.0001);
        slot.SeriesStep.Should().BeApproximately(20, 0.0001);
    }

    [Fact]
    public void BuildColumnPrimitives_UsesAuthoredGapWidthAndOverlap()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.BarGapWidthPercent = 0;
        chart.BarOverlapPercent = 50;

        var primitives = ChartRenderPlanner.BuildColumnPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        var first = primitives.Single(p => p.SeriesIndex == 0 && p.CategoryIndex == 0);
        var second = primitives.Single(p => p.SeriesIndex == 1 && p.CategoryIndex == 0);
        first.Bounds.X.Should().BeApproximately(0, 0.0001);
        first.Bounds.Width.Should().BeApproximately(65.6667, 0.0001);
        second.Bounds.X.Should().BeApproximately(33.3333, 0.0001);
        second.Bounds.Width.Should().BeApproximately(65.6667, 0.0001);
        second.Bounds.X.Should().BeLessThan(first.Bounds.Right, "positive overlap draws clustered series into the same category band");
    }

    [Fact]
    public void BuildColumnPrimitives_UsesAuthoredGapDepthAsRendererNeutralDepthOffset()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.BarGapDepthPercent = 250;

        var primitives = ChartRenderPlanner.BuildColumnPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        var first = primitives.Single(p => p.SeriesIndex == 0 && p.CategoryIndex == 0);
        var second = primitives.Single(p => p.SeriesIndex == 1 && p.CategoryIndex == 0);
        first.Depth.Should().Be(new ChartBarDepthPlan(
            GapDepthPercent: 250,
            OffsetX: 1.25,
            OffsetY: -1.25,
            IsHorizontalBar: false,
            IsStacked: false));
        second.Depth.Should().Be(new ChartBarDepthPlan(
            GapDepthPercent: 250,
            OffsetX: 3.75,
            OffsetY: -3.75,
            IsHorizontalBar: false,
            IsStacked: false));
        first.Bounds.X.Should().BeApproximately(31.25, 0.0001);
        first.Bounds.Y.Should().BeApproximately(78.75, 0.0001);
        second.Bounds.X.Should().BeApproximately(53.75, 0.0001);
        second.Bounds.Y.Should().BeApproximately(36.25, 0.0001);
    }

    [Fact]
    public void BuildBarGapDepthPlan_ClampsAuthoredDepthAndPreservesOrientationContract()
    {
        var chart = MakeTwoSeriesChart(ChartType.BarClustered);
        chart.BarGapDepthPercent = 650;

        var plan = ChartRenderPlanner.BuildBarGapDepthPlan(
            chart,
            categorySize: 100,
            seriesIndex: 1,
            seriesCount: 2,
            isHorizontalBar: true,
            stacked: false);

        plan.Should().Be(new ChartBarDepthPlan(
            GapDepthPercent: 500,
            OffsetX: 7.5,
            OffsetY: -7.5,
            IsHorizontalBar: true,
            IsStacked: false));
    }

    [Fact]
    public void BuildColumnPrimitives_HundredPercentStackedNormalizesEachCategory()
    {
        var chart = new ChartShape { ChartType = ChartType.ColumnStacked100 };
        chart.Categories.Add("Q1");
        var first = new ChartSeries { Name = "Actual" };
        first.Values.Add(20);
        var second = new ChartSeries { Name = "Forecast" };
        second.Values.Add(30);
        chart.Series.Add(first);
        chart.Series.Add(second);

        var primitives = ChartRenderPlanner.BuildColumnPrimitives(
            chart,
            new ChartPlanRect(0, 0, 100, 100));

        var actual = primitives.Single(p => p.SeriesIndex == 0);
        var forecast = primitives.Single(p => p.SeriesIndex == 1);
        actual.Bounds.Should().Be(new ChartPlanRect(30, 60, 40, 40));
        forecast.Bounds.Should().Be(new ChartPlanRect(30, 0, 40, 60));
    }

    [Fact]
    public void BuildColumnPrimitives_DisplayBlanksAsZero_MaterializesZeroHeightBlankPoint()
    {
        var series = new ChartSeries { Name = "Actual" };
        series.Values.AddRange(new double?[] { 10, null, 30 });
        var chart = new ChartShape
        {
            ChartType = ChartType.ColumnClustered,
            DisplayBlanksAs = ChartDisplayBlanksAs.Zero
        };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });
        chart.Series.Add(series);

        var primitives = ChartRenderPlanner.BuildColumnPrimitives(
            chart,
            new ChartPlanRect(0, 0, 300, 100));

        primitives.Should().HaveCount(3);
        var blank = primitives.Single(p => p.CategoryIndex == 1);
        blank.SeriesIndex.Should().Be(0);
        blank.Bounds.X.Should().BeApproximately(130, 0.0001);
        blank.Bounds.Y.Should().BeApproximately(100, 0.0001);
        blank.Bounds.Height.Should().BeApproximately(0.5, 0.0001);
    }

    [Fact]
    public void BuildColumnPrimitives_UsesPointGradientFillPlan()
    {
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(new[] { "Q1", "Q2" });
        var series = new ChartSeries { Name = "Sales" };
        series.Values.AddRange(new double?[] { 10, 20 });
        chart.Series.Add(series);

        var gradient = new ResolvedFill.Gradient(
            new SrgbColor(0x10, 0x20, 0x30),
            new SrgbColor(0xD0, 0xE0, 0xF0),
            angleDegrees: 45);
        var fillPlans = new ChartFillPlanSet
        {
            PointFills = new Dictionary<ChartFillKey, ChartFillPlan>
            {
                [new ChartFillKey(0, 1)] = new ChartFillPlan(new SrgbColor(0x10, 0x20, 0x30), 255)
                {
                    Fill = gradient
                }
            }
        };

        var primitives = ChartRenderPlanner.BuildColumnPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100),
            new[] { new SrgbColor(0x40, 0x50, 0x60) },
            fillPlans);

        var q2 = primitives.Single(p => p.SeriesIndex == 0 && p.CategoryIndex == 1);
        q2.Fill.Fill.Should().BeSameAs(gradient);
    }

    [Fact]
    public void BuildColumnPrimitives_UsesPointPatternFillPlan()
    {
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(new[] { "Q1", "Q2" });
        var series = new ChartSeries { Name = "Sales" };
        series.Values.AddRange(new double?[] { 10, 20 });
        chart.Series.Add(series);

        var pattern = new ResolvedFill.PatternFill(
            "diagStripe",
            new SrgbColor(0x10, 0x20, 0x30),
            new SrgbColor(0xE0, 0xE1, 0xE2));
        var fillPlans = new ChartFillPlanSet
        {
            PointFills = new Dictionary<ChartFillKey, ChartFillPlan>
            {
                [new ChartFillKey(0, 1)] = new ChartFillPlan(new SrgbColor(0x10, 0x20, 0x30), 255)
                {
                    Fill = pattern
                }
            }
        };

        var primitives = ChartRenderPlanner.BuildColumnPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100),
            new[] { new SrgbColor(0x40, 0x50, 0x60) },
            fillPlans);

        var q2 = primitives.Single(p => p.SeriesIndex == 0 && p.CategoryIndex == 1);
        q2.Fill.Fill.Should().BeSameAs(pattern);
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
    public void BuildBarPrimitives_UsesAuthoredGapWidthAndOverlap()
    {
        var chart = MakeTwoSeriesChart(ChartType.BarClustered);
        chart.BarGapWidthPercent = 300;
        chart.BarOverlapPercent = -100;

        var primitives = ChartRenderPlanner.BuildBarPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        var first = primitives.Single(p => p.SeriesIndex == 0 && p.CategoryIndex == 0);
        var second = primitives.Single(p => p.SeriesIndex == 1 && p.CategoryIndex == 0);
        first.Bounds.Y.Should().BeApproximately(77.0833, 0.0001);
        first.Bounds.Height.Should().BeApproximately(3.1667, 0.0001);
        second.Bounds.Y.Should().BeApproximately(68.75, 0.0001);
        second.Bounds.Height.Should().BeApproximately(3.1667, 0.0001);
        second.Bounds.Bottom.Should().BeLessThan(first.Bounds.Y, "negative overlap leaves a visible gap between series bars");
    }

    [Fact]
    public void BuildBarPrimitives_UsesAuthoredGapDepthAsRendererNeutralDepthOffset()
    {
        var chart = MakeTwoSeriesChart(ChartType.BarClustered);
        chart.BarGapDepthPercent = 250;

        var primitives = ChartRenderPlanner.BuildBarPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        var first = primitives.Single(p => p.SeriesIndex == 0 && p.CategoryIndex == 0);
        var second = primitives.Single(p => p.SeriesIndex == 1 && p.CategoryIndex == 0);
        first.Depth.Should().Be(new ChartBarDepthPlan(
            GapDepthPercent: 250,
            OffsetX: 1.125,
            OffsetY: -1.125,
            IsHorizontalBar: true,
            IsStacked: false));
        second.Depth.Should().Be(new ChartBarDepthPlan(
            GapDepthPercent: 250,
            OffsetX: 3.375,
            OffsetY: -3.375,
            IsHorizontalBar: true,
            IsStacked: false));
        first.Bounds.X.Should().BeApproximately(1.125, 0.0001);
        first.Bounds.Y.Should().BeApproximately(73.875, 0.0001);
        second.Bounds.X.Should().BeApproximately(3.375, 0.0001);
        second.Bounds.Y.Should().BeApproximately(61.625, 0.0001);
    }

    [Fact]
    public void BuildBarPrimitives_HundredPercentStackedNormalizesEachCategory()
    {
        var chart = new ChartShape { ChartType = ChartType.BarStacked100 };
        chart.Categories.Add("Q1");
        var first = new ChartSeries { Name = "Actual" };
        first.Values.Add(20);
        var second = new ChartSeries { Name = "Forecast" };
        second.Values.Add(30);
        chart.Series.Add(first);
        chart.Series.Add(second);

        var primitives = ChartRenderPlanner.BuildBarPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        var actual = primitives.Single(p => p.SeriesIndex == 0);
        var forecast = primitives.Single(p => p.SeriesIndex == 1);
        actual.Bounds.Should().Be(new ChartPlanRect(0, 30, 80, 40));
        forecast.Bounds.Should().Be(new ChartPlanRect(80, 30, 120, 40));
    }

    [Fact]
    public void BuildBarPrimitives_DisplayBlanksAsZero_MaterializesZeroWidthBlankPoint()
    {
        var series = new ChartSeries { Name = "Actual" };
        series.Values.AddRange(new double?[] { 10, null, 30 });
        var chart = new ChartShape
        {
            ChartType = ChartType.BarClustered,
            DisplayBlanksAs = ChartDisplayBlanksAs.Zero
        };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });
        chart.Series.Add(series);

        var primitives = ChartRenderPlanner.BuildBarPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 120));

        primitives.Should().HaveCount(3);
        var blank = primitives.Single(p => p.CategoryIndex == 1);
        blank.SeriesIndex.Should().Be(0);
        blank.Bounds.X.Should().Be(0);
        blank.Bounds.Y.Should().BeApproximately(52, 0.0001);
        blank.Bounds.Width.Should().BeApproximately(0.5, 0.0001);
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
    public void BuildLineSeriesPrimitives_DisplayBlanksAsSpan_ConnectsAcrossBlankPoint()
    {
        var series = new ChartSeries { Name = "Line" };
        series.Values.AddRange(new double?[] { 10, null, 30 });
        var chart = new ChartShape
        {
            ChartType = ChartType.LineMarkers,
            DisplayBlanksAs = ChartDisplayBlanksAs.Span
        };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });
        chart.Series.Add(series);

        var primitive = ChartRenderPlanner.BuildLineSeriesPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100),
            withMarkers: true).Single();

        primitive.Points[0].Should().Be(new ChartPlanPoint(0, 75));
        primitive.Points[1].Should().BeNull();
        primitive.Points[2].Should().Be(new ChartPlanPoint(200, 25));
        primitive.LineSegments.Should().ContainSingle();
        primitive.LineSegments[0].StartPointIndex.Should().Be(0);
        primitive.LineSegments[0].EndPointIndex.Should().Be(2);
        primitive.Markers.Select(marker => marker.PointIndex).Should().Equal(0, 2);
    }

    [Fact]
    public void BuildLineSeriesPrimitives_DisplayBlanksAsZero_PlansZeroPointAndAdjacentSegments()
    {
        var series = new ChartSeries { Name = "Line" };
        series.Values.AddRange(new double?[] { 10, null, 30 });
        var chart = new ChartShape
        {
            ChartType = ChartType.LineMarkers,
            DisplayBlanksAs = ChartDisplayBlanksAs.Zero
        };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });
        chart.Series.Add(series);

        var primitive = ChartRenderPlanner.BuildLineSeriesPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100),
            withMarkers: true).Single();

        primitive.Points[1].Should().Be(new ChartPlanPoint(100, 100));
        primitive.LineSegments.Should().HaveCount(2);
        primitive.LineSegments.Select(segment => (segment.StartPointIndex, segment.EndPointIndex))
            .Should()
            .Equal((0, 1), (1, 2));
        primitive.Markers.Select(marker => marker.PointIndex).Should().Equal(0, 1, 2);
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
        primitive.LinePaths.Should().ContainSingle();
        primitive.LinePaths[0].Segments.Select(segment => segment.Kind)
            .Should()
            .Equal(ChartLinePathSegmentKind.Line, ChartLinePathSegmentKind.Line);
        primitive.Markers.Should().HaveCount(3);
        primitive.Markers[0].Fill.Should().Be(new ChartFillPlan(
            seriesColor,
            ChartRenderPlanner.RectSeriesFillAlpha));
        primitive.Markers[0].Stroke.Should().Be(new ChartStrokePlan(
            seriesColor,
            Alpha: 255,
            Thickness: ChartRenderPlanner.LineMarkerStrokeThickness));
        primitive.IsSmoothed.Should().BeFalse();
    }

    [Fact]
    public void BuildLineSeriesPrimitives_CarriesAuthoredSmoothLineDecision()
    {
        var series = new ChartSeries
        {
            Name = "Smoothed",
            SmoothLine = true
        };
        series.Values.AddRange(new double?[] { 10, 20, 30 });
        var chart = new ChartShape { ChartType = ChartType.Line };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });
        chart.Series.Add(series);

        var primitive = ChartRenderPlanner.BuildLineSeriesPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100),
            withMarkers: false).Single();

        primitive.IsSmoothed.Should().BeTrue();
    }

    [Fact]
    public void BuildLineSeriesPrimitives_SmoothedSeriesPlansCubicPath()
    {
        var series = new ChartSeries
        {
            Name = "Smoothed",
            SmoothLine = true
        };
        series.Values.AddRange(new double?[] { 10, 20, 30 });
        var chart = new ChartShape { ChartType = ChartType.Line };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });
        chart.Series.Add(series);

        var primitive = ChartRenderPlanner.BuildLineSeriesPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100),
            withMarkers: false).Single();

        primitive.LineSegments.Should().HaveCount(2);
        primitive.LinePaths.Should().ContainSingle();
        var path = primitive.LinePaths[0];
        path.Start.Should().Be(new ChartPlanPoint(0, 75));
        path.Stroke.Should().Be(primitive.Stroke);
        path.Segments.Select(segment => segment.Kind)
            .Should()
            .Equal(ChartLinePathSegmentKind.CubicBezier, ChartLinePathSegmentKind.CubicBezier);

        path.Segments[0].Control1.X.Should().BeApproximately(16.6667, 0.0001);
        path.Segments[0].Control1.Y.Should().BeApproximately(70.8333, 0.0001);
        path.Segments[0].Control2.X.Should().BeApproximately(66.6667, 0.0001);
        path.Segments[0].Control2.Y.Should().BeApproximately(58.3333, 0.0001);
        path.Segments[0].End.Should().Be(new ChartPlanPoint(100, 50));

        path.Segments[1].Control1.X.Should().BeApproximately(133.3333, 0.0001);
        path.Segments[1].Control1.Y.Should().BeApproximately(41.6667, 0.0001);
        path.Segments[1].Control2.X.Should().BeApproximately(183.3333, 0.0001);
        path.Segments[1].Control2.Y.Should().BeApproximately(29.1667, 0.0001);
        path.Segments[1].End.Should().Be(new ChartPlanPoint(200, 25));
    }

    [Fact]
    public void BuildLineSeriesPrimitives_SmoothedSeriesKeepsBlankGapsAsSeparateFigures()
    {
        var series = new ChartSeries
        {
            Name = "Smoothed",
            SmoothLine = true
        };
        series.Values.AddRange(new double?[] { 10, 20, null, 40, 50 });
        var chart = new ChartShape { ChartType = ChartType.Line };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3", "Q4", "Q5" });
        chart.Series.Add(series);

        var primitive = ChartRenderPlanner.BuildLineSeriesPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100),
            withMarkers: false).Single();

        primitive.LineSegments.Should().HaveCount(2);
        primitive.LinePaths.Should().HaveCount(2);
        primitive.LinePaths.Select(path => path.Segments.Single().Kind)
            .Should()
            .Equal(ChartLinePathSegmentKind.Line, ChartLinePathSegmentKind.Line);
    }

    [Fact]
    public void BuildLineSeriesPrimitives_UsesAuthoredSeriesAndPointMarkerStyle()
    {
        var series = new ChartSeries
        {
            Name = "Styled",
            FillColor = new ThemeAwareColor(new SrgbColor(0x22, 0x44, 0x66)),
            LineStyle = new ChartLineStyle
            {
                Color = new ThemeAwareColor(new SrgbColor(0x11, 0x22, 0x33)),
                WidthPt = 2.25,
                Dash = OutlineDash.DashDot
            },
            MarkerStyle = new ChartMarkerStyle
            {
                Symbol = ChartMarkerSymbol.Diamond,
                SizePt = 9,
                FillColor = new ThemeAwareColor(new SrgbColor(0xAA, 0xBB, 0xCC)),
                StrokeColor = new ThemeAwareColor(new SrgbColor(0x01, 0x02, 0x03)),
                StrokeWidthPt = 1.5
            }
        };
        series.Values.AddRange(new double?[] { 10, 20 });
        series.PointStyles[1] = new ChartPointStyle
        {
            FillColor = new ThemeAwareColor(new SrgbColor(0xEE, 0xDD, 0xCC)),
            StrokeColor = new ThemeAwareColor(new SrgbColor(0x44, 0x55, 0x66)),
            StrokeWidthPt = 3,
            Marker = new ChartMarkerStyle
            {
                Symbol = ChartMarkerSymbol.Square,
                SizePt = 12
            }
        };
        var chart = new ChartShape { ChartType = ChartType.LineMarkers };
        chart.Categories.AddRange(new[] { "Q1", "Q2" });
        chart.Series.Add(series);

        var primitive = ChartRenderPlanner.BuildLineSeriesPrimitives(
            chart,
            new ChartPlanRect(0, 0, 100, 100),
            withMarkers: true).Single();

        primitive.Stroke.Should().Be(new ChartStrokePlan(new SrgbColor(0x11, 0x22, 0x33), Alpha: 255, Thickness: 3.0, Dash: OutlineDash.DashDot));
        primitive.LineSegments[0].Stroke.Should().Be(primitive.Stroke);
        primitive.MarkerRadius.Should().BeApproximately(6.0, 0.0001);
        primitive.MarkerFill.Should().Be(new ChartFillPlan(new SrgbColor(0xAA, 0xBB, 0xCC), Alpha: 255));
        primitive.MarkerStroke.Should().Be(new ChartStrokePlan(new SrgbColor(0x01, 0x02, 0x03), Alpha: 255, Thickness: 2.0));
        primitive.Markers[0].Symbol.Should().Be(ChartMarkerPrimitiveSymbol.Diamond);
        primitive.Markers[0].Radius.Should().BeApproximately(6.0, 0.0001);
        primitive.Markers[0].Fill.Should().Be(new ChartFillPlan(new SrgbColor(0xAA, 0xBB, 0xCC), Alpha: 255));
        primitive.Markers[1].Symbol.Should().Be(ChartMarkerPrimitiveSymbol.Square);
        primitive.Markers[1].Radius.Should().BeApproximately(8.0, 0.0001);
        primitive.Markers[1].Fill.Should().Be(new ChartFillPlan(new SrgbColor(0xEE, 0xDD, 0xCC), Alpha: 255));
        primitive.Markers[1].Stroke.Should().Be(new ChartStrokePlan(new SrgbColor(0x44, 0x55, 0x66), Alpha: 255, Thickness: 4.0));
    }

    [Fact]
    public void BuildLineSeriesPrimitives_ClassicThreeDLineCarriesSharedDepthPlan()
    {
        var series = new ChartSeries { Name = "Line" };
        series.Values.AddRange(new double?[] { 10, 20, 30 });
        var chart = new ChartShape
        {
            ChartType = ChartType.LineMarkers,
            ThreeDStyle = ChartThreeDStyle.Line
        };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });
        chart.Series.Add(series);

        var primitive = ChartRenderPlanner.BuildLineSeriesPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100),
            withMarkers: true).Single();

        primitive.Depth.Should().Be(new ChartClassicThreeDDepthPlan(
            OffsetX: 4.5,
            OffsetY: -4.5,
            StrokeAlpha: 120,
            FillAlpha: 70));
        primitive.LineSegments.Should().HaveCount(2);
        primitive.LineSegments[0].Start.Should().Be(new ChartPlanPoint(0, 75));
        primitive.LineSegments[0].End.Should().Be(new ChartPlanPoint(100, 50));
    }

    [Fact]
    public void BuildLineSeriesPrimitives_TwoDimensionalLineDoesNotCarryDepthPlan()
    {
        var series = new ChartSeries { Name = "Line" };
        series.Values.AddRange(new double?[] { 10, 20 });
        var chart = new ChartShape { ChartType = ChartType.LineMarkers };
        chart.Categories.AddRange(new[] { "Q1", "Q2" });
        chart.Series.Add(series);

        var primitive = ChartRenderPlanner.BuildLineSeriesPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100),
            withMarkers: true).Single();

        primitive.Depth.Should().BeNull();
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
    public void BuildAreaSeriesPrimitives_StackedAreaUsesPriorSeriesAsBaseline()
    {
        var chart = new ChartShape { ChartType = ChartType.AreaStacked };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });

        var first = new ChartSeries { Name = "Actual" };
        first.Values.AddRange(new double?[] { 20, 40, 60 });
        chart.Series.Add(first);

        var second = new ChartSeries { Name = "Forecast" };
        second.Values.AddRange(new double?[] { 10, 20, 30 });
        chart.Series.Add(second);

        var primitives = ChartRenderPlanner.BuildAreaSeriesPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        primitives.Should().HaveCount(2);
        primitives[0].SeriesIndex.Should().Be(1);
        primitives[0].BaselineStart.Should().Be(new ChartPlanPoint(0, 80));
        primitives[0].BaselineEnd.Should().Be(new ChartPlanPoint(200, 40));
        primitives[0].Points.Should().Equal(
            new ChartPlanPoint(0, 70),
            new ChartPlanPoint(100, 40),
            new ChartPlanPoint(200, 10));
        primitives[0].AreaPath.Points.Should().Equal(
            new ChartPlanPoint(0, 80),
            new ChartPlanPoint(0, 70),
            new ChartPlanPoint(100, 40),
            new ChartPlanPoint(200, 10),
            new ChartPlanPoint(200, 40),
            new ChartPlanPoint(100, 60));

        primitives[1].SeriesIndex.Should().Be(0);
        primitives[1].BaselineStart.Should().Be(new ChartPlanPoint(0, 100));
        primitives[1].BaselineEnd.Should().Be(new ChartPlanPoint(200, 100));
        primitives[1].Points.Should().Equal(
            new ChartPlanPoint(0, 80),
            new ChartPlanPoint(100, 60),
            new ChartPlanPoint(200, 40));
    }

    [Fact]
    public void BuildAreaSeriesPrimitives_DisplayBlanksAsGap_SplitsFilledAreaAtBlankPoint()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Area,
            DisplayBlanksAs = ChartDisplayBlanksAs.Gap
        };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });
        var series = new ChartSeries { Name = "Actual" };
        series.Values.AddRange(new double?[] { 10, null, 30 });
        chart.Series.Add(series);

        var primitives = ChartRenderPlanner.BuildAreaSeriesPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        primitives.Should().HaveCount(2);
        primitives[0].Points.Should().Equal(new ChartPlanPoint(0, 75));
        primitives[0].AreaPath.Points.Should().Equal(
            new ChartPlanPoint(0, 100),
            new ChartPlanPoint(0, 75),
            new ChartPlanPoint(0, 100));
        primitives[1].Points.Should().Equal(new ChartPlanPoint(200, 25));
        primitives[1].AreaPath.Points.Should().Equal(
            new ChartPlanPoint(200, 100),
            new ChartPlanPoint(200, 25),
            new ChartPlanPoint(200, 100));
    }

    [Fact]
    public void BuildAreaSeriesPrimitives_DisplayBlanksAsZero_PlansZeroPointInSingleFilledArea()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Area,
            DisplayBlanksAs = ChartDisplayBlanksAs.Zero
        };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });
        var series = new ChartSeries { Name = "Actual" };
        series.Values.AddRange(new double?[] { 10, null, 30 });
        chart.Series.Add(series);

        var primitive = ChartRenderPlanner.BuildAreaSeriesPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100)).Single();

        primitive.Points.Should().Equal(
            new ChartPlanPoint(0, 75),
            new ChartPlanPoint(100, 100),
            new ChartPlanPoint(200, 25));
        primitive.AreaPath.Points.Should().Equal(
            new ChartPlanPoint(0, 100),
            new ChartPlanPoint(0, 75),
            new ChartPlanPoint(100, 100),
            new ChartPlanPoint(200, 25),
            new ChartPlanPoint(200, 100));
    }

    [Fact]
    public void BuildAreaSeriesPrimitives_ClassicThreeDAreaCarriesSharedDepthPlan()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Area,
            ThreeDStyle = ChartThreeDStyle.Area
        };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });
        var series = new ChartSeries { Name = "Actual" };
        series.Values.AddRange(new double?[] { 10, 20, 30 });
        chart.Series.Add(series);

        var primitive = ChartRenderPlanner.BuildAreaSeriesPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100)).Single();

        primitive.Depth.Should().Be(new ChartClassicThreeDDepthPlan(
            OffsetX: 4.5,
            OffsetY: -4.5,
            StrokeAlpha: 120,
            FillAlpha: 70));
        primitive.AreaPath.Points.Should().Equal(
            new ChartPlanPoint(0, 100),
            new ChartPlanPoint(0, 75),
            new ChartPlanPoint(100, 50),
            new ChartPlanPoint(200, 25),
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
    public void BuildScatterPrimitivePlan_DisplayBlanksAsSpan_ConnectsAcrossBlankYPoint()
    {
        var series = new ChartSeries { Name = "XY" };
        series.XValues.AddRange(new double?[] { 0, 50, 100 });
        series.Values.AddRange(new double?[] { 10, null, 30 });
        var chart = new ChartShape
        {
            ChartType = ChartType.Scatter,
            ScatterStyle = ScatterStyle.LineMarker,
            DisplayBlanksAs = ChartDisplayBlanksAs.Span
        };
        chart.Series.Add(series);

        var plan = ChartRenderPlanner.BuildScatterPrimitivePlan(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        var primitive = plan.Series.Single();
        primitive.Points[1].Should().BeNull();
        primitive.LineSegments.Should().ContainSingle();
        primitive.LineSegments[0].StartPointIndex.Should().Be(0);
        primitive.LineSegments[0].EndPointIndex.Should().Be(2);
        primitive.Markers.Select(marker => marker.PointIndex).Should().Equal(0, 2);
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
    public void BuildScatterPrimitivePlan_SmoothStylePlansCubicPath()
    {
        var series = new ChartSeries { Name = "XY" };
        series.XValues.AddRange(new double?[] { 0, 50, 100 });
        series.Values.AddRange(new double?[] { 10, 20, 30 });
        var chart = new ChartShape
        {
            ChartType = ChartType.Scatter,
            ScatterStyle = ScatterStyle.SmoothMarker
        };
        chart.Series.Add(series);

        var plan = ChartRenderPlanner.BuildScatterPrimitivePlan(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        var primitive = plan.Series.Single();
        primitive.IsSmoothed.Should().BeTrue();
        primitive.DrawLines.Should().BeTrue();
        primitive.DrawMarkers.Should().BeTrue();
        primitive.LineSegments.Should().HaveCount(2);
        primitive.LinePaths.Should().ContainSingle();
        var path = primitive.LinePaths[0];
        path.Start.Should().Be(new ChartPlanPoint(0, 75));
        path.Stroke.Should().Be(primitive.LineSegments[0].Stroke);
        path.Segments.Select(segment => segment.Kind)
            .Should()
            .Equal(ChartLinePathSegmentKind.CubicBezier, ChartLinePathSegmentKind.CubicBezier);

        path.Segments[0].Control1.X.Should().BeApproximately(13.3333, 0.0001);
        path.Segments[0].Control1.Y.Should().BeApproximately(70.8333, 0.0001);
        path.Segments[0].Control2.X.Should().BeApproximately(53.3333, 0.0001);
        path.Segments[0].Control2.Y.Should().BeApproximately(58.3333, 0.0001);
        path.Segments[0].End.Should().Be(new ChartPlanPoint(80, 50));

        path.Segments[1].Control1.X.Should().BeApproximately(106.6667, 0.0001);
        path.Segments[1].Control1.Y.Should().BeApproximately(41.6667, 0.0001);
        path.Segments[1].Control2.X.Should().BeApproximately(146.6667, 0.0001);
        path.Segments[1].Control2.Y.Should().BeApproximately(29.1667, 0.0001);
        path.Segments[1].End.Should().Be(new ChartPlanPoint(160, 25));
        primitive.Markers.Select(marker => marker.PointIndex).Should().Equal(0, 1, 2);
    }

    [Fact]
    public void BuildScatterPrimitivePlan_SeriesSmoothDecisionOverridesScatterStyle()
    {
        var smoothed = new ChartSeries
        {
            Name = "Authored smooth",
            SmoothLine = true
        };
        smoothed.XValues.AddRange(new double?[] { 0, 50, 100 });
        smoothed.Values.AddRange(new double?[] { 10, 20, 30 });

        var straight = new ChartSeries
        {
            Name = "Authored straight",
            SmoothLine = false
        };
        straight.XValues.AddRange(new double?[] { 0, 50, 100 });
        straight.Values.AddRange(new double?[] { 30, 20, 10 });

        var chart = new ChartShape
        {
            ChartType = ChartType.Scatter,
            ScatterStyle = ScatterStyle.Smooth
        };
        chart.Series.Add(smoothed);
        chart.Series.Add(straight);

        var plan = ChartRenderPlanner.BuildScatterPrimitivePlan(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        plan.Series[0].IsSmoothed.Should().BeTrue();
        plan.Series[0].LinePaths.Single().Segments
            .Should()
            .OnlyContain(segment => segment.Kind == ChartLinePathSegmentKind.CubicBezier);
        plan.Series[1].IsSmoothed.Should().BeFalse();
        plan.Series[1].LinePaths.Single().Segments
            .Should()
            .OnlyContain(segment => segment.Kind == ChartLinePathSegmentKind.Line);
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
    public void BuildBubblePrimitivePlan_WidthSizingUsesLinearRadii()
    {
        var series = new ChartSeries { Name = "Bubble" };
        series.XValues.AddRange(new double?[] { 0, 100 });
        series.Values.AddRange(new double?[] { 0, 40 });
        series.BubbleSizes.AddRange(new double?[] { 25, 100 });

        var chart = new ChartShape
        {
            ChartType = ChartType.Bubble,
            BubbleSizeRepresents = BubbleSizeRepresentation.Width
        };
        chart.Series.Add(series);

        var plan = ChartRenderPlanner.BuildBubblePrimitivePlan(
            chart,
            new ChartPlanRect(0, 0, 160, 80));

        plan.Bubbles.Should().HaveCount(2);
        plan.Bubbles[0].Radius.Should().BeApproximately(2.5, 0.0001);
        plan.Bubbles[1].Radius.Should().BeApproximately(10, 0.0001);
    }

    [Fact]
    public void BuildBubblePrimitivePlan_BubbleScaleChangesMaxRadius()
    {
        var series = new ChartSeries { Name = "Bubble" };
        series.XValues.AddRange(new double?[] { 0, 100 });
        series.Values.AddRange(new double?[] { 0, 40 });
        series.BubbleSizes.AddRange(new double?[] { 25, 100 });

        var chart = new ChartShape
        {
            ChartType = ChartType.Bubble,
            BubbleScalePercent = 150
        };
        chart.Series.Add(series);

        var plan = ChartRenderPlanner.BuildBubblePrimitivePlan(
            chart,
            new ChartPlanRect(0, 0, 160, 80));

        plan.Bubbles.Should().HaveCount(2);
        plan.Bubbles[0].Radius.Should().BeApproximately(7.5, 0.0001);
        plan.Bubbles[1].Radius.Should().BeApproximately(15, 0.0001);
    }

    [Fact]
    public void BuildBubblePrimitivePlan_HidesNegativeBubblesByDefault()
    {
        var series = new ChartSeries { Name = "Bubble" };
        series.XValues.AddRange(new double?[] { 0, 50, 100 });
        series.Values.AddRange(new double?[] { 0, 20, 40 });
        series.BubbleSizes.AddRange(new double?[] { 25, -50, 100 });

        var chart = new ChartShape { ChartType = ChartType.Bubble };
        chart.Series.Add(series);

        var plan = ChartRenderPlanner.BuildBubblePrimitivePlan(
            chart,
            new ChartPlanRect(0, 0, 160, 80));

        plan.Bubbles.Should().HaveCount(2);
        plan.Bubbles.Select(b => b.PointIndex).Should().Equal(0, 2);
    }

    [Fact]
    public void BuildBubblePrimitivePlan_ShowNegativeBubblesUsesAbsoluteAuthoredSize()
    {
        var series = new ChartSeries { Name = "Bubble" };
        series.XValues.AddRange(new double?[] { 0, 50, 100 });
        series.Values.AddRange(new double?[] { 0, 20, 40 });
        series.BubbleSizes.AddRange(new double?[] { 25, -100, 100 });

        var chart = new ChartShape
        {
            ChartType = ChartType.Bubble,
            ShowNegativeBubbles = true
        };
        chart.Series.Add(series);

        var plan = ChartRenderPlanner.BuildBubblePrimitivePlan(
            chart,
            new ChartPlanRect(0, 0, 160, 80));

        plan.Bubbles.Should().HaveCount(3);
        plan.Bubbles[1].Radius.Should().BeApproximately(10, 0.0001);
        plan.Bubbles[2].Radius.Should().BeApproximately(10, 0.0001);
    }

    [Fact]
    public void BuildBubblePrimitivePlan_MissingXCoordinatesUsePointIndexFallback()
    {
        var series = new ChartSeries { Name = "Bubble" };
        series.Values.AddRange(new double?[] { 10, 20, 30 });
        series.BubbleSizes.AddRange(new double?[] { 25, 100, 25 });

        var chart = new ChartShape { ChartType = ChartType.Bubble };
        chart.Series.Add(series);

        var plan = ChartRenderPlanner.BuildBubblePrimitivePlan(
            chart,
            new ChartPlanRect(0, 0, 120, 60));

        plan.Bubbles.Should().HaveCount(3);
        plan.Bubbles.Select(b => b.PointIndex).Should().Equal(0, 1, 2);
        plan.Bubbles[0].Center.Should().Be(new ChartPlanPoint(0, 45));
        plan.Bubbles[1].Center.X.Should().BeApproximately(48, 0.0001);
        plan.Bubbles[1].Center.Y.Should().BeApproximately(30, 0.0001);
        plan.Bubbles[1].Radius.Should().BeApproximately(7.5, 0.0001);
        plan.Bubbles[2].Center.X.Should().BeApproximately(96, 0.0001);
        plan.Bubbles[2].Center.Y.Should().BeApproximately(15, 0.0001);
    }

    [Fact]
    public void BuildBubblePrimitivePlan_MissingYCoordinatesRemainUnplanned()
    {
        var series = new ChartSeries { Name = "Bubble" };
        series.Values.AddRange(new double?[] { 10, null, 30 });
        series.BubbleSizes.AddRange(new double?[] { 25, 100, 25 });

        var chart = new ChartShape { ChartType = ChartType.Bubble };
        chart.Series.Add(series);

        var plan = ChartRenderPlanner.BuildBubblePrimitivePlan(
            chart,
            new ChartPlanRect(0, 0, 120, 60));

        plan.Bubbles.Should().HaveCount(2);
        plan.Bubbles.Select(b => b.PointIndex).Should().Equal(0, 2);
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
        plan.Series[0].Paths.Should().ContainSingle();
        plan.Series[0].Path.IsClosed.Should().BeTrue();
        plan.Series[0].Path.Points.Should().Equal(plan.Series[0].Points.Select(point => point!.Value));
        plan.Series[0].Path.Fill.Should().Be(new ChartFillPlan(seriesColor, ChartRenderPlanner.RadarFillAlpha));
        plan.Series[0].Stroke.Should().Be(new ChartStrokePlan(
            seriesColor,
            Alpha: 255,
            Thickness: ChartRenderPlanner.RadarSeriesStrokeThickness));
        plan.Series[0].Markers.Should().BeEmpty();
        plan.Series[0].Points[0].HasValue.Should().BeTrue();
        plan.Series[0].Points[0]!.Value.X.Should().BeApproximately(100, 0.0001);
        plan.Series[0].Points[0]!.Value.Y.Should().BeApproximately(40.625, 0.0001);
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
    public void BuildRadarPrimitivePlan_DisplayBlanksAsGap_BreaksSegmentsAroundBlankPoint()
    {
        var series = new ChartSeries { Name = "Radar" };
        series.Values.AddRange(new double?[] { 1, null, 3, 4 });

        var chart = new ChartShape
        {
            ChartType = ChartType.Radar,
            RadarStyle = RadarStyle.Filled,
            DisplayBlanksAs = ChartDisplayBlanksAs.Gap
        };
        chart.Categories.AddRange(new[] { "North", "East", "South", "West" });
        chart.Series.Add(series);

        var plan = ChartRenderPlanner.BuildRadarPrimitivePlan(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        var primitive = plan.Series.Should().ContainSingle().Subject;
        primitive.Points[0].HasValue.Should().BeTrue();
        primitive.Points[1].Should().BeNull();
        primitive.Points[2].HasValue.Should().BeTrue();
        primitive.Points[3].HasValue.Should().BeTrue();
        primitive.Paths.Should().HaveCount(2);
        primitive.Paths[0].Points.Should().Equal(primitive.Points[2]!.Value, primitive.Points[3]!.Value);
        primitive.Paths[1].Points.Should().Equal(primitive.Points[3]!.Value, primitive.Points[0]!.Value);
        primitive.Paths.Should().OnlyContain(path => !path.IsClosed && !path.Fill.HasValue);
    }

    [Fact]
    public void BuildRadarPrimitivePlan_DefaultDisplayBlanksAsGap_BreaksSegmentsAroundBlankPoint()
    {
        var series = new ChartSeries { Name = "Radar" };
        series.Values.AddRange(new double?[] { 1, null, 3, 4 });

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

        var primitive = plan.Series.Should().ContainSingle().Subject;
        primitive.Points[1].Should().BeNull();
        primitive.Paths.Should().HaveCount(2);
        primitive.Paths[0].Points.Should().Equal(primitive.Points[2]!.Value, primitive.Points[3]!.Value);
        primitive.Paths[1].Points.Should().Equal(primitive.Points[3]!.Value, primitive.Points[0]!.Value);
        primitive.Paths.Should().OnlyContain(path => !path.IsClosed && !path.Fill.HasValue);
    }

    [Fact]
    public void BuildRadarPrimitivePlan_DisplayBlanksAsZero_MaterializesBlankPointAtCenter()
    {
        var series = new ChartSeries { Name = "Radar" };
        series.Values.AddRange(new double?[] { 1, null, 3, 4 });

        var chart = new ChartShape
        {
            ChartType = ChartType.Radar,
            RadarStyle = RadarStyle.Marker,
            DisplayBlanksAs = ChartDisplayBlanksAs.Zero
        };
        chart.Categories.AddRange(new[] { "North", "East", "South", "West" });
        chart.Series.Add(series);

        var plan = ChartRenderPlanner.BuildRadarPrimitivePlan(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        var primitive = plan.Series.Should().ContainSingle().Subject;
        primitive.Points[1].Should().Be(new ChartPlanPoint(100, 50));
        primitive.Paths.Should().ContainSingle();
        primitive.Path.IsClosed.Should().BeTrue();
        primitive.Path.Points.Should().HaveCount(4);
        primitive.Markers.Select(marker => marker.PointIndex).Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public void BuildRadarPrimitivePlan_DisplayBlanksAsSpan_BridgesBlankPoint()
    {
        var series = new ChartSeries { Name = "Radar" };
        series.Values.AddRange(new double?[] { 1, null, 3, 4 });

        var chart = new ChartShape
        {
            ChartType = ChartType.Radar,
            RadarStyle = RadarStyle.Filled,
            DisplayBlanksAs = ChartDisplayBlanksAs.Span
        };
        chart.Categories.AddRange(new[] { "North", "East", "South", "West" });
        chart.Series.Add(series);

        var plan = ChartRenderPlanner.BuildRadarPrimitivePlan(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        var primitive = plan.Series.Should().ContainSingle().Subject;
        primitive.Points[1].Should().BeNull();
        primitive.Paths.Should().ContainSingle();
        primitive.Path.IsClosed.Should().BeTrue();
        primitive.Path.Fill.Should().NotBeNull();
        primitive.Path.Points.Should().Equal(
            primitive.Points[0]!.Value,
            primitive.Points[2]!.Value,
            primitive.Points[3]!.Value);
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
    public void BuildPieSlicePrimitives_UsesAuthoredFirstSliceAngle()
    {
        var series = new ChartSeries { Name = "Share" };
        series.Values.AddRange(new double?[] { 1, 3 });
        var chart = new ChartShape
        {
            ChartType = ChartType.Pie,
            FirstSliceAngleDegrees = 90
        };
        chart.Series.Add(series);

        var slices = ChartRenderPlanner.BuildPieSlicePrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        slices.Should().HaveCount(2);
        slices[0].StartAngle.Should().BeApproximately(0, 0.0001);
        slices[0].EndAngle.Should().BeApproximately(Math.PI / 2, 0.0001);
        slices[0].OuterStart.X.Should().BeApproximately(142.5, 0.0001);
        slices[0].OuterStart.Y.Should().BeApproximately(50, 0.0001);
        slices[0].OuterEnd.X.Should().BeApproximately(100, 0.0001);
        slices[0].OuterEnd.Y.Should().BeApproximately(92.5, 0.0001);
    }

    [Fact]
    public void BuildPieSlicePrimitives_ThreeDPiePlansBoundedCompressedTopFace()
    {
        var series = new ChartSeries { Name = "Share" };
        series.Values.AddRange(new double?[] { 1, 3 });
        var chart = new ChartShape
        {
            ChartType = ChartType.Pie,
            ThreeDStyle = ChartThreeDStyle.Pie
        };
        chart.Series.Add(series);

        var slices = ChartRenderPlanner.BuildPieSlicePrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        var first = slices[0];
        first.HasThreeDDepth.Should().BeTrue();
        first.EffectiveVerticalScale.Should().Be(ChartRenderPlanner.ThreeDPieVerticalScale);
        ChartRenderPlanner.ThreeDPieDepthFillAlpha.Should().Be(140);
        first.DepthOffsetY.Should().BeApproximately(9.35, 0.0001);
        first.OuterRadiusY.Should().BeApproximately(30.6, 0.0001);
        first.OuterStart.Y.Should().BeApproximately(19.4, 0.0001);
        first.OuterEnd.Y.Should().BeApproximately(50, 0.0001);
        slices.Should().OnlyContain(slice => slice.HasThreeDDepth);
    }

    [Fact]
    public void BuildPieSlicePrimitives_VaryColorsUsesPointFallbackPalette()
    {
        var series = new ChartSeries { Name = "Share" };
        series.Values.AddRange(new double?[] { 1, 1, 1 });
        var chart = new ChartShape
        {
            ChartType = ChartType.Pie,
            VaryColors = true
        };
        chart.Series.Add(series);
        var colors = new[]
        {
            new SrgbColor(0x10, 0x20, 0x30),
            new SrgbColor(0x40, 0x50, 0x60),
            new SrgbColor(0x70, 0x80, 0x90)
        };

        var slices = ChartRenderPlanner.BuildPieSlicePrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100),
            colors);

        slices.Should().HaveCount(3);
        slices.Select(slice => slice.Fill!.Value.Color)
            .Should()
            .Equal(colors);
    }

    [Fact]
    public void BuildPieSlicePrimitives_NullAndNonpositiveValuesHaveNoSweepAndPreservePointIdentity()
    {
        var series = new ChartSeries { Name = "Share" };
        series.Values.AddRange(new double?[] { 2, null, 0, -4, 6 });
        var chart = new ChartShape
        {
            ChartType = ChartType.Pie,
            VaryColors = true
        };
        chart.Series.Add(series);
        var colors = new[]
        {
            new SrgbColor(0x10, 0x20, 0x30),
            new SrgbColor(0x40, 0x50, 0x60),
            new SrgbColor(0x70, 0x80, 0x90),
            new SrgbColor(0xA0, 0xB0, 0xC0),
            new SrgbColor(0xD0, 0xE0, 0xF0)
        };

        var slices = ChartRenderPlanner.BuildPieSlicePrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100),
            colors);

        slices.Should().HaveCount(2);
        slices.Select(slice => slice.PointIndex).Should().Equal(0, 4);
        slices.Select(slice => slice.Fill!.Value.Color).Should().Equal(colors[0], colors[4]);
        slices[0].SweepAngle.Should().BeApproximately(Math.PI / 2, 0.0001);
        slices[1].SweepAngle.Should().BeApproximately(Math.PI * 1.5, 0.0001);
    }

    [Fact]
    public void BuildPieSlicePrimitives_AllNullOrNonpositiveValuesReturnNoVisibleSlices()
    {
        var series = new ChartSeries { Name = "Share" };
        series.Values.AddRange(new double?[] { null, 0, -4 });
        var chart = new ChartShape { ChartType = ChartType.Pie };
        chart.Series.Add(series);

        var slices = ChartRenderPlanner.BuildPieSlicePrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        slices.Should().BeEmpty();
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
    public void BuildDoughnutSlicePrimitives_UsesAuthoredFirstSliceAngleForEveryRing()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Doughnut,
            FirstSliceAngleDegrees = 180,
            DoughnutHolePercent = 50
        };

        var inner = new ChartSeries { Name = "Inner" };
        inner.Values.AddRange(new double?[] { 1, 1 });
        chart.Series.Add(inner);

        var outer = new ChartSeries { Name = "Outer" };
        outer.Values.AddRange(new double?[] { 1, 1 });
        chart.Series.Add(outer);

        var slices = ChartRenderPlanner.BuildDoughnutSlicePrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        slices.Should().HaveCount(4);
        slices[0].SeriesIndex.Should().Be(0);
        slices[0].StartAngle.Should().BeApproximately(Math.PI / 2, 0.0001);
        slices[2].SeriesIndex.Should().Be(1);
        slices[2].StartAngle.Should().BeApproximately(Math.PI / 2, 0.0001);
    }

    [Fact]
    public void BuildDoughnutSlicePrimitives_NullAndNonpositiveValuesHaveNoSweepPerRing()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Doughnut,
            DoughnutHolePercent = 50
        };

        var inner = new ChartSeries { Name = "Inner" };
        inner.Values.AddRange(new double?[] { null, 3, 0 });
        chart.Series.Add(inner);

        var outer = new ChartSeries { Name = "Outer" };
        outer.Values.AddRange(new double?[] { -5, 2, null, 2 });
        chart.Series.Add(outer);

        var slices = ChartRenderPlanner.BuildDoughnutSlicePrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        slices.Should().HaveCount(3);
        slices.Select(slice => (slice.SeriesIndex, slice.PointIndex))
            .Should()
            .Equal((0, 1), (1, 1), (1, 3));
        slices[0].SweepAngle.Should().BeApproximately(Math.PI * 2, 0.0001);
        slices[1].SweepAngle.Should().BeApproximately(Math.PI, 0.0001);
        slices[2].SweepAngle.Should().BeApproximately(Math.PI, 0.0001);
    }

    [Fact]
    public void BuildDataLabelPlans_PieLabelsPreserveOriginalCategoriesAfterNoSweepPoints()
    {
        var series = new ChartSeries { Name = "Share" };
        series.Values.AddRange(new double?[] { null, 2, 0, 6 });
        var chart = new ChartShape
        {
            ChartType = ChartType.Pie,
            DataLabels = new ChartDataLabels
            {
                ShowCategoryName = true,
                ShowValue = true,
                ShowPercent = true
            }
        };
        chart.Categories.AddRange(new[] { "Blank", "Small", "Zero", "Large" });
        chart.Series.Add(series);

        var labels = ChartRenderPlanner.BuildDataLabelPlans(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        labels.Should().HaveCount(2);
        labels.Select(label => (label.CategoryIndex, label.Text))
            .Should()
            .Equal((1, "Small 2 25%"), (3, "Large 6 75%"));
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

    [Fact]
    public void BuildDataLabelPlans_ColumnLabelsFollowAuthoredGapWidthAndOverlap()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.BarGapWidthPercent = 0;
        chart.BarOverlapPercent = 50;
        chart.DataLabels = new ChartDataLabels
        {
            ShowValue = true,
            Position = DataLabelPosition.Center
        };

        var planned = ChartRenderPlanner.BuildDataLabelPlans(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        var label = planned.Single(p => p.SeriesIndex == 1 && p.CategoryIndex == 0);
        label.Bounds.X.Should().BeApproximately(33.3333, 0.0001);
        label.Bounds.Width.Should().BeApproximately(66.6667, 0.0001);
    }

    [Fact]
    public void BuildDataLabelPlans_HundredPercentStackedColumnLabelsUseNormalizedBounds()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.ColumnStacked100,
            DataLabels = new ChartDataLabels
            {
                ShowValue = true,
                ShowPercent = true,
                Position = DataLabelPosition.Center
            }
        };
        chart.Categories.Add("Q1");
        var first = new ChartSeries { Name = "Actual" };
        first.Values.Add(20);
        var second = new ChartSeries { Name = "Forecast" };
        second.Values.Add(30);
        chart.Series.Add(first);
        chart.Series.Add(second);

        var planned = ChartRenderPlanner.BuildDataLabelPlans(
            chart,
            new ChartPlanRect(0, 0, 100, 100));

        var label = planned.Single(p => p.SeriesIndex == 1);
        label.Text.Should().Be("30 60%");
        label.Bounds.Should().Be(new ChartPlanRect(30, 24.5, 40, 11));
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

    private static ChartShape MakeStockChart()
    {
        var chart = new ChartShape { ChartType = ChartType.Stock };
        chart.Categories.AddRange(new[] { "Day 1", "Day 2", "Day 3" });

        foreach (var (name, values) in new[]
        {
            ("Open", new double?[] { 10, 12, 11 }),
            ("High", new double?[] { 14, 16, 15 }),
            ("Low", new double?[] { 8, 9, 10 }),
            ("Close", new double?[] { 13, 11, 14 })
        })
        {
            var series = new ChartSeries { Name = name };
            series.Values.AddRange(values);
            chart.Series.Add(series);
        }

        return chart;
    }

    private static ChartShape MakeStockVolumeChart()
    {
        var chart = new ChartShape { ChartType = ChartType.Stock };
        chart.Categories.AddRange(new[] { "Day 1", "Day 2", "Day 3" });

        foreach (var (name, values) in new[]
        {
            ("Volume", new double?[] { 1000, 1500, 750 }),
            ("Open", new double?[] { 10, 12, 11 }),
            ("High", new double?[] { 14, 16, 15 }),
            ("Low", new double?[] { 8, 9, 10 }),
            ("Close", new double?[] { 13, 11, 14 })
        })
        {
            var series = new ChartSeries { Name = name };
            series.Values.AddRange(values);
            chart.Series.Add(series);
        }

        return chart;
    }

    private static ChartShape MakeSurfaceChart(ChartType chartType)
    {
        var chart = new ChartShape { ChartType = chartType };
        chart.Categories.AddRange(new[] { "North", "East", "South" });

        var low = new ChartSeries { Name = "Low Band" };
        low.Values.AddRange(new double?[] { 10, 20, 15 });
        chart.Series.Add(low);

        var high = new ChartSeries { Name = "High Band" };
        high.Values.AddRange(new double?[] { 30, 25, 35 });
        chart.Series.Add(high);

        return chart;
    }

    private static ChartShape MakeSecondaryAxisChart()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.ColumnClustered,
            SecondaryValueAxis = new ChartAxis()
        };
        chart.Categories.AddRange(new[] { "Q1", "Q2" });

        var primary = new ChartSeries { Name = "Revenue" };
        primary.Values.AddRange(new double?[] { 20, 100 });
        chart.Series.Add(primary);

        var secondary = new ChartSeries { Name = "Margin", OnSecondaryAxis = true };
        secondary.Values.AddRange(new double?[] { 200_000, 1_000_000 });
        chart.Series.Add(secondary);

        return chart;
    }
}
