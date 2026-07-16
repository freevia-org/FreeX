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
        scene.AxisTicks.Stroke.Should().Be(expectedStroke);
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
            Thickness: 0.5));
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
        plan.CategoryLabels.Select(label => label.Text)
            .Should().Equal("Speed", "Power", "Agility", "Stamina", "Tech");
        plan.CategoryLabels.Should().OnlyContain(label => !label.Text.Contains("...", StringComparison.Ordinal));
        plan.Series.Should().HaveCount(2);
        plan.Series.Should().OnlyContain(series =>
            series.Stroke.Thickness == ChartRenderPlanner.ImportedRadarSeriesStrokeThickness);
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
            ChartMarkerPrimitiveSymbol.X,
            ChartMarkerPrimitiveSymbol.Triangle);
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
            .Should().BeApproximately(146.5, 0.0001,
                "Surface3D height follows the PowerPoint value axis instead of normalizing to the smallest data value");
        surfaceGeometry.Facets.Should().HaveCount(4);
        surfaceGeometry.WireframeSegments.Should().HaveCountGreaterThan(surfaceGeometry.Facets.Count);
        surfaceGeometry.ContourSegments.Should().NotBeEmpty();
        surfaceGeometry.Facets.Count(facet => facet.Points.Count == 3).Should().Be(2);
        surfaceGeometry.Facets.Count(facet => facet.Points.Count == 4).Should().Be(2);
        surfaceGeometry.RenderFacets.Should().HaveCount(8,
            "imported PowerPoint Surface3D cells render a continuous triangulated surface, including blank-cell fallbacks");
        surfaceGeometry.RenderFacets.Should().OnlyContain(facet => facet.Points.Count == 3);
        var firstSurfaceCellFacets = surfaceGeometry.RenderFacets
            .Where(facet => facet.SeriesIndex == 0 && facet.CategoryIndex == 0)
            .ToArray();
        firstSurfaceCellFacets.Should().HaveCount(2);
        firstSurfaceCellFacets[0].Points.Select(point => point.X)
            .Should().Equal(new[] { 0.0, 147.6, 62.0 },
                "PowerPoint splits the first imported surface cell along the 0-3 diagonal");
        firstSurfaceCellFacets[1].Points.Select(point => point.X)
            .Should().Equal(new[] { 147.6, 194.8, 62.0 },
                "the paired imported surface triangle shares the alternate diagonal");
        surfaceGeometry.Points.Single(point => point.SeriesIndex == 2 && point.CategoryIndex == 0).Point.X
            .Should().BeApproximately(124.0, 0.0001,
                "PowerPoint's rear-left surface vertex follows the projected frame depth wall");
        surfaceGeometry.RenderFacets.Should().OnlyContain(facet => facet.Fill.Alpha == 255,
            "PowerPoint's imported Surface3D facets are opaque fills");
        surfaceGeometry.RenderFacets.Should().OnlyContain(facet => facet.Stroke.Alpha == 0,
            "PowerPoint's imported Surface3D faces do not draw opaque white facet outlines");
        surfaceGeometry.RenderFacets
            .Select(facet => facet.Fill.Color)
            .Should()
            .Equal(
                new SrgbColor(0x45, 0x74, 0xC8),
                new SrgbColor(0xF2, 0x80, 0x32),
                new SrgbColor(0xB6, 0x60, 0x26),
                new SrgbColor(0xD5, 0x71, 0x2C),
                new SrgbColor(0xD5, 0x71, 0x2C),
                new SrgbColor(0x98, 0xBC, 0x80),
                new SrgbColor(0x98, 0xBC, 0x80),
                new SrgbColor(0x98, 0xBC, 0x80));
        surfaceGeometry.FrameSegments.Should().NotBeEmpty(
            "PowerPoint renders the projected Surface3D frame behind the facets");
        ChartRenderPlanner.BuildSurfaceSeriesAxisLabelPlans(surface, surfaceFrame)
            .Select(label => label.Text)
            .Should().Equal("Low band", "Mid band", "High band");

        var scatter = charts.Single(chart => chart.ChartType == ChartType.Scatter);
        scatter.Series.Should().OnlyContain(series => !series.OnSecondaryAxis,
            "scatter uses two independent value axes for X and Y, not a secondary series axis");
        ChartRenderPlanner.BuildFramePlan(scatter, new ChartPlanRect(0, 0, 480, 288)).Plot
            .Should().Be(new ChartPlanRect(34.25, 57.5, 421.25, 200),
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
        stackedScene.ValueAxisLabels.Select(label => label.Text)
            .Should()
            .Equal("0%", "10%", "20%", "30%", "40%", "50%", "60%", "70%", "80%", "90%", "100%");
        stackedScene.GridLines.GridLines
            .Count(line => Math.Abs(line.Start.X - line.End.X) < 0.0001)
            .Should().Be(stacked.Categories.Count);
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
        stackedLabels.Should().OnlyContain(label => label.Bounds.Width >= 100);
        stackedLabels.Should().OnlyContain(label =>
            label.TextBounds.HasValue &&
            label.LegendKeyBounds.HasValue &&
            label.LegendKeyFill.HasValue);
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
            .Should().Be(new ChartPlanRect(75.5, 15.5, 312.2, 220.25));
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
        pie.DataLabels.Should().BeNull();

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

        foreach (var chart in charts)
        {
            var scene = ChartRenderPlanner.BuildScenePlan(chart, new ChartPlanRect(0, 0, 960, 540));

            scene.GridLines.Stroke.Should().Be(new ChartStrokePlan(
                new SrgbColor(0xD9, 0xD9, 0xD9),
                Alpha: 255,
                Thickness: 0.5));
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

        var readiness = ChartRenderPlanner.BuildVisualBaselineReadinessPlan(
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

        var readiness = ChartRenderPlanner.BuildVisualBaselineReadinessPlan(
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

        var readiness = ChartRenderPlanner.BuildVisualBaselineReadinessPlan(
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

        var readiness = ChartRenderPlanner.BuildVisualBaselineReadinessPlan(
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

        var readiness = ChartRenderPlanner.BuildVisualBaselineReadinessPlan(
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

        var readiness = ChartRenderPlanner.BuildVisualBaselineReadinessPlan(
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

    private static string FindCorpusDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "FreeP.RenderCompare", "corpus");
            if (File.Exists(Path.Combine(candidate, "22-chart-baseline-depth.pptx")))
                return candidate;
        }

        throw new DirectoryNotFoundException("Could not locate tools/FreeP.RenderCompare/corpus.");
    }

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
