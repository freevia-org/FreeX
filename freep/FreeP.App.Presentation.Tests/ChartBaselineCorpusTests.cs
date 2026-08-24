using FreeP.App.Compositor;
using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartBaselineCorpusTests
{
    [Fact]
    public void ChartLabelsCorpus_PreservesAuthoritativeChartStyle()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "19-chart-labels.pptx");
        var chart = PptxPackageReader.Read(deckPath).Slides[2].Shapes
            .Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;
        chart.StyleId.Should().Be(2);
        chart.ValueAxis.HasMajorGridlines.Should().BeTrue();
    }

    [Fact]
    public void ChartLabelsCorpus_PreservesAuthoredAxisDisplayTokens()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "19-chart-labels.pptx");
        var chart = PptxPackageReader.Read(deckPath).Slides[2].Shapes
            .Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;

        chart.CategoryAxis.MajorTickMark.Should().Be(ChartTickMark.Out);
        chart.CategoryAxis.MinorTickMark.Should().Be(ChartTickMark.None);
        chart.CategoryAxis.TickLabelPosition.Should().Be(ChartTickLabelPosition.NextTo);
        chart.CategoryAxis.LabelOffsetPercent.Should().Be(100);
        chart.CategoryAxis.NoMultiLevelLabels.Should().BeFalse();
        chart.ValueAxis.MajorTickMark.Should().Be(ChartTickMark.Out);
        chart.ValueAxis.MinorTickMark.Should().Be(ChartTickMark.None);
        chart.ValueAxis.TickLabelPosition.Should().Be(ChartTickLabelPosition.NextTo);
        chart.ValueAxis.CrossBetween.Should().Be(ChartCrossBetween.Between);
        chart.CategoryAxis.AutoCrossing.Should().BeTrue();
        chart.CategoryAxis.LabelAlignment.Should().Be(ChartLabelAlignment.Center);
        chart.CategoryAxis.Crosses.Should().Be(ChartAxisCrossing.AutoZero);
        chart.ValueAxis.Crosses.Should().Be(ChartAxisCrossing.AutoZero);
    }

    [Fact]
    public void ChartBaselineCorpus_ImportedStockUsesPowerPointBlackGridAndAxisStrokes()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "22-chart-baseline-depth.pptx");
        var chart = PptxPackageReader.Read(deckPath).Slides[0].Shapes
            .Single(shape => shape.Chart?.ChartType == ChartType.Stock).Chart!;

        var scene = ChartRenderPlanner.BuildScenePlan(
            chart,
            new ChartPlanRect(0, 0, 480, 288));
        var expectedStroke = new ChartStrokePlan(
            new SrgbColor(0x00, 0x00, 0x00),
            Alpha: 255,
            Thickness: 1.0);

        scene.GridLines.Stroke.Should().Be(expectedStroke);
        scene.GridLines.GridLines.Should().HaveCount(14,
            "PowerPoint renders ten stock value lines plus four category boundaries");
        var stockValueGridLines = scene.GridLines.GridLines.Take(10).ToArray();
        stockValueGridLines[0].Start.Y
            .Should().BeApproximately(scene.Frame.Plot.Bottom, 0.0001,
                "PowerPoint rasterizes imported stock value gridlines on integer pixel rows");
        stockValueGridLines[^1].Start.Y
            .Should().BeApproximately(scene.Frame.Plot.Y, 0.0001,
                "the imported stock value grid spans the complete plot height");
        scene.AxisTicks.Stroke.Should().Be(expectedStroke);
        scene.AxisTicks.CategoryTicks.Should().HaveCount(7,
            "PowerPoint renders three category-center ticks plus four minor boundary ticks");
        scene.AxisTicks.ValueTicks.Should().HaveCount(46,
            "PowerPoint renders the stock value axis at 0.4-unit minor intervals");
        scene.AxisTicks.CategoryTicks.Skip(3).Select(tick => tick.Start.X)
            .Should().Equal(
                scene.Frame.Plot.X,
                scene.Frame.Plot.X + scene.Frame.Plot.Width / 3,
                scene.Frame.Plot.X + scene.Frame.Plot.Width * 2 / 3,
                scene.Frame.Plot.Right);
    }

    [Fact]
    public void ChartLabelsCorpus_ImportedComboUsesPowerPointAxisIntervalsAndGeneralLabels()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "19-chart-labels.pptx");
        var chart = PptxPackageReader.Read(deckPath).Slides[2].Shapes
            .Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;

        chart.Series.Should().Contain(series => series.OnSecondaryAxis);
        ChartRenderPlanner.ComputePrimaryValueAxisRange(chart)
            .Should().Be((0, 200, 20));
        ChartRenderPlanner.ComputeSecondaryValueAxisRange(chart)
            .Should().Be((0, 8000, 1000));

        var scene = ChartRenderPlanner.BuildScenePlan(
            chart,
            new ChartPlanRect(0, 0, 960, 540));
        scene.ValueAxisLabels.Select(label => label.Text)
            .Should().Equal("0", "20", "40", "60", "80", "100", "120", "140", "160", "180", "200");
        scene.SecondaryAxis.Labels.Select(label => label.Text)
            .Should().Equal("0", "1000", "2000", "3000", "4000", "5000", "6000", "7000", "8000");
        scene.SecondaryAxis.Ticks.Should().HaveCount(
            9 + 8 * (ChartRenderPlanner.ImportedComboSecondaryMinorTickDivisions - 1));
    }

    [Fact]
    public void ChartLabelsCorpus_ImportedComboUsesPowerPointOverlayAndLegendStyling()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "19-chart-labels.pptx");
        var chart = PptxPackageReader.Read(deckPath).Slides[2].Shapes
            .Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;

        var scene = ChartRenderPlanner.BuildScenePlan(
            chart,
            new ChartPlanRect(0, 0, 960, 540));

        scene.GridLines.Stroke.Should().Be(new ChartStrokePlan(
            new SrgbColor(0x89, 0x89, 0x89),
            Alpha: 255,
            Thickness: 1.0));
        scene.AxisTicks.Stroke.Should().Be(new ChartStrokePlan(
            new SrgbColor(0x89, 0x89, 0x89),
            Alpha: 255,
            Thickness: 0.75));
        scene.SecondaryAxis.TickStroke.Should().Be(scene.AxisTicks.Stroke);

        var overlay = scene.ComboLineSeries.Should().ContainSingle().Subject;
        overlay.Stroke.Thickness.Should().Be(ChartRenderPlanner.ImportedLineSeriesStrokeThickness);
        scene.LegendItems.Should().HaveCount(3);
        scene.LegendItems[0].SwatchBounds.Width.Should().Be(ChartRenderPlanner.ImportedComboLegendSwatchWidth);
        scene.LegendItems[0].SwatchBounds.Height.Should().Be(ChartRenderPlanner.ImportedComboLegendSwatchHeight);
        scene.LegendItems[1].SwatchBounds.Height.Should().Be(ChartRenderPlanner.ImportedComboLegendSwatchHeight);
        scene.LegendItems[2].SwatchBounds.Width.Should().Be(ChartRenderPlanner.ImportedComboLegendSwatchWidth);
        scene.LegendItems[2].IsLine.Should().BeTrue();
        (scene.LegendItems[1].Label.Bounds.Y - scene.LegendItems[0].Label.Bounds.Y)
            .Should().Be(ChartRenderPlanner.ImportedComboLegendLineHeight);
    }

    [Fact]
    public void ChartLabelsCorpus_ImportedLabeledGridCarriesWpfOnlySnapHint()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "19-chart-labels.pptx");
        var chart = PptxPackageReader.Read(deckPath).Slides[0].Shapes
            .Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;

        var scene = ChartRenderPlanner.BuildScenePlan(
            chart,
            new ChartPlanRect(0, 0, 1280, 720));

        scene.UseWpfPixelSnappedImportedGrid.Should().BeTrue();
    }

    [Fact]
    public void ChartLabelsCorpus_ImportedCartesianLabelsUsePowerPointGeometry()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "19-chart-labels.pptx");
        var chart = PptxPackageReader.Read(deckPath).Slides[2].Shapes
            .Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;

        var scene = ChartRenderPlanner.BuildScenePlan(
            chart,
            new ChartPlanRect(0, 0, 960, 540));

        scene.CategoryAxisLabels[0].Bounds.Y
            .Should().Be(scene.Frame.Plot.Bottom + ChartRenderPlanner.ImportedCartesianCategoryLabelOffset);
        scene.ValueAxisLabels[^1].Bounds.Right
            .Should().Be(scene.Frame.Plot.X - ChartRenderPlanner.ImportedCartesianValueLabelRightGap);
        scene.ValueAxisLabels[^1].Bounds.Y
            .Should().Be(scene.Frame.Plot.Y - ChartRenderPlanner.ImportedCartesianValueLabelVerticalOffset);
        scene.SecondaryAxis.Labels[^1].Bounds.Y
            .Should().Be(scene.Frame.Plot.Y - ChartRenderPlanner.ImportedCartesianValueLabelVerticalOffset);
    }

    [Fact]
    public void ChartLabelsCorpus_PiePercentLabelsPreservePowerPointSeparatorAndAutomaticTitle()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "19-chart-labels.pptx");
        var presentation = PptxPackageReader.Read(deckPath);
        var pie = presentation.Slides
            .SelectMany(slide => slide.Shapes)
            .Where(shape => shape.Kind == SlideShapeKind.Chart)
            .Select(shape => shape.Chart!)
            .Single(chart => chart.ChartType == ChartType.Pie);

        pie.HasAutomaticTitle.Should().BeTrue();
        pie.Title.Should().Be("Share");
        pie.DataLabels.Should().NotBeNull();
        pie.DataLabels!.ShowValue.Should().BeTrue();
        pie.DataLabels.ShowPercent.Should().BeTrue();
        pie.DataLabels.Separator.Should().Be(", ");

        var scene = ChartRenderPlanner.BuildScenePlan(
            pie,
            new ChartPlanRect(0, 0, 1280, 720));
        scene.LegendItems.Should().HaveCount(4);
        scene.LegendItems.Should().OnlyContain(item =>
            item.SwatchBounds.Width == ChartRenderPlanner.ImportedPieLegendSwatchSize &&
            item.SwatchBounds.Height == ChartRenderPlanner.ImportedPieLegendSwatchSize);
        (scene.LegendItems[1].SwatchBounds.Y - scene.LegendItems[0].SwatchBounds.Y)
            .Should().Be(ChartRenderPlanner.ImportedPieLegendLineHeight);
        scene.LegendItems[0].Label.TextColor
            .Should().Be(new SrgbColor(0x00, 0x00, 0x00));
        scene.LegendItems.Should().OnlyContain(item =>
            item.Label.FontFamily == ChartRenderPlanner.ImportedPieLegendFontFamily &&
            item.Label.HorizontalScale == ChartRenderPlanner.ImportedPieLegendTextScaleX &&
            item.Label.Bounds.Y == item.SwatchBounds.Y +
                ChartRenderPlanner.ImportedPieLegendLabelOffset - 3.0);
        scene.DataLabels.Should().OnlyContain(label =>
            label.TextColor == new SrgbColor(0x00, 0x00, 0x00));
    }

    [Fact]
    public void ChartTypesCorpus_PreservesPowerPointAutomaticTitlesForSingleSeriesCharts()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "18-chart-types.pptx");
        var presentation = PptxPackageReader.Read(deckPath);
        var charts = presentation.Slides
            .SelectMany(slide => slide.Shapes)
            .Where(shape => shape.Kind == SlideShapeKind.Chart)
            .Select(shape => shape.Chart!)
            .ToArray();

        charts[0].Title.Should().Be("Share");
        charts[0].HasAutomaticTitle.Should().BeTrue();
        charts[1].Title.Should().Be("Bubbles");
        charts[1].HasAutomaticTitle.Should().BeTrue();
        charts[3].Title.Should().Be("Series1");
        charts[3].HasAutomaticTitle.Should().BeTrue();

        var doughnutScene = ChartRenderPlanner.BuildScenePlan(
            charts[0],
            new ChartPlanRect(0, 0, 1280, 720));
        doughnutScene.LegendItems[0].SwatchBounds.X
            .Should().BeGreaterThan(doughnutScene.Frame.Bounds.Right - doughnutScene.Frame.LegendAreaWidth);
    }

    [Fact]
    public void ChartTypesCorpus_ImportedLineMarkerScatterUsesPowerPointAxesAndMarkerLegend()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "18-chart-types.pptx");
        var chart = PptxPackageReader.Read(deckPath).Slides[1].Shapes
            .Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;

        chart.ChartType.Should().Be(ChartType.Scatter);
        chart.ScatterStyle.Should().Be(ScatterStyle.LineMarker);
        chart.ValueAxis.HasMajorGridlines.Should().BeFalse(
            "the imported bottom X axis has no major gridlines in PowerPoint");
        chart.SecondaryValueAxis?.HasMajorGridlines.Should().BeTrue(
            "the imported left Y axis carries the major gridline setting");
        ChartRenderPlanner.ComputeScatterAxisRange(chart, useX: true)
            .Should().Be((0, 6, 1));
        ChartRenderPlanner.ComputePrimaryValueAxisRange(chart)
            .Should().Be((0, 4.5, 0.5));

        var scene = ChartRenderPlanner.BuildScenePlan(
            chart,
            new ChartPlanRect(0, 0, 1280, 720));

        scene.Frame.Plot.Should().Be(new ChartPlanRect(63, 68, 1068, 599));
        scene.DrawFlatGrid.Should().BeFalse(
            "scatter primitives own the axis grid so the generic frame grid is not duplicated");
        scene.Scatter.Should().NotBeNull();
        scene.Scatter!.Value.GridLines.Should().HaveCount(10,
            "PowerPoint renders ten horizontal Y-axis intervals and no vertical X-axis gridlines");
        scene.Scatter.Value.XAxisLabels.Select(label => label.Text)
            .Should().Equal("0", "1", "2", "3", "4", "5", "6");
        scene.Scatter.Value.YAxisLabels.Select(label => label.Text)
            .Should().Equal("0", "0.5", "1", "1.5", "2", "2.5", "3", "3.5", "4", "4.5");
        scene.Scatter.Value.GridLineStroke.Should().Be(new ChartStrokePlan(
            new SrgbColor(0x89, 0x89, 0x89),
            Alpha: 255,
            Thickness: 1.0));

        var legend = scene.LegendItems.Should().ContainSingle().Subject;
        legend.Label.Text.Should().Be("Bubbles");
        legend.MarkerSymbol.Should().Be(ChartMarkerPrimitiveSymbol.Diamond);
        legend.SwatchBounds.Width.Should().Be(12);
        legend.SwatchBounds.Height.Should().Be(12);
        scene.Scatter.Value.Series.Should().ContainSingle().Which.Markers
            .Should().OnlyContain(marker =>
                marker.Symbol == ChartMarkerPrimitiveSymbol.Diamond &&
                marker.Radius == 6.5);
    }

    [Fact]
    public void ChartTypesCorpus_BubbleWithoutSizesKeepsAxesButDoesNotInventBubbles()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "18-chart-types.pptx");
        var chart = PptxPackageReader.Read(deckPath).Slides[3].Shapes
            .Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;

        chart.ChartType.Should().Be(ChartType.Bubble);
        chart.Series.Should().ContainSingle().Which.BubbleSizes.Should().BeEmpty();

        var scene = ChartRenderPlanner.BuildScenePlan(
            chart,
            new ChartPlanRect(0, 0, 960, 540));

        scene.Bubble.Should().NotBeNull();
        scene.Bubble!.Value.Bubbles.Should().BeEmpty(
            "PowerPoint leaves a bubble chart with no c:bubbleSize data empty");
        scene.Bubble.Value.XAxisLabels.Select(label => label.Text)
            .Should().Equal("0", "1", "2", "3", "4", "5", "6");
        scene.Bubble.Value.YAxisLabels.Select(label => label.Text)
            .Should().Equal("0", "5", "10", "15", "20", "25", "30", "35", "40", "45", "50");
        scene.Bubble.Value.GridLines.Should().HaveCount(11,
            "PowerPoint renders eleven horizontal Y-axis intervals and no vertical X-axis gridlines");
        scene.Bubble.Value.GridLineStroke.Should().Be(new ChartStrokePlan(
            new SrgbColor(0x89, 0x89, 0x89),
            Alpha: 255,
            Thickness: 1.0));
        scene.DrawFlatGrid.Should().BeFalse();
        var legend = scene.LegendItems.Should().ContainSingle().Subject;
        legend.Label.Text.Should().Be("Series1");
        legend.MarkerSymbol.Should().Be(ChartMarkerPrimitiveSymbol.Circle);
        legend.SwatchBounds.X.Should().BeApproximately(
            scene.Frame.Plot.Right + ChartRenderPlanner.ImportedBubbleLegendRightGap,
            0.0001);
        legend.SwatchBounds.Y.Should().BeApproximately(
            scene.Frame.Plot.Y + (scene.Frame.Plot.Height - 28.0) / 2.0 +
            ChartRenderPlanner.ImportedBubbleLegendVerticalOffset + 3.0,
            0.0001);
        legend.Label.Bounds.X.Should().BeApproximately(
            legend.SwatchBounds.X + ChartRenderPlanner.ImportedBubbleLegendLabelInset,
            0.0001);
    }

    [Fact]
    public void ChartTypesCorpus_RadarUsesPowerPointScaleAndFullImportedLabels()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "18-chart-types.pptx");
        var chart = PptxPackageReader.Read(deckPath).Slides[2].Shapes
            .Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;

        chart.ChartType.Should().Be(ChartType.Radar);
        chart.RadarStyle.Should().Be(RadarStyle.Marker);
        chart.Categories.Should().Equal("Speed", "Power", "Agility", "Stamina", "Tech");

        var frame = ChartRenderPlanner.BuildFramePlan(chart, new ChartPlanRect(0, 0, 1280, 720));
        var plan = ChartRenderPlanner.BuildRadarPrimitivePlan(chart, frame.Plot);

        plan.Rings.Should().HaveCount(9);
        plan.ValueLabels.Select(label => label.Text)
            .Should().Equal("0", "10", "20", "30", "40", "50", "60", "70", "80", "90");
        plan.ValueLabels.Should().OnlyContain(label =>
            label.Bounds.X == plan.ValueLabels[0].Bounds.X &&
            label.Bounds.Width == 48.0);
        plan.ValueLabels[0].Bounds.X.Should().BeApproximately(
            frame.Plot.X + frame.Plot.Width / 2 + ChartRenderPlanner.ImportedRadarCenterOffsetX - 58.0 +
            ChartRenderPlanner.ImportedRadarValueLabelOffsetX,
            0.0001);
        plan.CategoryLabels.Select(label => label.Text)
            .Should().Equal("Speed", "Power", "Agility", "Stamina", "Tech");
        plan.CategoryLabels.Should().OnlyContain(label => !label.Text.Contains("...", StringComparison.Ordinal));
        plan.Series.Should().HaveCount(2);
        ChartRenderPlanner.ImportedRadarSeriesStrokeThickness.Should().Be(4.0);
        plan.Series.Should().OnlyContain(series =>
            series.Stroke.Thickness == ChartRenderPlanner.ImportedRadarSeriesStrokeThickness);

        var scene = ChartRenderPlanner.BuildScenePlan(
            chart,
            new ChartPlanRect(0, 0, 1280, 720));
        scene.LegendItems.Should().HaveCount(2);
        scene.LegendItems.Should().OnlyContain(item => item.IsLine && item.IsLineOnly);
        scene.LegendItems.Should().OnlyContain(item =>
            item.SwatchBounds.Width == ChartRenderPlanner.ImportedRadarLegendSwatchWidth &&
            item.SwatchBounds.Height == ChartRenderPlanner.ImportedRadarLegendSwatchHeight);
        scene.LegendItems[0].Label.Bounds.X
            .Should().BeApproximately(
                scene.LegendItems[0].SwatchBounds.X + ChartRenderPlanner.ImportedRadarLegendLabelInset,
                0.0001);
        (scene.LegendItems[1].SwatchBounds.Y - scene.LegendItems[0].SwatchBounds.Y)
            .Should().Be(ChartRenderPlanner.ImportedRadarLegendLineHeight);
    }

    [Fact]
    public void ChartsCorpus_Style2ColumnAndBarLegendsUsePowerPointKeyGeometry()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "06-charts.pptx");
        var presentation = PptxPackageReader.Read(deckPath);
        var charts = new[] { 0, 3 }
            .Select(slideIndex => presentation.Slides[slideIndex].Shapes
                .Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!)
            .ToArray();

        var column = ChartRenderPlanner.BuildScenePlan(charts[0], new ChartPlanRect(0, 0, 1280, 720));
        var bar = ChartRenderPlanner.BuildScenePlan(charts[1], new ChartPlanRect(0, 0, 1280, 720));

        column.LegendItems.Should().HaveCount(2);
        bar.LegendItems.Should().HaveCount(2);
        column.LegendItems.Should().OnlyContain(item =>
            item.SwatchBounds.Width == ChartRenderPlanner.ImportedStyle2LegendSwatchSize &&
            item.SwatchBounds.Height == ChartRenderPlanner.ImportedStyle2LegendSwatchSize);
        bar.LegendItems.Should().OnlyContain(item =>
            item.SwatchBounds.Width == ChartRenderPlanner.ImportedStyle2LegendSwatchSize &&
            item.SwatchBounds.Height == ChartRenderPlanner.ImportedStyle2LegendSwatchSize);
        (column.LegendItems[1].SwatchBounds.Y - column.LegendItems[0].SwatchBounds.Y)
            .Should().Be(ChartRenderPlanner.ImportedStyle2LegendLineHeight);
        (bar.LegendItems[1].SwatchBounds.Y - bar.LegendItems[0].SwatchBounds.Y)
            .Should().Be(ChartRenderPlanner.ImportedStyle2LegendLineHeight);
        column.LegendItems[0].SwatchBounds.X
            .Should().BeApproximately(
                column.LegendItems[0].Label.Bounds.X - ChartRenderPlanner.ImportedStyle2LegendLabelInset,
                0.0001);
        bar.LegendItems[0].SwatchBounds.X
            .Should().BeApproximately(
                bar.LegendItems[0].Label.Bounds.X - ChartRenderPlanner.ImportedStyle2LegendLabelInset,
                0.0001);
    }

    [Fact]
    public void ChartBaselineDepthCorpusDeck_ExercisesSharedPlannerDecisions()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "22-chart-baseline-depth.pptx");
        var presentation = PptxPackageReader.Read(deckPath);
        var charts = presentation.Slides
            .SelectMany(slide => slide.Shapes)
            .Where(shape => shape.Kind == SlideShapeKind.Chart)
            .Select(shape => shape.Chart!)
            .ToArray();

        charts.Select(chart => chart.ChartType)
            .Should()
            .Contain(new[]
            {
                ChartType.Stock,
                ChartType.Surface3D,
                ChartType.Scatter,
                ChartType.ColumnStacked100,
            });

        var scenes = charts
            .Select(chart => ChartRenderPlanner.BuildScenePlan(
                chart,
                new ChartPlanRect(0, 0, 480, 288)))
            .ToArray();
        scenes.Should().OnlyContain(scene => scene.Frame.HasPlot);
        scenes.Select(scene => scene.GeometryKind).Should().Contain(new[]
        {
            ChartSceneGeometryKind.Stock,
            ChartSceneGeometryKind.Surface,
            ChartSceneGeometryKind.Scatter,
            ChartSceneGeometryKind.Column,
        });

        var stock = charts.Single(chart => chart.ChartType == ChartType.Stock);
        stock.HasHighLowLines.Should().BeFalse(
            "the PowerPoint baseline stock chart relies on its line-series fallback");
        scenes.Single(scene => scene.GeometryKind == ChartSceneGeometryKind.Stock)
            .LineSeries.Should().HaveCount(4);
        var stockFallback = ChartRenderPlanner.BuildStockFallbackLineSeriesPrimitives(
            stock,
            new ChartPlanRect(0, 0, 360, 220));
        stockFallback.Should().HaveCount(4);
        stockFallback.Should().OnlyContain(series => series.Markers.Count == 3);
        stockFallback.Select(series => series.Markers[0].Symbol).Should().Equal(
            ChartMarkerPrimitiveSymbol.Diamond,
            ChartMarkerPrimitiveSymbol.Square,
            ChartMarkerPrimitiveSymbol.Triangle,
            ChartMarkerPrimitiveSymbol.X);
        stockFallback[0].Points[0]!.Value.X.Should().BeApproximately(60, 0.0001,
            "stock fallback points sit at PowerPoint category-band centers");
        ChartRenderPlanner.ComputePrimaryValueAxisRange(stock).Should().Be((0, 18, 2),
            "PowerPoint gives the fallback its denser stock-chart value scale");
        var stockPlan = ChartRenderPlanner.BuildStockPrimitivePlan(
            stock,
            new ChartPlanRect(0, 0, 360, 220));
        stockPlan.HighLowLines.Should().HaveCount(3);
        stockPlan.CloseTicks.Select(tick => tick.PriceMove)
            .Should()
            .Equal(
                ChartStockPriceMove.Rising,
                ChartStockPriceMove.Falling,
                ChartStockPriceMove.Unchanged);

        var surface = charts.Single(chart => chart.ChartType == ChartType.Surface3D);
        surface.VaryColors.Should().BeTrue();
        surface.View3D.Should().BeNull();
        scenes.Single(scene => scene.GeometryKind == ChartSceneGeometryKind.Surface)
            .Surface.Should().NotBeNull();
        var surfaceFrame = ChartRenderPlanner.BuildFramePlan(surface, new ChartPlanRect(0, 0, 480, 288));
        surfaceFrame.Plot
            .Should().Be(new ChartPlanRect(44, 57, 360, 189),
                "PowerPoint reserves a dedicated projected frame for classic Surface3D charts");
        var surfaceCategoryLabels = ChartRenderPlanner.BuildCategoryAxisLabelPlans(surface, surfaceFrame);
        surfaceCategoryLabels.Select(label => label.Text)
            .Should().Equal("North", "East", "South");
        surfaceCategoryLabels[0].Bounds.Y
            .Should().BeLessThan(surfaceCategoryLabels[^1].Bounds.Y,
                "PowerPoint projects Surface3D category labels down toward the far-right category");
        surfaceCategoryLabels[0].Bounds.X
            .Should().BeApproximately(31, 0.0001);
        surfaceCategoryLabels[^1].Bounds.X
            .Should().BeApproximately(331, 0.0001);
        var surfaceCells = ChartRenderPlanner.BuildSurfaceCellPrimitives(
            surface,
            new ChartPlanRect(0, 0, 360, 220));
        surfaceCells.Should().HaveCount(8);
        surfaceCells.Should().NotContain(cell => cell.SeriesIndex == 0 && cell.CategoryIndex == 1);
        surfaceCells.Single(cell => cell.SeriesIndex == 0 && cell.CategoryIndex == 2).Bounds.X
            .Should()
            .BeGreaterThan(surfaceCells.Single(cell => cell.SeriesIndex == 0 && cell.CategoryIndex == 0).Bounds.X);
        ChartRenderPlanner.ComputePrimaryValueAxisRange(surface)
            .Should().Be((0, 40, 10));
        var surfaceGeometry = ChartRenderPlanner.BuildSurfaceGeometryPlan(
            surface,
            new ChartPlanRect(0, 0, 360, 189));
        surfaceGeometry.Points.Single(point => point.SeriesIndex == 0 && point.CategoryIndex == 0).Point.Y
            .Should().BeApproximately(137.5, 0.0001,
                "Surface3D height follows the PowerPoint value axis instead of normalizing to the smallest data value");
        surfaceGeometry.Points.Single(point => point.SeriesIndex == 1 && point.CategoryIndex == 0).Point.Y
            .Should().BeApproximately(98.93, 0.0001,
                "the imported COM mesh registers the middle-row North vertex below the shared projection");
        surfaceGeometry.Points.Single(point => point.SeriesIndex == 2 && point.CategoryIndex == 0).Point.Y
            .Should().BeApproximately(25.86, 0.0001,
                "the imported COM mesh registers the rear-row North vertex below the shared projection");
        surfaceGeometry.Facets.Should().HaveCount(4);
        surfaceGeometry.WireframeSegments.Should().HaveCountGreaterThan(surfaceGeometry.Facets.Count);
        surfaceGeometry.ContourSegments.Should().BeEmpty(
            "PowerPoint's imported Surface3D baseline uses the projected frame and wireframe without contour overlays");
        surfaceGeometry.Facets.Count(facet => facet.Points.Count == 3).Should().Be(2);
        surfaceGeometry.Facets.Count(facet => facet.Points.Count == 4).Should().Be(2);
        surfaceGeometry.RenderFacets.Should().HaveCount(15,
            "imported PowerPoint Surface3D cells render a continuous triangulated surface and projected boundary faces");
        surfaceGeometry.AlternateRenderFacets.Should().HaveCount(20,
            "WPF applies five measured green-face registration overlays only to the imported default camera");
        surfaceGeometry.RenderFacets.Should().OnlyContain(facet => facet.Points.Count == 3);
        var firstSurfaceCellFacets = surfaceGeometry.RenderFacets
            .Where(facet => facet.SeriesIndex == 0 && facet.CategoryIndex == 0)
            .ToArray();
        firstSurfaceCellFacets.Should().HaveCount(2);
        firstSurfaceCellFacets[0].Points.Select(point => point.X)
            .Should().Equal(new[] { 3.5, 174.25, 65.5 },
                "PowerPoint splits the imported blank low-band cell along the first 0-3 triangle");
        firstSurfaceCellFacets[1].Points.Select(point => point.X)
            .Should().Equal(new[] { 174.25, 199.875, 29.5 },
                "the paired imported blank-cell triangle owns the widened light-orange value-axis boundary");
        var darkOrangeFacet = surfaceGeometry.RenderFacets
            .Single(facet => facet.SeriesIndex == 0 && facet.CategoryIndex == 1 &&
                facet.Fill.Color == new SrgbColor(0xB7, 0x60, 0x26));
        darkOrangeFacet.Points[0].X.Should().BeApproximately(161.25, 0.0001,
            "the imported dark-orange face owns its widened left edge without moving the shared blank vertex");
        firstSurfaceCellFacets[0].Points[1].Y
            .Should()
            .BeApproximately(158.1, 0.0001,
                "PowerPoint registers the imported blank low-band vertex below the interpolated surface");
        surfaceGeometry.Points.Single(point => point.SeriesIndex == 2 && point.CategoryIndex == 0).Point.X
            .Should().BeApproximately(127.5, 0.0001,
                "PowerPoint's rear-left surface vertex follows the projected frame depth wall");
        surfaceGeometry.RenderFacets.Should().OnlyContain(facet => facet.Fill.Alpha == 255,
            "PowerPoint's imported Surface3D facets are opaque fills");
        surfaceGeometry.RenderFacets.Should().OnlyContain(facet => facet.Stroke.Alpha == 0,
            "PowerPoint's imported Surface3D faces do not draw opaque white facet outlines");
        surfaceGeometry.RenderFacets
            .Take(8)
            .Select(facet => facet.Fill.Color)
            .Should()
            .Equal(
                new SrgbColor(0x99, 0xBD, 0x80),
                new SrgbColor(0xA3, 0xC9, 0x89),
                new SrgbColor(0x97, 0xBD, 0x80),
                new SrgbColor(0x99, 0xBD, 0x80),
                new SrgbColor(0x44, 0x74, 0xC7),
                new SrgbColor(0xF1, 0x80, 0x32),
                new SrgbColor(0xB7, 0x60, 0x26),
                new SrgbColor(0x97, 0xBD, 0x80));
        surfaceGeometry.RenderFacets
            .Skip(8)
            .Select(facet => facet.Fill.Color)
            .Should()
            .Equal(
                new SrgbColor(0xD5, 0x70, 0x2C),
                new SrgbColor(0xD5, 0x70, 0x2C),
                new SrgbColor(0xD5, 0x70, 0x2C),
                new SrgbColor(0x34, 0x58, 0x97),
                new SrgbColor(0x8B, 0xAB, 0x74),
                new SrgbColor(0xE7, 0xAD, 0x00),
                new SrgbColor(0x81, 0xA1, 0x6E));
        surfaceGeometry.RenderFacets[11].Points.Select(point => point.X)
            .Should().Equal(new[] { 144.0, 172.0, 234.0 },
                "the imported blue boundary face follows the widened COM registration");
        surfaceGeometry.RenderFacets[11].Points.Select(point => point.Y)
            .Should().Equal(new[] { 167.0, 121.0, 153.0 },
                "the imported blue boundary face retains the measured top-edge registration");
        surfaceGeometry.RenderFacets[8].Points.Select(point => point.X)
            .Should().Equal(new[] { 1.0, 72.0, 132.0 },
                "PowerPoint exposes a separate near-left dark-orange boundary triangle");
        surfaceGeometry.RenderFacets[8].Points.Select(point => point.Y)
            .Should().Equal(new[] { 125.0, 71.0, 71.0 },
                "the imported near-left boundary triangle uses the measured projected wall");
        surfaceGeometry.RenderFacets[9].Points.Select(point => point.X)
            .Should().Equal(new[] { 1.0, 132.0, 174.0 },
                "the paired near-left triangle closes the measured projected polygon");
        surfaceGeometry.RenderFacets[9].Points.Select(point => point.Y)
            .Should().Equal(new[] { 125.0, 71.0, 79.0 },
                "the paired near-left triangle retains the measured projected wall");
        surfaceGeometry.RenderFacets[10].Points.Select(point => point.X)
            .Should().Equal(new[] { 245.0, 319.0, 312.0 },
                "PowerPoint exposes a separate right-side dark-orange boundary triangle");
        surfaceGeometry.RenderFacets[10].Points.Select(point => point.Y)
            .Should().Equal(new[] { 99.0, 119.0, 137.0 },
                "the imported right-side boundary triangle uses the measured projected wall");
        surfaceGeometry.RenderFacets[12].Points.Select(point => point.X)
            .Should().Equal(new[] { 201.0, 232.0, 306.0 },
                "the imported rear green boundary face uses the measured right registration");
        surfaceGeometry.RenderFacets[12].Points.Select(point => point.Y)
            .Should().Equal(new[] { 72.0, 42.0, 33.0 },
                "the imported rear green boundary face uses the measured vertical registration");
        surfaceGeometry.RenderFacets[13].Points.Select(point => point.X)
            .Should().Equal(new[] { 301.0, 360.0, 349.0 },
                "the imported yellow boundary face uses the measured horizontal registration");
        surfaceGeometry.RenderFacets[13].Points.Select(point => point.Y)
            .Should().Equal(new[] { 42.0, 25.0, 50.0 },
                "the imported yellow boundary face uses the measured vertical registration");
        surfaceGeometry.RenderFacets[14].Points.Select(point => point.X)
            .Should().Equal(new[] { 194.0, 238.0, 201.0 },
                "the imported rear-green fold keeps the measured horizontal registration");
        surfaceGeometry.RenderFacets[14].Points.Select(point => point.Y)
            .Should().Equal(new[] { 76.0, 98.0, 72.0 },
                "the imported rear-green fold uses the measured lower-edge registration");
        surfaceGeometry.FrameSegments.Should().NotBeEmpty(
            "PowerPoint renders the projected Surface3D frame behind the facets");
        surfaceGeometry.FrameSegments.Should().HaveCount(37,
            "the imported PowerPoint frame carries five wall edges, 26 value-axis, and 6 category-axis tick strokes");
        surfaceGeometry.FrameSegments[0].Start.X
            .Should().BeApproximately(8.0, 0.0001,
                "the imported PowerPoint front frame edge starts inside the plot gutter");
        surfaceGeometry.FrameSegments[0].End.X
            .Should().BeApproximately(312.0, 0.0001,
                "the imported PowerPoint front frame edge uses the measured projected width");
        surfaceGeometry.FrameSegments[1].End.Y
            .Should().BeApproximately(42.0, 0.0001,
                "the imported value axis meets the projected top wall at the PowerPoint registration");
        surfaceGeometry.FrameSegments.Select(segment => segment.Stroke.Alpha)
            .Should().OnlyContain(alpha => alpha == 255,
                "imported PowerPoint Surface3D uses an opaque projected-frame stroke");
        surfaceGeometry.FrameSegments.Select(segment => segment.Stroke.Thickness)
            .Should().OnlyContain(thickness => thickness == 0.5,
                "the imported projected frame uses PowerPoint's half-point wall and axis stroke");
        ChartRenderPlanner.BuildSurfaceSeriesAxisLabelPlans(surface, surfaceFrame)
            .Select(label => label.Text)
            .Should().Equal("Low band", "Mid band", "High band");

        var scatter = charts.Single(chart => chart.ChartType == ChartType.Scatter);
        scatter.Series.Should().OnlyContain(series => !series.OnSecondaryAxis,
            "scatter uses two independent value axes for X and Y, not a secondary series axis");
        ChartRenderPlanner.BuildFramePlan(scatter, new ChartPlanRect(0, 0, 480, 288)).Plot
            .Should().Be(new ChartPlanRect(34.25, 54.5, 421.25, 200),
                "PowerPoint places imported scatter plots above the category-label band with a compact left axis gutter");
        ChartRenderPlanner.ComputeScatterAxisRange(scatter, useX: true)
            .Should().Be((0, 120, 20));
        ChartRenderPlanner.ComputePrimaryValueAxisRange(scatter)
            .Should().Be((0, 50, 10));
        var scatterColors = new[]
        {
            new SrgbColor(0x44, 0x72, 0xC4),
            new SrgbColor(0xED, 0x7D, 0x31),
        };
        var scatterPlan = ChartRenderPlanner.BuildScatterPrimitivePlan(
            scatter,
            new ChartPlanRect(0, 0, 360, 220),
            scatterColors);
        scatterPlan.Series[0].IsSmoothed.Should().BeTrue();
        scatterPlan.Series[0].LinePaths.Single().Segments
            .Should()
            .OnlyContain(segment => segment.Kind == ChartLinePathSegmentKind.CubicBezier);
        scatterPlan.Series[1].IsSmoothed.Should().BeFalse();
        scatterPlan.Series[1].LinePaths.Single().Segments
            .Should()
            .OnlyContain(segment => segment.Kind == ChartLinePathSegmentKind.Line);
        scatterPlan.Series[1].LinePaths.Single().Stroke.Color.Should().Be(scatterColors[1]);
        scatterPlan.Series[1].Markers.Select(marker => marker.Fill!.Value.Color)
            .Should().OnlyContain(color => color == scatterColors[1]);
        scatterPlan.GridLines.Should().HaveCount(18,
            "imported smooth scatter uses 11 value gridlines and 7 category gridlines");
        scatterPlan.YAxisLabels.Should().HaveCount(6);
        scatterPlan.GridLineStroke.Should().Be(new ChartStrokePlan(
            new SrgbColor(0x00, 0x00, 0x00),
            Alpha: 255,
            Thickness: 1.0));

        var stacked = charts.Single(chart => chart.ChartType == ChartType.ColumnStacked100);
        stacked.DataLabels.Should().NotBeNull();
        stacked.DataLabels!.ShowSeriesName.Should().BeTrue();
        stacked.DataLabels.ShowCategoryName.Should().BeTrue();
        stacked.DataLabels.ShowValue.Should().BeTrue();
        stacked.DataLabels.ShowPercent.Should().BeFalse();
        stacked.DataLabels.ShowLegendKey.Should().BeTrue();
        stacked.DataLabels.Separator.Should().Be(", ");
        var textStyles = charts.Select(chart => chart.TextStyle).ToArray();
        textStyles.Should().NotContainNulls();
        textStyles.Cast<ChartTextStyle>().Select(style => style.FontSizePt)
            .Should().OnlyContain(fontSize => fontSize == 18.0,
                "PowerPoint supplies an 18pt inherited title default when a chart has no explicit text properties");
        textStyles.Cast<ChartTextStyle>().Should().OnlyContain(style => style.IsImplicitDefault,
            "the source charts do not author c:chartSpace/c:txPr and must retain role-specific axis and label defaults");
        ChartRenderPlanner.ComputePrimaryValueAxisRange(stacked)
            .Should()
            .Be((0, 1, 0.1));
        var stackedScene = scenes.Single(scene => scene.GeometryKind == ChartSceneGeometryKind.Column);
        stackedScene.GridLines.Stroke.Should().Be(new ChartStrokePlan(
            new SrgbColor(0x00, 0x00, 0x00),
            Alpha: 255,
            Thickness: 1.0));
        stackedScene.ValueAxisLabels.Select(label => label.Text)
            .Should()
            .Equal("0%", "10%", "20%", "30%", "40%", "50%", "60%", "70%", "80%", "90%", "100%");
        stackedScene.GridLines.GridLines
            .Count(line => Math.Abs(line.Start.X - line.End.X) < 0.0001)
            .Should().Be(stacked.Categories.Count + 1,
                "PowerPoint registers the category grid at the shifted plot boundaries");
        double stackedCategoryStep = stackedScene.Frame.Plot.Width / stacked.Categories.Count;
        stackedScene.GridLines.GridLines
            .Where(line => Math.Abs(line.Start.X - line.End.X) < 0.0001)
            .Select(line => line.Start.X)
            .Should().Equal(Enumerable.Range(0, stacked.Categories.Count + 1)
                .Select(index => Math.Ceiling(stackedScene.Frame.Plot.X +
                    ChartRenderPlanner.ImportedPercentStackedGridEdgeOffsetX +
                    index * stackedCategoryStep)));
        var stackedFrame = ChartRenderPlanner.BuildFramePlan(
            stacked,
            new ChartPlanRect(0, 0, 480, 288));
        stackedFrame.Plot
            .Should().Be(new ChartPlanRect(31, 54, 415, 200),
                "PowerPoint gives imported 100%-stacked columns a compact category gutter and reserved lower band");
        var stackedBars = ChartRenderPlanner.BuildColumnPrimitives(
            stacked,
            stackedFrame.Plot);
        stackedBars.Should().NotBeEmpty();
        double categoryWidth = stackedFrame.Plot.Width / stacked.Categories.Count;
        double expectedBarWidth = categoryWidth / 3.5;
        foreach (var bar in stackedBars)
            bar.Bounds.Width.Should().BeApproximately(expectedBarWidth, 0.0001);
        var firstCategoryBars = stackedBars.Where(bar => bar.CategoryIndex == 0).OrderBy(bar => bar.SeriesIndex).ToArray();
        firstCategoryBars[0].Bounds.X.Should().BeApproximately(
            stackedFrame.Plot.X + (categoryWidth - expectedBarWidth) / 2,
            0.0001);
        firstCategoryBars[1].Bounds.X.Should().BeApproximately(
            firstCategoryBars[0].Bounds.X + categoryWidth / 3.5,
            0.0001);
        firstCategoryBars[0].Bounds.Height.Should().BeApproximately(stackedFrame.Plot.Height * 0.4, 0.0001);
        firstCategoryBars[1].Bounds.Height.Should().BeApproximately(stackedFrame.Plot.Height * 0.6, 0.0001);
        var stackedLabels = ChartRenderPlanner.BuildDataLabelPlans(
            stacked,
            new ChartPlanRect(0, 0, 360, 220));
        stackedLabels[0].Text.Should().Be("Actual, Q1, 20");
        stackedLabels.Select(label => label.Bounds.Width)
            .Should()
            .OnlyContain(width => Math.Abs(width - ChartRenderPlanner.ImportedPercentStackedDataLabelWidth) < 0.0001);
        stackedLabels.Should().OnlyContain(label => label.WrapText);
        stackedLabels.Should().OnlyContain(label =>
            label.TextBounds.HasValue &&
            label.LegendKeyBounds.HasValue &&
            label.LegendKeyFill.HasValue);
    }

    [Fact]
    public void Surface3DExplicitViewCorpus_PreservesAuthoredCameraAndUsesGeneralGeometry()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "25-chart-surface3d-view3d.pptx");
        var presentation = PptxPackageReader.Read(deckPath);
        var surface = presentation.Slides
            .SelectMany(slide => slide.Shapes)
            .Where(shape => shape.Kind == SlideShapeKind.Chart)
            .Select(shape => shape.Chart!)
            .Single(chart => chart.ChartType == ChartType.Surface3D);

        surface.View3D.Should().NotBeNull();
        surface.View3D!.RotationX.Should().Be(25);
        surface.View3D.RotationY.Should().Be(35);
        surface.View3D.Perspective.Should().Be(54);
        surface.View3D.DepthPercent.Should().Be(125);
        surface.WireframeSpecified.Should().BeTrue();
        surface.Wireframe.Should().BeFalse();

        var geometry = ChartRenderPlanner.BuildSurfaceGeometryPlan(
            surface,
            new ChartPlanRect(0, 0, 360, 189));
        geometry.RenderFacets.Should().HaveCount(8,
            "the renderer-neutral camera retains the eight triangulated top facets");
        geometry.AlternateRenderFacets.Should().HaveCount(10,
            "WPF uses eight top facets plus two measured side-material faces for this authored camera");
        geometry.WireframeSegments.Should().BeEmpty(
            "an explicit c:wireframe=0 camera must not receive the default mesh overlay");
        geometry.FrameSegments.Should().HaveCount(5,
            "an explicit c:wireframe=0 camera keeps the outer frame without the default wall grid");
        geometry.FrameSegments.Select(segment => segment.Stroke.Thickness)
            .Should().OnlyContain(thickness => thickness == 0.7);
        geometry.RenderFacets.Should().OnlyContain(facet => facet.Points.Count == 3);
        geometry.AlternateRenderFacets.Should().OnlyContain(facet =>
            facet.Points.Count == 3 || facet.Points.Count == 4 || facet.Points.Count == 5 ||
            facet.Points.Count == 11 || facet.Points.Count == 16);
        geometry.AlternateRenderFacets
            .Single(facet => facet.Fill.Color == new SrgbColor(0x34, 0x56, 0x95))
            .Points
            .Should()
            .Equal(
                new ChartPlanPoint(115, 150),
                new ChartPlanPoint(153, 104),
                new ChartPlanPoint(167, 153));
        geometry.RenderFacets.Should().OnlyContain(facet => facet.Fill.Alpha == 255);
        geometry.RenderFacets.Should().OnlyContain(facet => facet.Stroke.Alpha == 0);
        geometry.RenderFacets.Select(facet => facet.Fill.Color).Should().Equal(
            new SrgbColor(0x44, 0x72, 0xC3),
            new SrgbColor(0xEB, 0x7C, 0x30),
            new SrgbColor(0xB3, 0x5E, 0x24),
            new SrgbColor(0x9B, 0xC1, 0x83),
            new SrgbColor(0x9B, 0xBF, 0x81),
            new SrgbColor(0xA9, 0xD1, 0x8D),
            new SrgbColor(0x91, 0xB5, 0x7C),
            new SrgbColor(0xEB, 0xB1, 0x00));

        geometry.AlternateRenderFacets.Single(facet =>
                facet.Fill.Color == new SrgbColor(0xB3, 0x5E, 0x24))
            .Points.Should().Equal(
                new ChartPlanPoint(154, 108),
                new ChartPlanPoint(164, 97),
                new ChartPlanPoint(180, 80),
                new ChartPlanPoint(187, 73),
                new ChartPlanPoint(188, 73),
                new ChartPlanPoint(191, 78),
                new ChartPlanPoint(203, 101),
                new ChartPlanPoint(208, 120),
                new ChartPlanPoint(214, 143),
                new ChartPlanPoint(217, 155),
                new ChartPlanPoint(201, 155),
                new ChartPlanPoint(180, 154),
                new ChartPlanPoint(166, 153),
                new ChartPlanPoint(165, 150),
                new ChartPlanPoint(163, 143),
                new ChartPlanPoint(157, 120));
        geometry.AlternateRenderFacets.Single(facet =>
                facet.Fill.Color == new SrgbColor(0xDB, 0x74, 0x2C))
            .Points.Should().Equal(
                new ChartPlanPoint(32, 104),
                new ChartPlanPoint(165, 50),
                new ChartPlanPoint(200, 58),
                new ChartPlanPoint(247, 133),
                new ChartPlanPoint(263, 154));
        geometry.AlternateRenderFacets.Single(facet =>
                facet.Fill.Color == new SrgbColor(0xEB, 0x7C, 0x30))
            .Points.Should().Equal(
                new ChartPlanPoint(34, 100),
                new ChartPlanPoint(104, 84),
                new ChartPlanPoint(155, 72),
                new ChartPlanPoint(168, 69),
                new ChartPlanPoint(205, 72),
                new ChartPlanPoint(173, 84),
                new ChartPlanPoint(157, 101),
                new ChartPlanPoint(154, 106),
                new ChartPlanPoint(131, 106),
                new ChartPlanPoint(83, 104),
                new ChartPlanPoint(60, 103));
        geometry.AlternateRenderFacets.Single(facet =>
                facet.Fill.Color == new SrgbColor(0x91, 0xB5, 0x7C))
            .Points.Should().Equal(
                new ChartPlanPoint(200, 61),
                new ChartPlanPoint(201, 60),
                new ChartPlanPoint(206, 57),
                new ChartPlanPoint(225, 46),
                new ChartPlanPoint(246, 34),
                new ChartPlanPoint(250, 32),
                new ChartPlanPoint(281, 31),
                new ChartPlanPoint(291, 31),
                new ChartPlanPoint(282, 41),
                new ChartPlanPoint(246, 50),
                new ChartPlanPoint(201, 61));
    }

    [Fact]
    public void Surface3DExplicitDefaultCameraCorpus_UsesImportedDefaultProjection()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "27-chart-surface3d-4x4.pptx");
        var surface = PptxPackageReader.Read(deckPath).Slides
            .SelectMany(slide => slide.Shapes)
            .Single(shape => shape.Chart?.ChartType == ChartType.Surface3D).Chart!;

        surface.View3D.Should().NotBeNull();
        surface.View3D!.RotationX.Should().Be(15);
        surface.View3D.RotationY.Should().Be(20);
        surface.View3D.RightAngleAxes.Should().BeFalse();
        surface.Categories.Should().HaveCount(4);
        surface.Series.Should().HaveCount(4);
        surface.Series.SelectMany(series => series.Values).Should().OnlyContain(value => value.HasValue);

        var geometry = ChartRenderPlanner.BuildSurfaceGeometryPlan(
            surface,
            new ChartPlanRect(0, 0, 720, 378));
        geometry.Points.Should().HaveCount(16);
        geometry.RenderFacets.Should().HaveCount(18,
            "a default camera renders the 4x4 grid as two facets per complete cell");
        geometry.FrameSegments.Select(segment => segment.Stroke.Alpha)
            .Should().OnlyContain(alpha => alpha == 255,
                "explicit default view3D retains the imported projected frame");
    }

    [Fact]
    public void ChartLabelsCorpusDeck_InfersPowerPointPieValueAndPercentDefaults()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "19-chart-labels.pptx");
        var presentation = PptxPackageReader.Read(deckPath);
        var pie = presentation.Slides[1].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;

        pie.ChartType.Should().Be(ChartType.Pie);
        pie.Title.Should().Be("Share",
            "PowerPoint displays the single pie-series name when autoTitleDeleted is false");
        pie.HasAutomaticTitle.Should().BeTrue();
        pie.DataLabels.Should().NotBeNull();
        pie.DataLabels!.ShowValue.Should().BeTrue(
            "PowerPoint expands this imported pie label form to value-and-percent labels");
        pie.DataLabels.ShowPercent.Should().BeTrue();
        pie.DataLabels.ShowSeriesName.Should().BeFalse();
        pie.DataLabels.ShowCategoryName.Should().BeFalse();
    }

    [Fact]
    public void ChartsCorpusDeck_UsesPowerPointImportedBarFrameAndAxis()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "06-charts.pptx");
        var presentation = PptxPackageReader.Read(deckPath);
        var bar = presentation.Slides[3].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;

        bar.ChartType.Should().Be(ChartType.BarClustered);
        ChartRenderPlanner.BuildFramePlan(bar, new ChartPlanRect(0, 0, 480, 288)).Plot
            .Should().Be(new ChartPlanRect(73.5, 14.5, 307.2, 220.25));
        ChartRenderPlanner.ComputePrimaryValueAxisRange(bar).Should().Be((0, 120, 20));
    }

    [Fact]
    public void ChartsCorpusDeck_UsesPowerPointImportedPieFrame()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "06-charts.pptx");
        var presentation = PptxPackageReader.Read(deckPath);
        var pie = presentation.Slides[2].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;

        pie.ChartType.Should().Be(ChartType.Pie);
        pie.HasAutomaticTitle.Should().BeTrue();
        pie.DataLabels.Should().NotBeNull(
            "the imported chart explicitly persists leader-line configuration");
        pie.DataLabels!.ShowLeaderLines.Should().BeTrue();
        pie.DataLabels.ShowValue.Should().BeFalse();
        pie.DataLabels.ShowPercent.Should().BeFalse();
        pie.DataLabels.ShowCategoryName.Should().BeFalse();
        pie.DataLabels.ShowSeriesName.Should().BeFalse();

        ChartRenderPlanner.BuildFramePlan(pie, new ChartPlanRect(0, 0, 480, 288)).Plot
            .Should().Be(new ChartPlanRect(26.5, 11, 382.4, 310),
                "PowerPoint gives an imported automatic-title pie a larger, lifted plot frame");
    }

    [Fact]
    public void ChartsCorpusDeck_LineMarkersUsePowerPointDefaultMarkerPaletteAndStrokeWeight()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "06-charts.pptx");
        var presentation = PptxPackageReader.Read(deckPath);
        var line = presentation.Slides[1].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;

        line.ChartType.Should().Be(ChartType.LineMarkers);
        line.Series.Should().HaveCount(2);
        line.Series.Select(series => series.MarkerStyle).Should().OnlyContain(style => style == null);
        ChartRenderPlanner.ComputePrimaryValueAxisRange(line).Should().Be((0, 120, 20));

        var scene = ChartRenderPlanner.BuildScenePlan(line, new ChartPlanRect(0, 0, 960, 540));
        scene.LineSeries.Should().HaveCount(2);
        scene.LineSeries[0].Stroke.Thickness.Should().Be(ChartRenderPlanner.ImportedLineSeriesStrokeThickness);
        scene.LineSeries[1].Stroke.Thickness.Should().Be(ChartRenderPlanner.ImportedLineSeriesStrokeThickness);
        scene.LineSeries[0].Markers.Select(marker => marker.Symbol)
            .Should().OnlyContain(symbol => symbol == ChartMarkerPrimitiveSymbol.Diamond);
        scene.LineSeries[1].Markers.Select(marker => marker.Symbol)
            .Should().OnlyContain(symbol => symbol == ChartMarkerPrimitiveSymbol.Square);
        scene.LineSeries.SelectMany(series => series.Markers)
            .Select(marker => marker.Radius)
            .Should().OnlyContain(radius => radius == ChartRenderPlanner.ImportedLineMarkerRadius);
        scene.LegendItems.Should().HaveCount(2);
        scene.LegendItems.Select(item => item.IsLine)
            .Should().OnlyContain(isLine => isLine);
        scene.LegendItems.Select(item => item.MarkerSymbol)
            .Should().Equal(
                ChartMarkerPrimitiveSymbol.Diamond,
                ChartMarkerPrimitiveSymbol.Square);
        scene.LegendItems.Select(item => item.SwatchBounds.Width)
            .Should().OnlyContain(width => width == ChartRenderPlanner.ImportedLineMarkerLegendSwatchWidth);
        scene.LegendItems[0].Label.Bounds.X
            .Should().Be(scene.LegendItems[0].SwatchBounds.X + ChartRenderPlanner.ImportedLineMarkerLegendLabelInset);
    }

    [Fact]
    public void ChartsCorpusDeck_UsesPowerPointDarkAxisStrokesForImportedCartesianCharts()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "06-charts.pptx");
        var presentation = PptxPackageReader.Read(deckPath);
        var charts = new[] { presentation.Slides[0], presentation.Slides[1], presentation.Slides[3] }
            .Select(slide => slide.Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!)
            .ToArray();

        charts.Select(chart => chart.ChartType)
            .Should().Equal(ChartType.ColumnClustered, ChartType.LineMarkers, ChartType.BarClustered);

        ChartRenderPlanner.BuildFramePlan(charts[0], new ChartPlanRect(0, 0, 960, 540)).Plot
            .Should().Be(new ChartPlanRect(70.0, 21.0, 775.4, 467.0),
                "PowerPoint's imported style-2 column chart uses the wider plot band captured by the COM baseline");
        ChartRenderPlanner.BuildFramePlan(charts[1], new ChartPlanRect(0, 0, 960, 540)).Plot
            .Should().Be(new ChartPlanRect(70.0, 21.0, 781.0, 467.0),
                "PowerPoint's imported style-2 line chart reserves a narrower right legend band than the column chart");

        foreach (var chart in charts)
        {
            var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 960, 540));

            scene.GridLines.Stroke.Should().Be(new ChartStrokePlan(
                new SrgbColor(0x89, 0x89, 0x89),
                Alpha: 255,
                Thickness: 1.0));
            if (!scene.Frame.IsBar)
            {
                scene.GridLines.GridLines[0].Start.Y
                    .Should().Be(scene.Frame.Plot.Bottom + ChartRenderPlanner.ImportedCartesianGridLinePixelOffset);
            }
            scene.AxisTicks.Stroke.Should().Be(new ChartStrokePlan(
                new SrgbColor(0x89, 0x89, 0x89),
                Alpha: 255,
                Thickness: 0.75));
        }
    }

    [Fact]
    public void FillsCorpusDeck_MaterializesInheritedLineAndFontReferences()
    {
        var presentation = PptxPackageReader.Read(Path.Combine(FindCorpusDirectory(), "12-fills.pptx"));
        var shapes = presentation.Slides.Single().Shapes;

        foreach (var name in new[] { "Grad3Stop", "GradRadial", "PatternDiag", "PatternCross", "GradPreset" })
        {
            var shape = shapes.Single(candidate => candidate.Name == name);
            var outline = shape.Outline.Should().BeOfType<ShapeOutline.Visible>().Subject;
            outline.WidthPt.Should().Be(1.5);
            outline.Color.Resolved.Should().Be(new SrgbColor(0x03, 0x0E, 0x14));
            shape.TextBody!.Paragraphs.Single().Runs.Single().Color!.Resolved.Should().Be(SrgbColor.White);
        }

        ((ShapeFill.Pattern)shapes.Single(candidate => candidate.Name == "PatternCross").Fill!).Preset
            .Should().Be("cross");
    }

    [Fact]
    public void ChartBaselineDepthCorpusDeck_ProjectsPowerPointWpfAvaloniaBaselineReadiness()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "22-chart-baseline-depth.pptx");
        var presentation = PptxPackageReader.Read(deckPath);
        var charts = presentation.Slides
            .SelectMany(slide => slide.Shapes)
            .Where(shape => shape.Kind == SlideShapeKind.Chart)
            .Select(shape => shape.Chart!)
            .ToArray();

        var readiness = ChartVisualBaselineReadinessPlanner.Build(
            charts,
            slideIndex: 0,
            scenarioId: "Chart Baseline Depth");

        readiness.ScenarioId.Should().Be("chart-baseline-depth");
        readiness.SlideIndex.Should().Be(0);
        readiness.ChartCount.Should().Be(4);
        readiness.CaptureRequests.Should().HaveCount(12);
        readiness.PowerPointRequestCount.Should().Be(4);
        readiness.SharedHostRequestCount.Should().Be(8);
        readiness.IsPowerPointAuthoritativeReady.Should().BeTrue();
        readiness.CaptureRequests.Select(request => request.Host)
            .Should()
            .ContainInOrder(
                ChartVisualBaselineCaptureHost.PowerPoint,
                ChartVisualBaselineCaptureHost.Wpf,
                ChartVisualBaselineCaptureHost.Avalonia);

        var stockPowerPoint = readiness.CaptureRequests.First(request =>
            request.Host == ChartVisualBaselineCaptureHost.PowerPoint
            && request.ChartType == ChartType.Stock);
        stockPowerPoint.CaptureId.Should().Be("freep.chart-baseline-depth.slide-1.chart-1.stock.powerpoint");
        stockPowerPoint.SurfaceId.Should().Be("freep.chart-baseline-depth.slide-1.chart-1.stock");
        stockPowerPoint.RequiresPowerPointCom.Should().BeTrue();
        stockPowerPoint.EvidenceSummary.Should()
            .Contain("stock high-low/open-close tick plan")
            .And.Contain("4 series; 3 categories");

        var surfaceAvalonia = readiness.CaptureRequests.Single(request =>
            request.Host == ChartVisualBaselineCaptureHost.Avalonia
            && request.ChartType == ChartType.Surface3D);
        surfaceAvalonia.CaptureId.Should().Be("freep.chart-baseline-depth.slide-1.chart-2.surface3d.avalonia");
        surfaceAvalonia.RequiresPowerPointCom.Should().BeFalse();
        surfaceAvalonia.EvidenceSummary.Should().Contain("3-D surface projected facet");

        readiness.CaptureRequests.Single(request =>
                request.Host == ChartVisualBaselineCaptureHost.Wpf
                && request.ChartType == ChartType.Scatter)
            .EvidenceSummary
            .Should()
            .Contain("scatter smoothed Bezier path plan");

        readiness.CaptureRequests.Single(request =>
                request.Host == ChartVisualBaselineCaptureHost.PowerPoint
                && request.ChartType == ChartType.ColumnStacked100)
            .EvidenceSummary
            .Should()
            .Contain("100% stacked normalized axis");

        readiness.EvidenceLines.Should().Equal(
            "Scenario chart-baseline-depth: slide 1; charts 4",
            "Capture requests: 12; PowerPoint 4; WPF 4; Avalonia 4",
            "PowerPoint requests are readiness contracts and require desktop PowerPoint COM on the baseline machine");
    }

    [Fact]
    public void ChartBaselineDepthCorpus_RegistersImportedLightOrangeFacetBoundary()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "22-chart-baseline-depth.pptx");
        var surfaceChart = PptxPackageReader.Read(deckPath).Slides[0].Shapes
            .Single(shape => shape.Chart?.ChartType == ChartType.Surface3D).Chart!;
        var plan = ChartRenderPlanner.BuildSurfaceGeometryPlan(
            surfaceChart,
            new ChartPlanRect(0, 0, 360, 189));

        var lightOrange = plan.RenderFacets.Single(facet =>
            facet.SeriesIndex == 0 &&
            facet.CategoryIndex == 0 &&
            facet.Fill.Color == new SrgbColor(0xF1, 0x80, 0x32));
        lightOrange.Points.Should().Contain(
            new ChartPlanPoint(29.5, 98.93),
            "the imported PowerPoint light-orange face owns a wider value-axis boundary");
    }

    [Fact]
    public void ChartBaselineDepthCorpus_UsesAlternateBlueFacetForCanonicalImportedFrame()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "22-chart-baseline-depth.pptx");
        var surfaceChart = PptxPackageReader.Read(deckPath).Slides[0].Shapes
            .Single(shape => shape.Chart?.ChartType == ChartType.Surface3D).Chart!;
        var plan = ChartRenderPlanner.BuildSurfaceGeometryPlan(
            surfaceChart,
            new ChartPlanRect(596, 105, 360, 189));

        var blue = plan.AlternateRenderFacets.Single(facet =>
            facet.SeriesIndex == 0 &&
            facet.CategoryIndex == 0 &&
            facet.Fill.Color == new SrgbColor(0x44, 0x74, 0xC7));
        blue.Points.Should().Contain(new ChartPlanPoint(601, 230));
        blue.Points.Should().Contain(new ChartPlanPoint(765, 226));
        plan.RenderFacets.Single(facet =>
                facet.SeriesIndex == 0 &&
                facet.CategoryIndex == 0 &&
                facet.Fill.Color == new SrgbColor(0x44, 0x74, 0xC7))
            .Points
            .Should()
            .Contain(new ChartPlanPoint(599.5, 242.5));
    }

    [Fact]
    public void ChartBaselineDepthCorpus_UsesScaledCanonicalAlternateFacetForTallImportedFrame()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "22-chart-baseline-depth.pptx");
        var surfaceChart = PptxPackageReader.Read(deckPath).Slides[0].Shapes
            .Single(shape => shape.Chart?.ChartType == ChartType.Surface3D).Chart!;

        var plan = ChartRenderPlanner.BuildSurfaceGeometryPlan(
            surfaceChart,
            new ChartPlanRect(596, 105, 360, 240));

        var blue = plan.AlternateRenderFacets.Single(facet =>
            facet.SeriesIndex == 0 &&
            facet.CategoryIndex == 0 &&
            facet.Fill.Color == new SrgbColor(0x44, 0x74, 0xC7));
        blue.Points.Should().Contain(new ChartPlanPoint(601, 105 + 125 * 240.0 / 189.0));
    }

    [Fact]
    public void ChartBaselineDepthCorpus_UsesMeasuredAlternateOrangeFacetWithoutChangingSharedMesh()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "22-chart-baseline-depth.pptx");
        var surfaceChart = PptxPackageReader.Read(deckPath).Slides[0].Shapes
            .Single(shape => shape.Chart?.ChartType == ChartType.Surface3D).Chart!;
        var plan = ChartRenderPlanner.BuildSurfaceGeometryPlan(
            surfaceChart,
            new ChartPlanRect(596, 105, 360, 189));

        plan.AlternateRenderFacets.Single(facet =>
                facet.SeriesIndex == 0 &&
                facet.CategoryIndex == 0 &&
                facet.Fill.Color == new SrgbColor(0xF1, 0x80, 0x32))
            .Points
            .Should()
            .Contain(new ChartPlanPoint(604, 228))
            .And.Contain(new ChartPlanPoint(787, 185));
        plan.RenderFacets.Single(facet =>
                facet.SeriesIndex == 0 &&
                facet.CategoryIndex == 0 &&
                facet.Fill.Color == new SrgbColor(0xF1, 0x80, 0x32))
            .Points
            .Should()
            .Contain(new ChartPlanPoint(625.5, 203.93));
    }

    [Fact]
    public void RadarBaselineReadiness_ProjectsStyleSpecificSharedHostDecisionsWithoutPowerPointCom()
    {
        var standardRadar = BuildRadarChart(RadarStyle.Standard);
        var filledRadar = BuildRadarChart(RadarStyle.Filled);
        var markerRadar = BuildRadarChart(RadarStyle.Marker);
        var charts = new[] { standardRadar, filledRadar, markerRadar };

        var standardPlan = ChartRenderPlanner.BuildRadarPrimitivePlan(
            standardRadar,
            new ChartPlanRect(0, 0, 240, 180));
        standardPlan.Series[0].Path.Fill.Should().BeNull();
        standardPlan.Series[0].Markers.Should().BeEmpty();
        standardPlan.Series[0].Path.IsClosed.Should().BeTrue();

        var filledPlan = ChartRenderPlanner.BuildRadarPrimitivePlan(
            filledRadar,
            new ChartPlanRect(0, 0, 240, 180));
        filledPlan.Series[0].Path.Fill.Should()
            .Be(new ChartFillPlan(ChartRenderPlanner.ResolveSeriesColor(0, null), ChartRenderPlanner.RadarFillAlpha));
        filledPlan.Series[0].Markers.Should().BeEmpty();

        var markerPlan = ChartRenderPlanner.BuildRadarPrimitivePlan(
            markerRadar,
            new ChartPlanRect(0, 0, 240, 180));
        markerPlan.Series[0].Markers.Should().HaveCount(4);
        markerPlan.Series[0].Path.Fill.Should().BeNull();

        var readiness = ChartVisualBaselineReadinessPlanner.Build(
            charts,
            slideIndex: 2,
            scenarioId: "Radar Type Decisions");

        readiness.ChartCount.Should().Be(3);
        readiness.CaptureRequests.Should().HaveCount(9);
        readiness.PowerPointRequestCount.Should().Be(3);
        readiness.SharedHostRequestCount.Should().Be(6);
        readiness.CaptureRequests
            .Where(request => request.Host is ChartVisualBaselineCaptureHost.Wpf or ChartVisualBaselineCaptureHost.Avalonia)
            .Should()
            .OnlyContain(request => !request.RequiresPowerPointCom);

        var standardWpf = readiness.CaptureRequests.Single(request =>
            request.Host == ChartVisualBaselineCaptureHost.Wpf
            && request.ChartIndex == 0);
        standardWpf.CaptureId.Should().Be("freep.radar-type-decisions.slide-3.chart-1.radar.wpf");
        standardWpf.EvidenceSummary.Should().Contain("standard radar spoke-ring and blank-point plan");

        var filledAvalonia = readiness.CaptureRequests.Single(request =>
            request.Host == ChartVisualBaselineCaptureHost.Avalonia
            && request.ChartIndex == 1);
        filledAvalonia.CaptureId.Should().Be("freep.radar-type-decisions.slide-3.chart-2.radar.avalonia");
        filledAvalonia.EvidenceSummary.Should().Contain("filled radar area opacity");

        var markerWpf = readiness.CaptureRequests.Single(request =>
            request.Host == ChartVisualBaselineCaptureHost.Wpf
            && request.ChartIndex == 2);
        markerWpf.CaptureId.Should().Be("freep.radar-type-decisions.slide-3.chart-3.radar.wpf");
        markerWpf.EvidenceSummary.Should().Contain("radar marker");

        readiness.CaptureRequests.Single(request =>
                request.Host == ChartVisualBaselineCaptureHost.PowerPoint
                && request.ChartIndex == 0)
            .RequiresPowerPointCom
            .Should()
            .BeTrue();
    }

    [Fact]
    public void StockBaselineReadiness_ProjectsOhlcTickDecisionsWithoutPowerPointCom()
    {
        var stock = BuildStockChart();
        var stockPlan = ChartRenderPlanner.BuildStockPrimitivePlan(
            stock,
            new ChartPlanRect(0, 0, 240, 180));

        stockPlan.HighLowLines.Should().HaveCount(3);
        stockPlan.OpenTicks.Should().HaveCount(3);
        stockPlan.CloseTicks.Should().HaveCount(3);
        stockPlan.HighLowLines.Should().OnlyContain(line => line.Start.X == line.End.X);
        stockPlan.OpenTicks.Should().OnlyContain(tick =>
            tick.Segment.Start.X < tick.Segment.End.X &&
            tick.Segment.End.X == stockPlan.HighLowLines[tick.Segment.StartPointIndex].Start.X);
        stockPlan.CloseTicks.Should().OnlyContain(tick =>
            tick.Segment.Start.X == stockPlan.HighLowLines[tick.Segment.StartPointIndex].Start.X &&
            tick.Segment.End.X > tick.Segment.Start.X);
        stockPlan.CloseTicks.Select(tick => tick.PriceMove)
            .Should()
            .Equal(
                ChartStockPriceMove.Rising,
                ChartStockPriceMove.Falling,
                ChartStockPriceMove.Unchanged);

        var readiness = ChartVisualBaselineReadinessPlanner.Build(
            [stock],
            slideIndex: 5,
            scenarioId: "Stock OHLC Decisions");

        readiness.ChartCount.Should().Be(1);
        readiness.CaptureRequests.Should().HaveCount(3);
        readiness.PowerPointRequestCount.Should().Be(1);
        readiness.SharedHostRequestCount.Should().Be(2);
        readiness.CaptureRequests
            .Where(request => request.Host is ChartVisualBaselineCaptureHost.Wpf or ChartVisualBaselineCaptureHost.Avalonia)
            .Should()
            .OnlyContain(request => !request.RequiresPowerPointCom);

        var wpf = readiness.CaptureRequests.Single(request =>
            request.Host == ChartVisualBaselineCaptureHost.Wpf);
        wpf.CaptureId.Should().Be("freep.stock-ohlc-decisions.slide-6.chart-1.stock.wpf");
        wpf.EvidenceSummary.Should()
            .Contain("stock high-low/open-close tick plan")
            .And.Contain("4 series; 3 categories");

        var avalonia = readiness.CaptureRequests.Single(request =>
            request.Host == ChartVisualBaselineCaptureHost.Avalonia);
        avalonia.CaptureId.Should().Be("freep.stock-ohlc-decisions.slide-6.chart-1.stock.avalonia");
        avalonia.RequiresPowerPointCom.Should().BeFalse();

        readiness.CaptureRequests.Single(request =>
                request.Host == ChartVisualBaselineCaptureHost.PowerPoint)
            .RequiresPowerPointCom
            .Should()
            .BeTrue();
    }

    [Fact]
    public void StockVolumeBaselineReadiness_ProjectsVolumeAndOhlcDecisionsWithoutPowerPointCom()
    {
        var stock = BuildStockVolumeChart();
        var stockPlan = ChartRenderPlanner.BuildStockPrimitivePlan(
            stock,
            new ChartPlanRect(0, 0, 240, 180));
        var volumeBars = ChartRenderPlanner.BuildStockVolumePrimitives(
            stock,
            new ChartPlanRect(0, 0, 240, 180));

        stockPlan.HighLowLines.Should().HaveCount(3);
        stockPlan.OpenTicks.Should().HaveCount(3);
        stockPlan.CloseTicks.Should().HaveCount(3);
        volumeBars.Should().HaveCount(3);
        volumeBars.Should().OnlyContain(bar => bar.SeriesIndex == 0);
        volumeBars[1].Bounds.Height.Should().BeApproximately(
            180 * ChartRenderPlanner.StockVolumeBandHeightFraction,
            0.0001);
        volumeBars[2].Bounds.Height.Should().BeLessThan(volumeBars[1].Bounds.Height);

        var readiness = ChartVisualBaselineReadinessPlanner.Build(
            [stock],
            slideIndex: 6,
            scenarioId: "Stock Volume OHLC Decisions");

        readiness.ChartCount.Should().Be(1);
        readiness.CaptureRequests.Should().HaveCount(3);
        readiness.PowerPointRequestCount.Should().Be(1);
        readiness.SharedHostRequestCount.Should().Be(2);
        readiness.CaptureRequests
            .Where(request => request.Host is ChartVisualBaselineCaptureHost.Wpf or ChartVisualBaselineCaptureHost.Avalonia)
            .Should()
            .OnlyContain(request => !request.RequiresPowerPointCom);

        var wpf = readiness.CaptureRequests.Single(request =>
            request.Host == ChartVisualBaselineCaptureHost.Wpf);
        wpf.CaptureId.Should().Be("freep.stock-volume-ohlc-decisions.slide-7.chart-1.stock.wpf");
        wpf.EvidenceSummary.Should()
            .Contain("stock volume columns plus high-low/open-close tick plan")
            .And.Contain("5 series; 3 categories");

        var avalonia = readiness.CaptureRequests.Single(request =>
            request.Host == ChartVisualBaselineCaptureHost.Avalonia);
        avalonia.CaptureId.Should().Be("freep.stock-volume-ohlc-decisions.slide-7.chart-1.stock.avalonia");
        avalonia.RequiresPowerPointCom.Should().BeFalse();

        readiness.CaptureRequests.Single(request =>
                request.Host == ChartVisualBaselineCaptureHost.PowerPoint)
            .RequiresPowerPointCom
            .Should()
            .BeTrue();
    }

    [Fact]
    public void DoughnutBaselineReadiness_ProjectsHoleSizeAndRingOrderDecisionsWithoutPowerPointCom()
    {
        var doughnut = BuildDoughnutChart();
        var slices = ChartRenderPlanner.BuildDoughnutSlicePrimitives(
            doughnut,
            new ChartPlanRect(0, 0, 240, 180));

        slices.Should().HaveCount(6);
        slices[0].SeriesIndex.Should().Be(0);
        slices[0].PointIndex.Should().Be(0);
        slices[0].InnerRadius.Should().BeApproximately(30.6, 0.0001);
        slices[0].StartAngle.Should().BeApproximately(0, 0.0001);
        slices[3].SeriesIndex.Should().Be(1);
        slices[3].InnerRadius.Should().BeGreaterThan(slices[0].OuterRadius);

        var readiness = ChartVisualBaselineReadinessPlanner.Build(
            [doughnut],
            slideIndex: 3,
            scenarioId: "Doughnut Ring Decisions");

        readiness.ChartCount.Should().Be(1);
        readiness.CaptureRequests.Should().HaveCount(3);
        readiness.PowerPointRequestCount.Should().Be(1);
        readiness.SharedHostRequestCount.Should().Be(2);
        readiness.CaptureRequests
            .Where(request => request.Host is ChartVisualBaselineCaptureHost.Wpf or ChartVisualBaselineCaptureHost.Avalonia)
            .Should()
            .OnlyContain(request => !request.RequiresPowerPointCom);

        var avalonia = readiness.CaptureRequests.Single(request =>
            request.Host == ChartVisualBaselineCaptureHost.Avalonia);
        avalonia.CaptureId.Should().Be("freep.doughnut-ring-decisions.slide-4.chart-1.doughnut.avalonia");
        avalonia.EvidenceSummary.Should()
            .Contain("doughnut ring and first-slice plan")
            .And.Contain("2 series; 3 categories");

        readiness.CaptureRequests.Single(request =>
                request.Host == ChartVisualBaselineCaptureHost.PowerPoint)
            .RequiresPowerPointCom
            .Should()
            .BeTrue();
    }

    [Fact]
    public void ThreeDPieBaselineReadiness_ProjectsDepthPassDecisionsWithoutPowerPointCom()
    {
        var pie = BuildThreeDPieChart();
        var slices = ChartRenderPlanner.BuildPieSlicePrimitives(
            pie,
            new ChartPlanRect(0, 0, 240, 180));

        slices.Should().HaveCount(3);
        slices.Should().OnlyContain(slice => slice.HasThreeDDepth);
        slices[0].EffectiveVerticalScale.Should().Be(ChartRenderPlanner.ThreeDPieVerticalScale);
        slices[0].DepthOffsetY.Should().BeApproximately(14, 0.0001);

        var readiness = ChartVisualBaselineReadinessPlanner.Build(
            [pie],
            slideIndex: 4,
            scenarioId: "Pie 3D Depth Decisions");

        readiness.ChartCount.Should().Be(1);
        readiness.CaptureRequests.Should().HaveCount(3);
        readiness.PowerPointRequestCount.Should().Be(1);
        readiness.SharedHostRequestCount.Should().Be(2);
        readiness.CaptureRequests
            .Where(request => request.Host is ChartVisualBaselineCaptureHost.Wpf or ChartVisualBaselineCaptureHost.Avalonia)
            .Should()
            .OnlyContain(request => !request.RequiresPowerPointCom);

        var wpf = readiness.CaptureRequests.Single(request =>
            request.Host == ChartVisualBaselineCaptureHost.Wpf);
        wpf.CaptureId.Should().Be("freep.pie-3d-depth-decisions.slide-5.chart-1.pie.wpf");
        wpf.EvidenceSummary.Should()
            .Contain("3-D pie compressed top face")
            .And.Contain("1 series; 3 categories");

        readiness.CaptureRequests.Single(request =>
                request.Host == ChartVisualBaselineCaptureHost.PowerPoint)
            .RequiresPowerPointCom
            .Should()
            .BeTrue();
    }

    private static string FindCorpusDirectory() =>
        TestWorkspaceFileLocator.FindContainingDirectoryFromBaseDirectory(
            "tools", "FreeP.RenderCompare", "corpus", "22-chart-baseline-depth.pptx");

    private static ChartShape BuildRadarChart(RadarStyle style)
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Radar,
            RadarStyle = style
        };
        chart.Categories.AddRange(["North", "East", "South", "West"]);
        chart.Series.Add(new ChartSeries { Name = "Coverage" });
        chart.Series[0].Values.AddRange([4, 6, 3, 5]);
        return chart;
    }

    private static ChartShape BuildDoughnutChart()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Doughnut,
            DoughnutHolePercent = 40,
            FirstSliceAngleDegrees = 90
        };
        chart.Categories.AddRange(["Alpha", "Beta", "Gamma"]);
        chart.Series.Add(new ChartSeries { Name = "Inner" });
        chart.Series[0].Values.AddRange([3, 2, 1]);
        chart.Series.Add(new ChartSeries { Name = "Outer" });
        chart.Series[1].Values.AddRange([1, 2, 3]);
        return chart;
    }

    private static ChartShape BuildStockChart()
    {
        var chart = new ChartShape { ChartType = ChartType.Stock };
        chart.Categories.AddRange(["Day 1", "Day 2", "Day 3"]);

        foreach (var (name, values) in new[]
        {
            ("Open", new double?[] { 10, 12, 11 }),
            ("High", new double?[] { 14, 16, 15 }),
            ("Low", new double?[] { 8, 9, 10 }),
            ("Close", new double?[] { 13, 11, 11 })
        })
        {
            var series = new ChartSeries { Name = name };
            series.Values.AddRange(values);
            chart.Series.Add(series);
        }

        return chart;
    }

    private static ChartShape BuildStockVolumeChart()
    {
        var chart = new ChartShape { ChartType = ChartType.Stock };
        chart.Categories.AddRange(["Day 1", "Day 2", "Day 3"]);

        foreach (var (name, values) in new[]
        {
            ("Volume", new double?[] { 1000, 1500, 750 }),
            ("Open", new double?[] { 10, 12, 11 }),
            ("High", new double?[] { 14, 16, 15 }),
            ("Low", new double?[] { 8, 9, 10 }),
            ("Close", new double?[] { 13, 11, 11 })
        })
        {
            var series = new ChartSeries { Name = name };
            series.Values.AddRange(values);
            chart.Series.Add(series);
        }

        return chart;
    }

    private static ChartShape BuildThreeDPieChart()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Pie,
            ThreeDStyle = ChartThreeDStyle.Pie,
            FirstSliceAngleDegrees = 45
        };
        chart.Categories.AddRange(["North", "East", "West"]);
        chart.Series.Add(new ChartSeries { Name = "Share" });
        chart.Series[0].Values.AddRange([2, 3, 5]);
        return chart;
    }
}
