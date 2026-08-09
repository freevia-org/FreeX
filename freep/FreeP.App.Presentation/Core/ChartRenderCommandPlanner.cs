using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Measured native-renderer differences consumed by the shared chart command planner.
/// </summary>
public readonly record struct ChartRenderExecutionProfile(
    bool PixelSnapImportedHorizontalGridLines,
    bool PreferAlternateSurfaceFacets,
    bool DrawStandalonePlotAreaOutline,
    ChartPlanPoint StockFallbackTitleOffset,
    ChartPlanPoint StockFallbackValueLabelOffset,
    double ImportedRadarValueLabelOffsetY)
{
    public static ChartRenderExecutionProfile Wpf { get; } = new(
        PixelSnapImportedHorizontalGridLines: true,
        PreferAlternateSurfaceFacets: true,
        DrawStandalonePlotAreaOutline: false,
        StockFallbackTitleOffset: new ChartPlanPoint(5, 2),
        StockFallbackValueLabelOffset: new ChartPlanPoint(10, 6),
        ImportedRadarValueLabelOffsetY: 0);

    public static ChartRenderExecutionProfile Avalonia { get; } = new(
        PixelSnapImportedHorizontalGridLines: false,
        PreferAlternateSurfaceFacets: false,
        DrawStandalonePlotAreaOutline: true,
        StockFallbackTitleOffset: new ChartPlanPoint(0, 0),
        StockFallbackValueLabelOffset: new ChartPlanPoint(0, 0),
        ImportedRadarValueLabelOffsetY: 3);
}

public enum ChartRectangleRole
{
    PlotArea,
    EmptyPlot,
    Series,
    LegendKey,
    DataTableBackground,
    DataTableLegendKey
}

public enum ChartTextRole
{
    Title,
    DataLabel,
    DataTableCell,
    SecondaryAxisLabel,
    CategoryAxisLabel,
    ValueAxisLabel,
    SurfaceSeriesAxisLabel,
    ScatterXAxisLabel,
    ScatterYAxisLabel,
    BubbleXAxisLabel,
    BubbleYAxisLabel,
    RadarValueLabel,
    RadarCategoryLabel,
    AxisTitle,
    LegendLabel,
    TrendlineLabel
}

public enum ChartPieSliceRenderPass
{
    Depth,
    DepthSidewall,
    Body
}

public readonly record struct ChartTextRenderPlan(
    ChartTextPlan Label,
    ChartTextRole Role,
    int MaxLineCount,
    double RotationDegrees,
    ChartPlanPoint? RotationCenter,
    ChartPlanRect? ClipBounds,
    int SourceIndex = -1);

public abstract record ChartMarkerRenderPrimitive
{
    public sealed record Ellipse(
        ChartPlanPoint Center,
        double RadiusX,
        double RadiusY,
        ChartFillPlan? Fill,
        ChartStrokePlan? Stroke) : ChartMarkerRenderPrimitive;

    public sealed record Rectangle(
        ChartPlanRect Bounds,
        ChartFillPlan? Fill,
        ChartStrokePlan? Stroke) : ChartMarkerRenderPrimitive;

    public sealed record Path(
        ChartPathPrimitive Geometry,
        ChartStrokePlan? Stroke) : ChartMarkerRenderPrimitive;

    public sealed record Line(
        ChartPlanPoint Start,
        ChartPlanPoint End,
        ChartStrokePlan Stroke) : ChartMarkerRenderPrimitive;
}

public readonly record struct ChartMarkerRenderPlan(
    IReadOnlyList<ChartMarkerRenderPrimitive> Primitives);

public abstract record ChartRenderCommand
{
    public sealed record Frame(
        ChartPlanRect Bounds,
        ChartFillPlan Fill,
        ChartStrokePlan? Stroke,
        bool RoundedCorners) : ChartRenderCommand;

    public sealed record Rectangle(
        ChartPlanRect Bounds,
        ChartFillPlan? Fill,
        ChartStrokePlan? Stroke,
        ChartRectangleRole Role,
        ChartPlanRect? ClipBounds = null) : ChartRenderCommand;

    public sealed record Line(
        ChartLineSegmentPrimitive Primitive,
        bool PixelSnapHorizontal = false) : ChartRenderCommand;

    public sealed record Path(
        ChartPathPrimitive Primitive,
        ChartStrokePlan? Stroke,
        bool UseAreaGeometry = false) : ChartRenderCommand;

    public sealed record LinePath(
        ChartLinePathFigurePrimitive Primitive,
        ChartClassicThreeDDepthPlan? Depth = null) : ChartRenderCommand;

    public sealed record Marker(ChartMarkerRenderPlan Primitive) : ChartRenderCommand;

    public sealed record PieSlice(
        ChartPieSlicePrimitive Primitive,
        ChartFillPlan Fill,
        ChartPieSliceRenderPass Pass,
        double StartAngle = 0,
        double EndAngle = 0) : ChartRenderCommand;

    public sealed record DoughnutSlice(
        ChartPieSlicePrimitive Primitive,
        ChartFillPlan Fill) : ChartRenderCommand;

    public sealed record SurfaceFacet(ChartSurfaceFacetPrimitive Primitive) : ChartRenderCommand;

    public sealed record Bubble(ChartBubblePrimitive Primitive) : ChartRenderCommand;

    public sealed record Text(ChartTextRenderPlan Plan) : ChartRenderCommand;
}

public sealed class ChartRenderExecutionPlan
{
    public ShapeAffineTransform Transform { get; init; } = ShapeAffineTransform.Identity;
    public ChartScenePlan Scene { get; init; } = new();
    public IReadOnlyList<ChartRenderCommand> Commands { get; init; } = Array.Empty<ChartRenderCommand>();
}

/// <summary>
/// Expands a chart scene into one ordered, renderer-neutral paint list.
/// </summary>
public static class ChartRenderCommandPlanner
{
    private const double ErrorBarCapHalfLength = 3.0;
    private const double ImportedRadarAgilityLabelOffsetX = 35.0;
    private const double ImportedRadarStaminaLabelOffsetX = -51.0;
    private const double ImportedRadarLowerLabelOffsetY = -2.0;

    public static ChartRenderExecutionPlan Build(
        DrawOp.Chart chartOperation,
        ChartRenderExecutionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(chartOperation);

        var bounds = chartOperation.BoundsDip;
        var scene = ChartRenderPlanner.BuildScenePlan(
            chartOperation.ChartShape,
            new ChartPlanRect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            chartOperation.SeriesColors,
            chartOperation.FillPlans,
            chartOperation.ChartAreaFill,
            chartOperation.ChartAreaOutline,
            chartOperation.PlotAreaFill,
            chartOperation.PlotAreaOutline);

        return new ChartRenderExecutionPlan
        {
            Transform = ShapeTransformPlanner.PlanShapeTransform(
                bounds,
                chartOperation.RotationDeg,
                flipH: false,
                flipV: false),
            Scene = scene,
            Commands = Plan(scene, profile),
        };
    }

    public static IReadOnlyList<ChartRenderCommand> Plan(
        ChartScenePlan scene,
        ChartRenderExecutionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var commands = new List<ChartRenderCommand>();
        commands.Add(new ChartRenderCommand.Frame(
            scene.Frame.Bounds,
            scene.ChartAreaFill ?? new ChartFillPlan(SrgbColor.White, 255),
            scene.ChartAreaOutline,
            scene.RoundedCorners));

        if (scene.PlotAreaFill is { } plotFill)
        {
            commands.Add(new ChartRenderCommand.Rectangle(
                scene.Frame.Plot,
                plotFill,
                scene.PlotAreaOutline,
                ChartRectangleRole.PlotArea));
        }
        else if (profile.DrawStandalonePlotAreaOutline && scene.PlotAreaOutline is { } plotOutline)
        {
            commands.Add(new ChartRenderCommand.Rectangle(
                scene.Frame.Plot,
                Fill: null,
                plotOutline,
                ChartRectangleRole.PlotArea));
        }

        if (scene.Title is { } title)
        {
            if (scene.UsesStockLineFallback)
                title = OffsetText(title, profile.StockFallbackTitleOffset);
            AddText(commands, title, ChartTextRole.Title, maxLineCount: title.MaxLineCount);
        }

        if (!scene.Frame.HasPlot)
            return commands;

        if (scene.DrawFlatGrid)
        {
            foreach (var gridLine in scene.GridLines.GridLines ?? Array.Empty<ChartGridLinePlan>())
            {
                commands.Add(new ChartRenderCommand.Line(
                    ToLine(gridLine, scene.GridLines.Stroke),
                    PixelSnapHorizontal: profile.PixelSnapImportedHorizontalGridLines
                        && scene.UseWpfPixelSnappedImportedGrid
                        && IsHorizontal(gridLine)));
            }

            foreach (var gridLine in scene.MinorGridLines.GridLines ?? Array.Empty<ChartGridLinePlan>())
                AddLine(commands, gridLine, scene.MinorGridLines.Stroke);
        }

        if (scene.DrawProjectedThreeDBarFrame)
            AddProjectedThreeDBarFrame(commands, scene);

        AddGeometry(commands, scene, profile);

        foreach (var primitive in scene.ComboLineSeries)
            AddLineSeries(commands, primitive);

        foreach (var trendline in scene.Trendlines)
        {
            foreach (var segment in trendline.Segments)
                commands.Add(new ChartRenderCommand.Line(segment));
        }

        AddErrorBars(commands, scene.ErrorBars);

        foreach (var leaderLine in scene.DataLabelLeaderLines)
            commands.Add(new ChartRenderCommand.Line(leaderLine));

        foreach (var tick in scene.AxisTicks.CategoryTicks ?? Array.Empty<ChartGridLinePlan>())
            AddLine(commands, tick, scene.AxisTicks.Stroke);
        foreach (var tick in scene.AxisTicks.ValueTicks ?? Array.Empty<ChartGridLinePlan>())
            AddLine(commands, tick, scene.AxisTicks.Stroke);

        AddDataLabels(commands, scene.DataLabels);
        AddDataTable(commands, scene.DataTable);

        foreach (var tick in scene.SecondaryAxis.Ticks ?? Array.Empty<ChartGridLinePlan>())
            AddLine(commands, tick, scene.SecondaryAxis.TickStroke);
        foreach (var label in scene.SecondaryAxis.Labels ?? Array.Empty<ChartTextPlan>())
            AddText(commands, label, ChartTextRole.SecondaryAxisLabel);
        if (scene.SecondaryAxis.Title is { } secondaryAxisTitle)
            AddAxisTitle(commands, secondaryAxisTitle);

        foreach (var label in scene.CategoryAxisLabels)
            AddText(commands, label, ChartTextRole.CategoryAxisLabel);
        foreach (var label in scene.ValueAxisLabels)
        {
            AddText(
                commands,
                scene.UsesStockLineFallback
                    ? OffsetText(label, profile.StockFallbackValueLabelOffset)
                    : label,
                ChartTextRole.ValueAxisLabel);
        }
        foreach (var label in scene.SurfaceSeriesAxisLabels)
            AddText(commands, label, ChartTextRole.SurfaceSeriesAxisLabel);
        foreach (var axisTitle in scene.AxisTitles)
            AddAxisTitle(commands, axisTitle);

        AddLegend(commands, scene.LegendItems);

        foreach (var trendline in scene.Trendlines)
        {
            foreach (var label in trendline.Labels)
                AddText(commands, label, ChartTextRole.TrendlineLabel);
        }

        return commands;
    }

    private static void AddGeometry(
        List<ChartRenderCommand> commands,
        ChartScenePlan scene,
        ChartRenderExecutionProfile profile)
    {
        switch (scene.GeometryKind)
        {
            case ChartSceneGeometryKind.Column:
            case ChartSceneGeometryKind.Waterfall:
                AddColumn(commands, scene);
                break;
            case ChartSceneGeometryKind.Surface:
                AddSurface(commands, scene, profile.PreferAlternateSurfaceFacets);
                break;
            case ChartSceneGeometryKind.Bar:
                AddRectangles(commands, scene.Rectangles);
                AddLines(commands, scene.SeriesLines);
                break;
            case ChartSceneGeometryKind.Line:
                AddRectangles(commands, scene.UpDownBars);
                AddLines(commands, scene.DropLines);
                foreach (var primitive in scene.LineSeries)
                    AddLineSeries(commands, primitive);
                break;
            case ChartSceneGeometryKind.Stock:
                AddStock(commands, scene);
                break;
            case ChartSceneGeometryKind.Pie:
                AddPie(commands, scene);
                break;
            case ChartSceneGeometryKind.Doughnut:
                foreach (var primitive in scene.DoughnutSlices)
                {
                    commands.Add(new ChartRenderCommand.DoughnutSlice(
                        primitive,
                        primitive.Fill!.Value));
                }
                break;
            case ChartSceneGeometryKind.Funnel:
                foreach (var segment in scene.FunnelSegments)
                {
                    if (segment.Path.Fill is { })
                        commands.Add(new ChartRenderCommand.Path(segment.Path, Stroke: null));
                }
                break;
            case ChartSceneGeometryKind.Area:
                AddArea(commands, scene.AreaSeries);
                break;
            case ChartSceneGeometryKind.Scatter:
                AddScatter(commands, scene.Scatter);
                break;
            case ChartSceneGeometryKind.Bubble:
                AddBubble(commands, scene.Bubble);
                break;
            case ChartSceneGeometryKind.Radar:
                AddRadar(commands, scene.Radar, profile);
                break;
            default:
                commands.Add(new ChartRenderCommand.Rectangle(
                    scene.Frame.Plot,
                    new ChartFillPlan(SrgbColor.Black, 30),
                    Stroke: null,
                    ChartRectangleRole.EmptyPlot));
                break;
        }
    }

    private static void AddColumn(List<ChartRenderCommand> commands, ChartScenePlan scene)
    {
        foreach (var primitive in scene.Rectangles)
        {
            if (primitive.Depth is { IsThreeD: true } depth)
                AddThreeDColumn(commands, primitive, depth);
            else
                AddRectangle(commands, primitive);
        }

        AddLines(commands, scene.SeriesLines);
        AddLines(commands, scene.WaterfallConnectorLines);
    }

    private static void AddThreeDColumn(
        List<ChartRenderCommand> commands,
        ChartRectPrimitive primitive,
        ChartBarDepthPlan depth)
    {
        var rect = primitive.Bounds;
        var top = new ChartPathPrimitive(
            [
                new ChartPlanPoint(rect.X, rect.Y),
                new ChartPlanPoint(rect.Right, rect.Y),
                new ChartPlanPoint(rect.Right + depth.OffsetX, rect.Y + depth.OffsetY),
                new ChartPlanPoint(rect.X + depth.OffsetX, rect.Y + depth.OffsetY),
            ],
            IsClosed: true,
            ShadeBarFill(primitive.Fill, 0.75));
        var side = new ChartPathPrimitive(
            [
                new ChartPlanPoint(rect.Right, rect.Y),
                new ChartPlanPoint(rect.Right + depth.OffsetX, rect.Y + depth.OffsetY),
                new ChartPlanPoint(rect.Right + depth.OffsetX, rect.Bottom + depth.OffsetY),
                new ChartPlanPoint(rect.Right, rect.Bottom),
            ],
            IsClosed: true,
            ShadeBarFill(primitive.Fill, 0.60));

        commands.Add(new ChartRenderCommand.Path(side, Stroke: null));
        commands.Add(new ChartRenderCommand.Path(top, Stroke: null));
        AddRectangle(commands, primitive);
    }

    private static void AddSurface(
        List<ChartRenderCommand> commands,
        ChartScenePlan scene,
        bool preferAlternateFacets)
    {
        if (scene.Surface is not { } surface)
            return;

        AddLines(commands, surface.FrameSegments);
        AddLines(commands, surface.WireframeSegments);

        var facets = preferAlternateFacets && surface.WpfRenderFacets.Count > 0
            ? surface.WpfRenderFacets
            : surface.RenderFacets.Count > 0
                ? surface.RenderFacets
                : surface.Facets;
        if (facets.Count > 0)
        {
            foreach (var facet in facets)
                commands.Add(new ChartRenderCommand.SurfaceFacet(facet));
        }
        else
        {
            foreach (var cell in surface.Cells)
            {
                commands.Add(new ChartRenderCommand.Rectangle(
                    cell.Bounds,
                    cell.Fill,
                    cell.Stroke,
                    ChartRectangleRole.Series));
            }
        }

        AddLines(commands, surface.ContourSegments);
    }

    private static void AddStock(List<ChartRenderCommand> commands, ChartScenePlan scene)
    {
        if (scene.Stock is not { } stock)
        {
            AddLines(commands, scene.DropLines);
            foreach (var primitive in scene.LineSeries)
                AddLineSeries(commands, primitive);
            return;
        }

        AddRectangles(commands, scene.StockVolumes);
        foreach (var bar in scene.UpDownBars)
        {
            commands.Add(new ChartRenderCommand.Rectangle(
                bar.Bounds,
                bar.Fill,
                Stroke: null,
                ChartRectangleRole.Series));
        }

        AddLines(commands, stock.HighLowLines);
        foreach (var tick in stock.OpenTicks)
            commands.Add(new ChartRenderCommand.Line(tick.Segment));
        foreach (var tick in stock.CloseTicks)
            commands.Add(new ChartRenderCommand.Line(tick.Segment));
    }

    private static void AddPie(List<ChartRenderCommand> commands, ChartScenePlan scene)
    {
        AddLines(commands, scene.OfPieSeriesLines);
        foreach (var primitive in scene.PieSlices.Concat(scene.OfPieSecondarySlices))
        {
            if (primitive.DepthFill is { } depthFill)
            {
                if (primitive.DrawDepthSidewalls)
                {
                    foreach (var interval in GetPieDepthArcIntervals(primitive))
                    {
                        commands.Add(new ChartRenderCommand.PieSlice(
                            primitive,
                            ShadePieSidewall(depthFill, interval.Start, interval.End, primitive.PointIndex),
                            ChartPieSliceRenderPass.DepthSidewall,
                            interval.Start,
                            interval.End));
                    }
                }
                else
                {
                    commands.Add(new ChartRenderCommand.PieSlice(
                        primitive,
                        depthFill,
                        ChartPieSliceRenderPass.Depth));
                }
            }

            commands.Add(new ChartRenderCommand.PieSlice(
                primitive,
                primitive.Fill!.Value,
                ChartPieSliceRenderPass.Body));
        }

        if (scene.OfPieSecondaryType == OfPieType.Bar)
            AddColumn(commands, scene);
    }

    private static void AddArea(
        List<ChartRenderCommand> commands,
        IReadOnlyList<ChartAreaSeriesPrimitive> primitives)
    {
        foreach (var primitive in primitives)
        {
            if (primitive.AreaPath.Fill is not { } fill)
                continue;

            if (primitive.Depth is { } depth)
            {
                commands.Add(new ChartRenderCommand.Path(
                    OffsetPath(primitive.AreaPath, depth) with
                    {
                        Fill = fill.WithAlpha(depth.FillAlpha),
                    },
                    Stroke: null,
                    UseAreaGeometry: true));
            }

            commands.Add(new ChartRenderCommand.Path(
                primitive.AreaPath,
                Stroke: null,
                UseAreaGeometry: true));
        }
    }

    private static void AddScatter(
        List<ChartRenderCommand> commands,
        ChartScatterPrimitivePlan? scatter)
    {
        if (scatter is not { } plan)
            return;

        foreach (var gridLine in plan.GridLines)
            AddLine(commands, gridLine, plan.GridLineStroke);
        foreach (var primitive in plan.Series)
        {
            foreach (var path in primitive.LinePaths)
                commands.Add(new ChartRenderCommand.LinePath(path));
            foreach (var marker in primitive.Markers)
                commands.Add(new ChartRenderCommand.Marker(PlanMarker(marker)));
        }
        foreach (var label in plan.XAxisLabels)
            AddText(commands, label, ChartTextRole.ScatterXAxisLabel);
        foreach (var label in plan.YAxisLabels)
            AddText(commands, label, ChartTextRole.ScatterYAxisLabel);
        AddDataLabels(commands, plan.DataLabels);
    }

    private static void AddBubble(
        List<ChartRenderCommand> commands,
        ChartBubblePrimitivePlan? bubble)
    {
        if (bubble is not { } plan)
            return;

        foreach (var gridLine in plan.GridLines)
            AddLine(commands, gridLine, plan.GridLineStroke);
        foreach (var primitive in plan.Bubbles)
            commands.Add(new ChartRenderCommand.Bubble(primitive));
        foreach (var label in plan.XAxisLabels)
            AddText(commands, label, ChartTextRole.BubbleXAxisLabel);
        foreach (var label in plan.YAxisLabels)
            AddText(commands, label, ChartTextRole.BubbleYAxisLabel);
    }

    private static void AddRadar(
        List<ChartRenderCommand> commands,
        ChartRadarPrimitivePlan? radar,
        ChartRenderExecutionProfile profile)
    {
        if (radar is not { } plan)
            return;

        foreach (var ring in plan.Rings)
            commands.Add(new ChartRenderCommand.Path(ring.Path, ring.Stroke));
        foreach (var spoke in plan.Spokes)
            AddLine(commands, spoke, plan.SpokeStroke);

        bool isImportedCalibration = IsImportedRadarCalibration(plan);
        foreach (var label in plan.ValueLabels)
        {
            AddText(
                commands,
                isImportedCalibration
                    ? OffsetText(label, new ChartPlanPoint(0, profile.ImportedRadarValueLabelOffsetY))
                    : label,
                ChartTextRole.RadarValueLabel);
        }

        for (int index = 0; index < plan.CategoryLabels.Count; index++)
        {
            var label = plan.CategoryLabels[index];
            if (isImportedCalibration && index is 2 or 3)
            {
                label = OffsetText(
                    label,
                    new ChartPlanPoint(
                        index == 2
                            ? ImportedRadarAgilityLabelOffsetX
                            : ImportedRadarStaminaLabelOffsetX,
                        ImportedRadarLowerLabelOffsetY));
            }
            AddText(commands, label, ChartTextRole.RadarCategoryLabel, sourceIndex: index);
        }

        foreach (var primitive in plan.Series)
        {
            foreach (var path in primitive.Paths)
                commands.Add(new ChartRenderCommand.Path(path, primitive.Stroke));
            foreach (var marker in primitive.Markers)
                commands.Add(new ChartRenderCommand.Marker(PlanMarker(marker)));
        }
    }

    private static void AddLineSeries(
        List<ChartRenderCommand> commands,
        ChartLineSeriesPrimitive primitive)
    {
        if (primitive.Depth is { } depth)
        {
            foreach (var path in primitive.LinePaths)
            {
                commands.Add(new ChartRenderCommand.LinePath(
                    path with { Stroke = path.Stroke with { Alpha = depth.StrokeAlpha } },
                    depth));
            }
        }

        foreach (var path in primitive.LinePaths)
            commands.Add(new ChartRenderCommand.LinePath(path));
        foreach (var marker in primitive.Markers)
            commands.Add(new ChartRenderCommand.Marker(PlanMarker(marker)));
    }

    private static void AddErrorBars(
        List<ChartRenderCommand> commands,
        IReadOnlyList<ChartErrorBarPrimitive> errorBars)
    {
        foreach (var errorBar in errorBars)
        {
            if (errorBar.MinusEnd is { } minus)
            {
                AddLine(commands, errorBar.Center, minus, errorBar.Stroke);
                if (!errorBar.NoEndCap)
                    AddErrorBarCap(commands, minus, errorBar.Stroke, errorBar.Direction);
            }
            if (errorBar.PlusEnd is { } plus)
            {
                AddLine(commands, errorBar.Center, plus, errorBar.Stroke);
                if (!errorBar.NoEndCap)
                    AddErrorBarCap(commands, plus, errorBar.Stroke, errorBar.Direction);
            }
        }
    }

    private static void AddErrorBarCap(
        List<ChartRenderCommand> commands,
        ChartPlanPoint endpoint,
        ChartStrokePlan stroke,
        ChartErrorDirection direction)
    {
        var start = direction == ChartErrorDirection.Y
            ? new ChartPlanPoint(endpoint.X - ErrorBarCapHalfLength, endpoint.Y)
            : new ChartPlanPoint(endpoint.X, endpoint.Y - ErrorBarCapHalfLength);
        var end = direction == ChartErrorDirection.Y
            ? new ChartPlanPoint(endpoint.X + ErrorBarCapHalfLength, endpoint.Y)
            : new ChartPlanPoint(endpoint.X, endpoint.Y + ErrorBarCapHalfLength);
        AddLine(commands, start, end, stroke);
    }

    private static void AddDataLabels(
        List<ChartRenderCommand> commands,
        IReadOnlyList<ChartDataLabelPlan> labels)
    {
        foreach (var label in labels)
        {
            if (label.LegendKeyBounds is { } keyBounds && label.LegendKeyFill is { } keyFill)
            {
                commands.Add(new ChartRenderCommand.Rectangle(
                    keyBounds,
                    keyFill,
                    Stroke: null,
                    ChartRectangleRole.LegendKey));
            }

            AddText(
                commands,
                new ChartTextPlan(
                    label.Text,
                    label.TextBounds ?? label.Bounds,
                    label.IsBold,
                    label.FontSize,
                    label.Alignment)
                {
                    FontFamily = label.FontFamily,
                    TextColor = label.TextColor,
                    IsItalic = label.IsItalic,
                },
                ChartTextRole.DataLabel,
                maxLineCount: label.WrapText ? 2 : 1);
        }
    }

    private static void AddDataTable(
        List<ChartRenderCommand> commands,
        ChartDataTablePrimitivePlan table)
    {
        if (!table.Bounds.HasPositiveArea)
            return;

        if (table.BackgroundFill is { } backgroundFill)
        {
            commands.Add(new ChartRenderCommand.Rectangle(
                table.Bounds,
                backgroundFill,
                Stroke: null,
                ChartRectangleRole.DataTableBackground));
        }

        foreach (var border in table.HorizontalBorders)
            AddLine(commands, border, table.BorderStroke);
        foreach (var border in table.VerticalBorders)
            AddLine(commands, border, table.BorderStroke);
        foreach (var border in table.OutlineBorders)
            AddLine(commands, border, table.BorderStroke);

        foreach (var cell in table.Cells)
        {
            if (cell.LegendKeyFill is { } keyFill && cell.LegendKeyBounds is { } keyBounds)
            {
                commands.Add(new ChartRenderCommand.Rectangle(
                    keyBounds,
                    keyFill,
                    Stroke: null,
                    ChartRectangleRole.DataTableLegendKey,
                    cell.CellBounds));
            }

            AddText(
                commands,
                new ChartTextPlan(
                    cell.Text,
                    cell.Bounds,
                    cell.IsBold,
                    cell.FontSize,
                    cell.Alignment)
                {
                    FontFamily = cell.FontFamily,
                    TextColor = cell.TextColor,
                    IsItalic = cell.IsItalic,
                },
                ChartTextRole.DataTableCell,
                clipBounds: cell.CellBounds);
        }
    }

    private static void AddAxisTitle(
        List<ChartRenderCommand> commands,
        ChartAxisTitlePlan title)
    {
        if (title.Orientation == ChartAxisTitleOrientation.Horizontal)
        {
            AddText(commands, title.Label, ChartTextRole.AxisTitle);
            return;
        }

        var bounds = title.Label.Bounds;
        var center = new ChartPlanPoint(
            bounds.X + bounds.Width * 0.5,
            bounds.Y + bounds.Height * 0.5);
        var rotatedBounds = new ChartPlanRect(
            bounds.X + (bounds.Width - bounds.Height) * 0.5,
            bounds.Y + (bounds.Height - bounds.Width) * 0.5,
            bounds.Height,
            bounds.Width);
        AddText(
            commands,
            title.Label with { Bounds = rotatedBounds },
            ChartTextRole.AxisTitle,
            rotationDegrees: title.Orientation == ChartAxisTitleOrientation.VerticalClockwise ? 90 : -90,
            rotationCenter: center);
    }

    private static void AddLegend(
        List<ChartRenderCommand> commands,
        IReadOnlyList<ChartLegendItemPlan> items)
    {
        foreach (var item in items)
        {
            var swatch = item.SwatchBounds;
            if (item.IsLine)
            {
                double centerY = swatch.Y + swatch.Height / 2.0;
                AddLine(
                    commands,
                    new ChartPlanPoint(swatch.X, centerY),
                    new ChartPlanPoint(swatch.Right, centerY),
                    new ChartStrokePlan(
                        item.Fill.Color,
                        item.Fill.Alpha,
                        ChartRenderPlanner.ImportedLineSeriesStrokeThickness));
                if (item.MarkerSymbol is { } markerSymbol)
                {
                    commands.Add(new ChartRenderCommand.Marker(PlanMarker(CreateLegendMarker(item, markerSymbol))));
                }
                else if (!item.IsLineOnly)
                {
                    commands.Add(new ChartRenderCommand.Rectangle(
                        new ChartPlanRect(swatch.X + swatch.Width / 2.0 - 4, centerY - 4, 8, 8),
                        item.Fill,
                        Stroke: null,
                        ChartRectangleRole.LegendKey));
                }
            }
            else if (item.MarkerSymbol is { } markerSymbol)
            {
                commands.Add(new ChartRenderCommand.Marker(PlanMarker(CreateLegendMarker(item, markerSymbol))));
            }
            else
            {
                commands.Add(new ChartRenderCommand.Rectangle(
                    swatch,
                    item.Fill,
                    Stroke: null,
                    ChartRectangleRole.LegendKey));
            }

            AddText(commands, item.Label, ChartTextRole.LegendLabel);
        }
    }

    private static ChartCirclePrimitive CreateLegendMarker(
        ChartLegendItemPlan item,
        ChartMarkerPrimitiveSymbol markerSymbol) =>
        new(
            -1,
            -1,
            new ChartPlanPoint(
                item.SwatchBounds.X + item.SwatchBounds.Width / 2.0,
                item.SwatchBounds.Y + item.SwatchBounds.Height / 2.0),
            Math.Min(item.SwatchBounds.Width, item.SwatchBounds.Height) / 2.0,
            markerSymbol,
            item.Fill,
            Stroke: null);

    internal static ChartMarkerRenderPlan PlanMarker(ChartCirclePrimitive marker)
    {
        var center = marker.Center;
        double radius = marker.Radius;
        var lineStroke = marker.Stroke ?? (marker.Fill is { } fill
            ? new ChartStrokePlan(
                fill.Color,
                fill.Alpha,
                Math.Max(0.75, radius / 3.0))
            : null);

        IReadOnlyList<ChartMarkerRenderPrimitive> primitives = marker.Symbol switch
        {
            ChartMarkerPrimitiveSymbol.Square =>
            [
                new ChartMarkerRenderPrimitive.Rectangle(
                    new ChartPlanRect(center.X - radius, center.Y - radius, radius * 2, radius * 2),
                    marker.Fill,
                    marker.Stroke),
            ],
            ChartMarkerPrimitiveSymbol.Diamond =>
            [
                MarkerPath(
                    marker,
                    new ChartPlanPoint(center.X, center.Y - radius),
                    new ChartPlanPoint(center.X + radius, center.Y),
                    new ChartPlanPoint(center.X, center.Y + radius),
                    new ChartPlanPoint(center.X - radius, center.Y)),
            ],
            ChartMarkerPrimitiveSymbol.Triangle =>
            [
                MarkerPath(
                    marker,
                    new ChartPlanPoint(center.X, center.Y - radius),
                    new ChartPlanPoint(center.X + radius, center.Y + radius),
                    new ChartPlanPoint(center.X - radius, center.Y + radius)),
            ],
            ChartMarkerPrimitiveSymbol.Dash => MarkerLines(
                lineStroke,
                (new ChartPlanPoint(center.X - radius, center.Y), new ChartPlanPoint(center.X + radius, center.Y))),
            ChartMarkerPrimitiveSymbol.Plus or ChartMarkerPrimitiveSymbol.Star => MarkerLines(
                lineStroke,
                (new ChartPlanPoint(center.X - radius, center.Y), new ChartPlanPoint(center.X + radius, center.Y)),
                (new ChartPlanPoint(center.X, center.Y - radius), new ChartPlanPoint(center.X, center.Y + radius))),
            ChartMarkerPrimitiveSymbol.X => MarkerLines(
                lineStroke,
                (new ChartPlanPoint(center.X - radius, center.Y - radius), new ChartPlanPoint(center.X + radius, center.Y + radius)),
                (new ChartPlanPoint(center.X + radius, center.Y - radius), new ChartPlanPoint(center.X - radius, center.Y + radius))),
            _ =>
            [
                new ChartMarkerRenderPrimitive.Ellipse(
                    center,
                    radius,
                    radius,
                    marker.Fill,
                    marker.Stroke),
            ],
        };

        return new ChartMarkerRenderPlan(primitives);
    }

    private static ChartMarkerRenderPrimitive.Path MarkerPath(
        ChartCirclePrimitive marker,
        params ChartPlanPoint[] points) =>
        new(
            new ChartPathPrimitive(points, IsClosed: true, marker.Fill),
            marker.Stroke);

    private static IReadOnlyList<ChartMarkerRenderPrimitive> MarkerLines(
        ChartStrokePlan? stroke,
        params (ChartPlanPoint Start, ChartPlanPoint End)[] segments) =>
        stroke is { } resolvedStroke
            ? segments
                .Select(segment => (ChartMarkerRenderPrimitive)new ChartMarkerRenderPrimitive.Line(
                    segment.Start,
                    segment.End,
                    resolvedStroke))
                .ToArray()
            : Array.Empty<ChartMarkerRenderPrimitive>();

    private static void AddProjectedThreeDBarFrame(
        List<ChartRenderCommand> commands,
        ChartScenePlan scene)
    {
        var plot = scene.Frame.Plot;
        int lineCount = scene.ValueAxisLabels.Count;
        if (lineCount < 2)
            return;

        double leftX = plot.X + 21.0;
        double leftBaseline = plot.Bottom - (ChartRenderPlanner.ImportedThreeDBarBaseLift - 8.0);
        double depthY = Math.Min(plot.Height * 0.18, 94.0);
        double rightBaseline = leftBaseline + depthY;
        double rightTop = plot.Y + depthY * 0.39;
        for (int index = 0; index < lineCount; index++)
        {
            double fraction = index / (double)(lineCount - 1);
            AddLine(
                commands,
                new ChartPlanPoint(leftX, leftBaseline - (leftBaseline - plot.Y) * fraction),
                new ChartPlanPoint(plot.Right, rightBaseline - (rightBaseline - rightTop) * fraction),
                scene.GridLines.Stroke);
        }

        AddLine(
            commands,
            new ChartPlanPoint(leftX, leftBaseline),
            new ChartPlanPoint(leftX, plot.Y),
            scene.GridLines.Stroke);
        double frontRightX = plot.Right - 49.0;
        AddLine(
            commands,
            new ChartPlanPoint(leftX, leftBaseline),
            new ChartPlanPoint(frontRightX, rightBaseline),
            scene.GridLines.Stroke);

        int categoryCount = Math.Max(1, scene.CategoryAxisLabels.Count);
        for (int index = 0; index <= categoryCount; index++)
        {
            double fraction = index / (double)categoryCount;
            double x = leftX + (frontRightX - leftX) * fraction;
            double y = leftBaseline + depthY * fraction;
            AddLine(
                commands,
                new ChartPlanPoint(x, y),
                new ChartPlanPoint(x, y + 5.0),
                scene.GridLines.Stroke);
        }
    }

    private static void AddRectangles(
        List<ChartRenderCommand> commands,
        IReadOnlyList<ChartRectPrimitive> rectangles)
    {
        foreach (var rectangle in rectangles)
            AddRectangle(commands, rectangle);
    }

    private static void AddRectangle(
        List<ChartRenderCommand> commands,
        ChartRectPrimitive rectangle)
    {
        commands.Add(new ChartRenderCommand.Rectangle(
            rectangle.Bounds,
            rectangle.Fill,
            rectangle.Stroke,
            ChartRectangleRole.Series));
    }

    private static void AddLines(
        List<ChartRenderCommand> commands,
        IReadOnlyList<ChartLineSegmentPrimitive> lines)
    {
        foreach (var line in lines)
            commands.Add(new ChartRenderCommand.Line(line));
    }

    private static void AddLine(
        List<ChartRenderCommand> commands,
        ChartGridLinePlan line,
        ChartStrokePlan stroke) =>
        commands.Add(new ChartRenderCommand.Line(ToLine(line, stroke)));

    private static void AddLine(
        List<ChartRenderCommand> commands,
        ChartPlanPoint start,
        ChartPlanPoint end,
        ChartStrokePlan stroke) =>
        commands.Add(new ChartRenderCommand.Line(new ChartLineSegmentPrimitive(
            -1,
            -1,
            -1,
            start,
            end,
            stroke)));

    private static ChartLineSegmentPrimitive ToLine(
        ChartGridLinePlan line,
        ChartStrokePlan stroke) =>
        new(-1, -1, -1, line.Start, line.End, stroke);

    private static bool IsHorizontal(ChartGridLinePlan line) =>
        Math.Abs(line.Start.Y - line.End.Y) < 0.001;

    private static void AddText(
        List<ChartRenderCommand> commands,
        ChartTextPlan label,
        ChartTextRole role,
        int maxLineCount = 1,
        double rotationDegrees = 0,
        ChartPlanPoint? rotationCenter = null,
        ChartPlanRect? clipBounds = null,
        int sourceIndex = -1) =>
        commands.Add(new ChartRenderCommand.Text(new ChartTextRenderPlan(
            NormalizeTextForNativeContract(label, role),
            role,
            maxLineCount,
            rotationDegrees,
            rotationCenter,
            clipBounds,
            sourceIndex)));

    private static ChartTextPlan NormalizeTextForNativeContract(
        ChartTextPlan label,
        ChartTextRole role) =>
        role switch
        {
            ChartTextRole.Title => label with
            {
                IsItalic = false,
                HorizontalScale = 1,
            },
            ChartTextRole.DataLabel or ChartTextRole.DataTableCell or ChartTextRole.AxisTitle =>
                label with { HorizontalScale = 1 },
            ChartTextRole.SecondaryAxisLabel or
            ChartTextRole.CategoryAxisLabel or
            ChartTextRole.ValueAxisLabel or
            ChartTextRole.SurfaceSeriesAxisLabel => label with
            {
                IsItalic = false,
                FontFamily = null,
                HorizontalScale = 1,
            },
            ChartTextRole.ScatterXAxisLabel or
            ChartTextRole.ScatterYAxisLabel or
            ChartTextRole.BubbleXAxisLabel or
            ChartTextRole.BubbleYAxisLabel or
            ChartTextRole.RadarValueLabel or
            ChartTextRole.RadarCategoryLabel => label with
            {
                IsItalic = false,
                FontFamily = null,
                TextColor = null,
                HorizontalScale = 1,
            },
            ChartTextRole.LegendLabel => label with
            {
                IsItalic = false,
                FontFamily = null,
            },
            ChartTextRole.TrendlineLabel => label with { IsItalic = false },
            _ => label,
        };

    private static ChartTextPlan OffsetText(ChartTextPlan label, ChartPlanPoint offset) =>
        label with
        {
            Bounds = label.Bounds with
            {
                X = label.Bounds.X + offset.X,
                Y = label.Bounds.Y + offset.Y,
            },
        };

    private static ChartFillPlan ShadeBarFill(ChartFillPlan fill, double factor) =>
        new(
            new SrgbColor(
                ScaleChannel(fill.Color.R, factor),
                ScaleChannel(fill.Color.G, factor),
                ScaleChannel(fill.Color.B, factor)),
            fill.Alpha);

    private static ChartFillPlan ShadePieSidewall(
        ChartFillPlan fill,
        double startAngle,
        double endAngle,
        int pointIndex)
    {
        double factor = ChartRenderPlanner.ResolveImportedThreeDPieSidewallFactor(
            pointIndex,
            startAngle,
            endAngle);
        return ShadeBarFill(fill, factor);
    }

    private static byte ScaleChannel(byte channel, double factor) =>
        (byte)Math.Round(Math.Clamp(channel * factor, 0, 255));

    private static IEnumerable<(double Start, double End)> GetPieDepthArcIntervals(
        ChartPieSlicePrimitive primitive)
    {
        for (int turn = -1; turn <= 1; turn++)
        {
            double frontStart = turn * 2 * Math.PI;
            double frontEnd = frontStart + Math.PI;
            double start = Math.Max(primitive.StartAngle, frontStart);
            double end = Math.Min(primitive.EndAngle, frontEnd);
            if (end - start > 1e-6)
                yield return (start, end);
        }
    }

    private static ChartPathPrimitive OffsetPath(
        ChartPathPrimitive path,
        ChartClassicThreeDDepthPlan depth) =>
        path with
        {
            Points = path.Points
                .Select(point => new ChartPlanPoint(
                    point.X + depth.OffsetX,
                    point.Y + depth.OffsetY))
                .ToArray(),
        };

    private static bool IsImportedRadarCalibration(ChartRadarPrimitivePlan plan) =>
        plan.Rings.Count == 9
        && plan.CategoryLabels.Count == 5
        && plan.Series.Count == 2;
}
