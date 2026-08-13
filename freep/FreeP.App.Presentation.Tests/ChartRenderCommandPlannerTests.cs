using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartRenderCommandPlannerTests
{
    [Fact]
    public void Plan_AppliesOnlyTheMeasuredNativeProfileDifferences()
    {
        var scene = WithScene(
            BaseScene(),
            title: Text("Stock", new ChartPlanRect(10, 12, 80, 20)),
            valueLabels: [Text("10", new ChartPlanRect(2, 40, 20, 10))]);
        scene = new ChartScenePlan
        {
            Frame = scene.Frame,
            GeometryKind = ChartSceneGeometryKind.Empty,
            PlotAreaOutline = Stroke(),
            Title = scene.Title,
            UsesStockLineFallback = true,
            DrawFlatGrid = true,
            UseWpfPixelSnappedImportedGrid = true,
            GridLines = new ChartMajorGridLinePrimitivePlan(
                [new ChartGridLinePlan(new ChartPlanPoint(10, 30), new ChartPlanPoint(90, 30))],
                Stroke()),
            ValueAxisLabels = scene.ValueAxisLabels,
        };

        var wpf = ChartRenderCommandPlanner.Plan(scene, ChartRenderExecutionProfile.Wpf);
        var avalonia = ChartRenderCommandPlanner.Plan(scene, ChartRenderExecutionProfile.Avalonia);

        wpf.OfType<ChartRenderCommand.Rectangle>()
            .Should().NotContain(command => command.Role == ChartRectangleRole.PlotArea);
        avalonia.OfType<ChartRenderCommand.Rectangle>()
            .Should().ContainSingle(command => command.Role == ChartRectangleRole.PlotArea);
        wpf.OfType<ChartRenderCommand.Line>().First().PixelSnapHorizontal.Should().BeTrue();
        avalonia.OfType<ChartRenderCommand.Line>().First().PixelSnapHorizontal.Should().BeFalse();

        FindText(wpf, ChartTextRole.Title).Label.Bounds.Should().Be(new ChartPlanRect(15, 14, 80, 20));
        FindText(avalonia, ChartTextRole.Title).Label.Bounds.Should().Be(new ChartPlanRect(10, 12, 80, 20));
        FindText(wpf, ChartTextRole.ValueAxisLabel).Label.Bounds.Should().Be(new ChartPlanRect(12, 46, 20, 10));
        FindText(avalonia, ChartTextRole.ValueAxisLabel).Label.Bounds.Should().Be(new ChartPlanRect(2, 40, 20, 10));
    }

    [Fact]
    public void Plan_ExpandsThreeDColumnBeforeBodyAndKeepsConnectorOrder()
    {
        var fill = new ChartFillPlan(new SrgbColor(100, 80, 40), 255);
        var rectangle = new ChartRectPrimitive(
            0,
            0,
            new ChartPlanRect(10, 20, 30, 40),
            fill,
            Stroke())
        {
            Depth = new ChartBarDepthPlan(100, 6, -4, false, false) { IsThreeD = true },
        };
        var seriesLine = Line(1, 2);
        var waterfallLine = Line(3, 4);
        var scene = new ChartScenePlan
        {
            Frame = Frame(),
            GeometryKind = ChartSceneGeometryKind.Column,
            Rectangles = [rectangle],
            SeriesLines = [seriesLine],
            WaterfallConnectorLines = [waterfallLine],
        };

        var commands = ChartRenderCommandPlanner.Plan(scene, ChartRenderExecutionProfile.Avalonia);

        commands.Select(command => command.GetType()).Should().Equal(
            typeof(ChartRenderCommand.Frame),
            typeof(ChartRenderCommand.Path),
            typeof(ChartRenderCommand.Path),
            typeof(ChartRenderCommand.Rectangle),
            typeof(ChartRenderCommand.Line),
            typeof(ChartRenderCommand.Line));
        var paths = commands.OfType<ChartRenderCommand.Path>().ToArray();
        paths[0].Primitive.Fill!.Value.Color.Should().Be(new SrgbColor(60, 48, 24));
        paths[1].Primitive.Fill!.Value.Color.Should().Be(new SrgbColor(75, 60, 30));
        commands.OfType<ChartRenderCommand.Line>().Select(command => command.Primitive)
            .Should().Equal(seriesLine, waterfallLine);
    }

    [Fact]
    public void Plan_EmitsPieDepthSidewallBeforeTheBody()
    {
        var primitive = new ChartPieSlicePrimitive(
            0,
            2,
            new ChartPlanPoint(50, 50),
            0,
            20,
            0,
            Math.PI,
            new ChartFillPlan(new SrgbColor(200, 100, 50), 255))
        {
            DepthFill = new ChartFillPlan(new SrgbColor(160, 80, 40), 255),
            DepthOffsetY = 8,
            DrawDepthSidewalls = true,
            VerticalScale = 0.7,
        };
        var scene = new ChartScenePlan
        {
            Frame = Frame(),
            GeometryKind = ChartSceneGeometryKind.Pie,
            PieSlices = [primitive],
        };

        var commands = ChartRenderCommandPlanner.Plan(scene, ChartRenderExecutionProfile.Wpf)
            .OfType<ChartRenderCommand.PieSlice>()
            .ToArray();

        commands.Select(command => command.Pass).Should().Equal(
            ChartPieSliceRenderPass.DepthSidewall,
            ChartPieSliceRenderPass.Body);
        commands[0].StartAngle.Should().Be(0);
        commands[0].EndAngle.Should().Be(Math.PI);
        commands[0].Fill.Should().NotBe(primitive.DepthFill!.Value);
        commands[0].DepthSidewallGeometry.Should().NotBeNull();
        var sidewall = commands[0].DepthSidewallGeometry!.Value;
        sidewall.TopStart.X.Should().BeApproximately(70, 0.000001);
        sidewall.TopStart.Y.Should().BeApproximately(50, 0.000001);
        sidewall.TopEnd.X.Should().BeApproximately(30, 0.000001);
        sidewall.TopEnd.Y.Should().BeApproximately(50, 0.000001);
        sidewall.BottomEnd.Should().Be(new ChartPlanPoint(sidewall.TopEnd.X, sidewall.TopEnd.Y + 8));
        sidewall.BottomStart.Should().Be(new ChartPlanPoint(sidewall.TopStart.X, sidewall.TopStart.Y + 8));
        sidewall.RadiusX.Should().Be(20);
        sidewall.RadiusY.Should().Be(14);
        commands[1].Fill.Should().Be(primitive.Fill!.Value);
    }

    [Fact]
    public void Plan_PreservesPostGeometryTextOrderClippingAndRotation()
    {
        var tableCellBounds = new ChartPlanRect(10, 80, 60, 16);
        var verticalTitle = new ChartAxisTitlePlan(
            Text("Value", new ChartPlanRect(2, 20, 12, 80)) with
            {
                IsItalic = true,
                FontFamily = "Aptos",
                HorizontalScale = 0.8,
            },
            ChartAxisTitleOrientation.VerticalCounterclockwise,
            ChartAxisKind.Value);
        var scene = new ChartScenePlan
        {
            Frame = Frame(),
            GeometryKind = ChartSceneGeometryKind.Empty,
            DataLabels =
            [
                new ChartDataLabelPlan(0, 0, "Data", new ChartPlanRect(20, 20, 40, 12), false, 8, ChartPlanTextAlignment.Center)
                {
                    LegendKeyBounds = new ChartPlanRect(15, 22, 4, 4),
                    LegendKeyFill = Fill(),
                    WrapText = true,
                },
            ],
            DataTable = new ChartDataTablePrimitivePlan(
                new ChartPlanRect(8, 76, 80, 24),
                Fill(),
                [new ChartDataTableCellPlan(0, 0, "Cell", tableCellBounds, tableCellBounds, false, true, true, 7, SrgbColor.Black, ChartPlanTextAlignment.Left, new ChartPlanRect(12, 84, 4, 4), Fill(), "Aptos")],
                [],
                [],
                [],
                Stroke()),
            SecondaryAxis = new ChartSecondaryValueAxisPrimitivePlan(
                [Text("Secondary", new ChartPlanRect(90, 20, 20, 10))],
                [],
                Stroke(),
                verticalTitle),
            CategoryAxisLabels = [Text("Category", new ChartPlanRect(20, 102, 40, 10))],
            ValueAxisLabels = [Text("Value label", new ChartPlanRect(0, 40, 30, 10))],
            SurfaceSeriesAxisLabels = [Text("Series", new ChartPlanRect(70, 100, 30, 10))],
            AxisTitles = [verticalTitle],
            LegendItems = [new ChartLegendItemPlan(new ChartPlanRect(90, 80, 10, 10), Text("Legend", new ChartPlanRect(102, 80, 40, 10)), Fill())],
            Trendlines = [new ChartTrendlinePrimitive(0, ChartTrendlineType.Linear, [], Stroke(), false, false, [Text("y=x", new ChartPlanRect(40, 40, 20, 10))])],
        };

        var commands = ChartRenderCommandPlanner.Plan(scene, ChartRenderExecutionProfile.Avalonia);
        var text = commands.OfType<ChartRenderCommand.Text>().Select(command => command.Plan).ToArray();

        text.Select(plan => plan.Role).Should().Equal(
            ChartTextRole.DataLabel,
            ChartTextRole.DataTableCell,
            ChartTextRole.SecondaryAxisLabel,
            ChartTextRole.AxisTitle,
            ChartTextRole.CategoryAxisLabel,
            ChartTextRole.ValueAxisLabel,
            ChartTextRole.SurfaceSeriesAxisLabel,
            ChartTextRole.AxisTitle,
            ChartTextRole.LegendLabel,
            ChartTextRole.TrendlineLabel);
        text[0].MaxLineCount.Should().Be(2);
        text[1].ClipBounds.Should().Be(tableCellBounds);
        text[3].RotationDegrees.Should().Be(-90);
        text[3].Label.Bounds.Should().Be(new ChartPlanRect(-32, 54, 80, 12));
        text[3].Label.HorizontalScale.Should().Be(1);
        text[4].Label.FontFamily.Should().BeNull();
        text[8].Label.HorizontalScale.Should().Be(1);
    }

    [Fact]
    public void PlanMarker_ExpandsFilledSymbolsIntoNeutralGeometry()
    {
        var fill = new ChartFillPlan(new SrgbColor(20, 40, 60), 210);
        var stroke = new ChartStrokePlan(new SrgbColor(70, 80, 90), 180, 1.25);

        var square = (ChartMarkerRenderPrimitive.Rectangle)ChartRenderCommandPlanner
            .PlanMarker(Marker(ChartMarkerPrimitiveSymbol.Square, fill, stroke))
            .Primitives.Single();
        square.Bounds.Should().Be(new ChartPlanRect(4, 14, 12, 12));
        square.Fill.Should().Be(fill);
        square.Stroke.Should().Be(stroke);

        var diamond = (ChartMarkerRenderPrimitive.Path)ChartRenderCommandPlanner
            .PlanMarker(Marker(ChartMarkerPrimitiveSymbol.Diamond, fill, stroke))
            .Primitives.Single();
        diamond.Geometry.Points.Should().Equal(
            new ChartPlanPoint(10, 14),
            new ChartPlanPoint(16, 20),
            new ChartPlanPoint(10, 26),
            new ChartPlanPoint(4, 20));
        diamond.Geometry.IsClosed.Should().BeTrue();
        diamond.Geometry.Fill.Should().Be(fill);
        diamond.Stroke.Should().Be(stroke);

        var triangle = (ChartMarkerRenderPrimitive.Path)ChartRenderCommandPlanner
            .PlanMarker(Marker(ChartMarkerPrimitiveSymbol.Triangle, fill, stroke))
            .Primitives.Single();
        triangle.Geometry.Points.Should().Equal(
            new ChartPlanPoint(10, 14),
            new ChartPlanPoint(16, 26),
            new ChartPlanPoint(4, 26));

        foreach (var symbol in new[]
        {
            ChartMarkerPrimitiveSymbol.Circle,
            ChartMarkerPrimitiveSymbol.Dot,
            (ChartMarkerPrimitiveSymbol)999,
        })
        {
            var ellipse = (ChartMarkerRenderPrimitive.Ellipse)ChartRenderCommandPlanner
                .PlanMarker(Marker(symbol, fill, stroke))
                .Primitives.Single();
            ellipse.Center.Should().Be(new ChartPlanPoint(10, 20));
            ellipse.RadiusX.Should().Be(6);
            ellipse.RadiusY.Should().Be(6);
            ellipse.Fill.Should().Be(fill);
            ellipse.Stroke.Should().Be(stroke);
        }
    }

    [Fact]
    public void PlanMarker_ExpandsLineSymbolsAndPreservesFallbackStroke()
    {
        var fill = new ChartFillPlan(new SrgbColor(30, 60, 90), 200);
        var authoredStroke = new ChartStrokePlan(new SrgbColor(90, 60, 30), 170, 1.75);

        var dash = ChartRenderCommandPlanner
            .PlanMarker(Marker(ChartMarkerPrimitiveSymbol.Dash, fill, Stroke: null))
            .Primitives.Cast<ChartMarkerRenderPrimitive.Line>()
            .Single();
        dash.Start.Should().Be(new ChartPlanPoint(4, 20));
        dash.End.Should().Be(new ChartPlanPoint(16, 20));
        dash.Stroke.Should().Be(new ChartStrokePlan(fill.Color, fill.Alpha, 2));

        foreach (var symbol in new[] { ChartMarkerPrimitiveSymbol.Plus, ChartMarkerPrimitiveSymbol.Star })
        {
            var lines = ChartRenderCommandPlanner
                .PlanMarker(Marker(symbol, fill, authoredStroke))
                .Primitives.Cast<ChartMarkerRenderPrimitive.Line>()
                .ToArray();
            lines.Should().HaveCount(2);
            lines[0].Should().Be(new ChartMarkerRenderPrimitive.Line(
                new ChartPlanPoint(4, 20),
                new ChartPlanPoint(16, 20),
                authoredStroke));
            lines[1].Should().Be(new ChartMarkerRenderPrimitive.Line(
                new ChartPlanPoint(10, 14),
                new ChartPlanPoint(10, 26),
                authoredStroke));
        }

        var xLines = ChartRenderCommandPlanner
            .PlanMarker(Marker(ChartMarkerPrimitiveSymbol.X, fill, authoredStroke))
            .Primitives.Cast<ChartMarkerRenderPrimitive.Line>()
            .ToArray();
        xLines.Should().Equal(
            new ChartMarkerRenderPrimitive.Line(
                new ChartPlanPoint(4, 14),
                new ChartPlanPoint(16, 26),
                authoredStroke),
            new ChartMarkerRenderPrimitive.Line(
                new ChartPlanPoint(16, 14),
                new ChartPlanPoint(4, 26),
                authoredStroke));

        ChartRenderCommandPlanner
            .PlanMarker(Marker(ChartMarkerPrimitiveSymbol.X, Fill: null, Stroke: null))
            .Primitives.Should().BeEmpty();
    }

    [Fact]
    public void Plan_ResolvesThreeDLinePathDepthBeforeNativeExecution()
    {
        var path = new ChartLinePathFigurePrimitive(
            new ChartPlanPoint(2, 3),
            [
                new ChartLinePathSegmentPrimitive(
                    ChartLinePathSegmentKind.CubicBezier,
                    new ChartPlanPoint(8, 9),
                    new ChartPlanPoint(4, 5),
                    new ChartPlanPoint(6, 7)),
            ],
            new ChartStrokePlan(new SrgbColor(10, 20, 30), 255, 2));
        var series = new ChartLineSeriesPrimitive(
            0,
            WithMarkers: false,
            Points: [],
            path.Stroke,
            MarkerFill: null,
            MarkerStroke: null,
            MarkerRadius: 0,
            LineSegments: [],
            LinePaths: [path],
            Markers: [],
            IsSmoothed: true)
        {
            Depth = new ChartClassicThreeDDepthPlan(5, -2, StrokeAlpha: 120, FillAlpha: 80),
        };
        var scene = new ChartScenePlan
        {
            Frame = Frame(),
            GeometryKind = ChartSceneGeometryKind.Line,
            LineSeries = [series],
        };

        var paths = ChartRenderCommandPlanner.Plan(scene, ChartRenderExecutionProfile.Wpf)
            .OfType<ChartRenderCommand.LinePath>()
            .Select(command => command.Primitive)
            .ToArray();

        paths.Should().HaveCount(2);
        paths[0].Start.Should().Be(new ChartPlanPoint(7, 1));
        paths[0].Segments[0].End.Should().Be(new ChartPlanPoint(13, 7));
        paths[0].Segments[0].Control1.Should().Be(new ChartPlanPoint(9, 3));
        paths[0].Segments[0].Control2.Should().Be(new ChartPlanPoint(11, 5));
        paths[0].Stroke.Alpha.Should().Be(120);
        paths[1].Should().Be(path);
    }

    [Fact]
    public void RendererSources_KeepTraversalAndDecisionPolicyInPresentationCore()
    {
        var planner = ReadWorkspaceFile("freep", "FreeP.App.Presentation", "Core", "ChartRenderCommandPlanner.cs");
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.ChartExecution.cs");
        var avalonia = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Avalonia", "SlideCanvas.ChartExecution.cs");
        var wpfCanvas = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avaloniaCanvas = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Avalonia", "SlideCanvas.cs");

        planner.Should().Contain("switch (scene.GeometryKind)");
        planner.Should().Contain("AddDataTable(commands, scene.DataTable)");
        planner.Should().Contain("AddLegend(commands, scene.LegendItems)");
        planner.Should().Contain("AddErrorBars(commands, scene.ErrorBars)");
        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ChartRenderCommandPlanner.Build(");
            source.Should().Contain("foreach (var command in plan.Commands)");
            source.Should().Contain("switch (command)");
            source.Should().NotContain("ChartRenderPlanner.BuildScenePlan(");
            source.Should().NotContain("switch (scene.GeometryKind)");
            source.Should().NotContain("foreach (var primitive in scene.");
            source.Should().NotContain("RenderColumnChart");
            source.Should().NotContain("RenderChartDataTable");
        }

        planner.Should().Contain("PlanMarker(marker)");
        planner.Should().Contain("ChartMarkerRenderPrimitive");
        foreach (var source in new[] { wpfCanvas, avaloniaCanvas })
        {
            source.Should().Contain("switch (primitive)");
            source.Should().Contain("ChartMarkerRenderPrimitive.Ellipse");
            source.Should().Contain("ToMarkerGeometry(path.Geometry)");
            source.Should().Contain("private static StreamGeometry ToMarkerGeometry(");
            source.Should().NotContain("switch (marker.Symbol)");
            source.Should().NotContain("ChartMarkerPrimitiveSymbol");
            source.Should().NotContain("MarkerPolygonGeometry");
            source.Should().NotContain("private static ChartPathPrimitive OffsetPath(");
        }
        wpfCanvas.Should().Contain("ctx.LineTo(point, isStroked: true, isSmoothJoin: false);");
        planner.Split("private static ChartPathPrimitive OffsetPath(").Should().HaveCount(2);
    }

    private static ChartScenePlan BaseScene() => new()
    {
        Frame = Frame(),
        GeometryKind = ChartSceneGeometryKind.Empty,
    };

    private static ChartScenePlan WithScene(
        ChartScenePlan scene,
        ChartTextPlan? title = null,
        IReadOnlyList<ChartTextPlan>? valueLabels = null) => new()
    {
        Frame = scene.Frame,
        GeometryKind = scene.GeometryKind,
        Title = title,
        ValueAxisLabels = valueLabels ?? [],
    };

    private static ChartFramePlan Frame() => new(
        new ChartPlanRect(0, 0, 120, 120),
        new ChartPlanRect(10, 20, 90, 80),
        null,
        false,
        false,
        0,
        0,
        ChartRenderFamily.Cartesian);

    private static ChartTextPlan Text(string text, ChartPlanRect bounds) =>
        new(text, bounds, false, 8, ChartPlanTextAlignment.Left);

    private static ChartFillPlan Fill() => new(new SrgbColor(30, 60, 90), 255);

    private static ChartStrokePlan Stroke() => new(SrgbColor.Black, 255, 1);

    private static ChartLineSegmentPrimitive Line(double startX, double endX) =>
        new(-1, -1, -1, new ChartPlanPoint(startX, 10), new ChartPlanPoint(endX, 10), Stroke());

    private static ChartCirclePrimitive Marker(
        ChartMarkerPrimitiveSymbol symbol,
        ChartFillPlan? Fill,
        ChartStrokePlan? Stroke) =>
        new(-1, -1, new ChartPlanPoint(10, 20), 6, symbol, Fill, Stroke);

    private static ChartTextRenderPlan FindText(
        IReadOnlyList<ChartRenderCommand> commands,
        ChartTextRole role) =>
        commands.OfType<ChartRenderCommand.Text>().Single(command => command.Plan.Role == role).Plan;

    private static string ReadWorkspaceFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllText(relativeParts);
}
