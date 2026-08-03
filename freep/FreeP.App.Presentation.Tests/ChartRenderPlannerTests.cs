using System.Collections.Generic;
using System.Linq;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartRenderPlannerTests
{
    [Fact]
    public void BuildScenePlan_ShowDataLabelsOverMaximumFiltersValuesBeyondExplicitAxisMaximum()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.ColumnClustered,
            Categories = { "Within", "Beyond" },
            DataLabels = new ChartDataLabels { ShowValue = true },
            ShowDataLabelsOverMaximum = false
        };
        chart.ValueAxis.Max = 10;
        var series = new ChartSeries { Name = "Revenue" };
        series.Values.AddRange(new double?[] { 5, 15 });
        chart.Series.Add(series);

        var hidden = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 480, 320));

        hidden.DataLabels.Should().ContainSingle().Which.CategoryIndex.Should().Be(0);

        chart.ShowDataLabelsOverMaximum = true;
        var shown = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 480, 320));
        shown.DataLabels.Should().HaveCount(2);
    }

    [Fact]
    public void BuildScenePlan_PieLeaderLinesFollowExplicitDataLabelOption()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Pie,
            Categories = { "North", "South", "West" },
            DataLabels = new ChartDataLabels
            {
                ShowPercent = true,
                ShowLeaderLines = true,
                Position = DataLabelPosition.OutsideEnd
            }
        };
        var series = new ChartSeries { Name = "Revenue" };
        series.Values.AddRange(new double?[] { 40, 35, 25 });
        chart.Series.Add(series);

        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 480, 320));

        scene.DataLabels.Should().HaveCount(3);
        scene.DataLabelLeaderLines.Should().HaveCount(6);
        scene.DataLabelLeaderLines.Should().AllSatisfy(line =>
        {
            line.Stroke.Thickness.Should().Be(0.75);
            line.Start.Should().NotBe(line.End);
        });
    }

    [Fact]
    public void BuildScenePlan_DoesNotEmitLeaderLinesForNonPieChartsOrDisabledOption()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Pie,
            Categories = { "North", "South" },
            DataLabels = new ChartDataLabels { ShowPercent = true, ShowLeaderLines = false }
        };
        var series = new ChartSeries();
        series.Values.AddRange(new double?[] { 60, 40 });
        chart.Series.Add(series);

        var disabled = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 480, 320));
        disabled.DataLabelLeaderLines.Should().BeEmpty();

        chart.ChartType = ChartType.ColumnClustered;
        chart.DataLabels.ShowLeaderLines = true;
        var column = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 480, 320));
        column.DataLabelLeaderLines.Should().BeEmpty();
    }

    [Fact]
    public void BuildScenePlan_ResolvesChartFamilyAndAllSharedPaintInputsOnce()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.LineMarkers,
            Title = "Revenue",
            Categories = { "Q1", "Q2", "Q3" },
            DataLabels = new ChartDataLabels { ShowValue = true }
        };
        var series = new ChartSeries { Name = "Actual" };
        series.Values.AddRange(new double?[] { 10, 30, 20 });
        chart.Series.Add(series);

        var scene = ChartRenderPlanner.BuildScenePlan(
            chart,
            new ChartPlanRect(0, 0, 400, 300));

        scene.GeometryKind.Should().Be(ChartSceneGeometryKind.Line);
        scene.Frame.Plot.HasPositiveArea.Should().BeTrue();
        scene.Title!.Value.Text.Should().Be("Revenue");
        scene.Title.Value.FontFamily.Should().Be("Arial");
        scene.LineSeries.Should().ContainSingle().Which.Markers.Should().HaveCount(3);
        scene.DataLabels.Should().HaveCount(3);
        scene.AxisTicks.ValueTicks.Should().NotBeEmpty();
        scene.ValueAxisLabels.Should().NotBeEmpty();
        scene.CategoryAxisLabels.Should().HaveCount(3);
        scene.AxisTitles.Should().BeEmpty();
        scene.LegendItems.Should().BeEmpty();
    }

    [Fact]
    public void BuildScenePlan_EmitsBothSidedVerticalErrorBarsForLinePoints()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.LineMarkers,
            Categories = { "Q1", "Q2", "Q3" },
        };
        var series = new ChartSeries
        {
            Name = "Actual",
            ErrorBars = new ChartErrorBars { Value = 2 },
        };
        series.Values.AddRange(new double?[] { 10, 20, 30 });
        chart.Series.Add(series);

        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        scene.ErrorBars.Should().HaveCount(3);
        var first = scene.ErrorBars[0];
        first.Direction.Should().Be(ChartErrorDirection.Y);
        first.MinusEnd.Should().NotBeNull();
        first.PlusEnd.Should().NotBeNull();
        first.MinusEnd!.Value.Y.Should().BeGreaterThan(first.Center.Y);
        first.PlusEnd!.Value.Y.Should().BeLessThan(first.Center.Y);
        first.NoEndCap.Should().BeFalse();
    }

    [Fact]
    public void BuildScenePlan_EmitsDropLinesForAuthoredLineChart()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.LineMarkers,
            ShowDropLines = true,
            Categories = { "Q1", "Q2", "Q3" },
        };
        var series = new ChartSeries { Name = "Actual" };
        series.Values.AddRange(new double?[] { 10, null, 30 });
        chart.Series.Add(series);

        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        scene.DropLines.Should().HaveCount(2);
        scene.DropLines.Should().AllSatisfy(line =>
        {
            line.Start.X.Should().Be(line.End.X);
            line.End.Y.Should().Be(scene.Frame.Plot.Bottom);
        });
    }

    [Fact]
    public void BuildScenePlan_DoesNotEmitDropLinesWhenFlagIsOmitted()
    {
        var chart = new ChartShape { ChartType = ChartType.LineMarkers };
        chart.Categories.AddRange(new[] { "Q1", "Q2" });
        var series = new ChartSeries { Name = "Actual" };
        series.Values.AddRange(new double?[] { 10, 20 });
        chart.Series.Add(series);

        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        scene.DropLines.Should().BeEmpty();
    }

    [Fact]
    public void BuildScenePlan_EmitsUpDownBarsBetweenFirstTwoLineSeries()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.LineMarkers,
            ShowUpDownBars = true,
            UpDownBarGapWidthPercent = 100,
            UpBarFill = new ShapeFill.Solid(new SrgbColor(0x11, 0x22, 0x33)),
            DownBarFill = new ShapeFill.Solid(new SrgbColor(0xAA, 0xBB, 0xCC)),
            Categories = { "Q1", "Q2", "Q3" },
        };
        var first = new ChartSeries { Name = "Open" };
        first.Values.AddRange(new double?[] { 10, 20, 15 });
        var second = new ChartSeries { Name = "Close" };
        second.Values.AddRange(new double?[] { 20, 10, 15 });
        chart.Series.Add(first);
        chart.Series.Add(second);

        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        scene.UpDownBars.Should().HaveCount(2);
        scene.UpDownBars[0].Bounds.Height.Should().BeGreaterThan(0);
        scene.UpDownBars[0].Fill.Color.Should().Be(new SrgbColor(0x11, 0x22, 0x33));
        scene.UpDownBars[1].Fill.Color.Should().Be(new SrgbColor(0xAA, 0xBB, 0xCC));
    }

    [Fact]
    public void BuildScenePlan_DoesNotEmitUpDownBarsForSingleSeries()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.LineMarkers,
            ShowUpDownBars = true,
            Categories = { "Q1", "Q2" },
        };
        var series = new ChartSeries { Name = "Actual" };
        series.Values.AddRange(new double?[] { 10, 20 });
        chart.Series.Add(series);

        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        scene.UpDownBars.Should().BeEmpty();
    }

    [Fact]
    public void BuildScenePlan_RespectsScatterXPlusOnlyAndNoEndCap()
    {
        var chart = new ChartShape { ChartType = ChartType.Scatter, ScatterStyle = ScatterStyle.Marker };
        var series = new ChartSeries
        {
            Name = "Actual",
            ErrorBars = new ChartErrorBars
            {
                Direction = ChartErrorDirection.X,
                BarType = ChartErrorBarType.Plus,
                ValueType = ChartErrorValueType.Percentage,
                Value = 10,
                NoEndCap = true,
            },
        };
        series.XValues.AddRange(new double?[] { 10, 20 });
        series.Values.AddRange(new double?[] { 5, 10 });
        chart.Series.Add(series);

        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        scene.ErrorBars.Should().HaveCount(2);
        scene.ErrorBars.Should().AllSatisfy(errorBar =>
        {
            errorBar.Direction.Should().Be(ChartErrorDirection.X);
            errorBar.MinusEnd.Should().BeNull();
            errorBar.PlusEnd.Should().NotBeNull();
            errorBar.NoEndCap.Should().BeTrue();
        });
        scene.ErrorBars[0].PlusEnd!.Value.X.Should().BeGreaterThan(scene.ErrorBars[0].Center.X);
    }

    [Fact]
    public void BuildScenePlan_EmitsVerticalErrorBarsForAreaPoints()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Area,
            Categories = { "Q1", "Q2", "Q3" },
        };
        var series = new ChartSeries
        {
            Name = "Actual",
            ErrorBars = new ChartErrorBars { Value = 1.5 },
        };
        series.Values.AddRange(new double?[] { 10, 20, 15 });
        chart.Series.Add(series);

        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        scene.ErrorBars.Should().HaveCount(3);
        scene.ErrorBars.Should().AllSatisfy(errorBar =>
        {
            errorBar.Direction.Should().Be(ChartErrorDirection.Y);
            errorBar.MinusEnd.Should().NotBeNull();
            errorBar.PlusEnd.Should().NotBeNull();
        });
    }

    [Fact]
    public void BuildScenePlan_ProjectsRadarValueErrorBarsAlongTheSpoke()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Radar,
            Categories = { "A", "B", "C", "D" },
            RadarStyle = RadarStyle.Marker,
        };
        var series = new ChartSeries
        {
            Name = "Actual",
            ErrorBars = new ChartErrorBars { Value = 2 },
        };
        series.Values.AddRange(new double?[] { 8, 6, 7, 9 });
        chart.Series.Add(series);

        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        scene.ErrorBars.Should().HaveCount(4);
        var first = scene.ErrorBars[0];
        first.MinusEnd.Should().NotBeNull();
        first.PlusEnd.Should().NotBeNull();
        var radar = scene.Radar!.Value;
        var radial = new ChartPlanPoint(
            first.Center.X - radar.Center.X,
            first.Center.Y - radar.Center.Y);
        var plus = new ChartPlanPoint(
            first.PlusEnd!.Value.X - first.Center.X,
            first.PlusEnd.Value.Y - first.Center.Y);
        (radial.X * plus.X + radial.Y * plus.Y).Should().BeLessThan(0);
    }

    [Fact]
    public void BuildScenePlan_TallImportedSurfaceWrapsItsPowerPointTitle()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Surface3D,
            Title = "Surface: blank cell grid retention",
            TextStyle = new ChartTextStyle { FontSizePt = 18.0 }
        };

        var scene = ChartRenderPlanner.BuildScenePlan(
            chart,
            new ChartPlanRect(0, 0, 400, 320));

        scene.Title.Should().NotBeNull();
        scene.Title!.Value.MaxLineCount.Should().Be(2);
        scene.Title.Value.Bounds.Should().Be(new ChartPlanRect(60, 11, 280, 56));
        scene.Frame.Plot.Should().Be(new ChartPlanRect(44, 95, 280, 171));

        var shortFrame = ChartRenderPlanner.BuildScenePlan(
            chart,
            new ChartPlanRect(0, 0, 480, 288));

        shortFrame.Title!.Value.MaxLineCount.Should().Be(1);
    }

    [Fact]
    public void BuildScenePlan_CorpusFamiliesCarryGeometryInSharedPlan()
    {
        var scatter = MakeTwoSeriesChart(ChartType.Scatter);
        scatter.Series[0].XValues.AddRange(new double?[] { 1, 2 });
        scatter.Series[1].XValues.AddRange(new double?[] { 2, 3 });
        var bubble = MakeTwoSeriesChart(ChartType.Bubble);
        bubble.Series[0].XValues.AddRange(new double?[] { 1, 2 });
        bubble.Series[1].XValues.AddRange(new double?[] { 2, 3 });
        bubble.Series[0].BubbleSizes.AddRange(new double?[] { 10, 20 });
        bubble.Series[1].BubbleSizes.AddRange(new double?[] { 15, 25 });
        var charts = new[]
        {
            MakeTwoSeriesChart(ChartType.ColumnClustered),
            MakeTwoSeriesChart(ChartType.BarClustered),
            MakeTwoSeriesChart(ChartType.Area),
            scatter,
            bubble,
            MakeTwoSeriesChart(ChartType.Radar),
            MakeTwoSeriesChart(ChartType.Pie),
            MakeTwoSeriesChart(ChartType.Doughnut),
            MakeSurfaceChart(ChartType.Surface3D),
            MakeStockChart()
        };

        var scenes = charts
            .Select(chart => ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 480, 288)))
            .ToArray();

        scenes.Should().OnlyContain(scene => scene.Frame.HasPlot);
        scenes.Select(scene => scene.GeometryKind).Should().Equal(
            ChartSceneGeometryKind.Column,
            ChartSceneGeometryKind.Bar,
            ChartSceneGeometryKind.Area,
            ChartSceneGeometryKind.Scatter,
            ChartSceneGeometryKind.Bubble,
            ChartSceneGeometryKind.Radar,
            ChartSceneGeometryKind.Pie,
            ChartSceneGeometryKind.Doughnut,
            ChartSceneGeometryKind.Surface,
            ChartSceneGeometryKind.Stock);
        scenes[0].Rectangles.Should().NotBeEmpty();
        scenes[2].AreaSeries.Should().NotBeEmpty();
        scenes[3].Scatter.Should().NotBeNull();
        scenes[4].Bubble.Should().NotBeNull();
        scenes[5].Radar.Should().NotBeNull();
        scenes[6].PieSlices.Should().NotBeEmpty();
        scenes[7].DoughnutSlices.Should().NotBeEmpty();
        scenes[8].Surface.Should().NotBeNull();
        scenes[9].Stock.Should().NotBeNull();
    }

    [Fact]
    public void UsesClassicOfficeChartStyle_DistinguishesStylelessAndStyledCharts()
    {
        ChartRenderPlanner.UsesClassicOfficeChartStyle(new ChartShape()).Should().BeTrue();
        ChartRenderPlanner.UsesClassicOfficeChartStyle(new ChartShape { StyleId = 2 }).Should().BeFalse();
        ChartRenderPlanner.UsesClassicOfficeChartStyle(new ChartShape { StyleId = 102 }).Should().BeFalse();
    }

    [Fact]
    public void BuildScenePlan_StyledChartTitleKeepsRendererDefaultTypeface()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.ColumnClustered,
            StyleId = 102,
            Title = "Revenue"
        };

        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        scene.Title!.Value.FontFamily.Should().BeNull();
    }

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
    public void ComputePrimaryValueAxisRange_AddsMajorUnitWhenAutoCeilingHasTooLittleHeadroom()
    {
        var series = new ChartSeries { Name = "Revenue" };
        series.Values.AddRange(new double?[] { 120, 195, 165, 240 });
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Series.Add(series);

        ChartRenderPlanner.ComputePrimaryValueAxisRange(chart)
            .Should().Be((0, 300, 50));
    }

    [Fact]
    public void ComputePrimaryValueAxisRange_HonorsAuthoredMajorUnit()
    {
        var series = new ChartSeries { Name = "Revenue" };
        series.Values.AddRange(new double?[] { 12, 48, 93 });
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Series.Add(series);
        chart.ValueAxis.MajorUnit = 25;

        ChartRenderPlanner.ComputePrimaryValueAxisRange(chart)
            .Should().Be((0, 100, 25));
    }

    [Fact]
    public void BuildMajorAxisTickPrimitivePlan_AddsAuthoredMinorTicks()
    {
        var series = new ChartSeries { Name = "Revenue" };
        series.Values.AddRange(new double?[] { 0, 100 });
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Series.Add(series);
        chart.ValueAxis.MajorUnit = 25;
        chart.ValueAxis.MinorUnit = 5;
        chart.ValueAxis.MinorTickMark = ChartTickMark.Out;
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var plan = ChartRenderPlanner.BuildMajorAxisTickPrimitivePlan(chart, frame);

        plan.ValueTicks.Should().HaveCount(21);
    }

    [Fact]
    public void ComputePrimaryValueAxisRange_KeepsCeilingWhenItHasSufficientHeadroom()
    {
        var series = new ChartSeries { Name = "Revenue" };
        series.Values.AddRange(new double?[] { 50, 80, 65, 90, 75, 110 });
        var chart = new ChartShape { ChartType = ChartType.BarClustered };
        chart.Series.Add(series);

        ChartRenderPlanner.ComputePrimaryValueAxisRange(chart)
            .Should().Be((0, 125, 25));
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

    [Fact]
    public void ResolveEffectiveLabels_ComboOverrideDoesNotInheritPrimaryGroupLabels()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.ColumnClustered,
            DataLabels = new ChartDataLabels { ShowValue = true }
        };
        chart.Series.Add(new ChartSeries { Name = "Columns" });
        chart.Series.Add(new ChartSeries { Name = "Line", OverrideChartType = ChartType.Line });

        ChartRenderPlanner.ResolveEffectiveLabels(chart, 0).Should().NotBeNull();
        ChartRenderPlanner.ResolveEffectiveLabels(chart, 1).Should().BeNull();
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
    public void FormatAxisLabelValue_DoesNotAbbreviateGeneralThousands()
    {
        ChartRenderPlanner.FormatAxisLabelValue(8000, "General").Should().Be("8000");
        ChartRenderPlanner.FormatAxisLabelValue(1200, null).Should().Be("1.2K");
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
    public void AxisLabelPlans_ApplyAuthoredDisplayUnitsToValueLabels()
    {
        var series = new ChartSeries { Name = "Revenue" };
        series.Values.AddRange(new double?[] { 0, 2_000 });
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Series.Add(series);
        chart.ValueAxis.Min = 0;
        chart.ValueAxis.Max = 2_000;
        chart.ValueAxis.MajorUnit = 1_000;
        chart.ValueAxis.DisplayUnit = ChartAxisDisplayUnit.Thousands;
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        ChartRenderPlanner.BuildValueAxisLabelPlans(chart, frame)
            .Select(label => label.Text)
            .Should().Equal("0", "1", "2");
    }

    [Fact]
    public void ValueAxisLabelPlans_HonorAuthoredTickLabelPosition()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));
        chart.ValueAxis.TickLabelPosition = ChartTickLabelPosition.High;

        var high = ChartRenderPlanner.BuildValueAxisLabelPlans(chart, frame);

        high.Should().HaveCount(6);
        high[0].Bounds.X.Should().BeGreaterThanOrEqualTo(frame.Plot.Right);
        high[0].Alignment.Should().Be(ChartPlanTextAlignment.Left);

        chart.ValueAxis.TickLabelPosition = ChartTickLabelPosition.None;
        ChartRenderPlanner.BuildValueAxisLabelPlans(chart, frame).Should().BeEmpty();
    }

    [Fact]
    public void AxisLabelPlans_ApplyCustomDisplayUnitToValueLabels()
    {
        var series = new ChartSeries { Name = "Revenue" };
        series.Values.AddRange(new double?[] { 0, 2_500 });
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Series.Add(series);
        chart.ValueAxis.Min = 0;
        chart.ValueAxis.Max = 2_500;
        chart.ValueAxis.MajorUnit = 2_500;
        chart.ValueAxis.DisplayUnit = ChartAxisDisplayUnit.Custom;
        chart.ValueAxis.CustomDisplayUnit = 2_500;
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        ChartRenderPlanner.BuildValueAxisLabelPlans(chart, frame)
            .Select(label => label.Text)
            .Should().Equal("0", "1");
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

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 1)]
    [InlineData(100, 2)]
    public void CategoryAxisLabelPlans_HonorAuthoredLabelOffsetPercent(int offsetPercent, double expectedGap)
    {
        var series = new ChartSeries { Name = "Sales" };
        series.Values.AddRange(new double?[] { 10, 20 });
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(new[] { "Jan", "Feb" });
        chart.Series.Add(series);
        chart.CategoryAxis.LabelOffsetPercent = offsetPercent;
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var labels = ChartRenderPlanner.BuildCategoryAxisLabelPlans(chart, frame);

        labels.Should().HaveCount(2);
        labels[0].Bounds.Y.Should().BeApproximately(frame.Plot.Bottom + expectedGap, 0.0001);
    }

    [Fact]
    public void BarCategoryAxisLabelPlans_HonorAuthoredLabelOffsetPercent()
    {
        var series = new ChartSeries { Name = "Sales" };
        series.Values.AddRange(new double?[] { 10, 20 });
        var chart = new ChartShape { ChartType = ChartType.BarClustered };
        chart.Categories.AddRange(new[] { "Jan", "Feb" });
        chart.Series.Add(series);
        chart.CategoryAxis.LabelOffsetPercent = 0;
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var label = ChartRenderPlanner.BuildCategoryAxisLabelPlans(chart, frame).First();

        (label.Bounds.X + label.Bounds.Width).Should().BeApproximately(frame.Plot.X, 0.0001);
    }

    [Fact]
    public void CategoryAxisLabelPlans_HonorAuthoredTickLabelPosition()
    {
        var column = MakeTwoSeriesChart(ChartType.ColumnClustered);
        var columnFrame = ChartRenderPlanner.BuildFramePlan(column, new ChartPlanRect(0, 0, 400, 300));
        column.CategoryAxis.TickLabelPosition = ChartTickLabelPosition.High;

        var highColumn = ChartRenderPlanner.BuildCategoryAxisLabelPlans(column, columnFrame);

        highColumn.Should().HaveCount(2);
        highColumn[0].Bounds.Bottom.Should().BeLessThan(columnFrame.Plot.Y);

        column.CategoryAxis.TickLabelPosition = ChartTickLabelPosition.None;
        ChartRenderPlanner.BuildCategoryAxisLabelPlans(column, columnFrame).Should().BeEmpty();

        var bar = MakeTwoSeriesChart(ChartType.BarClustered);
        var barFrame = ChartRenderPlanner.BuildFramePlan(bar, new ChartPlanRect(0, 0, 400, 300));
        bar.CategoryAxis.TickLabelPosition = ChartTickLabelPosition.High;

        var highBar = ChartRenderPlanner.BuildCategoryAxisLabelPlans(bar, barFrame);

        highBar.Should().HaveCount(2);
        highBar[0].Bounds.X.Should().BeGreaterThanOrEqualTo(barFrame.Plot.Right);
        highBar[0].Alignment.Should().Be(ChartPlanTextAlignment.Left);
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
    public void FormatDataLabel_IncludesBubbleSizeWhenRequested()
    {
        var labels = new ChartDataLabels
        {
            ShowValue = true,
            ShowBubbleSize = true,
        };

        ChartRenderPlanner.FormatDataLabel(labels, 4, 0, "Q1", "Bubbles", 12)
            .Should().Be("4 12");
    }

    [Fact]
    public void FormatDataLabel_UsesAuthoredSeparatorForImportedPowerPointLabels()
    {
        var labels = new ChartDataLabels
        {
            ShowValue = true,
            ShowPercent = true,
            Separator = ", "
        };

        ChartRenderPlanner.FormatDataLabel(labels, 45, 100, "Q1", "Sales")
            .Should().Be("45, 45%");
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
    [InlineData(ChartType.Scatter, 12.0)]
    [InlineData(ChartType.Surface3D, 11.0)]
    public void BuildFramePlan_ImportedChartTitlesUsePowerPointTitleBandOffsets(
        ChartType chartType,
        double titleOffset)
    {
        var chart = new ChartShape
        {
            ChartType = chartType,
            Title = "Revenue",
            TextStyle = new ChartTextStyle { FontSizePt = 18.0 }
        };

        var plan = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        plan.TitleBounds.Should().Be(new ChartPlanRect(20, titleOffset, 360, 28));
    }

    [Fact]
    public void ResolveTextFontSize_InheritedOfficeTitleDefault_UsesCompactRoleFallback()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.ColumnClustered,
            TextStyle = new ChartTextStyle { FontSizePt = 18.0, IsImplicitDefault = true }
        };

        ChartRenderPlanner.ResolveTextFontSize(chart, 6.5).Should().Be(10.0);
        ChartRenderPlanner.ResolveTitleFontSize(chart, 9.0).Should().Be(18.0);
    }

    [Fact]
    public void ImportedChartTitle_UsesRasterCalibrationAndAutomaticBandOffset()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.ColumnClustered,
            Title = "Revenue",
            HasAutomaticTitle = true,
            TextStyle = new ChartTextStyle { FontSizePt = 18.0 }
        };

        ChartRenderPlanner.ResolveTitleFontSize(chart, 9.0)
            .Should().Be(ChartRenderPlanner.ImportedChartTitleRasterFontSize);

        ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300))
            .TitleBounds!.Value.Y.Should()
            .Be(ChartRenderPlanner.ImportedAutomaticTitleVerticalAdjustment + 12.0);
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
    public void BuildScenePlan_StockLineFallback_UsesPowerPointStrokeAndMarkerDefaults()
    {
        var chart = MakeStockChart();
        chart.HasHighLowLines = false;

        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        scene.LineSeries.Should().HaveCount(4);
        scene.LineSeries.Select(series => series.Stroke.Thickness)
            .Should().OnlyContain(thickness => thickness == ChartRenderPlanner.StockFallbackLineSeriesStrokeThickness);
        scene.LineSeries.SelectMany(series => series.Markers)
            .Select(marker => marker.Radius)
            .Should().OnlyContain(radius => radius == ChartRenderPlanner.StockFallbackMarkerRadius);
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
    public void BuildScenePlan_StockUpDownBarsUseOpenCloseBands()
    {
        var chart = MakeStockChart();
        chart.ShowUpDownBars = true;
        chart.UpDownBarGapWidthPercent = 100;
        chart.UpBarFill = new ShapeFill.Solid(new SrgbColor(0x11, 0x22, 0x33));
        chart.DownBarFill = new ShapeFill.Solid(new SrgbColor(0xAA, 0xBB, 0xCC));

        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        scene.UpDownBars.Should().HaveCount(3);
        scene.UpDownBars[0].Fill.Color.Should().Be(new SrgbColor(0x11, 0x22, 0x33));
        scene.UpDownBars[1].Fill.Color.Should().Be(new SrgbColor(0xAA, 0xBB, 0xCC));
        scene.UpDownBars[2].Fill.Color.Should().Be(new SrgbColor(0x11, 0x22, 0x33));
        scene.UpDownBars.Should().OnlyContain(bar => bar.Bounds.Height > 0);
    }

    [Fact]
    public void BuildFunnelSegmentPrimitives_CreatesCenteredDescendingStages()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Funnel,
            Categories = { "Awareness", "Interest", "Conversion" }
        };
        var series = new ChartSeries { Name = "Value" };
        series.Values.AddRange(new double?[] { 100, 60, 18 });
        chart.Series.Add(series);

        var segments = ChartRenderPlanner.BuildFunnelSegmentPrimitives(
            chart,
            new ChartPlanRect(10, 20, 300, 240));

        segments.Should().HaveCount(3);
        segments.Select(segment => segment.Path.IsClosed).Should().OnlyContain(value => value);
        segments.Select(segment => segment.Path.Points[0].X + segment.Path.Points[1].X)
            .Should().OnlyContain(value => Math.Abs(value - 320) < 0.001);
        segments[0].Path.Points[1].X.Should().BeGreaterThan(segments[1].Path.Points[1].X);
        segments[1].Path.Points[1].X.Should().BeGreaterThan(segments[2].Path.Points[1].X);
    }

    [Fact]
    public void BuildWaterfallPrimitives_UsesCumulativeStartAndEndValues()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Waterfall,
            Categories = { "Start", "Reduction", "Growth" }
        };
        var series = new ChartSeries { Name = "Value" };
        series.Values.AddRange(new double?[] { 100, -30, 20 });
        chart.Series.Add(series);

        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 400, 300));
        var bars = ChartRenderPlanner.BuildWaterfallPrimitives(
            chart,
            scene.Frame.Plot);

        scene.GeometryKind.Should().Be(ChartSceneGeometryKind.Waterfall);
        bars.Should().HaveCount(3);
        bars.Should().OnlyContain(bar => bar.Bounds.HasPositiveArea);
        bars[1].Bounds.Y.Should().BeApproximately(bars[0].Bounds.Y, 0.001);
        bars[1].Bounds.Height.Should().BeGreaterThan(0);
        bars[2].Bounds.Y.Should().BeLessThan(bars[1].Bounds.Y + bars[1].Bounds.Height);
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

    [Theory]
    [InlineData(0.1, 0.028)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.9, 0.972)]
    public void GradientColorInterpolation_EasesPowerPointStopPositions(double fraction, double expected)
    {
        GradientColorInterpolation.EasePowerPointPosition(fraction)
            .Should()
            .BeApproximately(expected, 0.0001);
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
    public void BuildSurfaceCellPrimitives_DisplayBlanksAsZero_MaterializesZeroValueCell()
    {
        var chart = MakeSurfaceChart(ChartType.Surface3D);
        chart.DisplayBlanksAs = ChartDisplayBlanksAs.Zero;
        chart.Series[0].Values[1] = null;

        var cells = ChartRenderPlanner.BuildSurfaceCellPrimitives(
            chart,
            new ChartPlanRect(0, 0, 300, 120));

        cells.Should().HaveCount(6);
        cells.Single(cell => cell.SeriesIndex == 0 && cell.CategoryIndex == 1)
            .Value.Should().Be(0);
        cells.Single(cell => cell.SeriesIndex == 0 && cell.CategoryIndex == 1)
            .Bounds.Should().Be(new ChartPlanRect(100, 0, 100, 60));
    }

    [Theory]
    [InlineData(ChartType.Surface)]
    [InlineData(ChartType.Surface3D)]
    public void BuildSurfaceGeometryPlan_DisplayBlanksAsSpan_InterpolatesSurfaceVertex(
        ChartType chartType)
    {
        var chart = MakeSurfaceChart(chartType);
        chart.Series[0].Values[1] = null;
        chart.DisplayBlanksAs = ChartDisplayBlanksAs.Span;

        var plan = ChartRenderPlanner.BuildSurfaceGeometryPlan(
            chart,
            new ChartPlanRect(0, 0, 360, 189));
        var north = plan.Points.Single(point => point.SeriesIndex == 0 && point.CategoryIndex == 0);
        var south = plan.Points.Single(point => point.SeriesIndex == 0 && point.CategoryIndex == 2);
        var expectedSpanPoint = new ChartPlanPoint(
            (north.Point.X + south.Point.X) / 2.0,
            (north.Point.Y + south.Point.Y) / 2.0);

        plan.RenderFacets
            .Where(facet => facet.SeriesIndex == 0 && facet.CategoryIndex == 0)
            .First()
            .Points
            .Should()
            .Contain(expectedSpanPoint,
                "surface span blanks should interpolate the missing same-row vertex");
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
        plan.FrameSegments.Should().NotBeEmpty();
        plan.FrameSegments.Select(segment => segment.Stroke.Alpha)
            .Should().OnlyContain(alpha => alpha == 220,
                "authored Surface3D keeps its existing projected-frame opacity");
        plan.FrameSegments.Select(segment => segment.Stroke.Thickness)
            .Should().OnlyContain(thickness => thickness == 0.7,
                "authored Surface3D keeps its existing projected-frame weight");
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
    public void BuildSurfaceGeometryPlan_ImportedFrameRegistrationScalesWithPlot()
    {
        var chart = MakeSurfaceChart(ChartType.Surface3D);
        chart.TextStyle = new ChartTextStyle { FontSizePt = 18.0 };

        var plan = ChartRenderPlanner.BuildSurfaceGeometryPlan(
            chart,
            new ChartPlanRect(10, 20, 720, 378));

        plan.FrameSegments[1].End.Should().Be(new ChartPlanPoint(26, 104));
        plan.FrameSegments.Select(segment => segment.Stroke.Thickness)
            .Should().OnlyContain(thickness => thickness == 0.5);
    }

    [Fact]
    public void BuildSurfaceGeometryPlan_ImportedBoundaryFacesAnchorToPlotFloorOnTallDefaultFrames()
    {
        var chart = MakeSurfaceChart(ChartType.Surface3D);
        chart.TextStyle = new ChartTextStyle { FontSizePt = 18.0 };
        chart.VaryColors = true;
        chart.Series.Add(new ChartSeries { Name = "Third Band", Values = { 28, 24, 35 } });

        var canonical = ChartRenderPlanner.BuildSurfaceGeometryPlan(
            chart,
            new ChartPlanRect(0, 0, 360, 189));
        var tall = ChartRenderPlanner.BuildSurfaceGeometryPlan(
            chart,
            new ChartPlanRect(0, 0, 280, 221));

        var canonicalYellow = canonical.RenderFacets.Single(facet =>
            facet.Fill.Color == new SrgbColor(0xE7, 0xAD, 0x00));
        canonicalYellow.Points.Select(point => point.Y)
            .Should().Equal(42.0, 25.0, 50.0);

        var tallYellow = tall.RenderFacets.Single(facet =>
            facet.Fill.Color == new SrgbColor(0xE7, 0xAD, 0x00));
        tallYellow.Points.Select(point => point.Y)
            .Should().Equal(74.0, 57.0, 82.0);
    }

    [Fact]
    public void BuildSurfaceGeometryPlan_UsesAuthoredView3DForSurfaceProjection()
    {
        var chart = MakeSurfaceChart(ChartType.Surface3D);
        var baseline = ChartRenderPlanner.BuildSurfaceGeometryPlan(
            chart,
            new ChartPlanRect(0, 0, 300, 120));

        chart.View3D = new Chart3DView
        {
            RotationX = 30,
            RotationY = 40,
            Perspective = 60,
            HeightPercent = 140,
            DepthPercent = 180,
        };
        var authoredView = ChartRenderPlanner.BuildSurfaceGeometryPlan(
            chart,
            new ChartPlanRect(0, 0, 300, 120));

        var baselinePoint = baseline.Points.Single(point =>
            point.SeriesIndex == 1 && point.CategoryIndex == 1);
        var authoredPoint = authoredView.Points.Single(point =>
            point.SeriesIndex == 1 && point.CategoryIndex == 1);
        authoredPoint.Point.X.Should().BeGreaterThan(baselinePoint.Point.X,
            "a larger authored azimuth and depth should move the rear category farther right");
        authoredPoint.Point.Y.Should().BeLessThan(baselinePoint.Point.Y,
            "a taller authored camera should raise the projected surface");
    }

    [Fact]
    public void BuildSurfaceGeometryPlan_AuthoredViewDoesNotUseImportedSurfaceRegistration()
    {
        var chart = MakeSurfaceChart(ChartType.Surface3D);
        chart.TextStyle = new ChartTextStyle { FontSizePt = 18.0 };
        chart.VaryColors = true;
        chart.View3D = new Chart3DView
        {
            RotationX = 25,
            RotationY = 35,
            Perspective = 54,
            DepthPercent = 125,
            HeightPercent = 100
        };

        var plan = ChartRenderPlanner.BuildSurfaceGeometryPlan(
            chart,
            new ChartPlanRect(0, 0, 360, 189));

        plan.RenderFacets.Should().HaveCount(2,
            "authored view3D should use the general surface mesh without imported boundary facets");
        plan.FrameSegments.Select(segment => segment.Stroke.Thickness)
            .Should().OnlyContain(thickness => thickness == 0.7,
                "authored view3D should retain the general projected-frame stroke");
        plan.FrameSegments.Select(segment => segment.Stroke.Alpha)
            .Should().OnlyContain(alpha => alpha == 220,
                "authored view3D should retain the general projected-frame opacity");
    }

    [Fact]
    public void BuildSurfaceGeometryPlan_RightAngleAxesSuppressesPerspectiveLift()
    {
        var chart = MakeSurfaceChart(ChartType.Surface3D);
        chart.View3D = new Chart3DView
        {
            RotationX = 30,
            RotationY = 40,
            Perspective = 80,
            HeightPercent = 100,
            DepthPercent = 100,
            RightAngleAxes = false
        };

        var perspective = ChartRenderPlanner.BuildSurfaceGeometryPlan(
            chart,
            new ChartPlanRect(0, 0, 300, 120));

        chart.View3D.RightAngleAxes = true;
        var rightAngle = ChartRenderPlanner.BuildSurfaceGeometryPlan(
            chart,
            new ChartPlanRect(0, 0, 300, 120));

        var perspectivePoint = perspective.Points.Single(point =>
            point.SeriesIndex == 1 && point.CategoryIndex == 1);
        var rightAnglePoint = rightAngle.Points.Single(point =>
            point.SeriesIndex == 1 && point.CategoryIndex == 1);
        rightAnglePoint.Point.Y.Should().BeGreaterThan(
            perspectivePoint.Point.Y,
            "right-angle axes remove the authored perspective lift while retaining the camera orientation");
    }

    [Fact]
    public void BuildSurfaceGeometryPlan_NonCanonicalImportedSurfaceUsesElevationPalette()
    {
        var chart = MakeSurfaceChart(ChartType.Surface3D);
        chart.TextStyle = new ChartTextStyle { FontSizePt = 18.0 };
        chart.VaryColors = true;
        chart.Series[0].Values[0] = 2;
        chart.Series[0].Values[1] = 12;
        chart.Series[0].Values[2] = 22;
        chart.Series[1].Values[0] = 32;
        chart.Series[1].Values[1] = 42;
        chart.Series[1].Values[2] = 52;

        var plan = ChartRenderPlanner.BuildSurfaceGeometryPlan(
            chart,
            new ChartPlanRect(0, 0, 300, 120));

        plan.Facets.Select(facet => facet.Fill.Color).Distinct()
            .Should().HaveCountGreaterThan(1,
                "non-canonical imported surfaces should retain PowerPoint's elevation color bands");
        plan.Facets.Should().OnlyContain(facet => facet.Fill.Alpha == 255);
    }

    [Fact]
    public void UsesProjectedSurfaceFrame_OnlyForSurface3D()
    {
        ChartRenderPlanner.UsesProjectedSurfaceFrame(new ChartShape { ChartType = ChartType.Surface3D })
            .Should().BeTrue();
        ChartRenderPlanner.UsesProjectedSurfaceFrame(new ChartShape { ChartType = ChartType.Surface })
            .Should().BeFalse();
        ChartRenderPlanner.UsesProjectedSurfaceFrame(new ChartShape { ChartType = ChartType.ColumnClustered })
            .Should().BeFalse();
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
    public void BuildAxisTitlePlans_UsesIndependentAuthoredTitleStyle()
    {
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.ValueAxis.Title = "Revenue";
        chart.ValueAxis.TitleStyle = new ChartTextStyle
        {
            FontFamily = "Aptos Display",
            FontSizePt = 15,
            Bold = true,
            Italic = true,
            Color = new ThemeAwareColor(SrgbColor.FromRgb(0x1F4E79)),
        };

        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));
        var title = ChartRenderPlanner.BuildAxisTitlePlans(chart, frame).Single();

        title.Label.FontFamily.Should().Be("Aptos Display");
        title.Label.FontSize.Should().Be(15);
        title.Label.IsBold.Should().BeTrue();
        title.Label.IsItalic.Should().BeTrue();
        title.Label.TextColor.Should().Be(SrgbColor.FromRgb(0x1F4E79));
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
            new SrgbColor(0x00, 0x00, 0x00),
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
    public void BuildMinorGridLinePrimitivePlan_UsesMinorUnitAndSkipsMajorPositions()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.ValueAxis.Min = 0;
        chart.ValueAxis.Max = 20;
        chart.ValueAxis.MajorUnit = 10;
        chart.ValueAxis.MinorUnit = 2;
        chart.ValueAxis.HasMinorGridlines = true;
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var plan = ChartRenderPlanner.BuildMinorGridLinePrimitivePlan(chart, frame);

        plan.GridLines.Should().HaveCount(8);
        plan.GridLines.Should().NotContain(line =>
            Math.Abs(line.Start.Y - (frame.Plot.Bottom - frame.Plot.Height * 0.5)) < 0.001);
        plan.Stroke.Should().Be(new ChartStrokePlan(
            new SrgbColor(0xB7, 0xB7, 0xB7),
            Alpha: 170,
            Thickness: 0.75));
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
            new SrgbColor(0x00, 0x00, 0x00),
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
    public void BuildMajorAxisTickPrimitivePlan_HonorsAuthoredMajorTickMark()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.CategoryAxis.MajorTickMark = ChartTickMark.In;
        chart.ValueAxis.MajorTickMark = ChartTickMark.In;
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var plan = ChartRenderPlanner.BuildMajorAxisTickPrimitivePlan(chart, frame);

        plan.CategoryTicks[0].Start.Should().Be(new ChartPlanPoint(134, frame.Plot.Bottom - ChartRenderPlanner.AxisMajorTickLength));
        plan.CategoryTicks[0].End.Should().Be(new ChartPlanPoint(134, frame.Plot.Bottom));
        plan.ValueTicks[0].Start.Should().Be(new ChartPlanPoint(frame.Plot.X, frame.Plot.Bottom));
        plan.ValueTicks[0].End.Should().Be(new ChartPlanPoint(frame.Plot.X + ChartRenderPlanner.AxisMajorTickLength, frame.Plot.Bottom));

        chart.ValueAxis.MajorTickMark = ChartTickMark.Cross;
        var cross = ChartRenderPlanner.BuildMajorAxisTickPrimitivePlan(chart, frame).ValueTicks[0];
        cross.Start.Should().Be(new ChartPlanPoint(frame.Plot.X - ChartRenderPlanner.AxisMajorTickLength, frame.Plot.Bottom));
        cross.End.Should().Be(new ChartPlanPoint(frame.Plot.X + ChartRenderPlanner.AxisMajorTickLength, frame.Plot.Bottom));

        chart.ValueAxis.MajorTickMark = ChartTickMark.None;
        ChartRenderPlanner.BuildMajorAxisTickPrimitivePlan(chart, frame).ValueTicks.Should().BeEmpty();
    }

    [Fact]
    public void BuildMajorAxisTickPrimitivePlan_HonorsAuthoredMinorTickMark()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.ValueAxis.Min = 0;
        chart.ValueAxis.Max = 20;
        chart.ValueAxis.MajorUnit = 10;
        chart.ValueAxis.MinorUnit = 2;
        chart.ValueAxis.MinorTickMark = ChartTickMark.In;
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var valueTicks = ChartRenderPlanner.BuildMajorAxisTickPrimitivePlan(chart, frame).ValueTicks;

        valueTicks.Should().HaveCount(11);
        valueTicks[3].Start.Should().Be(new ChartPlanPoint(frame.Plot.X, frame.Plot.Bottom - frame.Plot.Height * 0.1));
        valueTicks[3].End.Should().Be(new ChartPlanPoint(
            frame.Plot.X + ChartRenderPlanner.AxisMinorTickLength,
            frame.Plot.Bottom - frame.Plot.Height * 0.1));

        chart.ValueAxis.MinorTickMark = ChartTickMark.None;
        ChartRenderPlanner.BuildMajorAxisTickPrimitivePlan(chart, frame).ValueTicks.Should().HaveCount(3);
    }

    [Fact]
    public void BuildMajorAxisTickPrimitivePlan_MidCatCrossingMovesValueTicksToFirstCategory()
    {
        var columnChart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        columnChart.ValueAxis.CrossBetween = ChartCrossBetween.MidCat;
        var columnFrame = ChartRenderPlanner.BuildFramePlan(columnChart, new ChartPlanRect(0, 0, 400, 300));

        var columnTicks = ChartRenderPlanner.BuildMajorAxisTickPrimitivePlan(columnChart, columnFrame).ValueTicks;
        double columnCrossing = columnFrame.Plot.X + columnFrame.Plot.Width / columnChart.Categories.Count / 2.0;
        columnTicks[0].Start.Should().Be(new ChartPlanPoint(
            columnCrossing - ChartRenderPlanner.AxisMajorTickLength,
            columnFrame.Plot.Bottom));
        columnTicks[0].End.Should().Be(new ChartPlanPoint(columnCrossing, columnFrame.Plot.Bottom));

        var barChart = MakeTwoSeriesChart(ChartType.BarClustered);
        barChart.ValueAxis.CrossBetween = ChartCrossBetween.MidCat;
        var barFrame = ChartRenderPlanner.BuildFramePlan(barChart, new ChartPlanRect(0, 0, 400, 300));

        var barTicks = ChartRenderPlanner.BuildMajorAxisTickPrimitivePlan(barChart, barFrame).ValueTicks;
        double barCrossing = barFrame.Plot.Bottom - barFrame.Plot.Height / barChart.Categories.Count / 2.0;
        barTicks[0].Start.Should().Be(new ChartPlanPoint(barFrame.Plot.X, barCrossing));
        barTicks[0].End.Should().Be(new ChartPlanPoint(
            barFrame.Plot.X,
            barCrossing + ChartRenderPlanner.AxisMajorTickLength));
    }

    [Fact]
    public void BuildValueAxisLabelPlans_MidCatCrossingFollowsValueAxis()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));
        var between = ChartRenderPlanner.BuildValueAxisLabelPlans(chart, frame);

        chart.ValueAxis.CrossBetween = ChartCrossBetween.MidCat;
        var midCat = ChartRenderPlanner.BuildValueAxisLabelPlans(chart, frame);

        midCat[0].Bounds.X.Should().BeGreaterThan(between[0].Bounds.X);
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
    public void BuildScenePlan_ComboLineOverlay_UsesCenteredSmoothSquareMarkerDefaults()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.ColumnClustered,
            SecondaryValueAxis = new ChartAxis()
        };
        chart.Categories.AddRange(new[] { "Jan", "Feb", "Mar", "Apr" });

        var columns = new ChartSeries { Name = "Revenue" };
        columns.Values.AddRange(new double?[] { 120, 145, 98, 175 });
        chart.Series.Add(columns);

        var line = new ChartSeries
        {
            Name = "Units",
            OnSecondaryAxis = true,
            OverrideChartType = ChartType.LineMarkers
        };
        line.Values.AddRange(new double?[] { 5200, 6100, 4800, 7400 });
        chart.Series.Add(line);

        var scene = ChartRenderPlanner.BuildScenePlan(
            chart,
            new ChartPlanRect(0, 0, 480, 288));
        var overlay = scene.ComboLineSeries.Should().ContainSingle().Subject;
        double categoryWidth = scene.Frame.Plot.Width / chart.Categories.Count;

        overlay.WithMarkers.Should().BeTrue();
        overlay.Markers.Should().HaveCount(4);
        overlay.Markers.Select(marker => marker.Symbol)
            .Should()
            .OnlyContain(symbol => symbol == ChartMarkerPrimitiveSymbol.Square);
        overlay.Markers.Select(marker => marker.Radius)
            .Should()
            .OnlyContain(radius => Math.Abs(radius - 5.0) < 0.0001);
        overlay.IsSmoothed.Should().BeTrue();
        overlay.Points[0]!.Value.X.Should().BeApproximately(
            scene.Frame.Plot.X + categoryWidth / 2.0,
            0.0001);
        overlay.Points[^1]!.Value.X.Should().BeApproximately(
            scene.Frame.Plot.Right - categoryWidth / 2.0,
            0.0001);
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
    public void BuildSecondaryValueAxisPlan_HonorAuthoredTickLabelPosition()
    {
        var chart = MakeSecondaryAxisChart();
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));
        chart.SecondaryValueAxis!.TickLabelPosition = ChartTickLabelPosition.Low;

        var low = ChartRenderPlanner.BuildSecondaryValueAxisPrimitivePlan(chart, frame);

        low.Labels.Should().HaveCount(6);
        (low.Labels[0].Bounds.X + low.Labels[0].Bounds.Width)
            .Should().BeLessThan(frame.Plot.X);
        low.Labels[0].Alignment.Should().Be(ChartPlanTextAlignment.Right);

        chart.SecondaryValueAxis.TickLabelPosition = ChartTickLabelPosition.None;
        var none = ChartRenderPlanner.BuildSecondaryValueAxisPrimitivePlan(chart, frame);
        none.Labels.Should().BeEmpty();
        none.Ticks.Should().HaveCount(6);
    }

    [Fact]
    public void BuildSecondaryValueAxisPrimitivePlan_HonorsAuthoredMajorTickMark()
    {
        var chart = MakeSecondaryAxisChart();
        chart.SecondaryValueAxis!.MajorTickMark = ChartTickMark.Cross;
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var tick = ChartRenderPlanner.BuildSecondaryValueAxisPrimitivePlan(chart, frame).Ticks[0];

        tick.Start.Should().Be(new ChartPlanPoint(
            frame.Plot.Right - ChartRenderPlanner.AxisMajorTickLength,
            frame.Plot.Bottom));
        tick.End.Should().Be(new ChartPlanPoint(
            frame.Plot.Right + ChartRenderPlanner.AxisMajorTickLength,
            frame.Plot.Bottom));
    }

    [Fact]
    public void BuildSecondaryValueAxisPrimitivePlan_MidCatCrossingMovesRightAxisInward()
    {
        var chart = MakeSecondaryAxisChart();
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));
        chart.SecondaryValueAxis!.CrossBetween = ChartCrossBetween.MidCat;

        var plan = ChartRenderPlanner.BuildSecondaryValueAxisPrimitivePlan(chart, frame);
        double crossing = frame.Plot.Right - frame.Plot.Width / chart.Categories.Count / 2.0;

        plan.Ticks[0].Start.Should().Be(new ChartPlanPoint(crossing, frame.Plot.Bottom));
        plan.Ticks[0].End.Should().Be(new ChartPlanPoint(
            crossing + ChartRenderPlanner.AxisMajorTickLength,
            frame.Plot.Bottom));
        plan.Labels[0].Bounds.X.Should().BeLessThan(342);
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
        items[0].SwatchBounds.Should().Be(new ChartPlanRect(316, 139, 8, 8));
        items[0].Label.Text.Should().Be("Point 1");
        items[0].Label.Bounds.Should().Be(new ChartPlanRect(326, 136, 66, ChartRenderPlanner.LegendHeight));
        items[0].Fill.Should().Be(new ChartFillPlan(suppliedColor, Alpha: 255));
        items[1].SwatchBounds.Should().Be(new ChartPlanRect(316, 153, 8, 8));
        items[1].Label.Text.Should().Be("Point 2");
        items[1].Fill.Should().Be(new ChartFillPlan(new SrgbColor(0x4F, 0x81, 0xBD), Alpha: 255));
    }

    [Fact]
    public void BuildLegendItemPlans_RightBarLegend_ReversesSeriesToMatchVisualOrder()
    {
        var chart = MakeTwoSeriesChart(ChartType.BarClustered);
        chart.Legend = LegendPosition.Right;
        var colors = new[]
        {
            new SrgbColor(0x11, 0x22, 0x33),
            new SrgbColor(0x44, 0x55, 0x66)
        };
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var items = ChartRenderPlanner.BuildLegendItemPlans(chart, frame, colors);

        items.Select(item => item.Label.Text).Should().Equal("Forecast", "Actual");
        items.Select(item => item.Fill.Color).Should().Equal(colors[1], colors[0]);
        items[0].Label.Bounds.Y.Should().Be(136);
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
            LayoutTarget = "outer",
            X = 0.20,
            Y = 0.15,
            Width = 0.50,
            Height = 0.40
        };

        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        frame.Plot.Should().Be(new ChartPlanRect(80, 45, 200, 120));
    }

    [Fact]
    public void BuildFramePlan_ImportedComboMovesPlotDownWhilePreservingBottomEdge()
    {
        var chart = MakeSecondaryAxisChart();
        chart.TextStyle = new ChartTextStyle { FontSizePt = 18.0 };
        chart.Series[1].OverrideChartType = ChartType.Line;

        var frame = ChartRenderPlanner.BuildFramePlan(
            chart,
            new ChartPlanRect(0, 0, 960, 540));

        frame.Plot.Should().Be(new ChartPlanRect(70, 21, 759, 465.5));
        frame.Plot.Bottom.Should().Be(486.5);
    }

    [Fact]
    public void BuildFramePlan_ManualPlotLayout_UsesEdgeRightAndBottomCoordinates()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.PlotAreaManualLayout = new ChartManualLayout
        {
            LayoutTarget = "outer",
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
    public void BuildFramePlan_ManualPlotLayout_InnerUsesAutomaticPlotFrame()
    {
        var automaticChart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        var automaticFrame = ChartRenderPlanner.BuildFramePlan(
            automaticChart,
            new ChartPlanRect(0, 0, 400, 300));

        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.PlotAreaManualLayout = new ChartManualLayout
        {
            LayoutTarget = "inner",
            X = 0.10,
            Y = 0.20,
            Width = 0.50,
            Height = 0.40
        };

        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        frame.Plot.X.Should().BeApproximately(automaticFrame.Plot.X + automaticFrame.Plot.Width * 0.10, 0.0001);
        frame.Plot.Y.Should().BeApproximately(automaticFrame.Plot.Y + automaticFrame.Plot.Height * 0.20, 0.0001);
        frame.Plot.Width.Should().BeApproximately(automaticFrame.Plot.Width * 0.50, 0.0001);
        frame.Plot.Height.Should().BeApproximately(automaticFrame.Plot.Height * 0.40, 0.0001);
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
    public void BuildLegendItemPlans_InnerManualLayoutUsesAutomaticLegendFrame()
    {
        var outerChart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        outerChart.Legend = LegendPosition.Right;
        outerChart.LegendOverlay = true;
        outerChart.LegendManualLayout = new ChartManualLayout
        {
            LayoutTarget = "outer",
            X = 0,
            Y = 0,
            Width = 1,
            Height = 1
        };
        var innerChart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        innerChart.Legend = LegendPosition.Right;
        innerChart.LegendOverlay = true;
        innerChart.LegendManualLayout = new ChartManualLayout
        {
            LayoutTarget = "inner",
            X = 0,
            Y = 0,
            Width = 1,
            Height = 1
        };
        var bounds = new ChartPlanRect(0, 0, 400, 300);
        var colors = new[]
        {
            new SrgbColor(0x11, 0x22, 0x33),
            new SrgbColor(0x44, 0x55, 0x66)
        };

        var outerItems = ChartRenderPlanner.BuildLegendItemPlans(
            outerChart,
            ChartRenderPlanner.BuildFramePlan(outerChart, bounds),
            colors);
        var innerItems = ChartRenderPlanner.BuildLegendItemPlans(
            innerChart,
            ChartRenderPlanner.BuildFramePlan(innerChart, bounds),
            colors);

        outerItems[0].SwatchBounds.X.Should().Be(0);
        innerItems[0].SwatchBounds.X.Should().BeGreaterThan(0);
        innerItems[0].SwatchBounds.X.Should().BeLessThan(outerItems[0].Label.Bounds.Right);
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

        var first = primitives.Single(p => p.SeriesIndex == 0 && p.CategoryIndex == 0);
        first.Bounds.X.Should().BeApproximately(21.4286, 0.0001);
        first.Bounds.Y.Should().BeApproximately(80, 0.0001);
        first.Bounds.Width.Should().BeApproximately(27.5714, 0.0001);
        first.Bounds.Height.Should().BeApproximately(20, 0.0001);
        first.Fill.Should().Be(new ChartFillPlan(
            new SrgbColor(0x4F, 0x81, 0xBD), ChartRenderPlanner.RectSeriesFillAlpha));

        var second = primitives.Single(p => p.SeriesIndex == 1 && p.CategoryIndex == 0);
        second.Bounds.X.Should().BeApproximately(50, 0.0001);
        second.Bounds.Y.Should().BeApproximately(40, 0.0001);
        second.Bounds.Width.Should().BeApproximately(27.5714, 0.0001);
        second.Bounds.Height.Should().BeApproximately(60, 0.0001);
        second.Fill.Should().Be(new ChartFillPlan(
            new SrgbColor(0xC0, 0x50, 0x4D), ChartRenderPlanner.RectSeriesFillAlpha));
    }

    [Fact]
    public void BuildColumnPrimitives_ReverseCategoryAxisOrderMirrorsCategorySlots()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.CategoryAxis.ReverseOrder = true;

        var primitives = ChartRenderPlanner.BuildColumnPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        var first = primitives.Single(p => p.SeriesIndex == 0 && p.CategoryIndex == 0);
        first.Bounds.X.Should().BeApproximately(121.4286, 0.0001);
    }

    [Fact]
    public void BuildColumnPrimitives_ReverseValueAxisMirrorsValuePositions()
    {
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.Add("Jan");
        var series = new ChartSeries { Name = "Sales" };
        series.Values.Add(5);
        chart.Series.Add(series);
        var plot = new ChartPlanRect(0, 0, 200, 100);

        var normal = ChartRenderPlanner.BuildColumnPrimitives(chart, plot).Single();
        chart.ValueAxis.ReverseOrder = true;
        var reversed = ChartRenderPlanner.BuildColumnPrimitives(chart, plot).Single();

        (normal.Bounds.Y + normal.Bounds.Height).Should().BeApproximately(plot.Bottom, 0.0001);
        reversed.Bounds.Y.Should().BeApproximately(plot.Y, 0.0001);
        reversed.Bounds.Height.Should().BeApproximately(normal.Bounds.Height, 0.0001);
    }

    [Fact]
    public void BuildLineSeriesPrimitives_ReverseValueAxisMirrorsValuePositions()
    {
        var chart = new ChartShape { ChartType = ChartType.Line };
        chart.Categories.AddRange(new[] { "Jan", "Feb" });
        var series = new ChartSeries { Name = "Sales" };
        series.Values.AddRange(new double?[] { 5, 20 });
        chart.Series.Add(series);
        var plot = new ChartPlanRect(0, 0, 200, 100);

        var normal = ChartRenderPlanner.BuildLineSeriesPrimitives(chart, plot, withMarkers: true).Single();
        chart.ValueAxis.ReverseOrder = true;
        var reversed = ChartRenderPlanner.BuildLineSeriesPrimitives(chart, plot, withMarkers: true).Single();

        (normal.Points[0]!.Value.Y + reversed.Points[0]!.Value.Y).Should().BeApproximately(plot.Height, 0.0001);
        (normal.Points[1]!.Value.Y + reversed.Points[1]!.Value.Y).Should().BeApproximately(plot.Height, 0.0001);
    }

    [Fact]
    public void BuildBarPrimitives_ReverseValueAxisMovesBarsToTheOppositeValueEdge()
    {
        var chart = new ChartShape { ChartType = ChartType.BarClustered };
        chart.Categories.Add("Jan");
        var series = new ChartSeries { Name = "Sales" };
        series.Values.Add(5);
        chart.Series.Add(series);
        var plot = new ChartPlanRect(0, 0, 200, 100);

        var normal = ChartRenderPlanner.BuildBarPrimitives(chart, plot).Single();
        chart.ValueAxis.ReverseOrder = true;
        var reversed = ChartRenderPlanner.BuildBarPrimitives(chart, plot).Single();

        normal.Bounds.X.Should().BeApproximately(0, 0.0001);
        reversed.Bounds.X.Should().BeApproximately(plot.Right - normal.Bounds.Width, 0.0001);
        reversed.Bounds.Width.Should().BeApproximately(normal.Bounds.Width, 0.0001);
    }

    [Fact]
    public void ValueAxisAnnotations_ReverseTogetherWithValueGeometry()
    {
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.Add("Jan");
        var series = new ChartSeries { Name = "Sales" };
        series.Values.Add(5);
        chart.Series.Add(series);
        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 400, 300));

        var normalGrid = ChartRenderPlanner.BuildMajorGridLinePrimitivePlan(chart, frame).GridLines;
        var normalLabels = ChartRenderPlanner.BuildValueAxisLabelPlans(chart, frame);
        chart.ValueAxis.ReverseOrder = true;
        var reversedGrid = ChartRenderPlanner.BuildMajorGridLinePrimitivePlan(chart, frame).GridLines;
        var reversedLabels = ChartRenderPlanner.BuildValueAxisLabelPlans(chart, frame);

        normalGrid[0].Start.Y.Should().BeApproximately(frame.Plot.Bottom, 0.0001);
        reversedGrid[0].Start.Y.Should().BeApproximately(frame.Plot.Y, 0.0001);
        normalLabels[0].Bounds.Y.Should().BeGreaterThan(reversedLabels[0].Bounds.Y);
    }

    [Fact]
    public void BuildColumnPrimitives_ImportedLabeledStyle2ColumnsUseFullSeriesSlot()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.StyleId = 2;
        chart.TextStyle = new ChartTextStyle { FontSizePt = 18 };
        chart.DataLabels = new ChartDataLabels { ShowValue = true };

        var primitives = ChartRenderPlanner.BuildColumnPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        var first = primitives.Single(p => p.SeriesIndex == 0 && p.CategoryIndex == 0);
        first.Bounds.Width.Should().BeApproximately(28.5714, 0.0001);
    }

    [Fact]
    public void BuildColumnPrimitives_ExcludesInterleavedComboLineFromClusterGeometry()
    {
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(new[] { "Jan", "Feb" });

        var firstColumn = new ChartSeries { Name = "Revenue" };
        firstColumn.Values.AddRange(new double?[] { 120, 145 });
        chart.Series.Add(firstColumn);

        var line = new ChartSeries
        {
            Name = "Units",
            OnSecondaryAxis = true,
            OverrideChartType = ChartType.LineMarkers
        };
        line.Values.AddRange(new double?[] { 5200, 6100 });
        chart.Series.Add(line);

        var secondColumn = new ChartSeries { Name = "Series 3" };
        secondColumn.Values.AddRange(new double?[] { 2, 3 });
        chart.Series.Add(secondColumn);

        var primitives = ChartRenderPlanner.BuildColumnPrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        primitives.Should().HaveCount(4);
        var first = primitives.Single(p => p.SeriesIndex == 0 && p.CategoryIndex == 0);
        var second = primitives.Single(p => p.SeriesIndex == 2 && p.CategoryIndex == 0);
        first.Bounds.X.Should().BeApproximately(21.4286, 0.0001);
        first.Bounds.Width.Should().BeApproximately(27.5714, 0.0001);
        second.Bounds.X.Should().BeApproximately(50, 0.0001,
            "the interleaved line series must not consume a column slot");
        second.Bounds.Width.Should().BeApproximately(27.5714, 0.0001);
    }

    [Fact]
    public void ResolveBarClusterSpacing_DefaultMatchesPowerPointBarRelativeGapGeometry()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);

        var slot = ChartRenderPlanner.ResolveBarClusterSpacing(
            chart,
            categorySize: 100,
            seriesCount: 2,
            stacked: false);

        slot.CategoryStart.Should().BeApproximately(21.4286, 0.0001);
        slot.ClusterSize.Should().BeApproximately(57.1429, 0.0001);
        slot.SeriesSize.Should().BeApproximately(28.5714, 0.0001);
        slot.SeriesStep.Should().BeApproximately(28.5714, 0.0001);

        var threeSeriesSlot = ChartRenderPlanner.ResolveBarClusterSpacing(
            chart,
            categorySize: 100,
            seriesCount: 3,
            stacked: false);
        threeSeriesSlot.SeriesSize.Should().BeApproximately(22.2222, 0.0001,
            "PowerPoint's 150% gap is measured against one series column");
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
        first.Bounds.X.Should().BeApproximately(22.6786, 0.0001);
        first.Bounds.Y.Should().BeApproximately(78.75, 0.0001);
        second.Bounds.X.Should().BeApproximately(53.75, 0.0001);
        second.Bounds.Y.Should().BeApproximately(36.25, 0.0001);
    }

    [Fact]
    public void ImportedThreeDColumn_UsesPowerPointAxisAndProjectedFrameDefaults()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.TextStyle = new ChartTextStyle { FontSizePt = 18.0 };
        chart.ThreeDStyle = ChartThreeDStyle.Column;
        chart.Series[0].Values.Clear();
        chart.Series[0].Values.AddRange(new double?[] { 120, 200 });
        chart.Series[1].Values.Clear();
        chart.Series[1].Values.AddRange(new double?[] { 140, 180 });

        ChartRenderPlanner.ComputePrimaryValueAxisRange(chart)
            .Should().Be((0, 200, 20));

        var scene = ChartRenderPlanner.BuildScenePlan(
            chart,
            new ChartPlanRect(0, 0, 960, 540));

        scene.DrawFlatGrid.Should().BeFalse();
        scene.DrawProjectedThreeDBarFrame.Should().BeTrue();
        scene.AxisTicks.CategoryTicks.Should().BeEmpty();
        scene.AxisTicks.ValueTicks.Should().BeEmpty();
        scene.ValueAxisLabels.Should().HaveCount(11);

        var first = scene.Rectangles
            .Single(rectangle => rectangle.SeriesIndex == 0 && rectangle.CategoryIndex == 0);
        first.Depth.Should().NotBeNull();
        first.Depth!.Value.IsThreeD.Should().BeTrue();
        first.Depth.Value.CategorySkewY.Should().Be(ChartRenderPlanner.ImportedThreeDBarCategorySkewY);
        first.Bounds.Width.Should().BeLessThan(
            scene.Frame.Plot.Width / chart.Categories.Count,
            "PowerPoint narrows 3-D columns after applying perspective");
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

        var first = primitives.Single(p => p.SeriesIndex == 0 && p.CategoryIndex == 0);
        first.Bounds.X.Should().BeApproximately(0, 0.0001);
        first.Bounds.Y.Should().BeApproximately(75, 0.0001);
        first.Bounds.Width.Should().BeApproximately(40, 0.0001);
        first.Bounds.Height.Should().BeApproximately(13.2857, 0.0001);
        first.Fill.Should().Be(new ChartFillPlan(
            new SrgbColor(0x4F, 0x81, 0xBD), ChartRenderPlanner.RectSeriesFillAlpha));

        var second = primitives.Single(p => p.SeriesIndex == 1 && p.CategoryIndex == 0);
        second.Bounds.X.Should().BeApproximately(0, 0.0001);
        second.Bounds.Y.Should().BeApproximately(60.7143, 0.0001);
        second.Bounds.Width.Should().BeApproximately(120, 0.0001);
        second.Bounds.Height.Should().BeApproximately(13.2857, 0.0001);
        second.Fill.Should().Be(new ChartFillPlan(
            new SrgbColor(0xC0, 0x50, 0x4D), ChartRenderPlanner.RectSeriesFillAlpha));
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
        first.Bounds.Y.Should().BeApproximately(79.1667, 0.0001);
        first.Bounds.Height.Should().BeApproximately(7.3333, 0.0001);
        second.Bounds.Y.Should().BeApproximately(62.5, 0.0001);
        second.Bounds.Height.Should().BeApproximately(7.3333, 0.0001);
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
        second.Bounds.Y.Should().BeApproximately(57.3393, 0.0001);
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
    public void BuildLineSeriesPrimitives_ImportedLineMarkersUseCategoryCenters()
    {
        var series = new ChartSeries { Name = "2023" };
        series.Values.AddRange(new double?[] { 80, 100, 60, 90 });
        var chart = new ChartShape
        {
            ChartType = ChartType.LineMarkers,
            TextStyle = new ChartTextStyle { FontSizePt = 18.0, IsImplicitDefault = true }
        };
        chart.Categories.AddRange(new[] { "North", "South", "East", "West" });
        chart.Series.Add(series);

        var plan = ChartRenderPlanner.BuildLineSeriesPrimitives(
            chart,
            new ChartPlanRect(0, 0, 400, 100),
            withMarkers: true);

        plan.Should().ContainSingle();
        plan[0].Points.Select(point => point!.Value.X).Should().Equal(50.0, 150.0, 250.0, 350.0);
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
    public void BuildScatterPrimitivePlan_LegendKeyOnlyKeepsSwatchesWithoutText()
    {
        var series = new ChartSeries { Name = "XY" };
        series.XValues.AddRange(new double?[] { 0, 50 });
        series.Values.AddRange(new double?[] { 10, 20 });

        var chart = new ChartShape
        {
            ChartType = ChartType.Scatter,
            ScatterStyle = ScatterStyle.Marker,
            DataLabels = new ChartDataLabels { ShowLegendKey = true }
        };
        chart.Series.Add(series);

        var plan = ChartRenderPlanner.BuildScatterPrimitivePlan(
            chart,
            new ChartPlanRect(0, 0, 100, 100),
            new[] { new SrgbColor(0x20, 0x40, 0x60) });

        plan.DataLabels.Should().HaveCount(2);
        plan.DataLabels.Should().OnlyContain(label =>
            label.Text == string.Empty &&
            label.LegendKeyBounds.HasValue &&
            label.LegendKeyFill.HasValue &&
            label.TextBounds.HasValue);
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
    public void BuildScenePlan_OfPiePieSplitsSecondarySlicesUsingAuthoredPosition()
    {
        var series = new ChartSeries { Name = "Share" };
        series.Values.AddRange(new double?[] { 40, 30, 20, 10 });
        var chart = new ChartShape
        {
            ChartType = ChartType.OfPie,
            OfPieType = OfPieType.Pie,
            OfPieSplitType = OfPieSplitType.Position,
            OfPieSplitPosition = 2
        };
        chart.Categories.AddRange(new[] { "A", "B", "C", "D" });
        chart.Series.Add(series);

        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 480, 320));

        scene.GeometryKind.Should().Be(ChartSceneGeometryKind.Pie);
        scene.OfPieSecondaryType.Should().Be(OfPieType.Pie);
        scene.PieSlices.Select(slice => slice.PointIndex).Should().Equal(0, 1);
        scene.OfPieSecondarySlices.Select(slice => slice.PointIndex).Should().Equal(2, 3);
        scene.OfPieSecondarySlices.Select(slice => slice.Center).Distinct().Should().ContainSingle();
        scene.Rectangles.Should().BeEmpty();
    }

    [Fact]
    public void BuildScenePlan_OfPieSeriesLines_EmitsTwoConnectorsOnlyWhenEnabled()
    {
        var series = new ChartSeries { Name = "Share" };
        series.Values.AddRange(new double?[] { 40, 30, 20, 10 });
        var chart = new ChartShape
        {
            ChartType = ChartType.OfPie,
            OfPieType = OfPieType.Pie,
            OfPieSplitType = OfPieSplitType.Position,
            OfPieSplitPosition = 2,
            OfPieSeriesLinesSpecified = true
        };
        chart.Series.Add(series);

        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 480, 320));

        scene.OfPieSeriesLines.Should().HaveCount(2);
        scene.OfPieSeriesLines.Should().OnlyContain(line => line.Start.X < line.End.X);
    }

    [Fact]
    public void BuildScenePlan_OfPieSeriesLines_StaysEmptyWhenFlagIsOmitted_AndSupportsBarSecondary()
    {
        var series = new ChartSeries { Name = "Share" };
        series.Values.AddRange(new double?[] { 50, 25, 15, 10 });
        var chart = new ChartShape
        {
            ChartType = ChartType.OfPie,
            OfPieType = OfPieType.Bar,
            OfPieSplitType = OfPieSplitType.Percent,
            OfPieSplitPosition = 20
        };
        chart.Series.Add(series);

        var disabled = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 480, 320));
        disabled.OfPieSeriesLines.Should().BeEmpty();

        chart.OfPieSeriesLinesSpecified = true;
        var enabled = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 480, 320));
        enabled.OfPieSeriesLines.Should().HaveCount(2);
    }

    [Fact]
    public void BuildScenePlan_OfPieGapWidth_UsesAuthoredPlotSeparation()
    {
        var series = new ChartSeries { Name = "Share" };
        series.Values.AddRange(new double?[] { 40, 30, 20, 10 });
        var chart = new ChartShape
        {
            ChartType = ChartType.OfPie,
            OfPieType = OfPieType.Pie,
            OfPieSplitType = OfPieSplitType.Position,
            OfPieSplitPosition = 2
        };
        chart.Series.Add(series);

        var defaultScene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 480, 320));
        chart.BarGapWidthPercent = 300;
        var wideScene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 480, 320));

        double defaultDistance = defaultScene.OfPieSecondarySlices[0].Center.X - defaultScene.PieSlices[0].Center.X;
        double wideDistance = wideScene.OfPieSecondarySlices[0].Center.X - wideScene.PieSlices[0].Center.X;
        wideDistance.Should().BeGreaterThan(defaultDistance);
    }

    [Fact]
    public void BuildScenePlan_UsesAuthoredRoundedCornersFlag()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.ColumnClustered,
            RoundedCorners = true
        };

        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 480, 320));

        scene.RoundedCorners.Should().BeTrue();
        ChartRenderPlanner.RoundedChartCornerRadius.Should().Be(8.0);

        var defaultScene = ChartRenderPlanner.BuildScenePlan(
            new ChartShape { ChartType = ChartType.ColumnClustered },
            new ChartPlanRect(0, 0, 480, 320));
        defaultScene.RoundedCorners.Should().BeFalse();
    }

    [Fact]
    public void BuildScenePlan_OfPieBarUsesSecondaryColumnPrimitives()
    {
        var series = new ChartSeries { Name = "Share" };
        series.Values.AddRange(new double?[] { 50, 25, 15, 10 });
        var chart = new ChartShape
        {
            ChartType = ChartType.OfPie,
            OfPieType = OfPieType.Bar,
            OfPieSplitType = OfPieSplitType.Percent,
            OfPieSplitPosition = 20
        };
        chart.Series.Add(series);

        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 480, 320));

        scene.OfPieSecondaryType.Should().Be(OfPieType.Bar);
        scene.PieSlices.Should().HaveCount(2);
        scene.OfPieSecondarySlices.Should().BeEmpty();
        scene.Rectangles.Should().HaveCount(2);
        scene.Rectangles.Select(rectangle => rectangle.CategoryIndex).Should().Equal(2, 3);
        scene.Rectangles[0].Bounds.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void BuildScenePlan_OfPieCustomSplitUsesAuthoredPointIndices()
    {
        var series = new ChartSeries { Name = "Share" };
        series.Values.AddRange(new double?[] { 40, 30, 20, 10 });
        var chart = new ChartShape
        {
            ChartType = ChartType.OfPie,
            OfPieType = OfPieType.Pie,
            OfPieSplitType = OfPieSplitType.Custom
        };
        chart.OfPieCustomPointIndices.AddRange(new[] { 1, 3 });
        chart.Series.Add(series);

        var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 480, 320));

        scene.PieSlices.Select(slice => slice.PointIndex).Should().Equal(0, 2);
        scene.OfPieSecondarySlices.Select(slice => slice.PointIndex).Should().Equal(1, 3);
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
    public void BuildPieSlicePrimitives_ExplodesOnlyTheAuthoredPointAlongItsBisector()
    {
        var series = new ChartSeries { Name = "Share" };
        series.Values.AddRange(new double?[] { 1, 3 });
        series.PointStyles[0] = new ChartPointStyle { ExplosionPercent = 20 };
        var chart = new ChartShape { ChartType = ChartType.Pie };
        chart.Series.Add(series);

        var slices = ChartRenderPlanner.BuildPieSlicePrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        slices[0].Center.X.Should().BeApproximately(106.0104, 0.0001);
        slices[0].Center.Y.Should().BeApproximately(43.9896, 0.0001);
        slices[1].Center.Should().Be(new ChartPlanPoint(100, 50));
    }

    [Fact]
    public void BuildDoughnutSlicePrimitives_AppliesExplosionPerRingAndPoint()
    {
        var chart = new ChartShape { ChartType = ChartType.Doughnut, DoughnutHolePercent = 50 };
        var inner = new ChartSeries { Name = "Inner" };
        inner.Values.AddRange(new double?[] { 1, 1 });
        inner.PointStyles[1] = new ChartPointStyle { ExplosionPercent = 50 };
        chart.Series.Add(inner);

        var slices = ChartRenderPlanner.BuildDoughnutSlicePrimitives(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        slices[0].Center.Should().Be(new ChartPlanPoint(100, 50));
        slices[1].Center.X.Should().BeApproximately(78.75, 0.0001);
        slices[1].Center.Y.Should().BeApproximately(50, 0.0001);
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
    public void BuildPieSlicePrimitives_ImportedThreeDPiePlansPowerPointDepthAndLighting()
    {
        var series = new ChartSeries { Name = "Share" };
        series.Values.AddRange(new double?[] { 2, 3, 5 });
        var chart = new ChartShape
        {
            ChartType = ChartType.Pie,
            ThreeDStyle = ChartThreeDStyle.Pie,
            TextStyle = new ChartTextStyle { FontSizePt = 18.0 }
        };
        chart.Series.Add(series);
        var sourceColor = new SrgbColor(100, 150, 200);

        var slices = ChartRenderPlanner.BuildPieSlicePrimitives(
            chart,
            new ChartPlanRect(0, 0, 400, 200),
            seriesColors: new[] { sourceColor });

        var first = slices[0];
        first.Center.Should().Be(new ChartPlanPoint(200, 71.5));
        first.OuterRadius.Should().BeApproximately(196.0, 0.0001);
        first.OuterRadiusY.Should().BeApproximately(35.28, 0.0001);
        first.DepthOffsetY.Should().BeApproximately(66.64, 0.0001);
        first.DrawDepthSidewalls.Should().BeTrue();
        first.Fill!.Value.Color.Should().Be(new SrgbColor(92, 138, 184));
        first.DepthFill!.Value.Color.Should().Be(sourceColor);
        slices.Should().OnlyContain(slice => slice.DrawDepthSidewalls);
    }

    [Theory]
    [InlineData(0, 0.30)]
    [InlineData(1, 0.80)]
    [InlineData(2, 0.35)]
    public void ResolveImportedThreeDPieSidewallFactor_UsesPowerPointSliceLighting(
        int pointIndex,
        double expectedFactor)
    {
        ChartRenderPlanner.ResolveImportedThreeDPieSidewallFactor(
                pointIndex,
                startAngle: 0.4,
                endAngle: 1.2)
            .Should()
            .Be(expectedFactor);
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
    public void BuildDataLabelPlans_PieBestFitKeepsValuePercentLabelsInsideSlices()
    {
        var series = new ChartSeries { Name = "Share" };
        series.Values.AddRange(new double?[] { 45, 30, 15, 10 });
        var chart = new ChartShape
        {
            ChartType = ChartType.Pie,
            DataLabels = new ChartDataLabels
            {
                ShowValue = true,
                ShowPercent = true,
                Position = DataLabelPosition.BestFit
            }
        };
        chart.Series.Add(series);

        var labels = ChartRenderPlanner.BuildDataLabelPlans(
            chart,
            new ChartPlanRect(0, 0, 200, 200));

        labels.Select(label => label.Text).Should().Equal("45 45%", "30 30%", "15 15%", "10 10%");
        labels.Should().OnlyContain(label => label.Bounds.Width >= 72);
        (labels[0].Bounds.X + labels[0].Bounds.Width / 2).Should().BeLessThan(160,
            "PowerPoint resolves best-fit pie labels within their slices when they fit");
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
    public void BuildDataLabelPlans_ColumnLegendKeyOnlyKeepsSwatchesWithoutText()
    {
        var chart = MakeTwoSeriesChart(ChartType.ColumnClustered);
        chart.DataLabels = new ChartDataLabels { ShowLegendKey = true };

        var planned = ChartRenderPlanner.BuildDataLabelPlans(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        planned.Should().HaveCount(chart.Categories.Count * chart.Series.Count);
        planned.Should().OnlyContain(label =>
            label.Text == string.Empty &&
            label.LegendKeyBounds.HasValue &&
            label.LegendKeyFill.HasValue &&
            label.TextBounds.HasValue);
    }

    [Theory]
    [InlineData(ChartType.LineMarkers)]
    [InlineData(ChartType.BarClustered)]
    public void BuildDataLabelPlans_NonColumnLegendKeyOnlyKeepsSwatches(ChartType chartType)
    {
        var chart = MakeTwoSeriesChart(chartType);
        chart.DataLabels = new ChartDataLabels { ShowLegendKey = true };

        var planned = ChartRenderPlanner.BuildDataLabelPlans(
            chart,
            new ChartPlanRect(0, 0, 200, 100));

        planned.Should().HaveCount(chart.Categories.Count * chart.Series.Count);
        planned.Should().OnlyContain(label =>
            label.Text == string.Empty &&
            label.LegendKeyBounds.HasValue &&
            label.LegendKeyFill.HasValue &&
            label.TextBounds.HasValue);
    }

    [Fact]
    public void BuildDataLabelPlans_PieLegendKeyOnlyKeepsSwatchesWithoutText()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Pie,
            DataLabels = new ChartDataLabels { ShowLegendKey = true }
        };
        chart.Categories.AddRange(new[] { "Alpha", "Beta", "Gamma" });
        var series = new ChartSeries { Name = "Share" };
        series.Values.AddRange(new double?[] { 40, 35, 25 });
        chart.Series.Add(series);

        var planned = ChartRenderPlanner.BuildDataLabelPlans(
            chart,
            new ChartPlanRect(0, 0, 200, 200));

        planned.Should().HaveCount(3);
        planned.Should().OnlyContain(label =>
            label.Text == string.Empty &&
            label.LegendKeyBounds.HasValue &&
            label.LegendKeyFill.HasValue &&
            label.TextBounds.HasValue);
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

    [Fact]
    public void BuildDataLabelPlans_UsesAuthoredTextStyle()
    {
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.Add("Q1");
        var series = new ChartSeries { Name = "Sales" };
        series.Values.Add(42);
        chart.Series.Add(series);
        chart.DataLabels = new ChartDataLabels
        {
            ShowValue = true,
            TextStyle = new ChartTextStyle
            {
                FontSizePt = 12.5,
                Bold = true,
                Italic = true,
                FontFamily = "Arial"
            }
        };

        var plan = ChartRenderPlanner.BuildDataLabelPlans(
            chart,
            new ChartPlanRect(0, 0, 240, 180));

        plan.Should().ContainSingle();
        plan[0].FontSize.Should().Be(12.5);
        plan[0].IsBold.Should().BeTrue();
        plan[0].IsItalic.Should().BeTrue();
        plan[0].FontFamily.Should().Be("Arial");
    }
}
