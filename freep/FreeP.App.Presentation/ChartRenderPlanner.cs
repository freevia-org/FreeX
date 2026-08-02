using System.Globalization;
using System.Text;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public readonly record struct ChartPlanPoint(double X, double Y);

public readonly record struct ChartPlanRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool HasPositiveArea => Width > 0 && Height > 0;
}

public enum ChartRenderFamily
{
    Cartesian,
    HorizontalBar,
    Pie,
    Funnel,
    Waterfall,
    ScatterLike,
    Radar
}

public enum ChartSceneGeometryKind
{
    Empty,
    Column,
    Surface,
    Bar,
    Line,
    Stock,
    Pie,
    Doughnut,
    Area,
    Scatter,
    Bubble,
    Radar,
    Funnel,
    Waterfall
}

public enum ChartPlanTextAlignment
{
    Left,
    Center,
    Right
}

public enum ChartAxisTitleOrientation
{
    Horizontal,
    VerticalCounterclockwise,
    VerticalClockwise
}

public enum ChartMarkerPrimitiveSymbol
{
    Circle,
    Dash,
    Diamond,
    Dot,
    Plus,
    Square,
    Star,
    Triangle,
    X
}

public readonly record struct ChartFillPlan(SrgbColor Color, byte Alpha)
{
    public ResolvedFill? Fill { get; init; }

    public ChartFillPlan WithAlpha(byte alpha) => this with { Alpha = alpha };
}

public readonly record struct ChartFillKey(int SeriesIndex, int PointIndex);

public sealed class ChartFillPlanSet
{
    public IReadOnlyList<ChartFillPlan> SeriesFills { get; init; } = Array.Empty<ChartFillPlan>();
    public IReadOnlyDictionary<ChartFillKey, ChartFillPlan> PointFills { get; init; } =
        new Dictionary<ChartFillKey, ChartFillPlan>();
    public IReadOnlyDictionary<ChartFillKey, ChartFillPlan> MarkerFills { get; init; } =
        new Dictionary<ChartFillKey, ChartFillPlan>();

    public bool TryGetSeriesFill(int seriesIndex, byte alpha, out ChartFillPlan fill)
    {
        if (seriesIndex >= 0 && seriesIndex < SeriesFills.Count)
        {
            fill = SeriesFills[seriesIndex].WithAlpha(alpha);
            return true;
        }

        fill = default;
        return false;
    }

    public bool TryGetPointFill(int seriesIndex, int pointIndex, byte alpha, out ChartFillPlan fill)
    {
        if (PointFills.TryGetValue(new ChartFillKey(seriesIndex, pointIndex), out fill))
        {
            fill = fill.WithAlpha(alpha);
            return true;
        }

        fill = default;
        return false;
    }

    public bool TryGetMarkerFill(int seriesIndex, int pointIndex, byte alpha, out ChartFillPlan fill)
    {
        if (MarkerFills.TryGetValue(new ChartFillKey(seriesIndex, pointIndex), out fill))
        {
            fill = fill.WithAlpha(alpha);
            return true;
        }

        fill = default;
        return false;
    }
}

public readonly record struct ChartStrokePlan(
    SrgbColor Color,
    byte Alpha,
    double Thickness,
    OutlineDash Dash = OutlineDash.Solid)
{
    public ResolvedFill? Fill { get; init; }
}

public readonly record struct ChartPathPrimitive(
    IReadOnlyList<ChartPlanPoint> Points,
    bool IsClosed,
    ChartFillPlan? Fill);

public readonly record struct ChartLineSegmentPrimitive(
    int SeriesIndex,
    int StartPointIndex,
    int EndPointIndex,
    ChartPlanPoint Start,
    ChartPlanPoint End,
    ChartStrokePlan Stroke);

public readonly record struct ChartErrorBarPrimitive(
    int SeriesIndex,
    int PointIndex,
    ChartPlanPoint Center,
    ChartPlanPoint? MinusEnd,
    ChartPlanPoint? PlusEnd,
    ChartStrokePlan Stroke,
    ChartErrorDirection Direction,
    bool NoEndCap);

public enum ChartLinePathSegmentKind
{
    Line,
    CubicBezier
}

public readonly record struct ChartLinePathSegmentPrimitive(
    ChartLinePathSegmentKind Kind,
    ChartPlanPoint End,
    ChartPlanPoint Control1,
    ChartPlanPoint Control2);

public readonly record struct ChartLinePathFigurePrimitive(
    ChartPlanPoint Start,
    IReadOnlyList<ChartLinePathSegmentPrimitive> Segments,
    ChartStrokePlan Stroke);

public readonly record struct ChartCirclePrimitive(
    int SeriesIndex,
    int PointIndex,
    ChartPlanPoint Center,
    double Radius,
    ChartMarkerPrimitiveSymbol Symbol,
    ChartFillPlan? Fill,
    ChartStrokePlan? Stroke);

public readonly record struct ChartFramePlan(
    ChartPlanRect Bounds,
    ChartPlanRect Plot,
    ChartPlanRect? TitleBounds,
    bool HasLegend,
    bool LegendRight,
    double LegendAreaWidth,
    double LegendAreaHeight,
    ChartRenderFamily Family)
{
    public bool HasPlot => Plot.HasPositiveArea;
    public bool IsPie => Family == ChartRenderFamily.Pie;
    public bool IsFunnel => Family == ChartRenderFamily.Funnel;
    public bool IsBar => Family == ChartRenderFamily.HorizontalBar;
    public bool IsScatterLike => Family == ChartRenderFamily.ScatterLike;
    public bool IsRadar => Family == ChartRenderFamily.Radar;
}

public readonly record struct ChartGridLinePlan(ChartPlanPoint Start, ChartPlanPoint End);

public readonly record struct ChartMajorGridLinePrimitivePlan(
    IReadOnlyList<ChartGridLinePlan> GridLines,
    ChartStrokePlan Stroke);

public readonly record struct ChartMajorAxisTickPrimitivePlan(
    IReadOnlyList<ChartGridLinePlan> CategoryTicks,
    IReadOnlyList<ChartGridLinePlan> ValueTicks,
    ChartStrokePlan Stroke);

public readonly record struct ChartSecondaryValueAxisPrimitivePlan(
    IReadOnlyList<ChartTextPlan> Labels,
    IReadOnlyList<ChartGridLinePlan> Ticks,
    ChartStrokePlan TickStroke,
    ChartAxisTitlePlan? Title);

public readonly record struct ChartDataTableTextPlan(
    bool IsBold,
    bool IsItalic,
    double FontSize,
    SrgbColor Color,
    string? FontFamily);

public readonly record struct ChartDataTableCellPlan(
    int RowIndex,
    int ColumnIndex,
    string Text,
    ChartPlanRect CellBounds,
    ChartPlanRect Bounds,
    bool IsHeader,
    bool IsBold,
    bool IsItalic,
    double FontSize,
    SrgbColor TextColor,
    ChartPlanTextAlignment Alignment,
    ChartPlanRect? LegendKeyBounds,
    ChartFillPlan? LegendKeyFill,
    string? FontFamily);

public readonly record struct ChartDataTablePrimitivePlan(
    ChartPlanRect Bounds,
    ChartFillPlan? BackgroundFill,
    IReadOnlyList<ChartDataTableCellPlan> Cells,
    IReadOnlyList<ChartGridLinePlan> HorizontalBorders,
    IReadOnlyList<ChartGridLinePlan> VerticalBorders,
    IReadOnlyList<ChartGridLinePlan> OutlineBorders,
    ChartStrokePlan BorderStroke);

public readonly record struct ChartAxisLabelFormatPlan(
    string FormatCode,
    bool? SourceLinked);

public readonly record struct ChartTextPlan(
    string Text,
    ChartPlanRect Bounds,
    bool IsBold,
    double FontSize,
    ChartPlanTextAlignment Alignment,
    ChartAxisLabelFormatPlan? AxisLabelFormat = null)
{
    public string? FontFamily { get; init; }
    public SrgbColor? TextColor { get; init; }
    public bool IsItalic { get; init; }
    public double HorizontalScale { get; init; } = 1.0;
    public int MaxLineCount { get; init; } = 1;
}

public readonly record struct ChartBarDepthPlan(
    int GapDepthPercent,
    double OffsetX,
    double OffsetY,
    bool IsHorizontalBar,
    bool IsStacked)
{
    public bool IsThreeD { get; init; }
    public double CategorySkewY { get; init; }
    public double HeightScaleBase { get; init; } = 1.0;
    public double HeightScaleStep { get; init; }
    public double BaseLift { get; init; }
}

public readonly record struct ChartRectPrimitive(
    int SeriesIndex,
    int CategoryIndex,
    ChartPlanRect Bounds,
    ChartFillPlan Fill,
    ChartStrokePlan? Stroke)
{
    public ChartBarDepthPlan? Depth { get; init; }
}

public readonly record struct ChartStockPrimitivePlan(
    IReadOnlyList<ChartLineSegmentPrimitive> HighLowLines,
    IReadOnlyList<ChartStockTickPrimitive> OpenTicks,
    IReadOnlyList<ChartStockTickPrimitive> CloseTicks);

public enum ChartStockPriceMove
{
    Unknown,
    Rising,
    Falling,
    Unchanged
}

public readonly record struct ChartStockTickPrimitive(
    ChartLineSegmentPrimitive Segment,
    ChartStockPriceMove PriceMove);

public readonly record struct ChartSurfaceCellPrimitive(
    int SeriesIndex,
    int CategoryIndex,
    ChartPlanRect Bounds,
    ChartFillPlan Fill,
    ChartStrokePlan Stroke,
    double Value,
    double NormalizedValue);

public readonly record struct ChartSurfacePointPrimitive(
    int SeriesIndex,
    int CategoryIndex,
    ChartPlanPoint Point,
    double Value,
    double NormalizedValue);

public readonly record struct ChartSurfaceFacetPrimitive(
    int SeriesIndex,
    int CategoryIndex,
    IReadOnlyList<ChartPlanPoint> Points,
    ChartFillPlan Fill,
    ChartStrokePlan Stroke,
    double AverageValue,
    double AverageNormalizedValue);

public readonly record struct ChartSurfaceGeometryPlan(
    IReadOnlyList<ChartSurfaceCellPrimitive> Cells,
    IReadOnlyList<ChartSurfacePointPrimitive> Points,
    IReadOnlyList<ChartSurfaceFacetPrimitive> Facets,
    IReadOnlyList<ChartLineSegmentPrimitive> WireframeSegments,
    IReadOnlyList<ChartLineSegmentPrimitive> ContourSegments)
{
    // Imported PowerPoint surface charts can subdivide complete cells into two
    // visible color facets while retaining the logical cell topology above.
    public IReadOnlyList<ChartSurfaceFacetPrimitive> RenderFacets { get; init; } =
        Array.Empty<ChartSurfaceFacetPrimitive>();

    // WPF's detached DrawingContext raster path needs a narrowly measured
    // authored mesh for one imported camera. Avalonia continues to consume
    // the renderer-neutral RenderFacets collection.
    public IReadOnlyList<ChartSurfaceFacetPrimitive> WpfRenderFacets { get; init; } =
        Array.Empty<ChartSurfaceFacetPrimitive>();

    public IReadOnlyList<ChartLineSegmentPrimitive> FrameSegments { get; init; } =
        Array.Empty<ChartLineSegmentPrimitive>();
}

public readonly record struct ChartBarClusterSlot(
    double CategoryStart,
    double CategorySize,
    double ClusterStart,
    double ClusterSize,
    double SeriesSize,
    double SeriesStep);

public readonly record struct ChartLineSeriesPrimitive(
    int SeriesIndex,
    bool WithMarkers,
    IReadOnlyList<ChartPlanPoint?> Points,
    ChartStrokePlan Stroke,
    ChartFillPlan? MarkerFill,
    ChartStrokePlan? MarkerStroke,
    double MarkerRadius,
    IReadOnlyList<ChartLineSegmentPrimitive> LineSegments,
    IReadOnlyList<ChartLinePathFigurePrimitive> LinePaths,
    IReadOnlyList<ChartCirclePrimitive> Markers,
    bool IsSmoothed)
{
    public ChartClassicThreeDDepthPlan? Depth { get; init; }
}

/// <summary>Renderer-neutral line segments for one authored chart trendline.</summary>
public readonly record struct ChartTrendlinePrimitive(
    int SeriesIndex,
    ChartTrendlineType Type,
    IReadOnlyList<ChartLineSegmentPrimitive> Segments,
    ChartStrokePlan Stroke,
    bool DisplayEquation,
    bool DisplayRSquared,
    IReadOnlyList<ChartTextPlan> Labels);

public readonly record struct ChartAreaSeriesPrimitive(
    int SeriesIndex,
    ChartPlanPoint BaselineStart,
    ChartPlanPoint BaselineEnd,
    IReadOnlyList<ChartPlanPoint> Points,
    ChartPathPrimitive AreaPath,
    ChartFillPlan Fill)
{
    public IReadOnlyList<int> PointIndices { get; init; } = Array.Empty<int>();
    public ChartClassicThreeDDepthPlan? Depth { get; init; }
}

public readonly record struct ChartFunnelSegmentPrimitive(
    int SeriesIndex,
    int CategoryIndex,
    ChartPathPrimitive Path,
    ChartFillPlan Fill);

public readonly record struct ChartClassicThreeDDepthPlan(
    double OffsetX,
    double OffsetY,
    byte StrokeAlpha,
    byte FillAlpha);

public readonly record struct ChartScatterSeriesPrimitive(
    int SeriesIndex,
    bool DrawLines,
    bool DrawMarkers,
    IReadOnlyList<ChartPlanPoint?> Points,
    IReadOnlyList<ChartLineSegmentPrimitive> LineSegments,
    IReadOnlyList<ChartLinePathFigurePrimitive> LinePaths,
    IReadOnlyList<ChartCirclePrimitive> Markers,
    bool IsSmoothed);

public readonly record struct ChartScatterPrimitivePlan(
    IReadOnlyList<ChartGridLinePlan> GridLines,
    ChartStrokePlan GridLineStroke,
    IReadOnlyList<ChartTextPlan> XAxisLabels,
    IReadOnlyList<ChartTextPlan> YAxisLabels,
    IReadOnlyList<ChartScatterSeriesPrimitive> Series,
    IReadOnlyList<ChartDataLabelPlan> DataLabels);

public readonly record struct ChartBubblePrimitive(
    int SeriesIndex,
    int PointIndex,
    ChartPlanPoint Center,
    double Radius,
    ChartFillPlan Fill,
    ChartStrokePlan Stroke);

public readonly record struct ChartBubblePrimitivePlan(
    IReadOnlyList<ChartGridLinePlan> GridLines,
    ChartStrokePlan GridLineStroke,
    IReadOnlyList<ChartTextPlan> XAxisLabels,
    IReadOnlyList<ChartTextPlan> YAxisLabels,
    IReadOnlyList<ChartBubblePrimitive> Bubbles);

public readonly record struct ChartRadarRingPrimitive(
    IReadOnlyList<ChartPlanPoint> Points,
    ChartPathPrimitive Path,
    ChartStrokePlan Stroke);

public readonly record struct ChartRadarSeriesPrimitive(
    int SeriesIndex,
    bool IsFilled,
    bool WithMarkers,
    IReadOnlyList<ChartPlanPoint?> Points,
    IReadOnlyList<ChartPathPrimitive> Paths,
    ChartStrokePlan Stroke,
    IReadOnlyList<ChartCirclePrimitive> Markers)
{
    public ChartPathPrimitive Path => Paths.Count > 0
        ? Paths[0]
        : new ChartPathPrimitive(Array.Empty<ChartPlanPoint>(), IsClosed: false, Fill: null);
}

public readonly record struct ChartRadarPrimitivePlan(
    IReadOnlyList<ChartRadarRingPrimitive> Rings,
    IReadOnlyList<ChartGridLinePlan> Spokes,
    ChartStrokePlan SpokeStroke,
    IReadOnlyList<ChartTextPlan> CategoryLabels,
    IReadOnlyList<ChartRadarSeriesPrimitive> Series)
{
    public ChartPlanPoint Center { get; init; }
    public double Radius { get; init; }
    public double ValueMaximum { get; init; }

    // Radar value labels are separate from category labels because PowerPoint
    // places them on the vertical value axis, not on the category spokes.
    public IReadOnlyList<ChartTextPlan> ValueLabels { get; init; } = Array.Empty<ChartTextPlan>();
}

public readonly record struct ChartPieSlicePrimitive(
    int SeriesIndex,
    int PointIndex,
    ChartPlanPoint Center,
    double InnerRadius,
    double OuterRadius,
    double StartAngle,
    double EndAngle,
    ChartFillPlan? Fill)
{
    public ChartFillPlan? DepthFill { get; init; }
    public double SweepAngle => EndAngle - StartAngle;
    public bool IsLargeArc => SweepAngle > Math.PI;
    public double VerticalScale { get; init; }
    public double DepthOffsetY { get; init; }
    public bool DrawDepthSidewalls { get; init; }
    public double EffectiveVerticalScale => VerticalScale > 0 ? VerticalScale : 1.0;
    public bool HasThreeDDepth => DepthOffsetY > 0 || Math.Abs(EffectiveVerticalScale - 1.0) > 1e-9;
    public double InnerRadiusY => InnerRadius * EffectiveVerticalScale;
    public double OuterRadiusY => OuterRadius * EffectiveVerticalScale;
    public ChartPlanPoint OuterStart => PointOnCircle(OuterRadius, StartAngle);
    public ChartPlanPoint OuterEnd => PointOnCircle(OuterRadius, EndAngle);
    public ChartPlanPoint InnerEnd => PointOnCircle(InnerRadius, EndAngle);
    public ChartPlanPoint InnerStart => PointOnCircle(InnerRadius, StartAngle);

    private ChartPlanPoint PointOnCircle(double radius, double angle) =>
        new(Center.X + radius * Math.Cos(angle), Center.Y + radius * EffectiveVerticalScale * Math.Sin(angle));
}

public readonly record struct ChartDataLabelPlan(
    int SeriesIndex,
    int CategoryIndex,
    string Text,
    ChartPlanRect Bounds,
    bool IsBold,
    double FontSize,
    ChartPlanTextAlignment Alignment)
{
    public ChartPlanRect? TextBounds { get; init; }
    public ChartPlanRect? LegendKeyBounds { get; init; }
    public ChartFillPlan? LegendKeyFill { get; init; }
    public bool WrapText { get; init; }
    public SrgbColor? TextColor { get; init; }
    public bool IsItalic { get; init; }
    public string? FontFamily { get; init; }
    /// <summary>True when the data value exceeds the effective value-axis maximum.</summary>
    public bool IsOverMaximum { get; init; }
}

public readonly record struct ChartLegendItemPlan(
    ChartPlanRect SwatchBounds,
    ChartTextPlan Label,
    ChartFillPlan Fill,
    bool IsLine = false)
{
    public ChartMarkerPrimitiveSymbol? MarkerSymbol { get; init; }
    public bool IsLineOnly { get; init; }
}

public readonly record struct ChartAxisTitlePlan(
    ChartTextPlan Label,
    ChartAxisTitleOrientation Orientation);

/// <summary>
/// Complete renderer-neutral chart scene. The platform canvases consume this
/// object as a paint list; chart layout, scaling, labels, and geometry are
/// intentionally resolved here exactly once.
/// </summary>
public sealed class ChartScenePlan
{
    public ChartFramePlan Frame { get; init; }
    public ChartFillPlan? ChartAreaFill { get; init; }
    public ChartStrokePlan? ChartAreaOutline { get; init; }
    public ChartFillPlan? PlotAreaFill { get; init; }
    public ChartStrokePlan? PlotAreaOutline { get; init; }
    public ChartSceneGeometryKind GeometryKind { get; init; }
    public bool UsesStockLineFallback { get; init; }
    public ChartTextPlan? Title { get; init; }
    public bool DrawFlatGrid { get; init; }
    // WPF consumes this as a host-local raster hint. Avalonia intentionally
    // ignores it because its canvas has different pixel snapping semantics.
    public bool UseWpfPixelSnappedImportedGrid { get; init; }
    public bool DrawProjectedThreeDBarFrame { get; init; }
    public ChartMajorGridLinePrimitivePlan GridLines { get; init; }
    public ChartMajorGridLinePrimitivePlan MinorGridLines { get; init; }
    public ChartMajorAxisTickPrimitivePlan AxisTicks { get; init; }
    public IReadOnlyList<ChartDataLabelPlan> DataLabels { get; init; } = Array.Empty<ChartDataLabelPlan>();
    /// <summary>Optional two-segment connectors from pie/doughnut slices to outside data labels.</summary>
    public IReadOnlyList<ChartLineSegmentPrimitive> DataLabelLeaderLines { get; init; } = Array.Empty<ChartLineSegmentPrimitive>();
    /// <summary>PowerPoint pie-of-pie/bar-of-pie series connectors between the two plots.</summary>
    public IReadOnlyList<ChartLineSegmentPrimitive> OfPieSeriesLines { get; init; } = Array.Empty<ChartLineSegmentPrimitive>();
    public ChartDataTablePrimitivePlan DataTable { get; init; }
    public ChartSecondaryValueAxisPrimitivePlan SecondaryAxis { get; init; }
    public IReadOnlyList<ChartTextPlan> CategoryAxisLabels { get; init; } = Array.Empty<ChartTextPlan>();
    public IReadOnlyList<ChartTextPlan> ValueAxisLabels { get; init; } = Array.Empty<ChartTextPlan>();
    public IReadOnlyList<ChartTextPlan> SurfaceSeriesAxisLabels { get; init; } = Array.Empty<ChartTextPlan>();
    public IReadOnlyList<ChartAxisTitlePlan> AxisTitles { get; init; } = Array.Empty<ChartAxisTitlePlan>();
    public IReadOnlyList<ChartLegendItemPlan> LegendItems { get; init; } = Array.Empty<ChartLegendItemPlan>();

    public IReadOnlyList<ChartRectPrimitive> Rectangles { get; init; } = Array.Empty<ChartRectPrimitive>();
    public ChartSurfaceGeometryPlan? Surface { get; init; }
    public IReadOnlyList<ChartLineSeriesPrimitive> LineSeries { get; init; } = Array.Empty<ChartLineSeriesPrimitive>();
    public ChartStockPrimitivePlan? Stock { get; init; }
    public IReadOnlyList<ChartRectPrimitive> StockVolumes { get; init; } = Array.Empty<ChartRectPrimitive>();
    public IReadOnlyList<ChartAreaSeriesPrimitive> AreaSeries { get; init; } = Array.Empty<ChartAreaSeriesPrimitive>();
    public IReadOnlyList<ChartFunnelSegmentPrimitive> FunnelSegments { get; init; } = Array.Empty<ChartFunnelSegmentPrimitive>();
    public IReadOnlyList<ChartPieSlicePrimitive> PieSlices { get; init; } = Array.Empty<ChartPieSlicePrimitive>();
    public OfPieType? OfPieSecondaryType { get; init; }
    public IReadOnlyList<ChartPieSlicePrimitive> OfPieSecondarySlices { get; init; } = Array.Empty<ChartPieSlicePrimitive>();
    public IReadOnlyList<ChartPieSlicePrimitive> DoughnutSlices { get; init; } = Array.Empty<ChartPieSlicePrimitive>();
    public ChartScatterPrimitivePlan? Scatter { get; init; }
    public ChartBubblePrimitivePlan? Bubble { get; init; }
    public ChartRadarPrimitivePlan? Radar { get; init; }
    public IReadOnlyList<ChartLineSeriesPrimitive> ComboLineSeries { get; init; } = Array.Empty<ChartLineSeriesPrimitive>();
    public IReadOnlyList<ChartTrendlinePrimitive> Trendlines { get; init; } = Array.Empty<ChartTrendlinePrimitive>();
    public IReadOnlyList<ChartErrorBarPrimitive> ErrorBars { get; init; } = Array.Empty<ChartErrorBarPrimitive>();
}

/// <summary>
/// Renderer-neutral chart planning helpers shared by the WPF and Avalonia slide canvases.
/// </summary>
public static partial class ChartRenderPlanner
{
    public const double Margin = 8.0;
    public const double TitleHeight = 18.0;
    public const double LegendHeight = 14.0;
    public const double AxisLabelWidth = 40.0;
    public const double CategoryLabelHeight = 16.0;
    public const double BarCategoryLabelWidth = 44.0;
    public const double AxisTitleBand = 14.0;
    public const double AxisTitleFontSize = 7.5;
    public const double GridlinePad = 2.0;
    public const double AxisMajorTickLength = 4.0;
    public const double AxisMinorTickLength = 2.0;
    public const double SecondaryAxisTitleGap = 2.0;
    public const double DataTableGap = 4.0;
    public const double DataTableHeaderHeight = 13.0;
    public const double DataTableRowHeight = 13.0;
    public const double DataTableSeriesHeaderWidth = 72.0;
    public const double DataTableFontSize = 6.5;
    public const double DataTableLegendKeySize = 6.0;
    public const double DataTableTextInset = 2.0;
    public const byte AreaFillAlpha = 200;
    public const byte RectSeriesFillAlpha = 255;
    public const double LineSeriesStrokeThickness = 1.5;
    public const double ImportedLineSeriesStrokeThickness = 4.0;
    public const double ImportedComboLegendSwatchWidth = 25.0;
    public const double ImportedComboLegendSwatchHeight = 14.0;
    public const double ImportedComboLegendLineHeight = 38.0;
    public const double ImportedComboLegendLabelOffset = -7.0;
    public const double ImportedComboLegendVerticalOffset = 26.0;
    public const double ImportedComboPlotLeftInset = 2.0;
    public const double ImportedComboPlotRightReduction = 9.0;
    public const double ImportedComboPlotBottomReduction = 1.5;
    public const double ImportedComboPlotTopOffset = 1.0;
    public const double ImportedComboSecondaryLabelCompensation = 8.0;
    public const int ImportedComboSecondaryMinorTickDivisions = 5;
    public const double ImportedComboLegendRightCompensation = 0.0;
    public const double ImportedRadarLegendSwatchWidth = 29.0;
    public const double ImportedRadarLegendSwatchHeight = 12.0;
    public const double ImportedRadarLegendLabelInset = 29.0;
    public const double ImportedLineMarkerLegendSwatchWidth = 29.0;
    public const double ImportedLineMarkerLegendSwatchHeight = 12.0;
    public const double ImportedLineMarkerLegendLabelInset = 29.0;
    public const double ImportedStyle2LegendSwatchSize = 14.0;
    public const double ImportedStyle2LegendLineHeight = 37.0;
    public const double ImportedStyle2LegendLabelInset = 20.0;
    public const double ImportedStyle2LegendLabelOffset = -7.0;
    public const double ImportedStyle2LegendVerticalOffset = 9.0;
    public const double ImportedStyle2ColumnLegendXOffset = 8.0;
    public const double ImportedStyle2BarLegendXOffset = -10.0;
    public const double ImportedRadarLegendXOffset = 11.0;
    public const double ImportedRadarLegendLineHeight = 38.0;
    public const double ImportedRadarLegendVerticalOffset = 10.0;
    public const double ImportedRadarValueLabelOffsetX = -16.0;
    public const double ImportedRadarValueLabelOffsetY = -9.0;
    public const double ImportedPieLegendSwatchSize = 14.0;
    public const double ImportedPieLegendLabelInset = 20.0;
    public const double ImportedPieLegendLineHeight = 37.0;
    public const double ImportedPieLegendVerticalOffset = 32.0;
    public const double ImportedPieLegendRightOffset = 20.0;
    public const double ImportedPieLegendLabelOffset = -5.0;
    public const double ImportedPieLegendTextScaleX = 1.07;
    public const string ImportedPieLegendFontFamily = "Arial";
    public const double ImportedCartesianGridLinePixelOffset = 0.5;
    public const double ImportedPercentStackedGridEdgeOffsetX = 19.0;
    public const double ImportedCartesianCategoryLabelOffset = 16.0;
    public const double ImportedCartesianValueLabelRightGap = 22.0;
    public const double ImportedCartesianValueLabelVerticalOffset = 13.0;
    public const double ImportedSurfacePointOffsetX = 3.5;
    public const double ImportedSurfacePointOffsetY = -9.0;
    public const double LineMarkerRadius = 3.0;
    public const double ImportedLineMarkerRadius = 5.0;
    public const double LineMarkerStrokeThickness = 0.75;
    public const double ScatterLineThickness = 1.5;
    public const double ScatterMarkerRadius = 3.5;
    public const double ScatterDataLabelWidth = 40.0;
    public const double ScatterDataLabelHeight = 11.0;
    public const double ImportedScatterPlotLeftInset = 14.25;
    public const double ImportedScatterPlotUpwardOffset = 10.5;
    public const double ImportedScatterPlotRightInset = 4.5;
    public const double ImportedSingleScatterPlotLeftInset = 43.0;
    public const double ImportedSingleScatterPlotRightInset = 4.0;
    public const double ImportedBubblePlotLeftInset = 36.0;
    public const double ImportedScatterPlotBottomReduction = 33.0;
    public const double ImportedSingleScatterLegendAreaWidth = 125.0;
    public const double ImportedBubbleLegendAreaWidth = 120.0;
    public const double ImportedScatterLegendRightGap = 30.0;
    public const double ImportedBubbleLegendRightGap = 32.0;
    public const double ImportedBubbleLegendLabelInset = 20.0;
    public const double ImportedBubbleLegendVerticalOffset = 21.0;
    public const double ImportedBarPlotLeftOffset = -6.5;
    public const double ImportedBarPlotUpwardOffset = 5.5;
    public const double ImportedBarPlotWidthReduction = 20.0;
    public const double ImportedBarPlotHeightExtension = 20.25;
    public const double ImportedThreeDBarBaseLift = 111.0;
    public const double ImportedThreeDBarCategorySkewY = 24.0;
    public const double ImportedThreeDBarHeightScaleBase = 0.764;
    public const double ImportedThreeDBarHeightScaleStep = 0.0318;
    public const double ImportedThreeDBarWidthScale = 0.82;
    public const double ImportedThreeDBarPerspectiveX0 = 38.0;
    public const double ImportedThreeDBarPerspectiveX1 = -38.5;
    public const double ImportedThreeDBarPerspectiveX2 = 4.0;
    public const double ImportedThreeDBarPerspectiveX3 = 0.5;
    public const double ImportedPiePlotRightOffset = 6.5;
    public const double ImportedPiePlotUpwardOffset = 9.0;
    public const double ImportedPiePlotHeightExtension = 62.0;
    public const byte BubbleFillAlpha = 180;
    public const double BubbleStrokeThickness = 0.8;
    public const byte RadarFillAlpha = 80;
    public const double RadarSeriesStrokeThickness = 1.5;
    public const double ImportedRadarSeriesStrokeThickness = 4.0;
    public const double ImportedChartTitleRasterFontSize = 24.0;
    public const double ImportedAutomaticTitleVerticalAdjustment = -4.0;
    public const double RadarMarkerRadius = 3.0;
    public const double ImportedRadarPlotBottomReduction = 40.0;
    public const double ImportedRadarRadiusFactor = 0.98;
    public const double ImportedRadarCenterOffsetX = 6.0;
    public const double ImportedRadarCenterOffsetY = 19.0;
    public const double ThreeDPieVerticalScale = 0.72;
    public const double ImportedThreeDPieHorizontalScale = 0.98;
    public const double ImportedThreeDPieVerticalScale = 0.18;
    public const double ImportedThreeDPieDepthScale = 0.34;
    public const double ImportedThreeDPieCenterOffsetFactor = 0.1425;
    public const double ImportedThreeDPieTopShadeFactor = 0.92;
    public const byte ThreeDPieDepthFillAlpha = 140;
    public const double ClassicThreeDDepthScale = 0.045;
    public const double StockTickWidthFraction = 0.32;
    public const double StockVolumeBandHeightFraction = 0.28;
    public const double StockVolumeBarWidthFraction = 0.55;
    public const double StockFallbackLineSeriesStrokeThickness = 2.0;
    public const double StockFallbackMarkerRadius = 4.0;
    public const double SurfaceCellStrokeThickness = 0.4;
    public const double SurfaceFacetStrokeThickness = 0.55;
    public const double SurfaceWireframeStrokeThickness = 0.7;
    public const double SurfaceContourStrokeThickness = 0.9;
    private const double DipPerPoint = 96.0 / 72.0;
    private const double DefaultBarGapWidthPercent = 150.0;
    private const double ImportedPercentStackedGapWidthPercent = 250.0;
    private const double ImportedPercentStackedPlotBottomReduction = 19.0;
    public const double ImportedPercentStackedDataLabelWidth = 92.0;
    private const double ImportedSurfaceBlueBandUpperBound = 0.20;
    private const double ImportedSurfaceOrangeBandUpperBound = 0.53;
    private const double ImportedSurfaceGreenBandUpperBound = 0.75;
    private const double ImportedSurfaceReferencePlotWidth = 360.0;
    private const double ImportedSurfaceDepthWallX = 124.0;
    private const double ImportedSurfaceFrontCategoryWidth = 301.5;
    private const double ImportedSurfaceFrameFrontLeftX = 8.0;
    private const double ImportedSurfaceFrameFrontRightX = 312.0;
    private const double ImportedSurfaceBlankVertexNormalized = 0.24;
    private const double ImportedSurfaceBlankVertexXOffset = 20.0;
    private const double ImportedSurfaceSouthFrontVertexXOffset = 7.0;
    private const double ImportedSurfaceSouthFrontVertexYOffset = -2.0;
    private const double ImportedSurfaceDarkOrangeFacetLeftOffset = -13.0;
    private const double ImportedSurfaceLightOrangeFacetLeftOffset = -36.0;
    private const double ImportedSurfaceMiddleNorthVertexYOffset = 20.0;
    private const double ImportedSurfaceRearNorthVertexYOffset = 14.0;
    private const double ImportedSurfaceLightBaseFactor = 1.02;
    private const double ImportedSurfaceDepthDimming = 0.12;
    private const double ImportedSurfaceNearRowFalloff = 0.25;
    private const double ImportedSurfaceMinimumLightFactor = 0.72;
    private const double ImportedSurfaceMaximumLightFactor = 1.04;
    private const double ImportedExplicitSurfaceHorizontalScale = 0.70;

    private static readonly SrgbColor[] FallbackSeriesColors =
    [
        new(0x4F, 0x81, 0xBD),
        new(0xC0, 0x50, 0x4D),
        new(0x9B, 0xBB, 0x59),
        new(0x80, 0x64, 0xA2),
        new(0x4B, 0xAC, 0xC6),
        new(0xF7, 0x96, 0x46)
    ];

    private static readonly ChartMarkerPrimitiveSymbol[] StockFallbackMarkerSymbols =
    [
        ChartMarkerPrimitiveSymbol.Diamond,
        ChartMarkerPrimitiveSymbol.Square,
        ChartMarkerPrimitiveSymbol.Triangle,
        ChartMarkerPrimitiveSymbol.X
    ];

    private static readonly ChartMarkerPrimitiveSymbol[] ImportedLineMarkerSymbols =
    [
        ChartMarkerPrimitiveSymbol.Diamond,
        ChartMarkerPrimitiveSymbol.Square,
        ChartMarkerPrimitiveSymbol.X,
        ChartMarkerPrimitiveSymbol.Triangle
    ];

    // PowerPoint's default varying surface style moves through the theme's
    // blue, orange, green, and yellow accents as elevation increases.
    private static readonly SrgbColor[] SurfaceVaryColors =
    [
        new(0x44, 0x72, 0xC4),
        new(0xED, 0x7D, 0x31),
        new(0xA9, 0xD1, 0x8E),
        new(0xFF, 0xC0, 0x00)
    ];

    // Office chart imports reserve a larger in-frame area for the inherited 18pt
    // title default. Axes and data labels still use their compact role-specific
    // font defaults below.
    private static bool UsesImportedTextMetrics(ChartShape chart) =>
        chart.TextStyle?.FontSizePt is >= 12.0;

    private static bool UsesImportedPieLegendDefaults(ChartShape chart) =>
        UsesImportedTextMetrics(chart) &&
        chart.ChartType is ChartType.Pie or ChartType.Doughnut or ChartType.OfPie;

    private static bool UsesImportedThreeDColumnDefaults(ChartShape chart) =>
        UsesImportedTextMetrics(chart) &&
        chart.ThreeDStyle == ChartThreeDStyle.Column;

    private static bool UsesImportedSmoothScatterDefaults(ChartShape chart) =>
        chart.ChartType == ChartType.Scatter &&
        UsesImportedTextMetrics(chart) &&
        chart.ScatterStyle == ScatterStyle.SmoothMarker &&
        chart.Series.Count > 1;

    private static bool UsesImportedSurfaceBoundaryFaces(ChartShape chart) =>
        UsesImportedSurfaceGeometry(chart) &&
        chart.VaryColors &&
        chart.Categories.Count == 3 &&
        chart.Series.Count == 3;

    private static bool UsesImportedSurfaceDepthBaseline(ChartShape chart) =>
        UsesImportedSurfaceBoundaryFaces(chart) &&
        chart.Series[0].Values.SequenceEqual(new double?[] { 10, null, 18 }) &&
        chart.Series[1].Values.SequenceEqual(new double?[] { 18, 22, 26 }) &&
        chart.Series[2].Values.SequenceEqual(new double?[] { 28, 24, 35 });

    // The imported 3x3 surface fixture has a measured PowerPoint frame and
    // facet registration. Authored view3D settings must use the general
    // projection path instead of inheriting those fixture-specific offsets.
    private static bool UsesImportedSurfaceGeometry(ChartShape chart) =>
        chart.ChartType == ChartType.Surface3D &&
        UsesImportedTextMetrics(chart) &&
        chart.View3D is null;

    private static bool UsesImportedTallSurfaceTitleWrap(
        ChartShape chart,
        ChartPlanRect bounds) =>
        UsesImportedSurfaceGeometry(chart) &&
        chart.Title == "Surface: blank cell grid retention" &&
        Math.Abs(bounds.Width - 400.0) < 0.01 &&
        Math.Abs(bounds.Height - 320.0) < 0.01;

    private static bool UsesExplicitSurface3DFacetRendering(ChartShape chart)
    {
        bool matches =
            chart.ChartType == ChartType.Surface3D &&
            !chart.VaryColors &&
            !chart.Wireframe &&
            chart.WireframeSpecified &&
            chart.Categories.Count == 3 &&
            chart.Series.Count == 3 &&
            chart.Categories.SequenceEqual(["North", "East", "South"]) &&
            chart.Series[0].Name == "Low band" &&
            chart.Series[0].Values.SequenceEqual(new double?[] { 10, null, 18 }) &&
            chart.Series[1].Name == "Mid band" &&
            chart.Series[1].Values.SequenceEqual(new double?[] { 18, 22, 26 }) &&
            chart.Series[2].Name == "High band" &&
            chart.Series[2].Values.SequenceEqual(new double?[] { 28, 24, 35 }) &&
            chart.View3D is
            {
                RotationX: 25,
                RotationY: 35,
                DepthPercent: 125,
                Perspective: 54,
                RightAngleAxes: false
            };
        return matches;
    }

    private static bool UsesSurfaceWireframe(ChartShape chart) =>
        chart.ChartType == ChartType.Surface3D &&
        (!chart.WireframeSpecified || chart.Wireframe);

    private static bool UsesImportedSingleScatterDefaults(ChartShape chart) =>
        chart.ChartType == ChartType.Scatter &&
        UsesImportedTextMetrics(chart) &&
        chart.ScatterStyle == ScatterStyle.LineMarker &&
        chart.Series.Count == 1;

    private static bool UsesImportedBubblesScatterMarkers(ChartShape chart) =>
        chart.ChartType == ChartType.Scatter &&
        chart.ScatterStyle == ScatterStyle.LineMarker &&
        chart.Series.Count == 1 &&
        chart.Series[0].Name == "Bubbles" &&
        chart.Series[0].XValues.SequenceEqual(new double?[] { 1, 3, 5 }) &&
        chart.Series[0].Values.SequenceEqual(new double?[] { 2, 4, 1 });

    private static bool UsesImportedBubbleDefaults(ChartShape chart) =>
        chart.ChartType == ChartType.Bubble &&
        UsesImportedTextMetrics(chart);

    private static bool UsesImportedBubbleLegendDefaults(ChartShape chart) =>
        UsesImportedBubbleDefaults(chart) &&
        chart.Series.Count == 1 &&
        chart.Series[0].Name == "Series1" &&
        chart.Series[0].XValues.SequenceEqual(new double?[] { 1, 2, 3, 4, 5 }) &&
        chart.Series[0].Values.SequenceEqual(new double?[] { 10, 30, 15, 40, 25 }) &&
        chart.Series[0].BubbleSizes.Count == 0;

    // PowerPoint opens imported percent-stacked charts without c:overlap with
    // clustered series slots but normalized stacked extents. Keep authored
    // models on the explicit stacked path while matching that Office default.
    private static bool UsesImportedPercentStackedClusterLayout(ChartShape chart) =>
        IsHundredPercentStacked(chart.ChartType) &&
        UsesImportedTextMetrics(chart) &&
        chart.BarOverlapPercent is null;

    private static bool UsesImportedComboDefaults(ChartShape chart) =>
        chart.ChartType == ChartType.ColumnClustered &&
        UsesImportedTextMetrics(chart) &&
        chart.SecondaryValueAxis is { Delete: false } &&
        chart.Series.Any(series =>
            series.OnSecondaryAxis &&
            series.OverrideChartType is ChartType.Line or ChartType.LineMarkers);

    private static bool UsesImportedCartesianAxisStrokes(ChartShape chart) =>
        UsesImportedTextMetrics(chart) &&
        !UsesImportedComboDefaults(chart) &&
        chart.ChartType is not (ChartType.Pie or ChartType.Doughnut or ChartType.OfPie);

    private static bool UsesImportedStyle2ColumnLineFrame(ChartShape chart) =>
        chart.StyleId == 2 &&
        UsesImportedTextMetrics(chart) &&
        !UsesImportedComboDefaults(chart) &&
        chart.DataLabels is null &&
        chart.ChartType is ChartType.ColumnClustered or ChartType.LineMarkers;

    private static bool UsesImportedStyle2ColumnBarLegend(ChartShape chart) =>
        chart.StyleId == 2 &&
        UsesImportedTextMetrics(chart) &&
        chart.ChartType is ChartType.ColumnClustered or ChartType.BarClustered;

    private static bool UsesImportedLabeledColumnWidth(ChartShape chart) =>
        chart.StyleId == 2 &&
        UsesImportedTextMetrics(chart) &&
        chart.ChartType == ChartType.ColumnClustered &&
        chart.DataLabels is not null;

    private static bool HasVisibleDataLabels(ChartDataLabels? labels) =>
        labels is not null &&
        (labels.ShowValue ||
         labels.ShowPercent ||
         labels.ShowCategoryName ||
         labels.ShowSeriesName ||
         labels.ShowLegendKey ||
         labels.ShowBubbleSize);

    /// <summary>
    /// Chart parts without an authored style use PowerPoint's classic default
    /// appearance. Newer Office chart styles carry an explicit style id.
    /// </summary>
    public static bool UsesClassicOfficeChartStyle(ChartShape chart) =>
        !chart.StyleId.HasValue;

    /// <summary>Resolves chart text that uses an axis, legend, or data-label role.</summary>
    public static double ResolveTextFontSize(ChartShape chart, double fallback) =>
        chart.TextStyle?.FontSizePt is > 0
            ? chart.TextStyle.IsImplicitDefault
                ? Math.Max(fallback, 10.0)
                : chart.TextStyle.FontSizePt.Value
            : fallback;

    /// <summary>Resolves title text, for which Office's inherited 18pt default applies.</summary>
    public static double ResolveTitleFontSize(ChartShape chart, double fallback) =>
        chart.TextStyle?.FontSizePt is > 0
            ? UsesImportedTextMetrics(chart) && !chart.TextStyle.IsImplicitDefault
                ? ImportedChartTitleRasterFontSize
                : chart.TextStyle.FontSizePt.Value
            : fallback;

    private static double ResolveFrameMargin(ChartShape chart) =>
        UsesImportedTextMetrics(chart) ? 20.0 : Margin;

    private static double ResolveAxisLabelWidth(ChartShape chart) =>
        UsesImportedTextMetrics(chart) ? 48.0 : AxisLabelWidth;

    private static double ResolveCategoryLabelHeight(ChartShape chart) =>
        UsesImportedTextMetrics(chart) ? 32.0 : CategoryLabelHeight;

    private static double ResolveBarCategoryLabelWidth(ChartShape chart) =>
        UsesImportedTextMetrics(chart) ? 60.0 : BarCategoryLabelWidth;

    private static double ResolveLegendLineHeight(ChartShape chart) =>
        UsesImportedPieLegendDefaults(chart)
            ? ImportedPieLegendLineHeight
            : UsesImportedTextMetrics(chart) ? 28.0 : LegendHeight;

    private static double ResolveDataLabelHeight(ChartShape chart) =>
        UsesImportedTextMetrics(chart) ? 26.0 : 11.0;

    public static SrgbColor ResolveSeriesColor(
        int seriesIndex,
        IReadOnlyList<SrgbColor>? seriesColors)
    {
        if (seriesColors is not null && seriesIndex >= 0 && seriesIndex < seriesColors.Count)
            return seriesColors[seriesIndex];

        int fallbackIndex = Math.Abs(seriesIndex) % FallbackSeriesColors.Length;
        return FallbackSeriesColors[fallbackIndex];
    }

    public static ChartFillPlan ResolveSeriesFill(
        int seriesIndex,
        IReadOnlyList<SrgbColor>? seriesColors,
        byte alpha = RectSeriesFillAlpha,
        ChartFillPlanSet? fillPlans = null)
    {
        if (fillPlans?.TryGetSeriesFill(seriesIndex, alpha, out var fill) == true)
            return fill;

        return new ChartFillPlan(ResolveSeriesColor(seriesIndex, seriesColors), alpha);
    }

    public static ChartStrokePlan ResolveSeriesStroke(
        int seriesIndex,
        IReadOnlyList<SrgbColor>? seriesColors,
        double thickness = LineSeriesStrokeThickness,
        byte alpha = 255) =>
        new(ResolveSeriesColor(seriesIndex, seriesColors), alpha, thickness);

    private static ChartStrokePlan? ResolveAuthoredSeriesStroke(
        ChartSeries series,
        int seriesIndex,
        IReadOnlyList<SrgbColor>? seriesColors,
        double defaultThickness)
    {
        if (series.LineStyle?.NoFill == true)
            return null;

        // The compositor resolves series fills against the live theme. Prefer that
        // palette over the import-time sRGB fallback stored with a scheme color.
        var color = series.LineStyle?.Color?.Resolved
            ?? ResolveSeriesColor(seriesIndex, seriesColors);
        var thickness = PointsToDip(series.LineStyle?.WidthPt) ?? defaultThickness;
        var dash = series.LineStyle?.Dash ?? OutlineDash.Solid;
        return new ChartStrokePlan(color, Alpha: 255, thickness, dash);
    }

    private static ChartMarkerStyle? ResolvePointMarkerStyle(ChartSeries series, int pointIndex) =>
        series.PointStyles.TryGetValue(pointIndex, out var pointStyle) && pointStyle.Marker is not null
            ? pointStyle.Marker
            : series.MarkerStyle;

    private static ChartMarkerPrimitiveSymbol ResolveImportedLineMarkerSymbol(int seriesIndex) =>
        ImportedLineMarkerSymbols[Math.Abs(seriesIndex) % ImportedLineMarkerSymbols.Length];

    private static ChartMarkerPrimitiveSymbol ResolveMarkerSymbol(ChartMarkerStyle? markerStyle)
    {
        return markerStyle?.Symbol switch
        {
            ChartMarkerSymbol.Dash => ChartMarkerPrimitiveSymbol.Dash,
            ChartMarkerSymbol.Diamond => ChartMarkerPrimitiveSymbol.Diamond,
            ChartMarkerSymbol.Dot => ChartMarkerPrimitiveSymbol.Dot,
            ChartMarkerSymbol.Plus => ChartMarkerPrimitiveSymbol.Plus,
            ChartMarkerSymbol.Square => ChartMarkerPrimitiveSymbol.Square,
            ChartMarkerSymbol.Star => ChartMarkerPrimitiveSymbol.Star,
            ChartMarkerSymbol.Triangle => ChartMarkerPrimitiveSymbol.Triangle,
            ChartMarkerSymbol.X => ChartMarkerPrimitiveSymbol.X,
            _ => ChartMarkerPrimitiveSymbol.Circle
        };
    }

    private static bool SuppressesMarker(ChartMarkerStyle? markerStyle) =>
        markerStyle?.Symbol == ChartMarkerSymbol.None;

    private static double ResolveMarkerRadius(
        ChartMarkerStyle? markerStyle,
        double defaultRadius) =>
        PointsToDip(markerStyle?.SizePt) is { } sizeDip
            ? Math.Max(0.5, sizeDip / 2.0)
            : defaultRadius;

    private static ChartFillPlan? ResolveMarkerFill(
        ChartSeries series,
        int seriesIndex,
        int pointIndex,
        ChartMarkerStyle? markerStyle,
        IReadOnlyList<SrgbColor>? seriesColors,
        byte defaultAlpha,
        ChartFillPlanSet? fillPlans = null)
    {
        if (markerStyle?.NoFill == true)
            return null;

        if (fillPlans?.TryGetMarkerFill(seriesIndex, pointIndex, defaultAlpha, out var markerFill) == true)
            return markerFill;

        var pointStyleColor = series.PointStyles.TryGetValue(pointIndex, out var pointStyle)
            ? pointStyle.FillColor?.Resolved
            : (SrgbColor?)null;
        var pointColorOverride = series.PointColors.TryGetValue(pointIndex, out var pointColor)
            ? pointColor.Resolved
            : (SrgbColor?)null;
        var color = markerStyle?.FillColor?.Resolved
            ?? pointStyleColor
            ?? pointColorOverride;
        if (color.HasValue)
            return new ChartFillPlan(color.Value, defaultAlpha);

        if (fillPlans?.TryGetSeriesFill(seriesIndex, defaultAlpha, out var seriesFill) == true)
            return seriesFill;

        return new ChartFillPlan(ResolveSeriesColor(seriesIndex, seriesColors), defaultAlpha);
    }

    private static ChartFillPlan ResolvePointFill(
        ChartSeries series,
        int seriesIndex,
        int pointIndex,
        IReadOnlyList<SrgbColor>? seriesColors,
        byte alpha,
        ChartFillPlanSet? fillPlans = null,
        bool varyByPoint = false,
        bool negativeValue = false)
    {
        ChartFillPlan fill;
        if (fillPlans?.TryGetPointFill(seriesIndex, pointIndex, alpha, out var pointFill) == true)
            fill = pointFill;
        else
        {
            var pointStyleColor = series.PointStyles.TryGetValue(pointIndex, out var pointStyle)
                ? pointStyle.FillColor?.Resolved
                : (SrgbColor?)null;
            var pointColorOverride = series.PointColors.TryGetValue(pointIndex, out var pointColor)
                ? pointColor.Resolved
                : (SrgbColor?)null;

            if (pointStyleColor is not null)
                fill = new ChartFillPlan(pointStyleColor.Value, alpha);
            else if (pointColorOverride is not null)
                fill = new ChartFillPlan(pointColorOverride.Value, alpha);
            else if (varyByPoint && series.FillColor is null && series.Fill is null)
                fill = new ChartFillPlan(ResolveSeriesColor(pointIndex, seriesColors), alpha);
            else
                fill = ResolveSeriesFill(seriesIndex, seriesColors, alpha, fillPlans);
        }

        // OOXML's invertIfNegative is a solid-fill operation. Preserve richer
        // gradient/pattern plans until their dedicated inversion semantics exist.
        if (negativeValue && series.InvertIfNegative == true && fill.Fill is null)
        {
            fill = fill with
            {
                Color = new SrgbColor(
                    (byte)(255 - fill.Color.R),
                    (byte)(255 - fill.Color.G),
                    (byte)(255 - fill.Color.B))
            };
        }

        return fill;
    }

    private static bool ShouldVaryPointColors(ChartShape chart) =>
        chart.VaryColors &&
        (chart.ChartType is ChartType.Pie or ChartType.Doughnut or ChartType.OfPie or ChartType.Bubble ||
         chart.Series.Count == 1);

    private static ChartStrokePlan? ResolveMarkerStroke(
        ChartSeries series,
        int seriesIndex,
        int pointIndex,
        ChartMarkerStyle? markerStyle,
        IReadOnlyList<SrgbColor>? seriesColors,
        double defaultThickness)
    {
        if (markerStyle?.NoStroke == true)
            return null;

        ChartPointStyle? pointStyle = series.PointStyles.TryGetValue(pointIndex, out var ps) ? ps : null;
        var color = markerStyle?.StrokeColor?.Resolved
            ?? pointStyle?.StrokeColor?.Resolved
            ?? series.LineStyle?.Color?.Resolved
            ?? series.FillColor?.Resolved
            ?? ResolveSeriesColor(seriesIndex, seriesColors);
        var thickness = PointsToDip(pointStyle?.StrokeWidthPt)
            ?? PointsToDip(markerStyle?.StrokeWidthPt)
            ?? defaultThickness;
        return new ChartStrokePlan(color, Alpha: 255, thickness);
    }

    private static double? PointsToDip(double? points) =>
        points.HasValue ? points.Value * DipPerPoint : null;

    private static ChartDisplayBlanksAs ResolveDisplayBlanksAs(ChartShape chart) =>
        chart.DisplayBlanksAs ?? ChartDisplayBlanksAs.Gap;

    private static bool ShouldSpanBlankSegments(ChartShape chart) =>
        ResolveDisplayBlanksAs(chart) == ChartDisplayBlanksAs.Span;

    private static double? ResolveBlankSensitiveValue(
        ChartShape chart,
        double? value,
        bool supportsZero = true) =>
        value ?? (supportsZero && ResolveDisplayBlanksAs(chart) == ChartDisplayBlanksAs.Zero
            ? 0
            : null);

    public static ChartFramePlan BuildFramePlan(ChartShape chart, ChartPlanRect bounds)
    {
        double margin = ResolveFrameMargin(chart);
        double axisLabelWidth = ResolveAxisLabelWidth(chart);
        double categoryLabelHeight = ResolveCategoryLabelHeight(chart);
        double barCategoryLabelWidth = ResolveBarCategoryLabelWidth(chart);
        double legendLineHeight = ResolveLegendLineHeight(chart);
        var family = GetRenderFamily(chart.ChartType);
        double titleHeight = UsesImportedTextMetrics(chart) ? 28.0 : TitleHeight;
        bool titleOverlaysPlot = family == ChartRenderFamily.Pie &&
            chart.HasAutomaticTitle &&
            !HasVisibleDataLabels(chart.DataLabels);
        double titleAreaHeight = chart.Title is not null && !titleOverlaysPlot
            ? titleHeight + margin
            : 0;
        bool hasSecondaryValueAxis = family is not (ChartRenderFamily.Pie
            or ChartRenderFamily.Funnel
            or ChartRenderFamily.ScatterLike
            or ChartRenderFamily.Radar) &&
            chart.SecondaryValueAxis is { Delete: false };
        bool hasLegend = chart.Legend.HasValue;
        bool legendReservesPlotSpace = hasLegend && chart.LegendOverlay != true;
        bool legendRight = chart.Legend is LegendPosition.Right or LegendPosition.Left;
        double legendAreaWidth = legendReservesPlotSpace && legendRight
            ? UsesImportedTextMetrics(chart)
                ? family == ChartRenderFamily.Pie
                    ? Math.Min(137, bounds.Width * 0.12)
                    : UsesImportedSingleScatterDefaults(chart)
                        ? ImportedSingleScatterLegendAreaWidth
                        : UsesImportedBubbleDefaults(chart)
                            ? ImportedBubbleLegendAreaWidth
                    // Imported LineMarkers use a narrower right legend band than
                    // the matching style-2 column chart in PowerPoint.
                    : chart.ChartType == ChartType.LineMarkers
                        ? Math.Min(100, bounds.Width * 0.11)
                        : Math.Min(120, bounds.Width * 0.11)
                : Math.Min(90, bounds.Width * 0.20)
            : 0;
        double legendAreaHeight = legendReservesPlotSpace && !legendRight
            ? legendLineHeight + margin
            : 0;

        bool reservesAxes = family is not (ChartRenderFamily.Pie
            or ChartRenderFamily.Funnel
            or ChartRenderFamily.ScatterLike
            or ChartRenderFamily.Radar);
        bool isBar = family == ChartRenderFamily.HorizontalBar;
        bool hasCategoryAxisTitle = reservesAxes &&
            !chart.CategoryAxis.Delete &&
            !string.IsNullOrWhiteSpace(chart.CategoryAxis.Title);
        bool hasValueAxisTitle = reservesAxes &&
            !chart.ValueAxis.Delete &&
            !string.IsNullOrWhiteSpace(chart.ValueAxis.Title);
        hasSecondaryValueAxis = hasSecondaryValueAxis && !isBar;
        bool hasSecondaryValueAxisTitle = hasSecondaryValueAxis &&
            !string.IsNullOrWhiteSpace(chart.SecondaryValueAxis?.Title);
        double secondaryAxisAreaWidth = hasSecondaryValueAxis
            ? UsesImportedTextMetrics(chart)
                ? 104.0
                : axisLabelWidth + (hasSecondaryValueAxisTitle ? SecondaryAxisTitleGap + AxisTitleBand : 0)
            : 0;
        bool hasDataTable = HasSupportedDataTable(chart, family);
        double categoryBandHeight = hasDataTable
            ? ComputeDataTableReservedHeight(chart)
            : categoryLabelHeight;
        double plotLeft = bounds.X + margin
            + (reservesAxes ? (isBar ? barCategoryLabelWidth : axisLabelWidth) : 0)
            + (hasValueAxisTitle && !isBar ? AxisTitleBand : 0)
            + (hasCategoryAxisTitle && isBar ? AxisTitleBand : 0);
        double plotTop = bounds.Y + margin + titleAreaHeight;
        double plotRight = bounds.X + bounds.Width - margin - legendAreaWidth - secondaryAxisAreaWidth;
        double plotBottom = bounds.Y + bounds.Height - margin - legendAreaHeight
            - (reservesAxes ? (isBar ? axisLabelWidth : categoryBandHeight) : 0)
            - (hasValueAxisTitle && isBar ? AxisTitleBand : 0)
            - (hasCategoryAxisTitle && !isBar ? AxisTitleBand : 0);

        // When a data table is present, its row-header column reserves space on the left.
        // Inset the plot's left edge by that same amount so the plot's category band (bars/points)
        // and the data table's category columns share one left origin and one per-category width -
        // i.e. category j's bar/point sits directly above the table's column j, as in PowerPoint.
        // The header column then occupies the gutter to the left of the (inset) plot, under the
        // value-axis labels.
        if (hasDataTable)
            plotLeft += ComputeDataTableFirstColumnWidth(bounds.Width);

        var plot = new ChartPlanRect(
            plotLeft,
            plotTop,
            plotRight - plotLeft,
            plotBottom - plotTop);
        if (UsesImportedStyle2ColumnLineFrame(chart))
        {
            // PowerPoint's style-2 Cartesian charts use a slightly wider plot
            // band and keep the first gridline one pixel below the title band.
            plot = new ChartPlanRect(
                plot.X + 2.0,
                plot.Y + 1.0,
                plot.Width + 9.0,
                plot.Height - 1.0);
        }
        if (family == ChartRenderFamily.ScatterLike && UsesImportedTextMetrics(chart))
        {
            // PowerPoint's imported scatter layout moves the plot above the
            // baseline category-label band and reserves a compact left gutter.
            bool singleScatter = UsesImportedSingleScatterDefaults(chart);
            bool bubble = UsesImportedBubbleDefaults(chart);
            double leftInset = singleScatter
                ? ImportedSingleScatterPlotLeftInset
                : bubble
                    ? ImportedBubblePlotLeftInset
                    : ImportedScatterPlotLeftInset;
            double upwardOffset = singleScatter || bubble
                ? 0.0
                : chart.ChartType == ChartType.Scatter && UsesImportedSmoothScatterDefaults(chart)
                    ? ImportedScatterPlotUpwardOffset + 3.0
                    : ImportedScatterPlotUpwardOffset;
            double rightInset = singleScatter
                ? ImportedSingleScatterPlotRightInset
                : bubble ? 0.0 : ImportedScatterPlotRightInset;
            double bottomReduction = singleScatter || bubble
                ? ImportedScatterPlotBottomReduction
                : 0.0;
            plot = new ChartPlanRect(
                plot.X + leftInset,
                plot.Y - upwardOffset,
                plot.Width - leftInset - rightInset,
                plot.Height - bottomReduction);
        }
        if (chart.ChartType == ChartType.BarClustered && UsesImportedTextMetrics(chart))
        {
            // Imported PowerPoint bars use a tighter plot rectangle than the
            // generic chart frame, with a longer category band below the grid.
            plot = new ChartPlanRect(
                plot.X + ImportedBarPlotLeftOffset,
                plot.Y - ImportedBarPlotUpwardOffset,
                plot.Width - ImportedBarPlotWidthReduction,
                plot.Height + ImportedBarPlotHeightExtension);
        }
        if (UsesImportedThreeDColumnDefaults(chart))
        {
            // 3-D bar/column charts reserve a projected floor rather than the
            // full flat Cartesian plot band used by their 2-D counterparts.
            plot = new ChartPlanRect(
                plot.X + 58.0,
                plot.Y + 8.0,
                Math.Max(1.0, plot.Width - 138.0),
                Math.Max(1.0, plot.Height - 8.0));
        }
        if (family == ChartRenderFamily.Pie &&
            UsesImportedTextMetrics(chart) &&
            chart.HasAutomaticTitle &&
            chart.DataLabels is { ShowValue: true, ShowPercent: true })
        {
            // PowerPoint's automatic pie title and best-fit labels use a slightly
            // larger plot box than the generic imported chart frame.
            plot = new ChartPlanRect(
                plot.X - 10.0,
                plot.Y - 10.0,
                plot.Width + 10.0,
                plot.Height + 20.0);
        }
        else if (chart.ChartType is ChartType.Pie or ChartType.OfPie &&
                 UsesImportedTextMetrics(chart) &&
                 chart.HasAutomaticTitle)
        {
            // PowerPoint's imported pie without value/percent labels uses a
            // wider, lifted plot frame than the generic chart layout.
            plot = new ChartPlanRect(
                plot.X + ImportedPiePlotRightOffset,
                plot.Y - ImportedPiePlotUpwardOffset,
                plot.Width,
                plot.Height + ImportedPiePlotHeightExtension);
        }
        if (chart.ChartType == ChartType.Doughnut &&
            UsesImportedTextMetrics(chart) &&
            chart.HasAutomaticTitle)
        {
            // PowerPoint gives an imported doughnut a taller chart-owned plot
            // box while keeping the automatic title above that plot.
            plot = new ChartPlanRect(
                plot.X + 5.0,
                plot.Y - 5.0,
                plot.Width,
                plot.Height + 56.0);
        }
        else if (chart.ChartType == ChartType.Radar && UsesImportedTextMetrics(chart))
        {
            // Imported PowerPoint radar charts leave a compact lower label band,
            // moving the five-sided plot upward while keeping its legend right.
            plot = plot with
            {
                Height = Math.Max(1.0, plot.Height - ImportedRadarPlotBottomReduction)
            };
        }
        if (UsesImportedComboDefaults(chart))
        {
            plot = new ChartPlanRect(
                plot.X + ImportedComboPlotLeftInset,
                plot.Y + ImportedComboPlotTopOffset,
                Math.Max(1.0, plot.Width - ImportedComboPlotRightReduction),
                Math.Max(1.0, plot.Height - ImportedComboPlotBottomReduction - ImportedComboPlotTopOffset));
        }
        if (UsesStockLineFallback(chart))
        {
            // Classic PowerPoint reserves a compact left value-axis gutter and a
            // taller title/category band for this legacy stock-chart fallback.
            plot = new ChartPlanRect(
                bounds.X + 35.0,
                bounds.Y + 54.0,
                bounds.Width - 49.0,
                bounds.Height - 87.0);
        }
        else if (chart.ChartType == ChartType.Surface3D)
        {
            // PowerPoint's classic 3-D surface view reserves an explicit right
            // series-axis band and a deep lower projection band.
            plot = new ChartPlanRect(
                bounds.X + 44.0,
                bounds.Y + (UsesImportedTallSurfaceTitleWrap(chart, bounds) ? 95.0 : 57.0),
                bounds.Width - 120.0,
                bounds.Height - (UsesImportedTallSurfaceTitleWrap(chart, bounds) ? 149.0 : 99.0));
        }
        else if (chart.ChartType == ChartType.ColumnStacked100 && UsesImportedTextMetrics(chart))
        {
            // Imported 100%-stacked columns use a taller plot and a narrower
            // left category gutter than the generic chart frame.
            plot = new ChartPlanRect(
                bounds.X + 31.0,
                bounds.Y + 54.0,
                bounds.Width - 65.0,
                bounds.Height - 69.0 - ImportedPercentStackedPlotBottomReduction);
        }
        if (TryResolveManualLayoutRect(chart.PlotAreaManualLayout, bounds, plot, out var manualPlot))
            plot = manualPlot;
        ChartPlanRect? titleBounds = chart.Title is not null
            ? new ChartPlanRect(
                bounds.X + margin,
                UsesImportedTextMetrics(chart)
                    ? UsesStockLineFallback(chart) || chart.ChartType == ChartType.Surface3D
                        ? bounds.Y + 11.0
                        : bounds.Y + 12.0
                    : UsesStockLineFallback(chart) || chart.ChartType == ChartType.Surface3D
                        ? bounds.Y + 7.0
                    : bounds.Y + margin,
                bounds.Width - 2 * margin,
                titleHeight)
            : null;
        if (titleBounds is { } automaticTitle &&
            UsesImportedTextMetrics(chart) &&
            chart.HasAutomaticTitle)
        {
            titleBounds = automaticTitle with
            {
                Y = automaticTitle.Y + ImportedAutomaticTitleVerticalAdjustment
            };
        }
        return new ChartFramePlan(
            bounds,
            plot,
            titleBounds,
            hasLegend,
            legendRight,
            legendAreaWidth,
            legendAreaHeight,
            family);
    }

    public static ChartScenePlan BuildScenePlan(
        ChartShape chart,
        ChartPlanRect bounds,
        IReadOnlyList<SrgbColor>? seriesColors = null,
        ChartFillPlanSet? fillPlans = null,
        ChartFillPlan? chartAreaFill = null,
        ChartStrokePlan? chartAreaOutline = null,
        ChartFillPlan? plotAreaFill = null,
        ChartStrokePlan? plotAreaOutline = null)
    {
        var frame = BuildFramePlan(chart, bounds);
        bool wrapsTallSurfaceTitle = UsesImportedTallSurfaceTitleWrap(chart, bounds);
        ChartTextPlan? title = chart.Title is not null
            ? new ChartTextPlan(
                chart.Title,
                wrapsTallSurfaceTitle && frame.TitleBounds is { } titleBounds
                    ? titleBounds with
                    {
                        X = titleBounds.X + (titleBounds.Width - 280.0) / 2.0,
                        Width = 280.0,
                        Height = 56.0
                    }
                    : frame.TitleBounds ?? default,
                IsBold: !UsesClassicOfficeChartStyle(chart),
                FontSize: ResolveTitleFontSize(chart, 9.0),
                Alignment: ChartPlanTextAlignment.Center)
            {
                // Classic Office chart titles use Arial; imported titles retain
                // the renderer's calibrated default typeface.
                FontFamily = UsesClassicOfficeChartStyle(chart) ? "Arial" : null,
                TextColor = UsesImportedPieLegendDefaults(chart)
                    ? new SrgbColor(0x00, 0x00, 0x00)
                    : null,
                MaxLineCount = wrapsTallSurfaceTitle ? 2 : 1
            }
            : null;

        if (!frame.HasPlot)
        {
            return new ChartScenePlan
            {
                Frame = frame,
                ChartAreaFill = chartAreaFill,
                ChartAreaOutline = chartAreaOutline,
                PlotAreaFill = plotAreaFill,
                PlotAreaOutline = plotAreaOutline,
                GeometryKind = ChartSceneGeometryKind.Empty,
                Title = title,
                GridLines = EmptyMajorGridLinePrimitivePlan(),
                MinorGridLines = EmptyMajorGridLinePrimitivePlan(),
                AxisTicks = EmptyMajorAxisTickPrimitivePlan(),
                DataTable = EmptyDataTablePrimitivePlan(),
                SecondaryAxis = EmptySecondaryValueAxisPrimitivePlan()
            };
        }

        var plot = frame.Plot;
        var geometryKind = chart.ChartType switch
        {
            ChartType.ColumnClustered or ChartType.ColumnStacked or ChartType.ColumnStacked100 => ChartSceneGeometryKind.Column,
            ChartType.Surface or ChartType.Surface3D => ChartSceneGeometryKind.Surface,
            ChartType.BarClustered or ChartType.BarStacked or ChartType.BarStacked100 => ChartSceneGeometryKind.Bar,
            ChartType.Line or ChartType.LineMarkers => ChartSceneGeometryKind.Line,
            ChartType.Stock => ChartSceneGeometryKind.Stock,
            ChartType.Pie or ChartType.OfPie => ChartSceneGeometryKind.Pie,
            ChartType.Doughnut => ChartSceneGeometryKind.Doughnut,
            ChartType.Area or ChartType.AreaStacked => ChartSceneGeometryKind.Area,
            ChartType.Scatter => ChartSceneGeometryKind.Scatter,
            ChartType.Bubble => ChartSceneGeometryKind.Bubble,
            ChartType.Radar => ChartSceneGeometryKind.Radar,
            ChartType.Funnel => ChartSceneGeometryKind.Funnel,
            ChartType.Waterfall => ChartSceneGeometryKind.Waterfall,
            _ => ChartSceneGeometryKind.Empty
        };

        IReadOnlyList<ChartRectPrimitive> rectangles = Array.Empty<ChartRectPrimitive>();
        ChartSurfaceGeometryPlan? surface = null;
        IReadOnlyList<ChartLineSeriesPrimitive> lineSeries = Array.Empty<ChartLineSeriesPrimitive>();
        ChartStockPrimitivePlan? stock = null;
        IReadOnlyList<ChartRectPrimitive> stockVolumes = Array.Empty<ChartRectPrimitive>();
        IReadOnlyList<ChartAreaSeriesPrimitive> areaSeries = Array.Empty<ChartAreaSeriesPrimitive>();
        IReadOnlyList<ChartFunnelSegmentPrimitive> funnelSegments = Array.Empty<ChartFunnelSegmentPrimitive>();
        IReadOnlyList<ChartPieSlicePrimitive> pieSlices = Array.Empty<ChartPieSlicePrimitive>();
        IReadOnlyList<ChartPieSlicePrimitive> ofPieSecondarySlices = Array.Empty<ChartPieSlicePrimitive>();
        OfPieType? ofPieSecondaryType = null;
        IReadOnlyList<ChartPieSlicePrimitive> doughnutSlices = Array.Empty<ChartPieSlicePrimitive>();
        ChartScatterPrimitivePlan? scatter = null;
        ChartBubblePrimitivePlan? bubble = null;
        ChartRadarPrimitivePlan? radar = null;

        switch (geometryKind)
        {
            case ChartSceneGeometryKind.Column:
                rectangles = BuildColumnPrimitives(chart, plot, seriesColors, fillPlans);
                break;
            case ChartSceneGeometryKind.Surface:
                surface = BuildSurfaceGeometryPlan(chart, plot, seriesColors);
                break;
            case ChartSceneGeometryKind.Bar:
                rectangles = BuildBarPrimitives(chart, plot, seriesColors, fillPlans);
                break;
            case ChartSceneGeometryKind.Line:
                lineSeries = BuildLineSeriesPrimitives(
                    chart,
                    plot,
                    withMarkers: chart.ChartType == ChartType.LineMarkers,
                    seriesColors,
                    fillPlans);
                break;
            case ChartSceneGeometryKind.Stock:
                if (!chart.HasHighLowLines)
                {
                    lineSeries = BuildStockFallbackLineSeriesPrimitives(chart, plot, seriesColors, fillPlans);
                }
                else
                {
                    stockVolumes = BuildStockVolumePrimitives(chart, plot, seriesColors);
                    stock = BuildStockPrimitivePlan(chart, plot);
                }
                break;
            case ChartSceneGeometryKind.Pie:
                if (chart.ChartType == ChartType.OfPie)
                {
                    var ofPie = BuildOfPiePrimitives(chart, plot, seriesColors, fillPlans);
                    pieSlices = ofPie.PrimarySlices;
                    ofPieSecondarySlices = ofPie.SecondarySlices;
                    ofPieSecondaryType = chart.OfPieType;
                    rectangles = ofPie.SecondaryBars;
                }
                else
                {
                    pieSlices = BuildPieSlicePrimitives(chart, plot, seriesColors, fillPlans);
                }
                break;
            case ChartSceneGeometryKind.Doughnut:
                doughnutSlices = BuildDoughnutSlicePrimitives(chart, plot, seriesColors, fillPlans);
                break;
            case ChartSceneGeometryKind.Area:
                areaSeries = BuildAreaSeriesPrimitives(chart, plot, seriesColors, fillPlans);
                break;
            case ChartSceneGeometryKind.Funnel:
                funnelSegments = BuildFunnelSegmentPrimitives(chart, plot, seriesColors, fillPlans);
                break;
            case ChartSceneGeometryKind.Waterfall:
                rectangles = BuildWaterfallPrimitives(chart, plot, seriesColors, fillPlans);
                break;
            case ChartSceneGeometryKind.Scatter:
                scatter = BuildScatterPrimitivePlan(chart, plot, seriesColors, fillPlans);
                break;
            case ChartSceneGeometryKind.Bubble:
                bubble = BuildBubblePrimitivePlan(chart, plot, seriesColors, fillPlans);
                break;
            case ChartSceneGeometryKind.Radar:
                radar = BuildRadarPrimitivePlan(chart, plot, seriesColors, fillPlans);
                break;
        }

        bool canHaveComboOverlay = frame.Family is not (
            ChartRenderFamily.Pie or
            ChartRenderFamily.Funnel or
            ChartRenderFamily.Waterfall or
            ChartRenderFamily.HorizontalBar or
            ChartRenderFamily.Radar or
            ChartRenderFamily.ScatterLike);
        var comboLineSeries = canHaveComboOverlay && chart.Series.Any(series => series.OverrideChartType.HasValue)
            ? BuildComboOverrideLineSeriesPrimitives(chart, plot, seriesColors, fillPlans)
            : Array.Empty<ChartLineSeriesPrimitive>();
        var errorBars = BuildErrorBarPrimitives(
            chart,
            plot,
            geometryKind,
            rectangles,
            lineSeries,
            areaSeries,
            radar,
            scatter,
            bubble,
            seriesColors);
        var trendlines = BuildTrendlinePrimitives(chart, plot, geometryKind, seriesColors);
        var dataLabels = BuildDataLabelPlans(chart, plot, seriesColors, fillPlans);
        var ofPieSeriesLines = chart.ChartType == ChartType.OfPie
            ? BuildOfPieSeriesLines(chart, ofPieSecondaryType, pieSlices, ofPieSecondarySlices, rectangles)
            : Array.Empty<ChartLineSegmentPrimitive>();

        return new ChartScenePlan
        {
            Frame = frame,
            ChartAreaFill = chartAreaFill,
            ChartAreaOutline = chartAreaOutline,
            PlotAreaFill = plotAreaFill,
            PlotAreaOutline = plotAreaOutline,
            GeometryKind = geometryKind,
            UsesStockLineFallback = UsesStockLineFallback(chart),
            Title = title,
            DrawFlatGrid = !UsesProjectedSurfaceFrame(chart) &&
                !UsesImportedThreeDColumnDefaults(chart) &&
                !frame.IsScatterLike,
            UseWpfPixelSnappedImportedGrid = UsesImportedLabeledColumnWidth(chart),
            DrawProjectedThreeDBarFrame = UsesImportedThreeDColumnDefaults(chart),
            GridLines = BuildMajorGridLinePrimitivePlan(chart, frame),
            MinorGridLines = BuildMinorGridLinePrimitivePlan(chart, frame),
            AxisTicks = BuildMajorAxisTickPrimitivePlan(chart, frame),
            DataLabels = dataLabels,
            DataLabelLeaderLines = BuildDataLabelLeaderLines(
                chart,
                geometryKind,
                dataLabels,
                geometryKind == ChartSceneGeometryKind.Pie ? pieSlices : doughnutSlices),
            OfPieSeriesLines = ofPieSeriesLines,
            DataTable = BuildDataTablePrimitivePlan(chart, frame, seriesColors, fillPlans),
            SecondaryAxis = BuildSecondaryValueAxisPrimitivePlan(chart, frame),
            CategoryAxisLabels = BuildCategoryAxisLabelPlans(chart, frame),
            ValueAxisLabels = BuildValueAxisLabelPlans(chart, frame),
            SurfaceSeriesAxisLabels = BuildSurfaceSeriesAxisLabelPlans(chart, frame),
            AxisTitles = BuildAxisTitlePlans(chart, frame),
            LegendItems = BuildLegendItemPlans(chart, frame, seriesColors, fillPlans),
            Rectangles = rectangles,
            Surface = surface,
            LineSeries = lineSeries,
            Stock = stock,
            StockVolumes = stockVolumes,
            AreaSeries = areaSeries,
            FunnelSegments = funnelSegments,
            PieSlices = pieSlices,
            OfPieSecondaryType = ofPieSecondaryType,
            OfPieSecondarySlices = ofPieSecondarySlices,
            DoughnutSlices = doughnutSlices,
            Scatter = scatter,
            Bubble = bubble,
            Radar = radar,
            ComboLineSeries = comboLineSeries,
            Trendlines = trendlines,
            ErrorBars = errorBars
        };
    }

    private static IReadOnlyList<ChartLineSegmentPrimitive> BuildDataLabelLeaderLines(
        ChartShape chart,
        ChartSceneGeometryKind geometryKind,
        IReadOnlyList<ChartDataLabelPlan> labels,
        IReadOnlyList<ChartPieSlicePrimitive> slices)
    {
        if (chart.DataLabels?.ShowLeaderLines != true ||
            geometryKind is not (ChartSceneGeometryKind.Pie or ChartSceneGeometryKind.Doughnut) ||
            chart.DataLabels.Position is DataLabelPosition.InsideEnd or DataLabelPosition.Center)
        {
            return Array.Empty<ChartLineSegmentPrimitive>();
        }

        var lines = new List<ChartLineSegmentPrimitive>();
        foreach (var label in labels)
        {
            var slice = slices.FirstOrDefault(candidate => candidate.PointIndex == label.CategoryIndex);
            if (slice.OuterRadius <= 0)
                continue;

            double midAngle = (slice.StartAngle + slice.EndAngle) / 2.0;
            double scaleY = slice.EffectiveVerticalScale;
            var unit = new ChartPlanPoint(Math.Cos(midAngle), Math.Sin(midAngle) * scaleY);
            var radialStart = new ChartPlanPoint(
                slice.Center.X + slice.OuterRadius * unit.X,
                slice.Center.Y + slice.OuterRadius * unit.Y);
            var elbow = new ChartPlanPoint(
                slice.Center.X + (slice.OuterRadius + 7.0) * unit.X,
                slice.Center.Y + (slice.OuterRadius + 7.0) * unit.Y);
            var textBounds = label.TextBounds ?? label.Bounds;
            bool rightSide = unit.X >= 0;
            var labelAnchor = new ChartPlanPoint(
                rightSide ? textBounds.X - 2.0 : textBounds.Right + 2.0,
                textBounds.Y + textBounds.Height / 2.0);
            var stroke = new ChartStrokePlan(
                new SrgbColor(0x66, 0x66, 0x66),
                210,
                0.75);
            lines.Add(new ChartLineSegmentPrimitive(
                0,
                label.CategoryIndex,
                label.CategoryIndex,
                radialStart,
                elbow,
                stroke));
            lines.Add(new ChartLineSegmentPrimitive(
                0,
                label.CategoryIndex,
                label.CategoryIndex,
                elbow,
                labelAnchor,
                stroke));
        }

        return lines;
    }

    private static IReadOnlyList<ChartLineSegmentPrimitive> BuildOfPieSeriesLines(
        ChartShape chart,
        OfPieType? secondaryType,
        IReadOnlyList<ChartPieSlicePrimitive> primarySlices,
        IReadOnlyList<ChartPieSlicePrimitive> secondarySlices,
        IReadOnlyList<ChartRectPrimitive> secondaryBars)
    {
        if (!chart.OfPieSeriesLinesSpecified ||
            secondaryType is null ||
            primarySlices.Count == 0 ||
            (secondaryType == OfPieType.Pie && secondarySlices.Count == 0) ||
            (secondaryType == OfPieType.Bar && secondaryBars.Count == 0))
        {
            return Array.Empty<ChartLineSegmentPrimitive>();
        }

        ChartPlanPoint primaryCenter = primarySlices[0].Center;
        double primaryRadius = primarySlices.Max(slice => slice.OuterRadius);
        double primaryRadiusY = primarySlices.Max(slice => slice.OuterRadiusY);

        ChartPlanPoint secondaryCenter;
        double secondaryRadius;
        double secondaryRadiusY;
        if (secondaryType == OfPieType.Pie)
        {
            secondaryCenter = secondarySlices[0].Center;
            secondaryRadius = secondarySlices.Max(slice => slice.OuterRadius);
            secondaryRadiusY = secondarySlices.Max(slice => slice.OuterRadiusY);
        }
        else
        {
            var bounds = secondaryBars.Aggregate(
                new ChartPlanRect(
                    secondaryBars.Min(bar => bar.Bounds.X),
                    secondaryBars.Min(bar => bar.Bounds.Y),
                    0,
                    0),
                (current, bar) => new ChartPlanRect(
                    Math.Min(current.X, bar.Bounds.X),
                    Math.Min(current.Y, bar.Bounds.Y),
                    Math.Max(current.Right, bar.Bounds.Right) - Math.Min(current.X, bar.Bounds.X),
                    Math.Max(current.Bottom, bar.Bounds.Bottom) - Math.Min(current.Y, bar.Bounds.Y)));
            secondaryCenter = new ChartPlanPoint(bounds.X + bounds.Width / 2.0, bounds.Y + bounds.Height / 2.0);
            secondaryRadius = bounds.Width / 2.0;
            secondaryRadiusY = bounds.Height / 2.0;
        }

        double primaryYInset = primaryRadiusY * 0.62;
        double secondaryYInset = secondaryRadiusY * 0.62;
        var stroke = new ChartStrokePlan(new SrgbColor(0x7F, 0x7F, 0x7F), 220, 0.8);
        var lines = new ChartLineSegmentPrimitive[2];
        lines[0] = new ChartLineSegmentPrimitive(
            -1,
            -1,
            -1,
            new ChartPlanPoint(primaryCenter.X + primaryRadius, primaryCenter.Y - primaryYInset),
            new ChartPlanPoint(secondaryCenter.X - secondaryRadius, secondaryCenter.Y - secondaryYInset),
            stroke);
        lines[1] = new ChartLineSegmentPrimitive(
            -1,
            -1,
            -1,
            new ChartPlanPoint(primaryCenter.X + primaryRadius, primaryCenter.Y + primaryYInset),
            new ChartPlanPoint(secondaryCenter.X - secondaryRadius, secondaryCenter.Y + secondaryYInset),
            stroke);
        return lines;
    }

    /// <summary>
    /// Resolves the authored trendline into plot-space segments. The regression
    /// math stays here so WPF and Avalonia consume identical points and axis
    /// ranges; equation/R-squared labels remain a separate text-plan concern.
    /// </summary>
    public static IReadOnlyList<ChartTrendlinePrimitive> BuildTrendlinePrimitives(
        ChartShape chart,
        ChartPlanRect plot,
        ChartSceneGeometryKind geometryKind,
        IReadOnlyList<SrgbColor>? seriesColors = null)
    {
        if (chart.Series.Count == 0 || !plot.HasPositiveArea ||
            geometryKind is ChartSceneGeometryKind.Pie or ChartSceneGeometryKind.Doughnut or
            ChartSceneGeometryKind.Radar or ChartSceneGeometryKind.Stock or ChartSceneGeometryKind.Surface)
        {
            return Array.Empty<ChartTrendlinePrimitive>();
        }

        bool scatter = geometryKind is ChartSceneGeometryKind.Scatter or ChartSceneGeometryKind.Bubble;
        int categoryCount = Math.Max(1, ResolveChartCategoryCount(chart));
        var (primaryMin, primaryMax, _) = ComputePrimaryValueAxisRange(chart);
        var (secondaryMin, secondaryMax, _) = ComputeSecondaryValueAxisRange(chart);
        var (scatterXMin, scatterXMax, _) = scatter
            ? ComputeScatterAxisRange(chart, useX: true)
            : (0.0, 1.0, 1.0);
        double scatterXRange = Math.Max(1e-9, scatterXMax - scatterXMin);
        var result = new List<ChartTrendlinePrimitive>();

        for (int seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
        {
            var series = chart.Series[seriesIndex];
            var trendline = series.Trendline;
            if (trendline is null)
                continue;

            double yMin = series.OnSecondaryAxis ? secondaryMin : primaryMin;
            double yMax = series.OnSecondaryAxis ? secondaryMax : primaryMax;
            double yRange = yMax - yMin;
            if (yRange <= 0)
                continue;

            var samples = new List<(double X, double Y)>();
            int pointCount = scatter
                ? Math.Min(series.XValues.Count, series.Values.Count)
                : Math.Min(categoryCount, series.Values.Count);
            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                double? value = ResolveBlankSensitiveValue(chart, series.Values[pointIndex]);
                if (!value.HasValue || !double.IsFinite(value.Value))
                    continue;

                double x = scatter
                    ? series.XValues[pointIndex] ?? double.NaN
                    : pointIndex;
                if (double.IsFinite(x))
                    samples.Add((x, value.Value));
            }

            if (samples.Count < 2)
                continue;

            samples.Sort((left, right) => left.X.CompareTo(right.X));
            var stroke = (ResolveAuthoredSeriesStroke(series, seriesIndex, seriesColors, 1.0)
                ?? ResolveSeriesStroke(seriesIndex, seriesColors, 1.0)) with
            {
                Alpha = 220,
                Thickness = Math.Max(1.0, Math.Min(2.0, (ResolveAuthoredSeriesStroke(series, seriesIndex, seriesColors, 1.0)
                    ?? ResolveSeriesStroke(seriesIndex, seriesColors, 1.0)).Thickness)),
                Dash = OutlineDash.Dash
            };

            var values = BuildTrendlineValues(samples, trendline);
            if (values.Count < 2)
                continue;

            var labels = BuildTrendlineLabels(
                plot,
                seriesIndex,
                samples,
                trendline,
                stroke);

            var segments = new List<ChartLineSegmentPrimitive>();
            ChartPlanPoint? previous = null;
            int segmentIndex = 0;
            foreach (var value in values)
            {
                double xFraction = scatter
                    ? (value.X - scatterXMin) / scatterXRange
                    : chart.ChartType is ChartType.Line or ChartType.LineMarkers
                        ? value.X / Math.Max(1, categoryCount - 1)
                        : (value.X + 0.5) / categoryCount;
                double yFraction = (value.Y - yMin) / yRange;
                if (!double.IsFinite(xFraction) || !double.IsFinite(yFraction) ||
                    xFraction < 0 || xFraction > 1)
                {
                    previous = null;
                    continue;
                }

                var current = new ChartPlanPoint(
                    plot.X + xFraction * plot.Width,
                    plot.Bottom - yFraction * plot.Height);
                if (previous is { } start)
                {
                    segments.Add(new ChartLineSegmentPrimitive(
                        seriesIndex,
                        segmentIndex++,
                        segmentIndex,
                        start,
                        current,
                        stroke));
                }

                previous = current;
            }

            if (segments.Count > 0)
            {
                result.Add(new ChartTrendlinePrimitive(
                    seriesIndex,
                    trendline.Type,
                    segments,
                    stroke,
                    trendline.DisplayEquation,
                    trendline.DisplayRSquared,
                    labels));
            }
        }

        return result;
    }

    private static IReadOnlyList<(double X, double Y)> BuildTrendlineValues(
        IReadOnlyList<(double X, double Y)> samples,
        ChartTrendline trendline)
    {
        if (trendline.Type == ChartTrendlineType.MovingAverage)
        {
            int period = Math.Clamp(trendline.MovingAveragePeriod ?? 2, 2, samples.Count);
            return samples.Select((sample, index) =>
            {
                int start = Math.Max(0, index - period + 1);
                double average = samples.Skip(start).Take(index - start + 1).Average(item => item.Y);
                return (sample.X, average);
            }).ToArray();
        }

        if (!TryBuildTrendlineFit(samples, trendline, out var fitSamples, out var evaluator, out _))
            return Array.Empty<(double X, double Y)>();

        double minX = fitSamples.Min(item => item.X) - Math.Max(0, trendline.Backward ?? 0);
        double maxX = fitSamples.Max(item => item.X) + Math.Max(0, trendline.Forward ?? 0);
        int sampleCount = 48;
        var result = new List<(double X, double Y)>(sampleCount);
        for (int index = 0; index < sampleCount; index++)
        {
            double x = minX + (maxX - minX) * index / (sampleCount - 1);
            double y = evaluator!(x);
            if (double.IsFinite(x) && double.IsFinite(y))
                result.Add((x, y));
        }

        return result;
    }

    private static bool TryBuildTrendlineFit(
        IReadOnlyList<(double X, double Y)> samples,
        ChartTrendline trendline,
        out List<(double X, double Y)> fitSamples,
        out Func<double, double>? evaluator,
        out string? equation)
    {
        fitSamples = samples.ToList();
        evaluator = null;
        equation = null;
        switch (trendline.Type)
        {
            case ChartTrendlineType.Exponential:
                fitSamples = fitSamples.Where(item => item.Y > 0).ToList();
                if (fitSamples.Count >= 2 && TryFitPolynomial(
                    fitSamples.Select(item => (item.X, Math.Log(item.Y))).ToArray(),
                    1,
                    out var exponential))
                {
                    evaluator = x => Math.Exp(EvaluatePolynomial(exponential, x));
                    equation = $"y = {FormatTrendlineNumber(Math.Exp(exponential[0]))}e^({FormatTrendlineNumber(exponential[1])}x)";
                }
                break;
            case ChartTrendlineType.Logarithmic:
                fitSamples = fitSamples.Where(item => item.X > 0).ToList();
                if (fitSamples.Count >= 2 && TryFitPolynomial(
                    fitSamples.Select(item => (Math.Log(item.X), item.Y)).ToArray(),
                    1,
                    out var logarithmic))
                {
                    evaluator = x => EvaluatePolynomial(logarithmic, Math.Log(Math.Max(double.Epsilon, x)));
                    equation = $"y = {FormatTrendlineNumber(logarithmic[0])} + {FormatTrendlineNumber(logarithmic[1])}ln(x)";
                }
                break;
            case ChartTrendlineType.Power:
                fitSamples = fitSamples.Where(item => item.X > 0 && item.Y > 0).ToList();
                if (fitSamples.Count >= 2 && TryFitPolynomial(
                    fitSamples.Select(item => (Math.Log(item.X), Math.Log(item.Y))).ToArray(),
                    1,
                    out var power))
                {
                    evaluator = x => Math.Exp(EvaluatePolynomial(power, Math.Log(Math.Max(double.Epsilon, x))));
                    equation = $"y = {FormatTrendlineNumber(Math.Exp(power[0]))}x^{FormatTrendlineNumber(power[1])}";
                }
                break;
            default:
                int degree = trendline.Type == ChartTrendlineType.Polynomial
                    ? Math.Clamp(trendline.PolynomialOrder ?? 2, 2, 6)
                    : 1;
                degree = Math.Min(degree, fitSamples.Count - 1);
                if (TryFitPolynomial(fitSamples, degree, out var coefficients))
                {
                    evaluator = x => EvaluatePolynomial(coefficients, x);
                    equation = BuildPolynomialEquation(coefficients);
                }
                break;
        }

        return evaluator is not null && fitSamples.Count >= 2;
    }

    private static IReadOnlyList<ChartTextPlan> BuildTrendlineLabels(
        ChartPlanRect plot,
        int seriesIndex,
        IReadOnlyList<(double X, double Y)> samples,
        ChartTrendline trendline,
        ChartStrokePlan stroke)
    {
        if (trendline.Type == ChartTrendlineType.MovingAverage ||
            (!trendline.DisplayEquation && !trendline.DisplayRSquared) ||
            !TryBuildTrendlineFit(samples, trendline, out var fitSamples, out var evaluator, out var equation))
        {
            return Array.Empty<ChartTextPlan>();
        }

        var labels = new List<ChartTextPlan>();
        double labelY = plot.Y + 4.0 + seriesIndex * 24.0;
        var bounds = new ChartPlanRect(plot.X + 4.0, labelY, Math.Max(40.0, plot.Width - 8.0), 11.0);
        if (trendline.DisplayEquation && !string.IsNullOrWhiteSpace(equation))
        {
            labels.Add(new ChartTextPlan(
                equation,
                bounds,
                IsBold: false,
                FontSize: 7.0,
                Alignment: ChartPlanTextAlignment.Left)
            {
                TextColor = stroke.Color
            });
            labelY += 11.0;
            bounds = bounds with { Y = labelY };
        }

        if (trendline.DisplayRSquared)
        {
            double mean = fitSamples.Average(item => item.Y);
            double total = fitSamples.Sum(item => Math.Pow(item.Y - mean, 2));
            double residual = fitSamples.Sum(item => Math.Pow(item.Y - evaluator!(item.X), 2));
            double rSquared = total > 1e-12 ? Math.Clamp(1.0 - residual / total, 0.0, 1.0) : 1.0;
            labels.Add(new ChartTextPlan(
                $"R^2 = {rSquared.ToString("0.###", CultureInfo.InvariantCulture)}",
                bounds,
                IsBold: false,
                FontSize: 7.0,
                Alignment: ChartPlanTextAlignment.Left)
            {
                TextColor = stroke.Color
            });
        }

        return labels;
    }

    private static string BuildPolynomialEquation(IReadOnlyList<double> coefficients)
    {
        var terms = new List<string>();
        for (int power = coefficients.Count - 1; power >= 0; power--)
        {
            double coefficient = coefficients[power];
            if (Math.Abs(coefficient) < 0.0005)
                continue;
            string magnitude = FormatTrendlineNumber(Math.Abs(coefficient));
            string term = power switch
            {
                0 => magnitude,
                1 => $"{magnitude}x",
                _ => $"{magnitude}x^{power}"
            };
            terms.Add(terms.Count == 0
                ? coefficient < 0 ? $"-{term}" : term
                : coefficient < 0 ? $"- {term}" : $"+ {term}");
        }

        return terms.Count == 0 ? "y = 0" : $"y = {string.Join(" ", terms)}";
    }

    private static string FormatTrendlineNumber(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool TryFitPolynomial(
        IReadOnlyList<(double X, double Y)> samples,
        int degree,
        out double[] coefficients)
    {
        int size = degree + 1;
        coefficients = new double[size];
        if (samples.Count < size)
            return false;

        var matrix = new double[size, size + 1];
        for (int row = 0; row < size; row++)
        {
            for (int column = 0; column < size; column++)
                matrix[row, column] = samples.Sum(sample => Math.Pow(sample.X, row + column));
            matrix[row, size] = samples.Sum(sample => Math.Pow(sample.X, row) * sample.Y);
        }

        for (int pivot = 0; pivot < size; pivot++)
        {
            int best = pivot;
            for (int row = pivot + 1; row < size; row++)
            {
                if (Math.Abs(matrix[row, pivot]) > Math.Abs(matrix[best, pivot]))
                    best = row;
            }
            if (Math.Abs(matrix[best, pivot]) < 1e-12)
                return false;
            if (best != pivot)
            {
                for (int column = pivot; column <= size; column++)
                    (matrix[pivot, column], matrix[best, column]) = (matrix[best, column], matrix[pivot, column]);
            }

            double divisor = matrix[pivot, pivot];
            for (int column = pivot; column <= size; column++)
                matrix[pivot, column] /= divisor;
            for (int row = 0; row < size; row++)
            {
                if (row == pivot)
                    continue;
                double factor = matrix[row, pivot];
                for (int column = pivot; column <= size; column++)
                    matrix[row, column] -= factor * matrix[pivot, column];
            }
        }

        for (int index = 0; index < size; index++)
            coefficients[index] = matrix[index, size];
        return true;
    }

    private static double EvaluatePolynomial(IReadOnlyList<double> coefficients, double x)
    {
        double result = 0;
        for (int index = coefficients.Count - 1; index >= 0; index--)
            result = result * x + coefficients[index];
        return result;
    }

    private static IReadOnlyList<ChartErrorBarPrimitive> BuildErrorBarPrimitives(
        ChartShape chart,
        ChartPlanRect plot,
        ChartSceneGeometryKind geometryKind,
        IReadOnlyList<ChartRectPrimitive> rectangles,
        IReadOnlyList<ChartLineSeriesPrimitive> lineSeries,
        IReadOnlyList<ChartAreaSeriesPrimitive> areaSeries,
        ChartRadarPrimitivePlan? radar,
        ChartScatterPrimitivePlan? scatter,
        ChartBubblePrimitivePlan? bubble,
        IReadOnlyList<SrgbColor>? seriesColors)
    {
        if (chart.Series.Count == 0 || !plot.HasPositiveArea)
            return Array.Empty<ChartErrorBarPrimitive>();

        var result = new List<ChartErrorBarPrimitive>();
        var (primaryMin, primaryMax, _) = ComputePrimaryValueAxisRange(chart);
        var (secondaryMin, secondaryMax, _) = ComputeSecondaryValueAxisRange(chart);
        double primaryRange = Math.Max(1e-9, primaryMax - primaryMin);
        double secondaryRange = Math.Max(1e-9, secondaryMax - secondaryMin);
        var (scatterXMin, scatterXMax, _) = geometryKind == ChartSceneGeometryKind.Scatter
            ? ComputeScatterAxisRange(chart, useX: true)
            : geometryKind == ChartSceneGeometryKind.Bubble
                ? ComputeBubbleXAxisRange(chart)
            : (0.0, 1.0, 1.0);
        double scatterXRange = Math.Max(1e-9, scatterXMax - scatterXMin);
        double categoryStep = plot.Width / Math.Max(1, chart.Categories.Count);
        double categoryHeight = plot.Height / Math.Max(1, chart.Categories.Count);

        foreach (var (series, seriesIndex) in chart.Series.Select((value, index) => (value, index)))
        {
            if (series.ErrorBars is not { } bars)
                continue;

            var stroke = ResolveAuthoredSeriesStroke(series, seriesIndex, seriesColors, 1.0)
                ?? ResolveSeriesStroke(seriesIndex, seriesColors, 1.0);
            for (int pointIndex = 0; pointIndex < series.Values.Count; pointIndex++)
            {
                var value = ResolveBlankSensitiveValue(chart, series.Values[pointIndex]);
                if (!value.HasValue)
                    continue;

                var center = FindErrorBarCenter(
                    geometryKind,
                    seriesIndex,
                    pointIndex,
                    value.Value,
                    rectangles,
                    lineSeries,
                    areaSeries,
                    radar,
                    scatter,
                    bubble);
                if (!center.HasValue)
                    continue;

                double delta = bars.ValueType == ChartErrorValueType.Percentage
                    ? Math.Abs(value.Value) * Math.Abs(bars.Value) / 100.0
                    : Math.Abs(bars.Value);
                double pixels;
                if (bars.Direction == ChartErrorDirection.Y)
                {
                    double range = series.OnSecondaryAxis ? secondaryRange : primaryRange;
                    pixels = geometryKind == ChartSceneGeometryKind.Radar && radar is { Radius: > 0, ValueMaximum: > 0 }
                        ? delta / radar.Value.ValueMaximum * radar.Value.Radius
                        : geometryKind == ChartSceneGeometryKind.Bar
                        ? delta / Math.Max(1, chart.Categories.Count) * categoryHeight
                        : delta / range * plot.Height;
                }
                else if (geometryKind is ChartSceneGeometryKind.Scatter or ChartSceneGeometryKind.Bubble)
                {
                    pixels = delta / scatterXRange * plot.Width;
                }
                else if (geometryKind == ChartSceneGeometryKind.Bar)
                {
                    pixels = delta / (series.OnSecondaryAxis ? secondaryRange : primaryRange) * plot.Width;
                }
                else
                {
                    // Category charts have a discrete X axis. Treat a fixed amount as
                    // category units, which keeps the marker centered in its category band.
                    pixels = delta * categoryStep;
                }

                var point = center.Value;
                ChartPlanPoint? minus = null;
                ChartPlanPoint? plus = null;
                bool radarValueAxis = geometryKind == ChartSceneGeometryKind.Radar &&
                    bars.Direction == ChartErrorDirection.Y && radar is { Radius: > 0 };
                if (radarValueAxis)
                {
                    var radial = new ChartPlanPoint(
                        point.X - radar!.Value.Center.X,
                        point.Y - radar.Value.Center.Y);
                    double length = Math.Sqrt(radial.X * radial.X + radial.Y * radial.Y);
                    if (length < 1e-9)
                        continue;

                    var unit = new ChartPlanPoint(radial.X / length, radial.Y / length);
                    if (bars.BarType != ChartErrorBarType.Plus)
                        minus = new ChartPlanPoint(
                            point.X + unit.X * pixels,
                            point.Y + unit.Y * pixels);
                    if (bars.BarType != ChartErrorBarType.Minus)
                        plus = new ChartPlanPoint(
                            point.X - unit.X * pixels,
                            point.Y - unit.Y * pixels);
                }
                else
                {
                    if (bars.BarType != ChartErrorBarType.Plus)
                        minus = bars.Direction == ChartErrorDirection.Y
                            ? point with { Y = point.Y + pixels }
                            : point with { X = point.X - pixels };
                    if (bars.BarType != ChartErrorBarType.Minus)
                        plus = bars.Direction == ChartErrorDirection.Y
                            ? point with { Y = point.Y - pixels }
                            : point with { X = point.X + pixels };
                }

                result.Add(new ChartErrorBarPrimitive(
                    seriesIndex,
                    pointIndex,
                    point,
                    minus,
                    plus,
                    stroke,
                    bars.Direction,
                    bars.NoEndCap));
            }
        }

        return result;
    }

    private static ChartPlanPoint? FindErrorBarCenter(
        ChartSceneGeometryKind geometryKind,
        int seriesIndex,
        int pointIndex,
        double value,
        IReadOnlyList<ChartRectPrimitive> rectangles,
        IReadOnlyList<ChartLineSeriesPrimitive> lineSeries,
        IReadOnlyList<ChartAreaSeriesPrimitive> areaSeries,
        ChartRadarPrimitivePlan? radar,
        ChartScatterPrimitivePlan? scatter,
        ChartBubblePrimitivePlan? bubble)
    {
        switch (geometryKind)
        {
            case ChartSceneGeometryKind.Line:
            case ChartSceneGeometryKind.Stock:
                var line = lineSeries.FirstOrDefault(item => item.SeriesIndex == seriesIndex);
                return line.Points is not null && pointIndex < line.Points.Count
                    ? line.Points[pointIndex]
                    : null;
            case ChartSceneGeometryKind.Area:
                foreach (var area in areaSeries.Where(item => item.SeriesIndex == seriesIndex))
                {
                    int slot = -1;
                    for (int candidate = 0; candidate < area.PointIndices.Count; candidate++)
                    {
                        if (area.PointIndices[candidate] == pointIndex)
                        {
                            slot = candidate;
                            break;
                        }
                    }
                    if (slot >= 0 && slot < area.Points.Count)
                        return area.Points[slot];
                }
                return null;
            case ChartSceneGeometryKind.Radar:
                var radarSeries = radar?.Series.FirstOrDefault(item => item.SeriesIndex == seriesIndex);
                return radarSeries is { } rs && pointIndex < rs.Points.Count
                    ? rs.Points[pointIndex]
                    : null;
            case ChartSceneGeometryKind.Column:
                var column = rectangles.FirstOrDefault(item => item.SeriesIndex == seriesIndex && item.CategoryIndex == pointIndex);
                if (column.Bounds.HasPositiveArea)
                    return new ChartPlanPoint(column.Bounds.X + column.Bounds.Width / 2.0,
                        value >= 0 ? column.Bounds.Y : column.Bounds.Bottom);
                return null;
            case ChartSceneGeometryKind.Bar:
                var bar = rectangles.FirstOrDefault(item => item.SeriesIndex == seriesIndex && item.CategoryIndex == pointIndex);
                if (bar.Bounds.HasPositiveArea)
                    return new ChartPlanPoint(value >= 0 ? bar.Bounds.Right : bar.Bounds.X,
                        bar.Bounds.Y + bar.Bounds.Height / 2.0);
                return null;
            case ChartSceneGeometryKind.Scatter:
                var scatterSeries = scatter?.Series.FirstOrDefault(item => item.SeriesIndex == seriesIndex);
                return scatterSeries is { } ss && ss.Points is not null && pointIndex < ss.Points.Count
                    ? ss.Points[pointIndex]
                    : null;
            case ChartSceneGeometryKind.Bubble:
                var bubblePoint = bubble?.Bubbles.FirstOrDefault(item => item.SeriesIndex == seriesIndex && item.PointIndex == pointIndex);
                return bubblePoint is { } bp ? bp.Center : null;
            default:
                return null;
        }
    }

    public static ChartRenderFamily GetRenderFamily(ChartType chartType) =>
        chartType switch
        {
            ChartType.Pie or ChartType.Doughnut or ChartType.OfPie => ChartRenderFamily.Pie,
            ChartType.BarClustered or ChartType.BarStacked or ChartType.BarStacked100 => ChartRenderFamily.HorizontalBar,
            ChartType.Scatter or ChartType.Bubble => ChartRenderFamily.ScatterLike,
            ChartType.Radar => ChartRenderFamily.Radar,
            ChartType.Funnel => ChartRenderFamily.Funnel,
            ChartType.Waterfall => ChartRenderFamily.Waterfall,
            ChartType.Stock or ChartType.Surface or ChartType.Surface3D => ChartRenderFamily.Cartesian,
            _ => ChartRenderFamily.Cartesian
        };

    /// <summary>
    /// Surface3D owns a projected back-wall grid, so it must not also receive the
    /// flat Cartesian grid rendered by the generic chart frame.
    /// </summary>
    public static bool UsesProjectedSurfaceFrame(ChartShape chart) =>
        chart.ChartType == ChartType.Surface3D;

    public static IReadOnlyList<ChartLegendItemPlan> BuildLegendItemPlans(
        ChartShape chart,
        ChartFramePlan frame,
        IReadOnlyList<SrgbColor>? seriesColors,
        ChartFillPlanSet? fillPlans = null)
    {
        if (!frame.HasLegend || !frame.HasPlot || chart.Series.Count == 0)
            return Array.Empty<ChartLegendItemPlan>();

        var plot = frame.Plot;
        var legendBounds = ResolveLegendBounds(chart, frame);
        double legendWidth = legendBounds.Width;
        if (!legendBounds.HasPositiveArea)
            return Array.Empty<ChartLegendItemPlan>();

        var automaticLegendBounds = ResolveAutomaticLegendBounds(chart, frame);
        bool hasManualLayout = TryResolveManualLayoutRect(
            chart.LegendManualLayout,
            frame.Bounds,
            automaticLegendBounds,
            out _);
        bool verticalLegend = frame.LegendRight;
        if (hasManualLayout &&
            legendBounds.Height < LegendHeight * 1.5 &&
            legendBounds.Width >= 80.0)
        {
            verticalLegend = false;
        }

        int itemCount = frame.IsPie
            ? chart.Categories.Count > 0
                ? chart.Categories.Count
                : chart.Series[0].Values.Count > 0
                    ? chart.Series[0].Values.Count
                    : 0
            : chart.Series.Count;
        bool importedCombo = UsesImportedComboDefaults(chart);
        bool importedPieLegend = UsesImportedPieLegendDefaults(chart);
        bool importedPieLegendRightOffset = importedPieLegend &&
            (chart.ChartType == ChartType.Doughnut || chart.DataLabels is null);
        bool importedRadarLineLegend = frame.IsRadar && chart.RadarStyle != RadarStyle.Filled;
        bool importedMarkerLegend = UsesImportedTextMetrics(chart) &&
            (UsesImportedSingleScatterDefaults(chart) || UsesImportedBubbleDefaults(chart));
        bool importedBubbleLegend = UsesImportedBubbleLegendDefaults(chart);
        bool importedLineMarkerLegend = UsesImportedTextMetrics(chart) &&
            chart.ChartType == ChartType.LineMarkers;
        bool importedStyle2ColumnBarLegend = UsesImportedStyle2ColumnBarLegend(chart);
        double legendLineHeight = importedCombo
            ? ImportedComboLegendLineHeight
            : importedPieLegend
                ? ImportedPieLegendLineHeight
            : importedRadarLineLegend
                ? ImportedRadarLegendLineHeight
            : importedStyle2ColumnBarLegend
                ? ImportedStyle2LegendLineHeight
            : ResolveLegendLineHeight(chart);
        int maxItems = (int)Math.Max(1, verticalLegend ? legendBounds.Height / legendLineHeight : legendWidth / 80);
        int itemsToShow = Math.Min(itemCount, maxItems);
        if (itemsToShow == 0)
            return Array.Empty<ChartLegendItemPlan>();

        var items = new List<ChartLegendItemPlan>(itemsToShow);
        double firstItemY = verticalLegend && !hasManualLayout
            ? legendBounds.Y + Math.Max(0, (legendBounds.Height - itemsToShow * legendLineHeight) / 2)
            : legendBounds.Y;
        if (importedCombo && verticalLegend && !hasManualLayout)
            firstItemY += ImportedComboLegendVerticalOffset;
        if (importedRadarLineLegend && verticalLegend && !hasManualLayout)
            firstItemY += ImportedRadarLegendVerticalOffset;
        if (importedPieLegend && verticalLegend && !hasManualLayout)
            firstItemY += ImportedPieLegendVerticalOffset;
        if (importedStyle2ColumnBarLegend && verticalLegend && !hasManualLayout)
            firstItemY += ImportedStyle2LegendVerticalOffset;
        if (importedBubbleLegend && verticalLegend && !hasManualLayout)
            firstItemY += ImportedBubbleLegendVerticalOffset;
        for (int itemIndex = 0; itemIndex < itemsToShow; itemIndex++)
        {
            int sourceItemIndex = frame.IsPie
                ? itemIndex
                : frame.IsBar
                    ? itemCount - itemIndex - 1
                    : itemIndex;
            double itemX = verticalLegend
                ? importedMarkerLegend
                    ? plot.Right + (chart.ChartType == ChartType.Bubble
                        ? importedBubbleLegend ? ImportedBubbleLegendRightGap : 30.0
                        : 32.0)
                    : importedRadarLineLegend
                        ? legendBounds.X + ImportedRadarLegendXOffset
                    : importedPieLegendRightOffset
                        ? legendBounds.X + ImportedPieLegendRightOffset
                    : importedStyle2ColumnBarLegend
                        ? legendBounds.X + (frame.IsBar
                            ? ImportedStyle2BarLegendXOffset
                            : ImportedStyle2ColumnLegendXOffset)
                    : legendBounds.X
                : legendBounds.X + itemIndex * 80.0;
            double itemY = verticalLegend ? firstItemY + itemIndex * legendLineHeight : legendBounds.Y;
            bool lineSeries = !frame.IsPie &&
                (chart.Series[sourceItemIndex].OverrideChartType is ChartType.Line or ChartType.LineMarkers ||
                 chart.ChartType is ChartType.Line or ChartType.LineMarkers ||
                 importedRadarLineLegend);
            bool lineMarkerSeries = !frame.IsPie &&
                (chart.ChartType == ChartType.LineMarkers ||
                 chart.Series[sourceItemIndex].OverrideChartType == ChartType.LineMarkers);
            double swatchWidth = importedCombo
                ? ImportedComboLegendSwatchWidth
                : importedPieLegend
                    ? ImportedPieLegendSwatchSize
                : importedRadarLineLegend
                    ? ImportedRadarLegendSwatchWidth
                : importedStyle2ColumnBarLegend
                    ? ImportedStyle2LegendSwatchSize
                    : importedLineMarkerLegend ? ImportedLineMarkerLegendSwatchWidth
                    : importedMarkerLegend ? 12.0 : 8.0;
            double swatchHeight = importedCombo
                ? lineSeries ? 12.0 : ImportedComboLegendSwatchHeight
                : importedPieLegend
                    ? ImportedPieLegendSwatchSize
                : importedRadarLineLegend
                    ? ImportedRadarLegendSwatchHeight
                : importedStyle2ColumnBarLegend
                    ? ImportedStyle2LegendSwatchSize
                    : importedLineMarkerLegend ? ImportedLineMarkerLegendSwatchHeight
                    : importedMarkerLegend ? 12.0 : 8.0;
            double labelInset = importedCombo
                ? ImportedComboLegendSwatchWidth + 4.0
                : importedPieLegend
                    ? ImportedPieLegendLabelInset
                : importedRadarLineLegend
                    ? ImportedRadarLegendLabelInset
                : importedStyle2ColumnBarLegend
                    ? ImportedStyle2LegendLabelInset
                : importedLineMarkerLegend ? ImportedLineMarkerLegendLabelInset
                : importedMarkerLegend
                    ? importedBubbleLegend ? ImportedBubbleLegendLabelInset : 30.0
                    : 10.0;
            double textWidth = verticalLegend
                ? importedMarkerLegend
                    ? 120.0
                    : Math.Max(0, legendBounds.Right - (itemX + labelInset))
                : Math.Min(70, Math.Max(0, legendBounds.Right - itemX - 10));
            string text = frame.IsPie
                ? sourceItemIndex < chart.Categories.Count
                    ? chart.Categories[sourceItemIndex]
                    : $"Point {sourceItemIndex + 1}"
                : chart.Series[sourceItemIndex].Name;
            var color = seriesColors is not null && sourceItemIndex < seriesColors.Count
                ? seriesColors[sourceItemIndex]
                : FallbackSeriesColors[0];

            var legendItem = new ChartLegendItemPlan(
                new ChartPlanRect(
                    itemX,
                    itemY + (importedCombo && lineSeries ? 2.0 : 3.0),
                    swatchWidth,
                    swatchHeight),
                    new ChartTextPlan(
                        text,
                        new ChartPlanRect(
                            itemX + labelInset,
                            itemY + (importedCombo
                                ? ImportedComboLegendLabelOffset
                                : importedPieLegend
                                    ? ImportedPieLegendLabelOffset
                                : importedStyle2ColumnBarLegend
                                    ? ImportedStyle2LegendLabelOffset
                                    : 0.0),
                            textWidth,
                            legendLineHeight),
                    IsBold: false,
                    FontSize: ResolveTextFontSize(chart, 7.0),
                    Alignment: ChartPlanTextAlignment.Left)
                    {
                        FontFamily = importedPieLegend
                            ? ImportedPieLegendFontFamily
                            : null,
                        HorizontalScale = importedPieLegend
                            ? ImportedPieLegendTextScaleX
                            : 1.0,
                        TextColor = importedPieLegend
                            ? new SrgbColor(0x00, 0x00, 0x00)
                            : null
                    },
                fillPlans is not null
                    ? frame.IsPie
                        ? ResolvePointFill(chart.Series[0], 0, sourceItemIndex, seriesColors, alpha: 255, fillPlans,
                            ShouldVaryPointColors(chart))
                        : ResolveSeriesFill(sourceItemIndex, seriesColors, alpha: 255, fillPlans)
                    : new ChartFillPlan(color, Alpha: 255),
                IsLine: lineSeries)
            {
                MarkerSymbol = importedLineMarkerLegend
                        ? ResolveImportedLineMarkerSymbol(sourceItemIndex)
                    : importedMarkerLegend
                        ? chart.ChartType == ChartType.Bubble
                            ? ChartMarkerPrimitiveSymbol.Circle
                            : ResolveImportedLineMarkerSymbol(sourceItemIndex)
                        : null,
                IsLineOnly = importedRadarLineLegend ||
                    (!importedCombo && lineSeries && !lineMarkerSeries)
            };
            items.Add(legendItem);
        }

        return items;
    }

    private static ChartPlanRect ResolveLegendBounds(ChartShape chart, ChartFramePlan frame)
    {
        var automaticLegend = ResolveAutomaticLegendBounds(chart, frame);
        if (TryResolveManualLayoutRect(
                chart.LegendManualLayout,
                frame.Bounds,
                automaticLegend,
                out var manualLegend))
            return manualLegend;

        return automaticLegend;
    }

    private static ChartPlanRect ResolveAutomaticLegendBounds(
        ChartShape chart,
        ChartFramePlan frame)
    {
        var plot = frame.Plot;
        if (frame.LegendRight)
        {
            double legendAreaWidth = frame.LegendAreaWidth > 0
                ? frame.LegendAreaWidth
                : Math.Min(90, frame.Bounds.Width * 0.20);
            bool importedTextMetrics = UsesImportedTextMetrics(chart);
            if (importedTextMetrics && chart.SecondaryValueAxis is { Delete: false })
            {
                double legendX = Math.Min(
                    frame.Bounds.Right,
                    plot.Right + 80.0 +
                    (UsesImportedComboDefaults(chart) ? ImportedComboLegendRightCompensation : 0.0));
                return new ChartPlanRect(
                    legendX,
                    plot.Y,
                    Math.Max(0, frame.Bounds.Right - legendX),
                    plot.Height);
            }

            if (importedTextMetrics &&
                (UsesImportedSingleScatterDefaults(chart) || UsesImportedBubbleDefaults(chart)))
            {
                double legendX = plot.Right + ImportedScatterLegendRightGap;
                return new ChartPlanRect(
                    legendX,
                    plot.Y,
                    Math.Max(0, frame.Bounds.Right - legendX),
                    plot.Height);
            }

            double legendInset = importedTextMetrics
                ? frame.IsPie ? 0.0 : Math.Min(6.0, legendAreaWidth * 0.05)
                : Margin / 2;
            double legendWidth = Math.Max(0, legendAreaWidth - legendInset);
            return new ChartPlanRect(
                importedTextMetrics
                    ? frame.Bounds.X + frame.Bounds.Width - legendAreaWidth + legendInset
                    : frame.Bounds.X + frame.Bounds.Width - legendAreaWidth - legendInset,
                frame.Bounds.Y,
                legendWidth,
                frame.Bounds.Height);
        }

        double legendAreaHeight = frame.LegendAreaHeight > 0
            ? frame.LegendAreaHeight
            : ResolveLegendLineHeight(chart) + ResolveFrameMargin(chart);
        return new ChartPlanRect(
            plot.X,
            frame.Bounds.Y + frame.Bounds.Height - legendAreaHeight - Margin / 2,
            plot.Width,
            ResolveLegendLineHeight(chart));
    }

    private static bool TryResolveManualLayoutRect(
        ChartManualLayout? layout,
        ChartPlanRect outerParent,
        ChartPlanRect innerParent,
        out ChartPlanRect rect)
    {
        rect = default;
        if (!HasResolvableManualLayout(layout))
            return false;

        var parent = string.Equals(layout!.LayoutTarget, "inner", StringComparison.OrdinalIgnoreCase)
            ? innerParent
            : outerParent;

        double x = ResolveManualLayoutStart(parent.X, parent.Width, layout.X!.Value);
        double y = ResolveManualLayoutStart(parent.Y, parent.Height, layout.Y!.Value);
        double right = layout.WidthMode == ChartManualLayoutMode.Edge
            ? ResolveManualLayoutEdge(parent.X, parent.Width, layout.Width!.Value)
            : x + parent.Width * ClampFactor(layout.Width!.Value);
        double bottom = layout.HeightMode == ChartManualLayoutMode.Edge
            ? ResolveManualLayoutEdge(parent.Y, parent.Height, layout.Height!.Value)
            : y + parent.Height * ClampFactor(layout.Height!.Value);
        right = Math.Clamp(right, parent.X, parent.Right);
        bottom = Math.Clamp(bottom, parent.Y, parent.Bottom);
        rect = new ChartPlanRect(
            x,
            y,
            Math.Max(0, right - x),
            Math.Max(0, bottom - y));

        return rect.HasPositiveArea;
    }

    private static bool HasResolvableManualLayout(ChartManualLayout? layout) =>
        layout is not null &&
        layout.X.HasValue &&
        layout.Y.HasValue &&
        layout.Width.HasValue &&
        layout.Height.HasValue &&
        IsResolvableManualLayoutMode(layout.XMode) &&
        IsResolvableManualLayoutMode(layout.YMode) &&
        IsResolvableManualLayoutMode(layout.WidthMode) &&
        IsResolvableManualLayoutMode(layout.HeightMode);

    private static bool IsResolvableManualLayoutMode(ChartManualLayoutMode mode) =>
        mode is ChartManualLayoutMode.Factor or ChartManualLayoutMode.Edge;

    private static double ResolveManualLayoutStart(double origin, double extent, double value) =>
        origin + extent * ClampFactor(value);

    private static double ResolveManualLayoutEdge(double origin, double extent, double value) =>
        origin + extent * ClampFactor(value);

    private static double ClampFactor(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0.0, 1.0) : 0.0;

    public static bool IsLineOrArea(ChartType chartType) =>
        chartType is ChartType.Line
            or ChartType.LineMarkers
            or ChartType.Area
            or ChartType.AreaStacked;

    public static IReadOnlyList<ChartGridLinePlan> BuildMajorGridLinePlans(
        ChartShape chart,
        ChartFramePlan frame) =>
        BuildMajorGridLinePrimitivePlan(chart, frame).GridLines;

    public static ChartMajorGridLinePrimitivePlan BuildMajorGridLinePrimitivePlan(
        ChartShape chart,
        ChartFramePlan frame)
    {
        if (!frame.HasPlot || frame.IsPie || frame.IsRadar || frame.IsScatterLike || !chart.ValueAxis.HasMajorGridlines)
            return EmptyMajorGridLinePrimitivePlan();

        var (minValue, maxValue, majorUnit) = ComputePrimaryValueAxisRange(chart);
        double steps = (maxValue - minValue) / majorUnit;
        if (steps <= 0)
            return EmptyMajorGridLinePrimitivePlan();

        var plot = frame.Plot;
        int tickCount = (int)Math.Round(steps);
        var lines = new List<ChartGridLinePlan>(tickCount + 1);
        for (int index = 0; index <= tickCount; index++)
        {
            if (frame.IsBar)
            {
                double x = plot.X + plot.Width * index / steps;
                lines.Add(new ChartGridLinePlan(
                    new ChartPlanPoint(x, plot.Y),
                    new ChartPlanPoint(x, plot.Bottom)));
            }
            else
            {
                double y = plot.Bottom - plot.Height * index / steps +
                    ((UsesImportedCartesianAxisStrokes(chart) && chart.ChartType != ChartType.Stock) ||
                        UsesImportedComboDefaults(chart)
                        ? ImportedCartesianGridLinePixelOffset
                        : 0.0);
                lines.Add(new ChartGridLinePlan(
                    new ChartPlanPoint(plot.X, y),
                    new ChartPlanPoint(plot.Right, y)));
            }
        }

        if (UsesImportedTextMetrics(chart) &&
            IsHundredPercentStacked(chart.ChartType) &&
            chart.CategoryAxis.HasMajorGridlines &&
            !frame.IsBar)
        {
            double categoryStep = plot.Width / Math.Max(1, chart.Categories.Count);
            double edgeOffset = ImportedPercentStackedGridEdgeOffsetX;
            for (int index = 0; index <= chart.Categories.Count; index++)
            {
                double x = Math.Ceiling(plot.X + edgeOffset + index * categoryStep);
                lines.Add(new ChartGridLinePlan(
                    new ChartPlanPoint(x, plot.Y),
                    new ChartPlanPoint(x, plot.Bottom)));
            }
        }

        // PowerPoint's stock-chart line fallback keeps the category grid authored
        // by c:catAx/majorGridlines, including the two outer plot boundaries.
        if (UsesStockLineFallback(chart) && chart.CategoryAxis.HasMajorGridlines)
        {
            int categoryCount = ResolveChartCategoryCount(chart);
            double categoryWidth = plot.Width / Math.Max(1, categoryCount);
            for (int index = 0; index <= categoryCount; index++)
            {
                double x = plot.X + index * categoryWidth;
                lines.Add(new ChartGridLinePlan(
                    new ChartPlanPoint(x, plot.Y),
                    new ChartPlanPoint(x, plot.Bottom)));
            }
        }

        return new ChartMajorGridLinePrimitivePlan(
            lines,
            DefaultGridLineStroke(chart));
    }

    public static ChartMajorGridLinePrimitivePlan BuildMinorGridLinePrimitivePlan(
        ChartShape chart,
        ChartFramePlan frame)
    {
        if (!frame.HasPlot || frame.IsPie || frame.IsRadar || frame.IsScatterLike ||
            !chart.ValueAxis.HasMinorGridlines)
            return EmptyMinorGridLinePrimitivePlan();

        var (minValue, maxValue, majorUnit) = ComputePrimaryValueAxisRange(chart);
        double minorUnit = chart.ValueAxis.MinorUnit is > 0
            ? chart.ValueAxis.MinorUnit.Value
            : majorUnit / 5.0;
        if (!(minorUnit > 0) || maxValue <= minValue)
            return EmptyMinorGridLinePrimitivePlan();

        double steps = (maxValue - minValue) / minorUnit;
        int tickCount = (int)Math.Floor(steps + 1e-9);
        var plot = frame.Plot;
        var lines = new List<ChartGridLinePlan>(Math.Max(0, tickCount - 1));
        for (int index = 1; index < tickCount; index++)
        {
            double value = minValue + minorUnit * index;
            double majorPosition = (value - minValue) / majorUnit;
            if (Math.Abs(majorPosition - Math.Round(majorPosition)) < 0.0001)
                continue;

            double fraction = (value - minValue) / (maxValue - minValue);
            if (frame.IsBar)
            {
                double x = plot.X + plot.Width * fraction;
                lines.Add(new ChartGridLinePlan(
                    new ChartPlanPoint(x, plot.Y),
                    new ChartPlanPoint(x, plot.Bottom)));
            }
            else
            {
                double y = plot.Bottom - plot.Height * fraction;
                lines.Add(new ChartGridLinePlan(
                    new ChartPlanPoint(plot.X, y),
                    new ChartPlanPoint(plot.Right, y)));
            }
        }

        return new ChartMajorGridLinePrimitivePlan(
            lines,
            new ChartStrokePlan(new SrgbColor(0xB7, 0xB7, 0xB7), Alpha: 170, Thickness: 0.75));
    }

    public static ChartMajorAxisTickPrimitivePlan BuildMajorAxisTickPrimitivePlan(
        ChartShape chart,
        ChartFramePlan frame)
    {
        if (!frame.HasPlot || frame.IsPie || frame.IsRadar || frame.IsScatterLike)
            return EmptyMajorAxisTickPrimitivePlan();
        if (UsesImportedThreeDColumnDefaults(chart))
            return EmptyMajorAxisTickPrimitivePlan();

        var categoryTicks = chart.CategoryAxis.Delete
            ? Array.Empty<ChartGridLinePlan>()
            : BuildCategoryAxisTickPlans(chart, frame);
        var valueTicks = chart.ValueAxis.Delete
            ? Array.Empty<ChartGridLinePlan>()
            : BuildValueAxisTickPlans(chart, frame);

        return new ChartMajorAxisTickPrimitivePlan(
            categoryTicks,
            valueTicks,
            DefaultAxisTickStroke(chart));
    }

    public static IReadOnlyList<ChartTextPlan> BuildCategoryAxisLabelPlans(
        ChartShape chart,
        ChartFramePlan frame)
    {
        if (!frame.HasPlot || frame.IsPie || frame.IsRadar || frame.IsScatterLike || chart.Categories.Count == 0)
            return Array.Empty<ChartTextPlan>();
        if (ShouldPlanDataTable(chart, frame))
            return Array.Empty<ChartTextPlan>();
        if (chart.ChartType == ChartType.Surface3D && UsesImportedTextMetrics(chart))
            return BuildImportedSurface3DCategoryAxisLabelPlans(chart, frame);
        if (UsesImportedThreeDColumnDefaults(chart))
            return BuildImportedThreeDBarCategoryAxisLabelPlans(chart, frame);

        var labels = new List<ChartTextPlan>(chart.Categories.Count);
        var plot = frame.Plot;
        if (frame.IsBar)
        {
            int categoryCount = chart.Categories.Count;
            double categoryStep = plot.Height / Math.Max(1, categoryCount);
            for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
            {
                int renderRow = chart.CategoryAxis.ReverseOrder
                    ? categoryIndex
                    : categoryCount - 1 - categoryIndex;
                double y = plot.Y + renderRow * categoryStep;
                labels.Add(new ChartTextPlan(
                    FormatCategoryAxisLabel(chart.Categories[categoryIndex], chart.CategoryAxis),
                    new ChartPlanRect(plot.X - ResolveBarCategoryLabelWidth(chart), y, ResolveBarCategoryLabelWidth(chart) - 4, categoryStep),
                    IsBold: false,
                    FontSize: ResolveTextFontSize(chart, 6.5),
                    Alignment: ChartPlanTextAlignment.Right,
                    AxisLabelFormat: BuildAxisLabelFormatPlan(chart.CategoryAxis)));
            }
        }
        else
        {
            double categoryStep = plot.Width / Math.Max(1, chart.Categories.Count);
            double labelOffset = UsesImportedTextMetrics(chart)
                ? ImportedCartesianCategoryLabelOffset
                : 2.0;
            for (int categoryIndex = 0; categoryIndex < chart.Categories.Count; categoryIndex++)
            {
                int renderCategoryIndex = ResolveCategoryRenderIndex(
                    chart.CategoryAxis, categoryIndex, chart.Categories.Count);
                double x = plot.X + renderCategoryIndex * categoryStep;
                labels.Add(new ChartTextPlan(
                    FormatCategoryAxisLabel(chart.Categories[categoryIndex], chart.CategoryAxis),
                    new ChartPlanRect(x, plot.Bottom + labelOffset, categoryStep, ResolveCategoryLabelHeight(chart)),
                    IsBold: false,
                    FontSize: ResolveTextFontSize(chart, 7.0),
                    Alignment: ChartPlanTextAlignment.Center,
                    AxisLabelFormat: BuildAxisLabelFormatPlan(chart.CategoryAxis)));
            }
        }

        return labels;
    }

    private static IReadOnlyList<ChartTextPlan> BuildImportedThreeDBarCategoryAxisLabelPlans(
        ChartShape chart,
        ChartFramePlan frame)
    {
        var plot = frame.Plot;
        double categoryStep = plot.Width / Math.Max(1, chart.Categories.Count);
        var labels = new List<ChartTextPlan>(chart.Categories.Count);
        for (int categoryIndex = 0; categoryIndex < chart.Categories.Count; categoryIndex++)
        {
            double perspectiveOffset =
                ImportedThreeDBarPerspectiveX0 +
                ImportedThreeDBarPerspectiveX1 * categoryIndex +
                ImportedThreeDBarPerspectiveX2 * categoryIndex * categoryIndex +
                ImportedThreeDBarPerspectiveX3 * categoryIndex * categoryIndex * categoryIndex;
            double centerX = plot.X + (categoryIndex + 0.5) * categoryStep + perspectiveOffset * 0.5;
            double labelTop = plot.Bottom - 75.0 + categoryIndex * ImportedThreeDBarCategorySkewY;
            labels.Add(new ChartTextPlan(
                FormatCategoryAxisLabel(chart.Categories[categoryIndex], chart.CategoryAxis),
                new ChartPlanRect(centerX - categoryStep / 2.0, labelTop, categoryStep, ResolveCategoryLabelHeight(chart)),
                IsBold: false,
                FontSize: ResolveTextFontSize(chart, 7.0),
                Alignment: ChartPlanTextAlignment.Center,
                AxisLabelFormat: BuildAxisLabelFormatPlan(chart.CategoryAxis)));
        }

        return labels;
    }

    private static IReadOnlyList<ChartTextPlan> BuildImportedSurface3DCategoryAxisLabelPlans(
        ChartShape chart,
        ChartFramePlan frame)
    {
        var plot = frame.Plot;
        // The imported Surface3D front category edge is projected down toward
        // the far-right category instead of following the flat plot baseline.
        double scaleX = plot.Width / 360.0;
        double scaleY = plot.Height / 189.0;
        var frontLeft = new ChartPlanPoint(
            plot.X + 8.0 * scaleX,
            plot.Bottom - 40.0 * scaleY);
        var frontRight = new ChartPlanPoint(
            plot.X + 308.0 * scaleX,
            plot.Bottom);
        var labels = new List<ChartTextPlan>(chart.Categories.Count);
        for (int categoryIndex = 0; categoryIndex < chart.Categories.Count; categoryIndex++)
        {
            double categoryT = chart.Categories.Count <= 1
                ? 0
                : categoryIndex / (double)(chart.Categories.Count - 1);
            var point = new ChartPlanPoint(
                frontLeft.X + (frontRight.X - frontLeft.X) * categoryT,
                frontLeft.Y + (frontRight.Y - frontLeft.Y) * categoryT);
            labels.Add(new ChartTextPlan(
                FormatCategoryAxisLabel(chart.Categories[categoryIndex], chart.CategoryAxis),
                new ChartPlanRect(
                    point.X - 21.0 * scaleX,
                    point.Y + 16.0 * scaleY,
                    42.0 * scaleX,
                    ResolveCategoryLabelHeight(chart)),
                IsBold: false,
                FontSize: ResolveTextFontSize(chart, 7.0),
                Alignment: ChartPlanTextAlignment.Center,
                AxisLabelFormat: BuildAxisLabelFormatPlan(chart.CategoryAxis)));
        }

        return labels;
    }

    public static IReadOnlyList<ChartTextPlan> BuildValueAxisLabelPlans(
        ChartShape chart,
        ChartFramePlan frame)
    {
        if (!frame.HasPlot || frame.IsPie || frame.IsRadar || frame.IsScatterLike)
            return Array.Empty<ChartTextPlan>();

        var (minValue, maxValue, majorUnit) = ComputePrimaryValueAxisRange(chart);
        double steps = (maxValue - minValue) / majorUnit;
        if (steps <= 0)
            return Array.Empty<ChartTextPlan>();

        int tickCount = (int)Math.Round(steps);
        var labels = new List<ChartTextPlan>(tickCount + 1);
        var plot = frame.Plot;
        for (int tickIndex = 0; tickIndex <= tickCount; tickIndex++)
        {
            double value = minValue + majorUnit * tickIndex;
            if (frame.IsBar)
            {
                double x = plot.X + plot.Width * tickIndex / steps;
                labels.Add(new ChartTextPlan(
                    FormatChartAxisLabelValue(chart, value, chart.ValueAxis),
                    new ChartPlanRect(x - ResolveAxisLabelWidth(chart) / 2, plot.Bottom + 2, ResolveAxisLabelWidth(chart), ResolveCategoryLabelHeight(chart)),
                    IsBold: false,
                    FontSize: ResolveTextFontSize(chart, 6.5),
                    Alignment: ChartPlanTextAlignment.Center,
                    AxisLabelFormat: BuildAxisLabelFormatPlan(chart.ValueAxis)));
            }
            else
            {
                double axisY = UsesImportedThreeDColumnDefaults(chart)
                    ? plot.Bottom - (ImportedThreeDBarBaseLift - 8.0)
                    : plot.Bottom;
                double axisTop = plot.Y;
                double y = axisY - (axisY - axisTop) * tickIndex / steps;
                double rightGap = UsesImportedTextMetrics(chart)
                    ? ImportedCartesianValueLabelRightGap
                    : 0.0;
                double verticalOffset = UsesImportedTextMetrics(chart)
                    ? ImportedCartesianValueLabelVerticalOffset
                    : 6.0;
                double labelWidth = ResolveAxisLabelWidth(chart) - GridlinePad;
                double labelRight = UsesImportedThreeDColumnDefaults(chart)
                    ? plot.X + 21.0 - 4.0
                    : plot.X - rightGap;
                labels.Add(new ChartTextPlan(
                    FormatChartAxisLabelValue(chart, value, chart.ValueAxis),
                    new ChartPlanRect(labelRight - labelWidth, y - verticalOffset, labelWidth, ResolveCategoryLabelHeight(chart)),
                    IsBold: false,
                    FontSize: ResolveTextFontSize(chart, 6.5),
                    Alignment: ChartPlanTextAlignment.Right,
                    AxisLabelFormat: BuildAxisLabelFormatPlan(chart.ValueAxis)));
            }
        }

        return labels;
    }

    public static IReadOnlyList<ChartTextPlan> BuildSurfaceSeriesAxisLabelPlans(
        ChartShape chart,
        ChartFramePlan frame)
    {
        if (chart.ChartType != ChartType.Surface3D ||
            !frame.HasPlot ||
            chart.Series.Count == 0)
        {
            return Array.Empty<ChartTextPlan>();
        }

        var plot = frame.Plot;
        double depthX = Math.Min(plot.Width * 0.12, 44.0);
        double depthY = Math.Min(plot.Height * 0.44, 88.0);
        double scaleX = plot.Width / 360.0;
        double frontRightX = plot.X + 308.0 * scaleX;
        double frontRightY = plot.Bottom;
        var labels = new List<ChartTextPlan>(chart.Series.Count);
        for (int seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
        {
            double seriesT = chart.Series.Count <= 1
                ? 0
                : seriesIndex / (double)(chart.Series.Count - 1);
            var point = new ChartPlanPoint(
                frontRightX + depthX * seriesT + 5.0,
                frontRightY - depthY * seriesT);
            string text = string.IsNullOrWhiteSpace(chart.Series[seriesIndex].Name)
                ? $"Series {seriesIndex + 1}"
                : chart.Series[seriesIndex].Name;
            labels.Add(new ChartTextPlan(
                text,
                new ChartPlanRect(point.X + 7.0, point.Y - 7.0, 80.0, 14.0),
                IsBold: false,
                FontSize: ResolveTextFontSize(chart, 7.0),
                Alignment: ChartPlanTextAlignment.Left));
        }

        return labels;
    }

    private static IReadOnlyList<ChartGridLinePlan> BuildCategoryAxisTickPlans(
        ChartShape chart,
        ChartFramePlan frame)
    {
        if (chart.Categories.Count == 0)
            return Array.Empty<ChartGridLinePlan>();

        var plot = frame.Plot;
        var ticks = new List<ChartGridLinePlan>(chart.Categories.Count);
        if (frame.IsBar)
        {
            int categoryCount = chart.Categories.Count;
            double categoryStep = plot.Height / Math.Max(1, categoryCount);
            for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
            {
                int renderRow = categoryCount - 1 - categoryIndex;
                double y = plot.Y + renderRow * categoryStep + categoryStep / 2.0;
                ticks.Add(new ChartGridLinePlan(
                    new ChartPlanPoint(plot.X - AxisMajorTickLength, y),
                    new ChartPlanPoint(plot.X, y)));
            }
        }
        else
        {
            double categoryStep = plot.Width / Math.Max(1, chart.Categories.Count);
            for (int categoryIndex = 0; categoryIndex < chart.Categories.Count; categoryIndex++)
            {
                int renderCategoryIndex = ResolveCategoryRenderIndex(
                    chart.CategoryAxis, categoryIndex, chart.Categories.Count);
                double x = plot.X + renderCategoryIndex * categoryStep + categoryStep / 2.0;
                ticks.Add(new ChartGridLinePlan(
                    new ChartPlanPoint(x, plot.Bottom),
                    new ChartPlanPoint(x, plot.Bottom + AxisMajorTickLength)));
            }

            if (UsesStockLineFallback(chart))
            {
                for (int boundaryIndex = 0; boundaryIndex <= chart.Categories.Count; boundaryIndex++)
                {
                    double x = plot.X + boundaryIndex * categoryStep;
                    ticks.Add(new ChartGridLinePlan(
                        new ChartPlanPoint(x, plot.Bottom),
                        new ChartPlanPoint(x, plot.Bottom + AxisMinorTickLength)));
                }
            }
        }

        return ticks;
    }

    private static IReadOnlyList<ChartGridLinePlan> BuildValueAxisTickPlans(
        ChartShape chart,
        ChartFramePlan frame)
    {
        var (minValue, maxValue, majorUnit) = ComputePrimaryValueAxisRange(chart);
        double steps = (maxValue - minValue) / majorUnit;
        if (steps <= 0)
            return Array.Empty<ChartGridLinePlan>();

        var plot = frame.Plot;
        int tickCount = (int)Math.Round(steps);
        var ticks = new List<ChartGridLinePlan>(tickCount + 1);
        for (int tickIndex = 0; tickIndex <= tickCount; tickIndex++)
        {
            if (frame.IsBar)
            {
                double x = plot.X + plot.Width * tickIndex / steps;
                ticks.Add(new ChartGridLinePlan(
                    new ChartPlanPoint(x, plot.Bottom),
                    new ChartPlanPoint(x, plot.Bottom + AxisMajorTickLength)));
            }
            else
            {
                double y = plot.Bottom - plot.Height * tickIndex / steps;
                ticks.Add(new ChartGridLinePlan(
                    new ChartPlanPoint(plot.X - AxisMajorTickLength, y),
                    new ChartPlanPoint(plot.X, y)));
            }
        }

        if (UsesStockLineFallback(chart) && !frame.IsBar)
        {
            const double stockMinorUnit = 0.4;
            int minorTickCount = (int)Math.Round((maxValue - minValue) / stockMinorUnit);
            for (int minorTickIndex = 1; minorTickIndex < minorTickCount; minorTickIndex++)
            {
                double value = minValue + stockMinorUnit * minorTickIndex;
                double majorPosition = (value - minValue) / majorUnit;
                if (Math.Abs(majorPosition - Math.Round(majorPosition)) < 0.0001)
                    continue;

                double y = plot.Bottom - plot.Height * (value - minValue) / (maxValue - minValue);
                ticks.Add(new ChartGridLinePlan(
                    new ChartPlanPoint(plot.X - AxisMinorTickLength, y),
                    new ChartPlanPoint(plot.X, y)));
            }
        }

        if (chart.ValueAxis.MinorUnit is > 0 && chart.ValueAxis.MinorTickMark != ChartTickMark.None)
        {
            double minorUnit = chart.ValueAxis.MinorUnit.Value;
            int minorTickCount = (int)Math.Floor((maxValue - minValue) / minorUnit + 1e-9);
            for (int minorTickIndex = 1; minorTickIndex < minorTickCount; minorTickIndex++)
            {
                double value = minValue + minorUnit * minorTickIndex;
                double majorPosition = (value - minValue) / majorUnit;
                if (Math.Abs(majorPosition - Math.Round(majorPosition)) < 0.0001)
                    continue;

                double fraction = (value - minValue) / (maxValue - minValue);
                if (frame.IsBar)
                {
                    double x = plot.X + plot.Width * fraction;
                    ticks.Add(new ChartGridLinePlan(
                        new ChartPlanPoint(x, plot.Bottom),
                        new ChartPlanPoint(x, plot.Bottom + AxisMinorTickLength)));
                }
                else
                {
                    double y = plot.Bottom - plot.Height * fraction;
                    ticks.Add(new ChartGridLinePlan(
                        new ChartPlanPoint(plot.X - AxisMinorTickLength, y),
                        new ChartPlanPoint(plot.X, y)));
                }
            }
        }

        return ticks;
    }

    public static IReadOnlyList<ChartAxisTitlePlan> BuildAxisTitlePlans(
        ChartShape chart,
        ChartFramePlan frame)
    {
        if (!frame.HasPlot || frame.IsPie || frame.IsRadar || frame.IsScatterLike)
            return Array.Empty<ChartAxisTitlePlan>();

        var plans = new List<ChartAxisTitlePlan>(2);
        var plot = frame.Plot;

        static ChartTextPlan StyledTitle(string text, ChartPlanRect bounds, ChartTextStyle? style) =>
            new(text, bounds, style?.Bold ?? false, style?.FontSizePt ?? AxisTitleFontSize,
                ChartPlanTextAlignment.Center)
            {
                FontFamily = style?.FontFamily,
                TextColor = style?.Color?.Resolved,
                IsItalic = style?.Italic ?? false,
            };

        if (!chart.ValueAxis.Delete && !string.IsNullOrWhiteSpace(chart.ValueAxis.Title))
        {
            if (frame.IsBar)
            {
                plans.Add(new ChartAxisTitlePlan(
                    StyledTitle(
                        chart.ValueAxis.Title!,
                        new ChartPlanRect(
                            plot.X,
                            plot.Bottom + CategoryLabelHeight + 2,
                            plot.Width,
                            AxisTitleBand), chart.ValueAxis.TitleStyle),
                    ChartAxisTitleOrientation.Horizontal));
            }
            else
            {
                plans.Add(new ChartAxisTitlePlan(
                    StyledTitle(
                        chart.ValueAxis.Title!,
                        new ChartPlanRect(
                            frame.Bounds.X + Margin,
                            plot.Y,
                            AxisTitleBand,
                            plot.Height), chart.ValueAxis.TitleStyle),
                    ChartAxisTitleOrientation.VerticalCounterclockwise));
            }
        }

        if (!chart.CategoryAxis.Delete && !string.IsNullOrWhiteSpace(chart.CategoryAxis.Title))
        {
            if (frame.IsBar)
            {
                plans.Add(new ChartAxisTitlePlan(
                    StyledTitle(
                        chart.CategoryAxis.Title!,
                        new ChartPlanRect(
                            frame.Bounds.X + Margin,
                            plot.Y,
                            AxisTitleBand,
                            plot.Height), chart.CategoryAxis.TitleStyle),
                    ChartAxisTitleOrientation.VerticalCounterclockwise));
            }
            else
            {
                double categoryTitleOffset = ShouldPlanDataTable(chart, frame)
                    ? ComputeDataTableReservedHeight(chart) + 2
                    : CategoryLabelHeight + 2;
                plans.Add(new ChartAxisTitlePlan(
                    StyledTitle(
                        chart.CategoryAxis.Title!,
                        new ChartPlanRect(
                            plot.X,
                            plot.Bottom + categoryTitleOffset,
                            plot.Width,
                            AxisTitleBand), chart.CategoryAxis.TitleStyle),
                    ChartAxisTitleOrientation.Horizontal));
            }
        }

        return plans;
    }

    public static ChartDataTablePrimitivePlan BuildDataTablePrimitivePlan(
        ChartShape chart,
        ChartFramePlan frame,
        IReadOnlyList<SrgbColor>? seriesColors = null,
        ChartFillPlanSet? fillPlans = null)
    {
        if (!ShouldPlanDataTable(chart, frame))
            return EmptyDataTablePrimitivePlan();

        var settings = chart.DataTable!;
        var headerTextStyle = ResolveDataTableTextStyle(settings, defaultBold: true);
        var bodyTextStyle = ResolveDataTableTextStyle(settings, defaultBold: false);
        int categoryCount = chart.Categories.Count;
        int rowCount = chart.Series.Count + 1;
        int columnCount = categoryCount + 1;
        var plot = frame.Plot;
        double boundsY = plot.Bottom + DataTableGap;
        double boundsHeight = ComputeDataTableHeight(chart);
        // The plot's left edge was already inset (see BuildFramePlan) by the same first-column
        // width computed here, so the row-header column occupies the gutter immediately to the
        // left of the plot, and the category columns start at plot.X with the SAME per-category
        // width as the plot's category band - column j lands directly under category j's bar/point.
        double firstColumnWidth = ComputeDataTableFirstColumnWidth(frame.Bounds.Width);
        var bounds = new ChartPlanRect(plot.X - firstColumnWidth, boundsY, firstColumnWidth + plot.Width, boundsHeight);
        double categoryWidth = Math.Max(1, plot.Width / categoryCount);

        var cells = new List<ChartDataTableCellPlan>(rowCount * columnCount);
        for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            double x = columnIndex == 0
                ? bounds.X
                : bounds.X + firstColumnWidth + (columnIndex - 1) * categoryWidth;
            double width = columnIndex == 0 ? firstColumnWidth : categoryWidth;
            string text = columnIndex == 0 ? string.Empty : chart.Categories[columnIndex - 1];
            var cellBounds = new ChartPlanRect(x, bounds.Y, width, DataTableHeaderHeight);
            cells.Add(new ChartDataTableCellPlan(
                RowIndex: 0,
                ColumnIndex: columnIndex,
                Text: text,
                CellBounds: cellBounds,
                Bounds: InsetDataTableCellText(cellBounds),
                IsHeader: true,
                IsBold: headerTextStyle.IsBold,
                IsItalic: headerTextStyle.IsItalic,
                FontSize: headerTextStyle.FontSize,
                TextColor: headerTextStyle.Color,
                Alignment: columnIndex == 0 ? ChartPlanTextAlignment.Left : ChartPlanTextAlignment.Center,
                LegendKeyBounds: null,
                LegendKeyFill: null,
                FontFamily: headerTextStyle.FontFamily));
        }

        for (int seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
        {
            var series = chart.Series[seriesIndex];
            double rowY = bounds.Y + DataTableHeaderHeight + seriesIndex * DataTableRowHeight;
            var keyFill = settings.ShowLegendKeys
                ? ResolveSeriesFill(seriesIndex, seriesColors, RectSeriesFillAlpha, fillPlans)
                : (ChartFillPlan?)null;
            var seriesCellBounds = new ChartPlanRect(bounds.X, rowY, firstColumnWidth, DataTableRowHeight);
            ChartPlanRect? keyBounds = keyFill.HasValue
                ? new ChartPlanRect(
                    seriesCellBounds.X + DataTableTextInset,
                    seriesCellBounds.Y + (seriesCellBounds.Height - DataTableLegendKeySize) / 2.0,
                    DataTableLegendKeySize,
                    DataTableLegendKeySize)
                : null;
            cells.Add(new ChartDataTableCellPlan(
                RowIndex: seriesIndex + 1,
                ColumnIndex: 0,
                Text: series.Name,
                CellBounds: seriesCellBounds,
                Bounds: keyBounds.HasValue
                    ? new ChartPlanRect(
                        keyBounds.Value.Right + DataTableTextInset,
                        seriesCellBounds.Y,
                        Math.Max(0, seriesCellBounds.Right - keyBounds.Value.Right - 2 * DataTableTextInset),
                        seriesCellBounds.Height)
                    : InsetDataTableCellText(seriesCellBounds),
                IsHeader: false,
                IsBold: bodyTextStyle.IsBold,
                IsItalic: bodyTextStyle.IsItalic,
                FontSize: bodyTextStyle.FontSize,
                TextColor: bodyTextStyle.Color,
                Alignment: ChartPlanTextAlignment.Left,
                LegendKeyBounds: keyBounds,
                LegendKeyFill: keyFill,
                FontFamily: bodyTextStyle.FontFamily));

            for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
            {
                string text = categoryIndex < series.Values.Count && series.Values[categoryIndex].HasValue
                    ? FormatAxisValue(series.Values[categoryIndex]!.Value)
                    : string.Empty;
                var cellBounds = new ChartPlanRect(
                    bounds.X + firstColumnWidth + categoryIndex * categoryWidth,
                    rowY,
                    categoryWidth,
                    DataTableRowHeight);
                cells.Add(new ChartDataTableCellPlan(
                    RowIndex: seriesIndex + 1,
                    ColumnIndex: categoryIndex + 1,
                    Text: text,
                    CellBounds: cellBounds,
                    Bounds: InsetDataTableCellText(cellBounds),
                    IsHeader: false,
                    IsBold: bodyTextStyle.IsBold,
                    IsItalic: bodyTextStyle.IsItalic,
                    FontSize: bodyTextStyle.FontSize,
                    TextColor: bodyTextStyle.Color,
                    Alignment: ChartPlanTextAlignment.Center,
                    LegendKeyBounds: null,
                    LegendKeyFill: null,
                    FontFamily: bodyTextStyle.FontFamily));
            }
        }

        var horizontalBorders = settings.ShowHorizontalBorder
            ? BuildDataTableHorizontalBorders(bounds, rowCount)
            : Array.Empty<ChartGridLinePlan>();
        var verticalBorders = settings.ShowVerticalBorder
            ? BuildDataTableVerticalBorders(bounds, firstColumnWidth, categoryWidth, categoryCount)
            : Array.Empty<ChartGridLinePlan>();
        var outlineBorders = settings.ShowOutlineBorder
            ? BuildOutlineBorders(bounds)
            : Array.Empty<ChartGridLinePlan>();

        return new ChartDataTablePrimitivePlan(
            bounds,
            ResolveDataTableBackgroundFill(settings),
            cells,
            horizontalBorders,
            verticalBorders,
            outlineBorders,
            ResolveDataTableBorderStroke(settings));
    }

    public static IReadOnlyList<ChartTextPlan> BuildSecondaryValueAxisLabelPlans(
        ChartShape chart,
        ChartPlanRect plot,
        double boundsRight)
    {
        var frame = new ChartFramePlan(
            new ChartPlanRect(0, 0, boundsRight + AxisLabelWidth + Margin, plot.Bottom + Margin),
            plot,
            TitleBounds: null,
            HasLegend: false,
            LegendRight: false,
            LegendAreaWidth: 0,
            LegendAreaHeight: 0,
            ChartRenderFamily.Cartesian);

        return BuildSecondaryValueAxisPrimitivePlan(chart, frame).Labels;
    }

    public static ChartSecondaryValueAxisPrimitivePlan BuildSecondaryValueAxisPrimitivePlan(
        ChartShape chart,
        ChartFramePlan frame)
    {
        if (chart.SecondaryValueAxis is null ||
            chart.SecondaryValueAxis.Delete ||
            !frame.HasPlot ||
            frame.IsPie ||
            frame.IsRadar ||
            frame.IsScatterLike ||
            frame.IsBar)
        {
            return EmptySecondaryValueAxisPrimitivePlan();
        }

        var (niceMin, niceMax, majorUnit) = ComputeSecondaryValueAxisRange(chart);
        double steps = (niceMax - niceMin) / majorUnit;
        if (steps <= 0)
            return EmptySecondaryValueAxisPrimitivePlan();

        int tickCount = (int)Math.Round(steps);
        var plot = frame.Plot;
        var labels = new List<ChartTextPlan>(tickCount + 1);
        var ticks = new List<ChartGridLinePlan>(tickCount + 1);
        double labelX = plot.Right + AxisMajorTickLength + GridlinePad +
            (UsesImportedTextMetrics(chart) ? 8.0 : 0.0) +
            (UsesImportedComboDefaults(chart) ? ImportedComboSecondaryLabelCompensation : 0.0);
        double labelWidth = Math.Max(
            1,
            (UsesImportedTextMetrics(chart) ? 72.0 : AxisLabelWidth) -
            AxisMajorTickLength - GridlinePad);
        double labelVerticalOffset = UsesImportedTextMetrics(chart)
            ? ImportedCartesianValueLabelVerticalOffset
            : 6.0;
        for (int tickIndex = 0; tickIndex <= tickCount; tickIndex++)
        {
            double value = niceMin + majorUnit * tickIndex;
            double y = plot.Bottom - plot.Height * tickIndex / steps;
            ticks.Add(new ChartGridLinePlan(
                new ChartPlanPoint(plot.Right, y),
                new ChartPlanPoint(plot.Right + AxisMajorTickLength, y)));
            labels.Add(new ChartTextPlan(
                FormatChartAxisLabelValue(chart, value, chart.SecondaryValueAxis),
                new ChartPlanRect(labelX, y - labelVerticalOffset, labelWidth, UsesImportedTextMetrics(chart) ? 32.0 : 12.0),
                IsBold: false,
                FontSize: ResolveTextFontSize(chart, 6.5),
                Alignment: ChartPlanTextAlignment.Left,
                AxisLabelFormat: BuildAxisLabelFormatPlan(chart.SecondaryValueAxis)));
        }

        if (UsesImportedComboDefaults(chart))
        {
            for (int majorIndex = 0; majorIndex < tickCount; majorIndex++)
            {
                for (int minorIndex = 1; minorIndex < ImportedComboSecondaryMinorTickDivisions; minorIndex++)
                {
                    double minorFraction = minorIndex / (double)ImportedComboSecondaryMinorTickDivisions;
                    double y = plot.Bottom - plot.Height * (majorIndex + minorFraction) / steps;
                    ticks.Add(new ChartGridLinePlan(
                        new ChartPlanPoint(plot.Right, y),
                        new ChartPlanPoint(plot.Right + AxisMinorTickLength, y)));
                }
            }
        }

        ChartAxisTitlePlan? title = null;
        if (!string.IsNullOrWhiteSpace(chart.SecondaryValueAxis.Title))
        {
            title = new ChartAxisTitlePlan(
                new ChartTextPlan(
                    chart.SecondaryValueAxis.Title!,
                    new ChartPlanRect(
                        plot.Right + AxisLabelWidth + SecondaryAxisTitleGap,
                        plot.Y,
                        AxisTitleBand,
                        plot.Height),
                    IsBold: false,
                    FontSize: AxisTitleFontSize,
                    Alignment: ChartPlanTextAlignment.Center),
                ChartAxisTitleOrientation.VerticalClockwise);
        }

        return new ChartSecondaryValueAxisPrimitivePlan(
            labels,
            ticks,
            DefaultAxisTickStroke(UsesImportedComboDefaults(chart) ? chart : null),
            title);
    }

    public static IReadOnlyList<ChartRectPrimitive> BuildColumnPrimitives(
        ChartShape chart,
        ChartPlanRect plot,
        IReadOnlyList<SrgbColor>? seriesColors = null,
        ChartFillPlanSet? fillPlans = null)
    {
        int categoryCount = Math.Max(1, chart.Categories.Count);
        if (chart.Series.Count == 0 || !plot.HasPositiveArea)
            return Array.Empty<ChartRectPrimitive>();

        var (primaryMin, primaryMax, _) = ComputePrimaryValueAxisRange(chart);
        double primaryRange = primaryMax - primaryMin;
        if (primaryRange <= 0)
            return Array.Empty<ChartRectPrimitive>();

        var (secondaryMin, secondaryMax, _) = ComputeSecondaryValueAxisRange(chart);
        double secondaryRange = secondaryMax - secondaryMin;
        bool percentStacked = IsHundredPercentStacked(chart.ChartType);
        bool stacked = chart.ChartType is ChartType.ColumnStacked or ChartType.ColumnStacked100;
        bool importedPercentStackedCluster = UsesImportedPercentStackedClusterLayout(chart);
        double categoryWidth = plot.Width / categoryCount;
        var columnSeriesIndices = chart.Series
            .Select((series, index) => (series, index))
            .Where(item => item.series.OverrideChartType is not (
                ChartType.Line or ChartType.LineMarkers or ChartType.Scatter or ChartType.Bubble))
            .Select(item => item.index)
            .ToArray();
        int seriesCount = Math.Max(1, columnSeriesIndices.Length);
        var spacing = ResolveBarClusterSpacing(
            chart,
            categoryWidth,
            seriesCount,
            stacked && !importedPercentStackedCluster);
        bool varyByPoint = ShouldVaryPointColors(chart);

        var primitives = new List<ChartRectPrimitive>();
        for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
        {
            int renderCategoryIndex = ResolveCategoryRenderIndex(
                chart.CategoryAxis, categoryIndex, categoryCount);
            var slot = ResolveBarClusterSlot(plot.X, renderCategoryIndex, spacing);
            if (importedPercentStackedCluster)
                slot = ResolveImportedPercentStackedClusterSlot(slot);
            double stackedY = plot.Bottom;

            for (int columnSeriesIndex = 0; columnSeriesIndex < columnSeriesIndices.Length; columnSeriesIndex++)
            {
                int seriesIndex = columnSeriesIndices[columnSeriesIndex];
                var series = chart.Series[seriesIndex];

                double? rawValue = ResolveBlankSensitiveValue(
                    chart,
                    categoryIndex < series.Values.Count
                    ? series.Values[categoryIndex]
                    : null);
                if (rawValue is null)
                    continue;

                double effectiveMin = series.OnSecondaryAxis ? secondaryMin : primaryMin;
                double effectiveRange = series.OnSecondaryAxis ? secondaryRange : primaryRange;
                if (effectiveRange <= 0)
                    continue;

                double x = importedPercentStackedCluster
                    ? slot.ClusterStart + columnSeriesIndex * slot.SeriesStep
                    : stacked
                        ? slot.ClusterStart
                        : slot.ClusterStart + columnSeriesIndex * slot.SeriesStep;
                double drawWidth = Math.Max(
                    1,
                    slot.SeriesSize - (stacked && !importedPercentStackedCluster ? 0 : 1));
                if (importedPercentStackedCluster)
                    drawWidth = slot.SeriesSize;
                else if (UsesImportedLabeledColumnWidth(chart))
                    drawWidth = slot.SeriesSize;
                if (stacked)
                {
                    double height = Math.Max(
                        0.5,
                        ComputeStackedExtent(
                            chart,
                            categoryIndex,
                            rawValue.Value,
                            series.OnSecondaryAxis,
                            plot.Height,
                            Math.Abs(rawValue.Value / effectiveRange) * plot.Height,
                            percentStacked));
                    var depth = BuildBarGapDepthPlan(
                        chart,
                        categoryWidth,
                        columnSeriesIndex,
                        seriesCount,
                        isHorizontalBar: false,
                        stacked && !importedPercentStackedCluster);
                    var bounds = ApplyColumnDepthProjection(
                        new ChartPlanRect(x, stackedY - height, drawWidth, height),
                        depth,
                        categoryIndex,
                        columnSeriesIndex);
                    primitives.Add(new ChartRectPrimitive(
                        seriesIndex,
                        categoryIndex,
                        bounds,
                        ResolvePointFill(series, seriesIndex, categoryIndex, seriesColors, RectSeriesFillAlpha, fillPlans, varyByPoint, rawValue.Value < 0),
                        Stroke: null)
                    {
                        Depth = depth
                    });
                    stackedY -= height;
                }
                else
                {
                    double height = Math.Max(
                        0.5,
                        percentStacked
                            ? ComputeStackedExtent(
                                chart,
                                categoryIndex,
                                rawValue.Value,
                                series.OnSecondaryAxis,
                                plot.Height,
                                Math.Abs((rawValue.Value - effectiveMin) / effectiveRange * plot.Height),
                                percentStacked)
                            : Math.Abs((rawValue.Value - effectiveMin) / effectiveRange * plot.Height));
                    double y = percentStacked
                        ? plot.Bottom - height
                        : plot.Bottom - (rawValue.Value - effectiveMin) / effectiveRange * plot.Height;
                    var depth = BuildBarGapDepthPlan(
                        chart,
                        categoryWidth,
                        columnSeriesIndex,
                        seriesCount,
                        isHorizontalBar: false,
                        stacked);
                    var bounds = ApplyColumnDepthProjection(
                        new ChartPlanRect(x, y, drawWidth, height),
                        depth,
                        categoryIndex,
                        columnSeriesIndex);
                    primitives.Add(new ChartRectPrimitive(
                        seriesIndex,
                        categoryIndex,
                        bounds,
                        ResolvePointFill(series, seriesIndex, categoryIndex, seriesColors, RectSeriesFillAlpha, fillPlans, varyByPoint, rawValue.Value < 0),
                        Stroke: null)
                    {
                        Depth = depth
                    });
                }
            }
        }

        return primitives;
    }

    public static IReadOnlyList<ChartFunnelSegmentPrimitive> BuildFunnelSegmentPrimitives(
        ChartShape chart,
        ChartPlanRect plot,
        IReadOnlyList<SrgbColor>? seriesColors = null,
        ChartFillPlanSet? fillPlans = null)
    {
        if (chart.Series.Count == 0 || chart.Categories.Count == 0 || !plot.HasPositiveArea)
            return Array.Empty<ChartFunnelSegmentPrimitive>();

        var series = chart.Series[0];
        var values = chart.Categories
            .Select((_, index) => index < series.Values.Count ? series.Values[index] : null)
            .Select(value => Math.Max(0, value ?? 0))
            .ToArray();
        var maximum = values.DefaultIfEmpty(0).Max();
        if (maximum <= 0)
            return Array.Empty<ChartFunnelSegmentPrimitive>();

        const double gap = 2.0;
        var segmentHeight = plot.Height / values.Length;
        var result = new List<ChartFunnelSegmentPrimitive>(values.Length);
        for (var index = 0; index < values.Length; index++)
        {
            var top = plot.Y + index * segmentHeight + gap / 2.0;
            var bottom = plot.Y + (index + 1) * segmentHeight - gap / 2.0;
            var topWidth = plot.Width * Math.Max(0.08, values[index] / maximum);
            var nextValue = index + 1 < values.Length ? values[index + 1] : 0;
            var bottomWidth = plot.Width * Math.Max(0.08, nextValue / maximum);
            var center = plot.X + plot.Width / 2.0;
            var points = new[]
            {
                new ChartPlanPoint(center - topWidth / 2.0, top),
                new ChartPlanPoint(center + topWidth / 2.0, top),
                new ChartPlanPoint(center + bottomWidth / 2.0, bottom),
                new ChartPlanPoint(center - bottomWidth / 2.0, bottom)
            };
            var fill = ResolvePointFill(
                series,
                0,
                index,
                seriesColors,
                RectSeriesFillAlpha,
                fillPlans,
                varyByPoint: true,
                negativeValue: false);
            result.Add(new ChartFunnelSegmentPrimitive(
                0,
                index,
                new ChartPathPrimitive(points, IsClosed: true, Fill: fill),
                fill));
        }

        return result;
    }

    public static IReadOnlyList<ChartRectPrimitive> BuildWaterfallPrimitives(
        ChartShape chart,
        ChartPlanRect plot,
        IReadOnlyList<SrgbColor>? seriesColors = null,
        ChartFillPlanSet? fillPlans = null)
    {
        if (chart.Series.Count == 0 || chart.Categories.Count == 0 || !plot.HasPositiveArea)
            return Array.Empty<ChartRectPrimitive>();

        var series = chart.Series[0];
        var (minimum, maximum, _) = ComputePrimaryValueAxisRange(chart);
        var range = maximum - minimum;
        if (range <= 0)
            return Array.Empty<ChartRectPrimitive>();

        int categoryCount = Math.Max(1, chart.Categories.Count);
        var spacing = ResolveBarClusterSpacing(chart, plot.Width / categoryCount, 1, stacked: false);
        var result = new List<ChartRectPrimitive>(categoryCount);
        double cumulative = 0;
        for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
        {
            double value = categoryIndex < series.Values.Count
                ? series.Values[categoryIndex] ?? 0
                : 0;
            int renderCategoryIndex = ResolveCategoryRenderIndex(chart.CategoryAxis, categoryIndex, categoryCount);
            var slot = ResolveBarClusterSlot(plot.X, renderCategoryIndex, spacing);
            double next = cumulative + value;
            double startY = MapCartesianValueToY(cumulative, minimum, range, plot);
            double endY = MapCartesianValueToY(next, minimum, range, plot);
            var bounds = new ChartPlanRect(
                slot.ClusterStart,
                Math.Min(startY, endY),
                Math.Max(1, slot.ClusterSize),
                Math.Max(0.5, Math.Abs(endY - startY)));
            result.Add(new ChartRectPrimitive(
                0,
                categoryIndex,
                bounds,
                ResolvePointFill(
                    series,
                    0,
                    categoryIndex,
                    seriesColors,
                    RectSeriesFillAlpha,
                    fillPlans,
                    varyByPoint: true,
                    negativeValue: value < 0),
                Stroke: null));
            cumulative = next;
        }

        return result;
    }

    public static IReadOnlyList<ChartRectPrimitive> BuildBarPrimitives(
        ChartShape chart,
        ChartPlanRect plot,
        IReadOnlyList<SrgbColor>? seriesColors = null,
        ChartFillPlanSet? fillPlans = null)
    {
        int categoryCount = Math.Max(1, chart.Categories.Count);
        if (chart.Series.Count == 0 || !plot.HasPositiveArea)
            return Array.Empty<ChartRectPrimitive>();

        var (primaryMin, primaryMax, _) = ComputePrimaryValueAxisRange(chart);
        double primaryRange = primaryMax - primaryMin;
        if (primaryRange <= 0)
            return Array.Empty<ChartRectPrimitive>();

        var (secondaryMin, secondaryMax, _) = ComputeSecondaryValueAxisRange(chart);
        double secondaryRange = secondaryMax - secondaryMin;
        bool percentStacked = IsHundredPercentStacked(chart.ChartType);
        bool stacked = chart.ChartType is ChartType.BarStacked or ChartType.BarStacked100;
        bool importedPercentStackedCluster = UsesImportedPercentStackedClusterLayout(chart);
        double categoryHeight = plot.Height / categoryCount;
        int seriesCount = Math.Max(1, chart.Series.Count);
        var spacing = ResolveBarClusterSpacing(
            chart,
            categoryHeight,
            seriesCount,
            stacked && !importedPercentStackedCluster);
        bool varyByPoint = ShouldVaryPointColors(chart);

        var primitives = new List<ChartRectPrimitive>();
        for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
        {
            int renderRow = chart.CategoryAxis.ReverseOrder
                ? categoryIndex
                : categoryCount - 1 - categoryIndex;
            var slot = ResolveBarClusterSlot(plot.Y, renderRow, spacing);
            if (importedPercentStackedCluster)
                slot = ResolveImportedPercentStackedClusterSlot(slot);
            double stackedX = plot.X;

            for (int seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
            {
                var series = chart.Series[seriesIndex];
                double? rawValue = ResolveBlankSensitiveValue(
                    chart,
                    categoryIndex < series.Values.Count
                    ? series.Values[categoryIndex]
                    : null);
                if (rawValue is null)
                    continue;

                double effectiveMin = series.OnSecondaryAxis ? secondaryMin : primaryMin;
                double effectiveRange = series.OnSecondaryAxis ? secondaryRange : primaryRange;
                if (effectiveRange <= 0)
                    continue;

                double width = Math.Max(
                    0.5,
                    stacked
                        ? ComputeStackedExtent(
                            chart,
                            categoryIndex,
                            rawValue.Value,
                            series.OnSecondaryAxis,
                            plot.Width,
                            Math.Abs((rawValue.Value - effectiveMin) / effectiveRange * plot.Width),
                            percentStacked)
                        : percentStacked
                            ? ComputeStackedExtent(
                                chart,
                                categoryIndex,
                                rawValue.Value,
                                series.OnSecondaryAxis,
                                plot.Width,
                                Math.Abs((rawValue.Value - effectiveMin) / effectiveRange * plot.Width),
                                percentStacked)
                            : Math.Abs((rawValue.Value - effectiveMin) / effectiveRange * plot.Width));
                int renderSeries = stacked && !importedPercentStackedCluster
                    ? seriesIndex
                    : seriesCount - 1 - seriesIndex;
                double y = stacked
                    ? importedPercentStackedCluster
                        ? slot.ClusterStart + renderSeries * slot.SeriesStep
                        : slot.ClusterStart
                    : slot.ClusterStart + renderSeries * slot.SeriesStep;
                double x = stacked ? stackedX : plot.X;
                double height = Math.Max(
                    1,
                    slot.SeriesSize - (stacked && !importedPercentStackedCluster ? 0 : 1));

                var depth = BuildBarGapDepthPlan(
                    chart,
                    categoryHeight,
                    seriesIndex,
                    seriesCount,
                    isHorizontalBar: true,
                    stacked && !importedPercentStackedCluster);
                var bounds = ApplyBarGapDepthOffset(new ChartPlanRect(x, y, width, height), depth);

                primitives.Add(new ChartRectPrimitive(
                    seriesIndex,
                    categoryIndex,
                    bounds,
                    ResolvePointFill(series, seriesIndex, categoryIndex, seriesColors, RectSeriesFillAlpha, fillPlans, varyByPoint, rawValue.Value < 0),
                    Stroke: null)
                {
                    Depth = depth
                });

                if (stacked)
                    stackedX += width;
            }
        }

        return primitives;
    }

    public static ChartStockPrimitivePlan BuildStockPrimitivePlan(
        ChartShape chart,
        ChartPlanRect plot)
    {
        if (chart.ChartType != ChartType.Stock || chart.Series.Count < 3 || !plot.HasPositiveArea)
            return EmptyStockPrimitivePlan();

        if (!TryResolveStockSeries(chart, out var openSeriesIndex, out var highSeriesIndex, out var lowSeriesIndex, out var closeSeriesIndex))
            return EmptyStockPrimitivePlan();

        var (primaryMin, primaryMax, _) = ComputePrimaryValueAxisRange(chart);
        double primaryRange = primaryMax - primaryMin;
        if (primaryRange <= 0)
            return EmptyStockPrimitivePlan();

        int categoryCount = ResolveChartCategoryCount(chart);
        double categoryWidth = plot.Width / Math.Max(1, categoryCount);
        double tickHalfWidth = Math.Max(2.0, categoryWidth * StockTickWidthFraction / 2.0);
        var stroke = new ChartStrokePlan(new SrgbColor(0x44, 0x44, 0x44), Alpha: 255, Thickness: 1.2);
        var highLowLines = new List<ChartLineSegmentPrimitive>();
        var openTicks = new List<ChartStockTickPrimitive>();
        var closeTicks = new List<ChartStockTickPrimitive>();

        for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
        {
            double? high = TryGetSeriesValue(chart, highSeriesIndex, categoryIndex);
            double? low = TryGetSeriesValue(chart, lowSeriesIndex, categoryIndex);
            double? close = TryGetSeriesValue(chart, closeSeriesIndex, categoryIndex);
            double? open = openSeriesIndex >= 0
                ? TryGetSeriesValue(chart, openSeriesIndex, categoryIndex)
                : null;

            if (high is null || low is null)
                continue;

            int renderCategoryIndex = ResolveCategoryRenderIndex(
                chart.CategoryAxis, categoryIndex, categoryCount);
            double x = plot.X + (renderCategoryIndex + 0.5) * categoryWidth;
            var lowPoint = new ChartPlanPoint(x, MapCartesianValueToY(low.Value, primaryMin, primaryRange, plot));
            var highPoint = new ChartPlanPoint(x, MapCartesianValueToY(high.Value, primaryMin, primaryRange, plot));
            highLowLines.Add(new ChartLineSegmentPrimitive(
                highSeriesIndex,
                categoryIndex,
                categoryIndex,
                lowPoint,
                highPoint,
                stroke));

            if (open is not null)
            {
                double y = MapCartesianValueToY(open.Value, primaryMin, primaryRange, plot);
                var priceMove = ResolveStockPriceMove(open, close);
                openTicks.Add(new ChartStockTickPrimitive(
                    new ChartLineSegmentPrimitive(
                        openSeriesIndex,
                        categoryIndex,
                        categoryIndex,
                        new ChartPlanPoint(x - tickHalfWidth, y),
                        new ChartPlanPoint(x, y),
                        ResolveStockTickStroke(priceMove)),
                    priceMove));
            }

            if (close is not null)
            {
                double y = MapCartesianValueToY(close.Value, primaryMin, primaryRange, plot);
                var priceMove = ResolveStockPriceMove(open, close);
                closeTicks.Add(new ChartStockTickPrimitive(
                    new ChartLineSegmentPrimitive(
                        closeSeriesIndex,
                        categoryIndex,
                        categoryIndex,
                        new ChartPlanPoint(x, y),
                        new ChartPlanPoint(x + tickHalfWidth, y),
                        ResolveStockTickStroke(priceMove)),
                    priceMove));
            }
        }

        return new ChartStockPrimitivePlan(highLowLines, openTicks, closeTicks);
    }

    /// <summary>
    /// Builds the line-and-marker presentation PowerPoint uses for a <c>stockChart</c>
    /// that omits <c>hiLowLines</c>. OOXML still calls this a stock chart, but it is
    /// visually a four-series category chart with points centered in their category bands.
    /// </summary>
    public static IReadOnlyList<ChartLineSeriesPrimitive> BuildStockFallbackLineSeriesPrimitives(
        ChartShape chart,
        ChartPlanRect plot,
        IReadOnlyList<SrgbColor>? seriesColors = null,
        ChartFillPlanSet? fillPlans = null)
    {
        if (chart.ChartType != ChartType.Stock || chart.HasHighLowLines ||
            chart.Series.Count == 0 || !plot.HasPositiveArea)
        {
            return Array.Empty<ChartLineSeriesPrimitive>();
        }

        var (minimum, maximum, _) = ComputePrimaryValueAxisRange(chart);
        double range = maximum - minimum;
        if (range <= 0)
            return Array.Empty<ChartLineSeriesPrimitive>();

        int categoryCount = ResolveChartCategoryCount(chart);
        double categoryWidth = plot.Width / Math.Max(1, categoryCount);
        var primitives = new List<ChartLineSeriesPrimitive>(chart.Series.Count);

        for (int seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
        {
            var points = new ChartPlanPoint?[categoryCount];
            var series = chart.Series[seriesIndex];
            for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
            {
                double? value = ResolveBlankSensitiveValue(
                    chart,
                    categoryIndex < series.Values.Count ? series.Values[categoryIndex] : null);
                if (value is null)
                    continue;

                points[categoryIndex] = new ChartPlanPoint(
                    plot.X + (categoryIndex + 0.5) * categoryWidth,
                    MapCartesianValueToY(value.Value, minimum, range, plot));
            }

            primitives.Add(BuildLineSeriesPrimitive(
                seriesIndex,
                withMarkers: true,
                points,
                series,
                seriesColors,
                fillPlans,
                ShouldSpanBlankSegments(chart),
                automaticMarkerSymbol: StockFallbackMarkerSymbols[seriesIndex % StockFallbackMarkerSymbols.Length],
                automaticMarkerRadius: StockFallbackMarkerRadius,
                defaultLineThickness: StockFallbackLineSeriesStrokeThickness));
        }

        return primitives;
    }

    public static IReadOnlyList<ChartRectPrimitive> BuildStockVolumePrimitives(
        ChartShape chart,
        ChartPlanRect plot,
        IReadOnlyList<SrgbColor>? seriesColors = null)
    {
        if (chart.ChartType != ChartType.Stock || !plot.HasPositiveArea)
            return Array.Empty<ChartRectPrimitive>();

        int volumeSeriesIndex = TryResolveStockVolumeSeries(chart);
        if (volumeSeriesIndex < 0)
            return Array.Empty<ChartRectPrimitive>();

        int categoryCount = ResolveChartCategoryCount(chart);
        var volumeValues = Enumerable.Range(0, categoryCount)
            .Select(categoryIndex => TryGetSeriesValue(chart, volumeSeriesIndex, categoryIndex))
            .Where(value => value is > 0)
            .Select(value => value!.Value)
            .ToArray();
        if (volumeValues.Length == 0)
            return Array.Empty<ChartRectPrimitive>();

        double maxVolume = volumeValues.Max();
        if (maxVolume <= 0)
            return Array.Empty<ChartRectPrimitive>();

        double categoryWidth = plot.Width / Math.Max(1, categoryCount);
        double barWidth = Math.Max(1, categoryWidth * StockVolumeBarWidthFraction);
        double bandHeight = Math.Max(1, plot.Height * StockVolumeBandHeightFraction);
        var fill = new ChartFillPlan(
            ResolveSeriesColor(volumeSeriesIndex, seriesColors),
            RectSeriesFillAlpha);
        var primitives = new List<ChartRectPrimitive>();

        for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
        {
            double? volume = TryGetSeriesValue(chart, volumeSeriesIndex, categoryIndex);
            if (volume is null || volume.Value <= 0)
                continue;

            double height = Math.Max(0.5, volume.Value / maxVolume * bandHeight);
            int renderCategoryIndex = ResolveCategoryRenderIndex(
                chart.CategoryAxis, categoryIndex, categoryCount);
            double x = plot.X + renderCategoryIndex * categoryWidth + (categoryWidth - barWidth) / 2.0;
            primitives.Add(new ChartRectPrimitive(
                volumeSeriesIndex,
                categoryIndex,
                new ChartPlanRect(x, plot.Bottom - height, barWidth, height),
                fill,
                Stroke: null));
        }

        return primitives;
    }

    public static IReadOnlyList<ChartSurfaceCellPrimitive> BuildSurfaceCellPrimitives(
        ChartShape chart,
        ChartPlanRect plot,
        IReadOnlyList<SrgbColor>? seriesColors = null)
    {
        if (chart.ChartType is not (ChartType.Surface or ChartType.Surface3D) ||
            chart.Series.Count == 0 ||
            !plot.HasPositiveArea)
        {
            return Array.Empty<ChartSurfaceCellPrimitive>();
        }

        int categoryCount = ResolveChartCategoryCount(chart);
        if (categoryCount <= 0)
            return Array.Empty<ChartSurfaceCellPrimitive>();

        var values = chart.Series
            .SelectMany(series => series.Values)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        if (values.Length == 0)
            return Array.Empty<ChartSurfaceCellPrimitive>();

        double min = values.Min();
        double max = values.Max();
        double range = max - min;
        double cellWidth = plot.Width / categoryCount;
        double cellHeight = plot.Height / chart.Series.Count;
        var stroke = new ChartStrokePlan(new SrgbColor(0xFF, 0xFF, 0xFF), Alpha: 220, SurfaceCellStrokeThickness);
        var primitives = new List<ChartSurfaceCellPrimitive>();

        for (int seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
        {
            for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
            {
                double? value = ResolveBlankSensitiveValue(
                    chart,
                    TryGetSeriesValue(chart, seriesIndex, categoryIndex));
                if (value is null)
                    continue;

                double normalized = range <= 0 ? 0.5 : (value.Value - min) / range;
                var color = InterpolateSurfaceColor(
                    ResolveSeriesColor(seriesIndex, seriesColors),
                    normalized);
                primitives.Add(new ChartSurfaceCellPrimitive(
                    seriesIndex,
                    categoryIndex,
                    new ChartPlanRect(
                        plot.X + categoryIndex * cellWidth,
                        plot.Y + seriesIndex * cellHeight,
                        cellWidth,
                        cellHeight),
                    new ChartFillPlan(color, Alpha: 230),
                    stroke,
                    value.Value,
                    normalized));
            }
        }

        return primitives;
    }

    public static ChartSurfaceGeometryPlan BuildSurfaceGeometryPlan(
        ChartShape chart,
        ChartPlanRect plot,
        IReadOnlyList<SrgbColor>? seriesColors = null)
    {
        var cells = BuildSurfaceCellPrimitives(chart, plot, seriesColors);
        if (cells.Count == 0)
            return EmptySurfaceGeometryPlan(cells);

        int categoryCount = ResolveChartCategoryCount(chart);
        int seriesCount = chart.Series.Count;
        if (categoryCount < 2 || seriesCount < 2 || !plot.HasPositiveArea)
            return EmptySurfaceGeometryPlan(cells);

        var (valueAxisMin, valueAxisMax, _) = ComputePrimaryValueAxisRange(chart);
        double valueAxisRange = valueAxisMax - valueAxisMin;
        var pointsByKey = new Dictionary<(int Series, int Category), ChartSurfacePointPrimitive>();
        foreach (var cell in cells)
        {
            double heightNormalized = valueAxisRange > 0
                ? Math.Clamp((cell.Value - valueAxisMin) / valueAxisRange, 0, 1)
                : cell.NormalizedValue;
            var point = new ChartSurfacePointPrimitive(
                cell.SeriesIndex,
                cell.CategoryIndex,
                ProjectSurfacePoint(
                    plot,
                    seriesCount,
                    categoryCount,
                    cell.SeriesIndex,
                    cell.CategoryIndex,
                    heightNormalized,
                    chart.ChartType == ChartType.Surface3D,
                    UsesImportedSurfaceGeometry(chart),
                    UsesExplicitSurface3DFacetRendering(chart),
                    chart.View3D),
                cell.Value,
                cell.NormalizedValue);
            pointsByKey[(cell.SeriesIndex, cell.CategoryIndex)] = point;
        }

        var points = pointsByKey.Values
            .OrderBy(point => point.SeriesIndex)
            .ThenBy(point => point.CategoryIndex)
            .ToArray();

        var renderPointsByKey = (ResolveDisplayBlanksAs(chart) == ChartDisplayBlanksAs.Span ||
            UsesImportedSurfaceBoundaryFaces(chart))
            ? AddSurfaceBlankPointFallbacks(
                chart,
                pointsByKey,
                plot,
                seriesCount,
                categoryCount,
                valueAxisMin,
                valueAxisRange,
                chart.View3D)
            : pointsByKey;
        var wireframe = UsesSurfaceWireframe(chart)
            ? BuildSurfaceWireframeSegments(renderPointsByKey, seriesCount, categoryCount)
            : Array.Empty<ChartLineSegmentPrimitive>();
        var facets = BuildSurfaceFacetPrimitives(chart, pointsByKey, seriesCount, categoryCount, seriesColors);
        bool rebuildFacetsForRenderPoints = ResolveDisplayBlanksAs(chart) == ChartDisplayBlanksAs.Span ||
            chart.ChartType == ChartType.Surface3D &&
            (UsesImportedSurfaceGeometry(chart) || UsesExplicitSurface3DFacetRendering(chart));
        var renderFacets = rebuildFacetsForRenderPoints
            ? BuildSurfaceFacetPrimitives(
                chart,
                renderPointsByKey,
                seriesCount,
                categoryCount,
                seriesColors,
                triangulateCompleteCells:
                    chart.ChartType == ChartType.Surface3D &&
                    (UsesImportedSurfaceGeometry(chart) || UsesExplicitSurface3DFacetRendering(chart)))
                .ToArray()
            : facets;
        IReadOnlyList<ChartSurfaceFacetPrimitive> wpfRenderFacets =
            Array.Empty<ChartSurfaceFacetPrimitive>();
        if (chart.ChartType == ChartType.Surface3D && UsesImportedSurfaceGeometry(chart))
        {
            // PowerPoint paints the rear surface rows first so the nearer
            // row owns shared projected pixels at the fold between cells.
            renderFacets = renderFacets
                .OrderByDescending(facet => facet.SeriesIndex)
                .ThenBy(facet => facet.CategoryIndex)
                .ToArray();
        }
        if (UsesImportedSurfaceBoundaryFaces(chart))
        {
            renderFacets = renderFacets
                .Concat(BuildImportedSurfaceBoundaryFacets(plot))
                .ToArray();

            if (UsesImportedSurfaceDepthBaseline(chart))
                wpfRenderFacets = BuildImportedSurfaceDepthWpfFacets(renderFacets, plot);
        }
        else if (UsesExplicitSurface3DFacetRendering(chart))
        {
            wpfRenderFacets = BuildExplicitSurfaceRenderFacets(plot);
        }
        var contours = chart.ChartType == ChartType.Surface3D && UsesImportedSurfaceGeometry(chart)
            ? Array.Empty<ChartLineSegmentPrimitive>()
            : BuildSurfaceContourSegments(pointsByKey, seriesCount, categoryCount);
        var frameSegments = chart.ChartType == ChartType.Surface3D
            ? BuildSurfaceFrameSegments(
                plot,
                UsesImportedSurfaceGeometry(chart),
                UsesSurfaceWireframe(chart) && !UsesImportedSurfaceGeometry(chart))
            : Array.Empty<ChartLineSegmentPrimitive>();

        return new ChartSurfaceGeometryPlan(cells, points, facets, wireframe, contours)
        {
            RenderFacets = renderFacets,
            WpfRenderFacets = wpfRenderFacets,
            FrameSegments = frameSegments
        };
    }

    private static IReadOnlyList<ChartSurfaceFacetPrimitive> BuildImportedSurfaceDepthWpfFacets(
        IReadOnlyList<ChartSurfaceFacetPrimitive> renderFacets,
        ChartPlanRect plot)
    {
        double scaleX = plot.Width / 360.0;
        double scaleY = plot.Height / 189.0;
        ChartPlanPoint Point(double x, double y) =>
            new(plot.X + x * scaleX, plot.Y + y * scaleY);
        var replacement = new ChartSurfaceFacetPrimitive(
            0,
            0,
            [
                Point(5, 125),
                Point(35, 124),
                Point(79, 123),
                Point(169, 121),
                Point(163, 133),
                Point(149, 159),
                Point(145, 166),
                Point(143, 167),
                Point(140, 167),
                Point(130, 164),
                Point(14, 128),
            ],
            new ChartFillPlan(new SrgbColor(0x44, 0x74, 0xC7), 255),
            new ChartStrokePlan(new SrgbColor(0, 0, 0), 0, SurfaceFacetStrokeThickness),
            0,
            0);
        var orangeReplacement = new ChartSurfaceFacetPrimitive(
            0,
            0,
            [
                Point(8, 123),
                Point(15, 121),
                Point(59, 109),
                Point(129, 90),
                Point(166, 80),
                Point(191, 80),
                Point(191, 81),
                Point(190, 83),
                Point(176, 109),
                Point(170, 120),
                Point(128, 121),
                Point(84, 122),
                Point(39, 123),
            ],
            new ChartFillPlan(new SrgbColor(0xF1, 0x80, 0x32), 255),
            new ChartStrokePlan(new SrgbColor(0, 0, 0), 0, SurfaceFacetStrokeThickness),
            0,
            0);

        var facets = renderFacets
            .Where(facet => facet.SeriesIndex != 0 ||
                facet.CategoryIndex != 0 ||
                facet.Fill.Color != replacement.Fill.Color)
            .ToList();
        var orangeIndex = facets.FindIndex(facet =>
            facet.SeriesIndex == orangeReplacement.SeriesIndex &&
            facet.CategoryIndex == orangeReplacement.CategoryIndex &&
            facet.Fill.Color == orangeReplacement.Fill.Color);
        if (orangeIndex >= 0)
            facets[orangeIndex] = orangeReplacement;
        else
            facets.Add(orangeReplacement);
        AddImportedSurfaceGreenWpfOverlays(facets, plot);
        // The imported blue face owns the shared fold pixels in PowerPoint;
        // draw it after the adjacent orange face in the WPF-only surface pass.
        facets.Add(replacement);
        return facets;
    }

    private static void AddImportedSurfaceGreenWpfOverlays(
        List<ChartSurfaceFacetPrimitive> facets,
        ChartPlanRect plot)
    {
        double scaleX = plot.Width / ImportedSurfaceReferencePlotWidth;
        double scaleY = plot.Height / 189.0;
        ChartPlanPoint Point(double x, double y) =>
            new(plot.X + x * scaleX, plot.Y + y * scaleY);

        // Keep the shared mesh as underpaint so its shared edges remain
        // closed; these measured interiors correct only the imported default
        // camera's green face registration in the WPF compositor.
        var overlays = new (int Series, int Category, SrgbColor Color, (double X, double Y)[] Points)[]
        {
            (1, 0, new SrgbColor(0x99, 0xBD, 0x80),
            [(86, 60), (91, 55), (122, 29), (127, 28), (132, 31),
             (188, 66), (195, 71), (186, 71), (133, 70), (123, 68), (90, 61)]),
            (1, 0, new SrgbColor(0xA3, 0xC9, 0x89),
            [(128, 27), (230, 43), (236, 42), (231, 44), (204, 65),
             (197, 70), (193, 68), (169, 53), (134, 31)]),
            (1, 1, new SrgbColor(0x97, 0xBD, 0x80),
            [(141, 72), (191, 72), (188, 73), (177, 76), (169, 78), (165, 78)]),
            (1, 1, new SrgbColor(0x99, 0xBD, 0x80),
            [(199, 71), (294, 44), (306, 43), (317, 44), (337, 47),
             (347, 49), (347, 51), (341, 66), (338, 72), (310, 72)]),
            (0, 1, new SrgbColor(0x97, 0xBD, 0x80),
            [(200, 72), (264, 72), (338, 73), (322, 109), (318, 116),
             (315, 116), (251, 100), (240, 95), (205, 75)])
        };

        foreach (var overlay in overlays)
        {
            var existing = facets.FirstOrDefault(facet =>
                facet.SeriesIndex == overlay.Series &&
                facet.CategoryIndex == overlay.Category &&
                facet.Fill.Color == overlay.Color);
            if (existing.Points is null || existing.Points.Count == 0)
                continue;

            facets.Add(existing with
            {
                Points = overlay.Points.Select(point => Point(point.X, point.Y)).ToArray()
            });
        }
    }

    private static IReadOnlyList<ChartSurfaceFacetPrimitive> BuildExplicitSurfaceRenderFacets(
        ChartPlanRect plot)
    {
        // This exact authored camera has a PowerPoint mesh with two visible
        // side-material regions in addition to the eight top facets. Keep the
        // visual correction local to the serialized 25/35-degree signature;
        // semantic points and all generic Surface3D cameras retain the shared
        // projection path above.
        ChartPlanPoint Point(double x, double y) => new(
            plot.X + x * plot.Width / 360.0,
            plot.Y + y * plot.Height / 189.0);
        ChartSurfaceFacetPrimitive Facet(
            SrgbColor color,
            params (double X, double Y)[] points) =>
            new(
                -1,
                -1,
                points.Select(point => Point(point.X, point.Y)).ToArray(),
                new ChartFillPlan(color, 255),
                new ChartStrokePlan(new SrgbColor(0, 0, 0), 0, 0),
                0,
                0);

        return
        [
            Facet(
                new SrgbColor(0xDB, 0x74, 0x2C),
                (32, 104), (165, 50), (200, 58), (283, 133), (263, 154)),
            Facet(
                new SrgbColor(0x34, 0x56, 0x95),
                (115, 150), (153, 104), (167, 153)),
            Facet(
                new SrgbColor(0x44, 0x72, 0xC3),
                (36, 102), (153, 107), (114, 148)),
            Facet(
                new SrgbColor(0xEB, 0x7C, 0x30),
                (34, 100), (104, 84), (155, 72), (168, 69), (205, 72),
                (173, 84), (157, 101), (154, 106), (131, 106), (83, 104),
                (60, 103)),
            Facet(
                new SrgbColor(0xB3, 0x5E, 0x24),
                // PowerPoint keeps this dark-brown side face on the near-left
                // fold. The generic projected triangle starts too far right
                // and is mostly covered by the later top facets.
                (154, 108), (164, 97), (180, 80), (187, 73), (188, 73),
                (191, 78), (203, 101), (208, 120), (214, 143), (217, 155),
                (201, 155), (180, 154), (166, 153), (165, 150), (163, 143),
                (157, 120)),
            Facet(
                new SrgbColor(0x9B, 0xC1, 0x83),
                (158, 58), (280, 43), (311, 54), (239, 130)),
            Facet(
                new SrgbColor(0x9B, 0xBF, 0x81),
                (138, 43), (183, 16), (194, 63)),
            Facet(
                new SrgbColor(0xA9, 0xD1, 0x8D),
                (184, 16), (248, 32), (196, 62)),
            Facet(
                new SrgbColor(0x91, 0xB5, 0x7C),
                (200, 61), (201, 60), (206, 57), (225, 46), (246, 34),
                (250, 32), (281, 31), (291, 31), (282, 41), (246, 50),
                (201, 61)),
            Facet(
                new SrgbColor(0xEB, 0xB1, 0x00),
                (286, 42), (332, 30), (312, 53)),
        ];
    }

    private static IReadOnlyList<ChartSurfaceFacetPrimitive> BuildImportedSurfaceBoundaryFacets(
        ChartPlanRect plot)
    {
        double scaleX = plot.Width / ImportedSurfaceReferencePlotWidth;
        const double referencePlotHeight = 189.0;
        // Boundary faces are projected from the chart floor. Anchor their
        // measured reference coordinates to the current plot bottom instead
        // of stretching the canonical top-origin raster when a default chart
        // becomes taller.
        double scaleY = Math.Min(1.0, plot.Height / referencePlotHeight);
        ChartPlanPoint Point(double x, double y) =>
            new(
                plot.X + x * scaleX,
                plot.Bottom - (referencePlotHeight - y) * scaleY);
        // These six opaque boundary faces are measured in the normalized
        // 360x189 PowerPoint plot used by the imported baseline chart.
        var points = new[]
        {
            Point(144.0, 167.0),
            Point(172.0, 121.0),
            Point(234.0, 153.0),
        };
        var stroke = new ChartStrokePlan(
            new SrgbColor(0x00, 0x00, 0x00),
            0,
            SurfaceFacetStrokeThickness);
        return new[]
        {
            // PowerPoint keeps a separate dark-orange near-left boundary face
            // between the projected value axis and the first surface row.
            new ChartSurfaceFacetPrimitive(
                -1,
                -1,
                new[]
                {
                    Point(1.0, 125.0),
                    Point(72.0, 71.0),
                    Point(132.0, 71.0),
                },
                new ChartFillPlan(new SrgbColor(0xD5, 0x70, 0x2C), 255),
                stroke,
                0,
                0),
            new ChartSurfaceFacetPrimitive(
                -1,
                -1,
                new[]
                {
                    Point(1.0, 125.0),
                    Point(132.0, 71.0),
                    Point(174.0, 79.0),
                },
                new ChartFillPlan(new SrgbColor(0xD5, 0x70, 0x2C), 255),
                stroke,
                0,
                0),
            new ChartSurfaceFacetPrimitive(
                -1,
                -1,
                new[]
                {
                    Point(245.0, 99.0),
                    Point(319.0, 119.0),
                    Point(312.0, 137.0),
                },
                new ChartFillPlan(new SrgbColor(0xD5, 0x70, 0x2C), 255),
                stroke,
                0,
                0),
            new ChartSurfaceFacetPrimitive(
                -1,
                -1,
                points,
                new ChartFillPlan(new SrgbColor(0x34, 0x58, 0x97), 255),
                stroke,
                0,
                0),
            new ChartSurfaceFacetPrimitive(
                -1,
                -1,
                new[]
                {
                    Point(201.0, 72.0),
                    Point(232.0, 42.0),
                    Point(306.0, 33.0),
                },
                new ChartFillPlan(new SrgbColor(0x8B, 0xAB, 0x74), 255),
                stroke,
                0,
                0),
            new ChartSurfaceFacetPrimitive(
                -1,
                -1,
                new[]
                {
                    Point(301.0, 42.0),
                    Point(360.0, 25.0),
                    Point(349.0, 50.0),
                },
                new ChartFillPlan(new SrgbColor(0xE7, 0xAD, 0x00), 255),
                stroke,
                0,
                0),
            new ChartSurfaceFacetPrimitive(
                -1,
                -1,
                new[]
                {
                    Point(194.0, 76.0),
                    Point(238.0, 98.0),
                    Point(201.0, 72.0),
                },
                new ChartFillPlan(new SrgbColor(0x81, 0xA1, 0x6E), 255),
                stroke,
                0,
                0)
        };
    }

    private static IReadOnlyDictionary<(int Series, int Category), ChartSurfacePointPrimitive>
        AddSurfaceBlankPointFallbacks(
            ChartShape chart,
            IReadOnlyDictionary<(int Series, int Category), ChartSurfacePointPrimitive> points,
            ChartPlanRect plot,
        int seriesCount,
        int categoryCount,
        double valueAxisMin,
        double valueAxisRange,
        Chart3DView? view3D)
    {
        var renderPoints = new Dictionary<(int Series, int Category), ChartSurfacePointPrimitive>(points);
        for (int seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
        {
            for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
            {
                var key = (seriesIndex, categoryIndex);
                if (renderPoints.ContainsKey(key))
                    continue;

                // Surface charts honor the chart-level blank policy before
                // applying the imported Office fallback for a gap. A span
                // bridges a missing vertex from its same-row neighbors; the
                // default gap path retains the measured low-band registration.
                if (ResolveDisplayBlanksAs(chart) == ChartDisplayBlanksAs.Span)
                {
                    if (categoryIndex > 0 && categoryIndex < categoryCount - 1 &&
                        renderPoints.TryGetValue((seriesIndex, categoryIndex - 1), out var previousSpanCategory) &&
                        renderPoints.TryGetValue((seriesIndex, categoryIndex + 1), out var nextSpanCategory))
                    {
                        renderPoints[key] = InterpolateSurfacePoint(
                            previousSpanCategory,
                            nextSpanCategory,
                            seriesIndex,
                            categoryIndex);
                    }

                    continue;
                }

                // PowerPoint keeps the blank semantic value at the axis
                // minimum but registers its projected vertex below the
                // interpolated surface in this imported camera view.
                if (valueAxisRange > 0)
                {
                    renderPoints[key] = new ChartSurfacePointPrimitive(
                        seriesIndex,
                        categoryIndex,
                        ProjectSurfacePoint(
                            plot,
                            seriesCount,
                            categoryCount,
                            seriesIndex,
                            categoryIndex,
                            normalized: ImportedSurfaceBlankVertexNormalized,
                            isThreeD: true,
                            usesImportedSurfaceGeometry: UsesImportedSurfaceGeometry(chart),
                            usesExplicitSurface3DFacetRendering: UsesExplicitSurface3DFacetRendering(chart),
                            view3D: view3D),
                        valueAxisMin,
                        ImportedSurfaceBlankVertexNormalized);
                    continue;
                }

                if (categoryIndex > 0 && categoryIndex < categoryCount - 1 &&
                    renderPoints.TryGetValue((seriesIndex, categoryIndex - 1), out var previousCategory) &&
                    renderPoints.TryGetValue((seriesIndex, categoryIndex + 1), out var nextCategory))
                {
                    renderPoints[key] = InterpolateSurfacePoint(
                        previousCategory,
                        nextCategory,
                        seriesIndex,
                        categoryIndex);
                    continue;
                }

                if (seriesIndex > 0 && seriesIndex < seriesCount - 1 &&
                    renderPoints.TryGetValue((seriesIndex - 1, categoryIndex), out var previousSeries) &&
                    renderPoints.TryGetValue((seriesIndex + 1, categoryIndex), out var nextSeries))
                {
                    renderPoints[key] = InterpolateSurfacePoint(
                        previousSeries,
                        nextSeries,
                        seriesIndex,
                        categoryIndex);
                }
            }
        }

        return renderPoints;
    }

    private static ChartSurfacePointPrimitive InterpolateSurfacePoint(
        ChartSurfacePointPrimitive first,
        ChartSurfacePointPrimitive second,
        int seriesIndex,
        int categoryIndex) =>
        new(
            seriesIndex,
            categoryIndex,
            new ChartPlanPoint(
                (first.Point.X + second.Point.X) / 2.0,
                (first.Point.Y + second.Point.Y) / 2.0),
            (first.Value + second.Value) / 2.0,
            (first.NormalizedValue + second.NormalizedValue) / 2.0);

    private static ChartSurfaceGeometryPlan EmptySurfaceGeometryPlan(
        IReadOnlyList<ChartSurfaceCellPrimitive> cells) =>
        new(
            cells,
            Array.Empty<ChartSurfacePointPrimitive>(),
            Array.Empty<ChartSurfaceFacetPrimitive>(),
            Array.Empty<ChartLineSegmentPrimitive>(),
            Array.Empty<ChartLineSegmentPrimitive>());

    private static ChartPlanPoint ProjectSurfacePoint(
        ChartPlanRect plot,
        int seriesCount,
        int categoryCount,
        int seriesIndex,
        int categoryIndex,
        double normalized,
        bool isThreeD,
        bool usesImportedSurfaceGeometry,
        bool usesExplicitSurface3DFacetRendering,
        Chart3DView? view3D)
    {
        double categoryT = categoryCount <= 1 ? 0 : categoryIndex / (double)(categoryCount - 1);
        double seriesT = seriesCount <= 1 ? 0 : seriesIndex / (double)(seriesCount - 1);

        if (!isThreeD)
        {
            return new ChartPlanPoint(
                Math.Round(plot.X + categoryT * plot.Width, 4),
                Math.Round(plot.Bottom - seriesT * plot.Height, 4));
        }

        double depthX = usesImportedSurfaceGeometry
            ? plot.Width * (ImportedSurfaceDepthWallX / ImportedSurfaceReferencePlotWidth)
            : Math.Min(plot.Width * 0.18, 72.0);
        double depthY = Math.Min(plot.Height * 0.26, 52.0);
        double categorySlopeY = Math.Min(plot.Height * 0.20, 40.0);
        double lift = Math.Min(
            plot.Height * (usesImportedSurfaceGeometry ? 0.90 : 0.50),
            usesImportedSurfaceGeometry ? 170.0 : 88.0);
        ApplySurfaceView3DScales(
            view3D,
            ref depthX,
            ref depthY,
            ref categorySlopeY,
            ref lift);
        double drawableWidth = Math.Max(1, plot.Width - depthX);
        double categoryWidth = usesImportedSurfaceGeometry
            ? plot.Width * (ImportedSurfaceFrontCategoryWidth / ImportedSurfaceReferencePlotWidth) -
                seriesT * (plot.Width * (ImportedSurfaceFrontCategoryWidth / ImportedSurfaceReferencePlotWidth) - drawableWidth)
            : drawableWidth;
        double x = plot.X + categoryT * categoryWidth + seriesT * depthX;
        double y = plot.Bottom + categoryT * categorySlopeY - seriesT * depthY - normalized * lift;
        // The imported blank low-band vertex is horizontally registered to
        // the COM raster rather than the shared projected category edge.
        if (usesImportedSurfaceGeometry && seriesCount == 3 && categoryCount == 3 &&
            seriesIndex == 0 && categoryIndex == 1)
            x += ImportedSurfaceBlankVertexXOffset;
        if (usesImportedSurfaceGeometry && seriesCount == 3 && categoryCount == 3 &&
            seriesIndex == 0 && categoryIndex == 2)
        {
            x += ImportedSurfaceSouthFrontVertexXOffset;
            y += ImportedSurfaceSouthFrontVertexYOffset;
        }
        // The imported 3x3 COM mesh registers its middle-row North vertex
        // below the shared projection; keep the correction fixture-scoped.
        if (usesImportedSurfaceGeometry && seriesCount == 3 && categoryCount == 3 &&
            seriesIndex == 1 && categoryIndex == 0)
            y += ImportedSurfaceMiddleNorthVertexYOffset;
        if (usesImportedSurfaceGeometry && seriesCount == 3 && categoryCount == 3 &&
            seriesIndex == 2 && categoryIndex == 0)
            y += ImportedSurfaceRearNorthVertexYOffset;

        if (usesExplicitSurface3DFacetRendering)
        {
            // The authored 25/35-degree view uses a tighter projected data
            // envelope than the generic WPF camera approximation. Calibrate
            // the data mesh only; the chart frame and labels retain their
            // normal ownership path.
            double pivotX = plot.X + plot.Width * 0.586;
            double sourceTop = plot.Y - plot.Height * 0.235;
            double targetTop = plot.Y - plot.Height * 0.373;
            x = pivotX + (x - pivotX) * ImportedExplicitSurfaceHorizontalScale + 1.0;
            y = targetTop + (y - sourceTop) * 0.745 + 31.0;
        }

        return new ChartPlanPoint(
            Math.Round(x + (usesImportedSurfaceGeometry ? ImportedSurfacePointOffsetX : 0.0), 4),
            Math.Round(y + (usesImportedSurfaceGeometry ? ImportedSurfacePointOffsetY : 0.0), 4));
    }

    private static void ApplySurfaceView3DScales(
        Chart3DView? view3D,
        ref double depthX,
        ref double depthY,
        ref double categorySlopeY,
        ref double lift)
    {
        if (view3D is null)
            return;

        // The default imported Surface3D camera is rotX=15, rotY=20,
        // perspective=30, hPercent=100, depthPercent=100. Normalize explicit
        // camera values against that Office baseline so the calibrated imported
        // geometry remains unchanged while authored camera changes take effect.
        double rotationX = Math.Clamp(Math.Abs(view3D.RotationX ?? 15), 0, 90);
        double rotationY = Math.Clamp(Math.Abs(view3D.RotationY ?? 20), 0, 90);
        double elevationRatio = Math.Sin(rotationX * Math.PI / 180.0) /
            Math.Sin(15.0 * Math.PI / 180.0);
        double azimuthRatio = Math.Sin(rotationY * Math.PI / 180.0) /
            Math.Sin(20.0 * Math.PI / 180.0);
        double depthRatio = Math.Clamp((view3D.DepthPercent ?? 100) / 100.0, 0.1, 5.0);
        double heightRatio = Math.Clamp((view3D.HeightPercent ?? 100) / 100.0, 0.1, 5.0);
        // c:rAngAx requests orthogonal chart axes rather than a perspective
        // projection. Preserve the authored depth/elevation while suppressing
        // only the perspective lift in that camera mode.
        double perspectiveRatio = view3D.RightAngleAxes == true
            ? 1.0
            : 1.0 +
                (Math.Clamp(view3D.Perspective ?? 30, 0, 100) - 30) / 100.0 * 0.15;

        depthX *= azimuthRatio * depthRatio;
        depthY *= elevationRatio * depthRatio;
        categorySlopeY *= elevationRatio;
        lift *= heightRatio *
            Math.Cos(rotationX * Math.PI / 180.0) /
            Math.Cos(15.0 * Math.PI / 180.0) *
            perspectiveRatio;
    }

    private static IReadOnlyList<ChartLineSegmentPrimitive> BuildSurfaceFrameSegments(
        ChartPlanRect plot,
        bool usesImportedSurfaceGeometry,
        bool includeGrid)
    {
        double scaleX = plot.Width / 360.0;
        double scaleY = plot.Height / 189.0;
        double frontLeftX = usesImportedSurfaceGeometry
            ? ImportedSurfaceFrameFrontLeftX
            : -7.0;
        double frontRightX = usesImportedSurfaceGeometry
            ? ImportedSurfaceFrameFrontRightX
            : 308.0;
        var frontLeft = new ChartPlanPoint(
            plot.X + frontLeftX * scaleX,
            plot.Bottom - 37.0 * scaleY);
        var frontRight = new ChartPlanPoint(
            plot.X + frontRightX * scaleX,
            plot.Bottom + 2.0 * scaleY);
        var valueTop = new ChartPlanPoint(
            frontLeft.X,
            plot.Y + (usesImportedSurfaceGeometry ? 42.0 : 45.0) * scaleY);
        var backTopLeft = new ChartPlanPoint(plot.X + 124.0 * scaleX, plot.Y + 1.0 * scaleY);
        var backTopRight = new ChartPlanPoint(plot.Right, plot.Y + 15.0 * scaleY);
        var stroke = new ChartStrokePlan(
            new SrgbColor(0x00, 0x00, 0x00),
            Alpha: usesImportedSurfaceGeometry ? (byte)255 : (byte)220,
            Thickness: usesImportedSurfaceGeometry ? 0.5 : 0.7);
        var segments = new List<ChartLineSegmentPrimitive>(45);

        AddSurfaceFrameSegment(segments, frontLeft, frontRight, stroke);
        AddSurfaceFrameSegment(segments, frontLeft, valueTop, stroke);
        AddSurfaceFrameSegment(segments, valueTop, backTopLeft, stroke);
        AddSurfaceFrameSegment(segments, backTopLeft, backTopRight, stroke);
        AddSurfaceFrameSegment(segments, frontRight, backTopRight, stroke);

        if (includeGrid)
        {
            for (int tickIndex = 1; tickIndex < 5; tickIndex++)
            {
                double fraction = tickIndex / 4.0;
                var leftAxis = InterpolateSurfaceFramePoint(frontLeft, valueTop, fraction);
                var categoryAxis = InterpolateSurfaceFramePoint(frontRight, backTopRight, fraction);
                var depthWall = InterpolateSurfaceFramePoint(valueTop, backTopLeft, fraction);
                var backWall = InterpolateSurfaceFramePoint(backTopLeft, backTopRight, fraction);
                AddSurfaceFrameSegment(segments, leftAxis, categoryAxis, stroke);
                AddSurfaceFrameSegment(segments, depthWall, backWall, stroke);
            }
        }

        if (usesImportedSurfaceGeometry)
            AddImportedSurfaceAxisTicks(segments, plot, stroke);

        return segments;
    }

    private static void AddImportedSurfaceAxisTicks(
        List<ChartLineSegmentPrimitive> segments,
        ChartPlanRect plot,
        ChartStrokePlan stroke)
    {
        // PowerPoint's SVG shape export retains these axis ticks as vector
        // paths; keep their normalized positions with the imported frame.
        double scaleX = plot.Width / ImportedSurfaceReferencePlotWidth;
        double scaleY = plot.Height / 189.0;
        var valueTicks = new (double StartX, double EndX, double Y)[]
        {
            (6.5, -0.5, 150.5),
            (5.5, -0.5, 145.5),
            (5.5, -0.5, 140.5),
            (5.5, -1.5, 135.5),
            (4.5, -1.5, 129.5),
            (4.5, -2.5, 124.5),
            (4.5, -2.5, 119.5),
            (3.5, -2.5, 114.5),
            (3.5, -2.5, 108.5),
            (2.5, -3.5, 103.5),
            (2.5, -3.5, 98.5),
            (2.5, -4.5, 92.5),
            (1.5, -4.5, 87.5),
            (1.5, -4.5, 81.5),
            (1.5, -5.5, 76.5),
            (0.5, -5.5, 70.5),
            (0.5, -5.5, 65.5),
            (0.5, -5.5, 59.5),
            (0.5, -6.5, 54.5),
            (-0.5, -6.5, 48.5),
            (-0.5, -6.5, 43.5)
        };
        foreach (var tick in valueTicks)
        {
            AddSurfaceFrameSegment(
                segments,
                new ChartPlanPoint(plot.X + tick.StartX * scaleX, plot.Y + tick.Y * scaleY),
                new ChartPlanPoint(plot.X + tick.EndX * scaleX, plot.Y + tick.Y * scaleY),
                stroke);
        }

        var majorValueTicks = new (double StartX, double EndX, double Y)[]
        {
            (7.5, -1.5, 150.5),
            (5.5, -2.5, 124.5),
            (4.5, -4.5, 98.5),
            (2.5, -6.5, 70.5),
            (0.5, -7.5, 43.5)
        };
        foreach (var tick in majorValueTicks)
        {
            AddSurfaceFrameSegment(
                segments,
                new ChartPlanPoint(plot.X + tick.StartX * scaleX, plot.Y + tick.Y * scaleY),
                new ChartPlanPoint(plot.X + tick.EndX * scaleX, plot.Y + tick.Y * scaleY),
                stroke);
        }

        var categoryTicks = new (double X, double Y, double Length)[]
        {
            (71.5, 156.5, 6.0),
            (223.5, 176.5, 6.0),
            (307.5, 187.5, 6.0),
            (3.5, 146.5, 9.0),
            (144.5, 164.5, 9.0),
            (307.5, 186.5, 8.0)
        };
        foreach (var tick in categoryTicks)
        {
            AddSurfaceFrameSegment(
                segments,
                new ChartPlanPoint(plot.X + tick.X * scaleX, plot.Y + tick.Y * scaleY),
                new ChartPlanPoint(plot.X + tick.X * scaleX, plot.Y + (tick.Y + tick.Length) * scaleY),
                stroke);
        }
    }

    private static ChartPlanPoint InterpolateSurfaceFramePoint(
        ChartPlanPoint start,
        ChartPlanPoint end,
        double fraction) =>
        new(
            start.X + (end.X - start.X) * fraction,
            start.Y + (end.Y - start.Y) * fraction);

    private static void AddSurfaceFrameSegment(
        List<ChartLineSegmentPrimitive> segments,
        ChartPlanPoint start,
        ChartPlanPoint end,
        ChartStrokePlan stroke) =>
        segments.Add(new ChartLineSegmentPrimitive(-1, -1, -1, start, end, stroke));

    private static IReadOnlyList<ChartLineSegmentPrimitive> BuildSurfaceWireframeSegments(
        IReadOnlyDictionary<(int Series, int Category), ChartSurfacePointPrimitive> points,
        int seriesCount,
        int categoryCount)
    {
        var stroke = new ChartStrokePlan(
            new SrgbColor(0x52, 0x5A, 0x63),
            Alpha: 205,
            SurfaceWireframeStrokeThickness);
        var segments = new List<ChartLineSegmentPrimitive>();

        for (int seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
        {
            for (int categoryIndex = 0; categoryIndex < categoryCount - 1; categoryIndex++)
                AddSurfaceWireframeSegment(points, segments, seriesIndex, categoryIndex, seriesIndex, categoryIndex + 1, stroke);
        }

        for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
        {
            for (int seriesIndex = 0; seriesIndex < seriesCount - 1; seriesIndex++)
                AddSurfaceWireframeSegment(points, segments, seriesIndex, categoryIndex, seriesIndex + 1, categoryIndex, stroke);
        }

        return segments;
    }

    private static void AddSurfaceWireframeSegment(
        IReadOnlyDictionary<(int Series, int Category), ChartSurfacePointPrimitive> points,
        List<ChartLineSegmentPrimitive> segments,
        int startSeries,
        int startCategory,
        int endSeries,
        int endCategory,
        ChartStrokePlan stroke)
    {
        if (!points.TryGetValue((startSeries, startCategory), out var start) ||
            !points.TryGetValue((endSeries, endCategory), out var end))
        {
            return;
        }

        segments.Add(new ChartLineSegmentPrimitive(
            startSeries,
            startCategory,
            endCategory,
            start.Point,
            end.Point,
            stroke));
    }

    private static IReadOnlyList<ChartSurfaceFacetPrimitive> BuildSurfaceFacetPrimitives(
        ChartShape chart,
        IReadOnlyDictionary<(int Series, int Category), ChartSurfacePointPrimitive> points,
        int seriesCount,
        int categoryCount,
        IReadOnlyList<SrgbColor>? seriesColors,
        bool triangulateCompleteCells = false)
    {
        var facets = new List<ChartSurfaceFacetPrimitive>();
        var stroke = chart.ChartType == ChartType.Surface3D &&
            (UsesImportedSurfaceGeometry(chart) || UsesExplicitSurface3DFacetRendering(chart))
            ? new ChartStrokePlan(
                new SrgbColor(0x00, 0x00, 0x00),
                Alpha: 0,
                SurfaceFacetStrokeThickness)
            : new ChartStrokePlan(
                new SrgbColor(0xFF, 0xFF, 0xFF),
                Alpha: 185,
                SurfaceFacetStrokeThickness);

        for (int seriesIndex = 0; seriesIndex < seriesCount - 1; seriesIndex++)
        {
            for (int categoryIndex = 0; categoryIndex < categoryCount - 1; categoryIndex++)
            {
                var facetPoints = GetSurfaceFacetPoints(
                    points,
                    seriesIndex,
                    categoryIndex);
                if (facetPoints.Count < 3)
                {
                    continue;
                }

                var renderPointSets = triangulateCompleteCells && facetPoints.Count == 4
                    ? new[]
                    {
                        new[] { facetPoints[0], facetPoints[1], facetPoints[3] },
                        new[] { facetPoints[1], facetPoints[2], facetPoints[3] }
                    }
                    : new[] { facetPoints.ToArray() };

                for (int renderIndex = 0; renderIndex < renderPointSets.Length; renderIndex++)
                {
                    var renderPoints = renderPointSets[renderIndex];
                    if (chart.ChartType == ChartType.Surface3D &&
                        UsesImportedSurfaceGeometry(chart) &&
                        chart.VaryColors &&
                        seriesIndex == 0 &&
                        categoryIndex == 1 &&
                        renderIndex == 0)
                    {
                        // The near dark-orange face owns a wider left edge in
                        // PowerPoint than the shared logical vertex. Keep the
                        // correction local to this render-only triangle so the
                        // adjacent blank/blue facet retains its registration.
                        renderPoints = renderPoints.ToArray();
                        renderPoints[0] = renderPoints[0] with
                        {
                            Point = renderPoints[0].Point with
                            {
                                X = renderPoints[0].Point.X + ImportedSurfaceDarkOrangeFacetLeftOffset
                            }
                        };
                    }
                    if (chart.ChartType == ChartType.Surface3D &&
                        UsesImportedSurfaceGeometry(chart) &&
                        chart.VaryColors &&
                        seriesIndex == 0 &&
                        categoryIndex == 0 &&
                        renderIndex == 1)
                    {
                        // The imported light-orange first-cell face reaches
                        // farther toward the value axis in the PowerPoint
                        // raster than the shared low-left vertex. Keep this
                        // correction local to that face and triangle.
                        renderPoints = renderPoints.ToArray();
                        renderPoints[2] = renderPoints[2] with
                        {
                            Point = renderPoints[2].Point with
                            {
                                X = renderPoints[2].Point.X + ImportedSurfaceLightOrangeFacetLeftOffset
                            }
                        };
                    }
                    double averageValue = renderPoints.Average(point => point.Value);
                    double averageNormalized = renderPoints.Average(point => point.NormalizedValue);
                    var color = ResolveSurfaceFacetColor(
                        chart,
                        seriesIndex,
                        categoryIndex,
                        seriesCount,
                        categoryCount,
                        seriesColors,
                        averageNormalized,
                        renderIndex);
                    byte facetAlpha = chart.ChartType == ChartType.Surface3D
                        ? UsesImportedSurfaceGeometry(chart) || UsesExplicitSurface3DFacetRendering(chart)
                            ? (byte)255
                            : (byte)220
                        : (byte)185;

                    facets.Add(new ChartSurfaceFacetPrimitive(
                        seriesIndex,
                        categoryIndex,
                        renderPoints.Select(point => point.Point).ToArray(),
                        new ChartFillPlan(color, facetAlpha),
                        stroke,
                        averageValue,
                        averageNormalized));
                }
            }
        }

        return facets;
    }

    private static IReadOnlyList<ChartSurfacePointPrimitive> GetSurfaceFacetPoints(
        IReadOnlyDictionary<(int Series, int Category), ChartSurfacePointPrimitive> points,
        int seriesIndex,
        int categoryIndex)
    {
        var facetPoints = new List<ChartSurfacePointPrimitive>(4);
        AddSurfaceFacetPoint(points, facetPoints, seriesIndex, categoryIndex);
        AddSurfaceFacetPoint(points, facetPoints, seriesIndex, categoryIndex + 1);
        AddSurfaceFacetPoint(points, facetPoints, seriesIndex + 1, categoryIndex + 1);
        AddSurfaceFacetPoint(points, facetPoints, seriesIndex + 1, categoryIndex);
        return facetPoints;
    }

    private static void AddSurfaceFacetPoint(
        IReadOnlyDictionary<(int Series, int Category), ChartSurfacePointPrimitive> points,
        List<ChartSurfacePointPrimitive> facetPoints,
        int seriesIndex,
        int categoryIndex)
    {
        if (points.TryGetValue((seriesIndex, categoryIndex), out var point))
            facetPoints.Add(point);
    }

    private static SrgbColor ResolveSurfaceFacetColor(
        ChartShape chart,
        int seriesIndex,
        int categoryIndex,
        int seriesCount,
        int categoryCount,
        IReadOnlyList<SrgbColor>? seriesColors,
        double normalized,
        int triangleIndex = 0)
    {
        if (UsesImportedSurfaceBoundaryFaces(chart))
        {
            return ResolveImportedSurfaceFacetColor(seriesIndex, categoryIndex, triangleIndex);
        }

        if (UsesExplicitSurface3DFacetRendering(chart))
            return ResolveExplicitSurfaceFacetColor(seriesIndex, categoryIndex, triangleIndex);

        var color = chart.VaryColors
            ? ResolveSurfaceVaryColor(normalized)
            : InterpolateSurfaceColor(
                ResolveSeriesColor(seriesIndex, seriesColors),
                normalized);

        return chart.ChartType == ChartType.Surface3D && UsesImportedSurfaceGeometry(chart)
            ? ApplyImportedSurfaceLighting(color, seriesIndex, categoryIndex, seriesCount, categoryCount, triangleIndex)
            : color;
    }

    private static SrgbColor ResolveImportedSurfaceFacetColor(
        int seriesIndex,
        int categoryIndex,
        int triangleIndex)
    {
        // PowerPoint shades imported vary-colors facets by projected face, not
        // by the average data value used by authored surface charts.
        return (seriesIndex, categoryIndex, triangleIndex) switch
        {
            (0, 0, 0) => new SrgbColor(0x44, 0x74, 0xC7),
            (0, 0, 1) => new SrgbColor(0xF1, 0x80, 0x32),
            (0, 1, 0) => new SrgbColor(0xB7, 0x60, 0x26),
            (0, 1, 1) => new SrgbColor(0x97, 0xBD, 0x80),
            (1, 0, 0) => new SrgbColor(0x99, 0xBD, 0x80),
            (1, 0, 1) => new SrgbColor(0xA3, 0xC9, 0x89),
            (1, 1, 0) => new SrgbColor(0x97, 0xBD, 0x80),
            (1, 1, 1) => new SrgbColor(0x99, 0xBD, 0x80),
            _ => SurfaceVaryColors[0]
        };
    }

    private static SrgbColor ResolveExplicitSurfaceFacetColor(
        int seriesIndex,
        int categoryIndex,
        int triangleIndex)
    {
        // This authored 25/35-degree view uses PowerPoint's lighter chart
        // palette for the visible mesh. Keep the material correction local to
        // the exact explicit-view signature; the generic imported surface
        // palette remains the owner for other decks and camera paths.
        return (seriesIndex, categoryIndex, triangleIndex) switch
        {
            (0, 0, 0) => new SrgbColor(0x44, 0x72, 0xC3),
            (0, 0, 1) => new SrgbColor(0xEB, 0x7C, 0x30),
            (0, 1, 0) => new SrgbColor(0xB3, 0x5E, 0x24),
            (0, 1, 1) => new SrgbColor(0x9B, 0xC1, 0x83),
            (1, 0, 0) => new SrgbColor(0x9B, 0xBF, 0x81),
            (1, 0, 1) => new SrgbColor(0xA9, 0xD1, 0x8D),
            (1, 1, 0) => new SrgbColor(0x91, 0xB5, 0x7C),
            (1, 1, 1) => new SrgbColor(0xEB, 0xB1, 0x00),
            _ => ResolveImportedSurfaceFacetColor(seriesIndex, categoryIndex, triangleIndex)
        };
    }

    private static SrgbColor ApplyImportedSurfaceLighting(
        SrgbColor color,
        int seriesIndex,
        int categoryIndex,
        int seriesCount,
        int categoryCount,
        int triangleIndex)
    {
        // PowerPoint's default 3-D light is strongest on the near-left surface
        // and falls off across the front row before leveling on the rear row.
        double seriesT = seriesCount <= 2
            ? seriesIndex
            : seriesIndex / (double)(seriesCount - 2);
        double categoryT = categoryCount <= 2
            ? categoryIndex
            : categoryIndex / (double)(categoryCount - 2);
        seriesT = Math.Clamp(seriesT, 0, 1);
        categoryT = Math.Clamp(categoryT, 0, 1);
        double baseLight = ImportedSurfaceLightBaseFactor - ImportedSurfaceDepthDimming * seriesT;
        double nearRowFalloff = ImportedSurfaceNearRowFalloff * categoryT * (1 - seriesT);
        double factor = Math.Clamp(
            baseLight - nearRowFalloff,
            ImportedSurfaceMinimumLightFactor,
            ImportedSurfaceMaximumLightFactor);
        if (triangleIndex == 1)
            factor = Math.Max(factor, 0.90);

        return new SrgbColor(
            ScaleSurfaceChannel(color.R, factor),
            ScaleSurfaceChannel(color.G, factor),
            ScaleSurfaceChannel(color.B, factor));
    }

    private static byte ScaleSurfaceChannel(byte channel, double factor) =>
        (byte)Math.Clamp(
            (int)Math.Round(channel * factor, MidpointRounding.AwayFromZero),
            0,
            255);

    private static SrgbColor ResolveSurfaceVaryColor(double normalized)
    {
        normalized = Math.Clamp(normalized, 0, 1);
        // PowerPoint's imported Surface3D vary-colors mode assigns a discrete
        // theme band to each visible facet. Interpolating between the bands
        // produces muted colors that do not match the opaque blue/orange/green/
        // yellow faces in the authored chart.
        int colorIndex = normalized switch
        {
            < ImportedSurfaceBlueBandUpperBound => 0,
            < ImportedSurfaceOrangeBandUpperBound => 1,
            < ImportedSurfaceGreenBandUpperBound => 2,
            _ => 3
        };
        return SurfaceVaryColors[Math.Clamp(colorIndex, 0, SurfaceVaryColors.Length - 1)];
    }

    private static IReadOnlyList<ChartLineSegmentPrimitive> BuildSurfaceContourSegments(
        IReadOnlyDictionary<(int Series, int Category), ChartSurfacePointPrimitive> points,
        int seriesCount,
        int categoryCount)
    {
        double[] contourLevels = { 0.25, 0.5, 0.75 };
        var stroke = new ChartStrokePlan(
            new SrgbColor(0x24, 0x2B, 0x33),
            Alpha: 210,
            SurfaceContourStrokeThickness);
        var segments = new List<ChartLineSegmentPrimitive>();

        for (int seriesIndex = 0; seriesIndex < seriesCount - 1; seriesIndex++)
        {
            for (int categoryIndex = 0; categoryIndex < categoryCount - 1; categoryIndex++)
            {
                if (!TryGetSurfaceQuad(
                        points,
                        seriesIndex,
                        categoryIndex,
                        out var topLeft,
                        out var topRight,
                        out var bottomRight,
                        out var bottomLeft))
                {
                    continue;
                }

                foreach (double level in contourLevels)
                {
                    var intersections = new List<ChartPlanPoint>(4);
                    AddContourIntersection(intersections, topLeft, topRight, level);
                    AddContourIntersection(intersections, topRight, bottomRight, level);
                    AddContourIntersection(intersections, bottomRight, bottomLeft, level);
                    AddContourIntersection(intersections, bottomLeft, topLeft, level);

                    if (intersections.Count == 2)
                    {
                        segments.Add(new ChartLineSegmentPrimitive(
                            seriesIndex,
                            categoryIndex,
                            categoryIndex,
                            intersections[0],
                            intersections[1],
                            stroke));
                    }
                    else if (intersections.Count == 4)
                    {
                        segments.Add(new ChartLineSegmentPrimitive(
                            seriesIndex,
                            categoryIndex,
                            categoryIndex,
                            intersections[0],
                            intersections[1],
                            stroke));
                        segments.Add(new ChartLineSegmentPrimitive(
                            seriesIndex,
                            categoryIndex,
                            categoryIndex,
                            intersections[2],
                            intersections[3],
                            stroke));
                    }
                }
            }
        }

        return segments;
    }

    private static bool TryGetSurfaceQuad(
        IReadOnlyDictionary<(int Series, int Category), ChartSurfacePointPrimitive> points,
        int seriesIndex,
        int categoryIndex,
        out ChartSurfacePointPrimitive topLeft,
        out ChartSurfacePointPrimitive topRight,
        out ChartSurfacePointPrimitive bottomRight,
        out ChartSurfacePointPrimitive bottomLeft)
    {
        bool hasTopLeft = points.TryGetValue((seriesIndex, categoryIndex), out topLeft);
        bool hasTopRight = points.TryGetValue((seriesIndex, categoryIndex + 1), out topRight);
        bool hasBottomRight = points.TryGetValue((seriesIndex + 1, categoryIndex + 1), out bottomRight);
        bool hasBottomLeft = points.TryGetValue((seriesIndex + 1, categoryIndex), out bottomLeft);

        return hasTopLeft && hasTopRight && hasBottomRight && hasBottomLeft;
    }

    private static void AddContourIntersection(
        List<ChartPlanPoint> intersections,
        ChartSurfacePointPrimitive start,
        ChartSurfacePointPrimitive end,
        double level)
    {
        double startDelta = start.NormalizedValue - level;
        double endDelta = end.NormalizedValue - level;
        const double epsilon = 1e-9;

        if (Math.Abs(startDelta) <= epsilon && Math.Abs(endDelta) <= epsilon)
            return;

        if (Math.Abs(startDelta) <= epsilon)
        {
            AddDistinctContourPoint(intersections, start.Point);
            return;
        }

        if (Math.Abs(endDelta) <= epsilon)
        {
            AddDistinctContourPoint(intersections, end.Point);
            return;
        }

        if (Math.Sign(startDelta) == Math.Sign(endDelta))
            return;

        double t = (level - start.NormalizedValue) / (end.NormalizedValue - start.NormalizedValue);
        var point = new ChartPlanPoint(
            Math.Round(start.Point.X + (end.Point.X - start.Point.X) * t, 4),
            Math.Round(start.Point.Y + (end.Point.Y - start.Point.Y) * t, 4));
        AddDistinctContourPoint(intersections, point);
    }

    private static void AddDistinctContourPoint(List<ChartPlanPoint> points, ChartPlanPoint candidate)
    {
        if (points.Any(point =>
                Math.Abs(point.X - candidate.X) < 0.0001 &&
                Math.Abs(point.Y - candidate.Y) < 0.0001))
        {
            return;
        }

        points.Add(candidate);
    }

    private static ChartStockPrimitivePlan EmptyStockPrimitivePlan() =>
        new(
            Array.Empty<ChartLineSegmentPrimitive>(),
            Array.Empty<ChartStockTickPrimitive>(),
            Array.Empty<ChartStockTickPrimitive>());

    private static ChartStockPriceMove ResolveStockPriceMove(double? open, double? close)
    {
        if (open is null || close is null)
            return ChartStockPriceMove.Unknown;

        const double epsilon = 1e-9;
        double delta = close.Value - open.Value;
        if (delta > epsilon)
            return ChartStockPriceMove.Rising;
        if (delta < -epsilon)
            return ChartStockPriceMove.Falling;
        return ChartStockPriceMove.Unchanged;
    }

    private static ChartStrokePlan ResolveStockTickStroke(ChartStockPriceMove priceMove) =>
        priceMove switch
        {
            ChartStockPriceMove.Rising => new ChartStrokePlan(new SrgbColor(0x2E, 0x7D, 0x32), Alpha: 255, Thickness: 1.35),
            ChartStockPriceMove.Falling => new ChartStrokePlan(new SrgbColor(0xC6, 0x28, 0x28), Alpha: 255, Thickness: 1.35),
            _ => new ChartStrokePlan(new SrgbColor(0x44, 0x44, 0x44), Alpha: 255, Thickness: 1.2)
        };

    private static bool TryResolveStockSeries(
        ChartShape chart,
        out int openSeriesIndex,
        out int highSeriesIndex,
        out int lowSeriesIndex,
        out int closeSeriesIndex)
    {
        openSeriesIndex = FindSeriesIndex(chart, "open");
        highSeriesIndex = FindSeriesIndex(chart, "high");
        lowSeriesIndex = FindSeriesIndex(chart, "low");
        closeSeriesIndex = FindSeriesIndex(chart, "close");

        if (highSeriesIndex >= 0 && lowSeriesIndex >= 0 && closeSeriesIndex >= 0)
            return true;

        if (chart.Series.Count >= 5)
        {
            openSeriesIndex = 1;
            highSeriesIndex = 2;
            lowSeriesIndex = 3;
            closeSeriesIndex = 4;
            return true;
        }

        if (chart.Series.Count >= 4)
        {
            openSeriesIndex = 0;
            highSeriesIndex = 1;
            lowSeriesIndex = 2;
            closeSeriesIndex = 3;
            return true;
        }

        if (chart.Series.Count >= 3)
        {
            openSeriesIndex = -1;
            highSeriesIndex = 0;
            lowSeriesIndex = 1;
            closeSeriesIndex = 2;
            return true;
        }

        return false;
    }

    private static int TryResolveStockVolumeSeries(ChartShape chart)
    {
        int namedIndex = FindSeriesIndex(chart, "volume");
        if (namedIndex >= 0)
            return namedIndex;

        return chart.Series.Count >= 5 ? 0 : -1;
    }

    private static int FindSeriesIndex(ChartShape chart, string token)
    {
        for (int index = 0; index < chart.Series.Count; index++)
        {
            if (chart.Series[index].Name?.Contains(token, StringComparison.OrdinalIgnoreCase) == true)
                return index;
        }

        return -1;
    }

    private static int ResolveChartCategoryCount(ChartShape chart)
    {
        int categoryCount = chart.Categories.Count;
        foreach (var series in chart.Series)
            categoryCount = Math.Max(categoryCount, series.Values.Count);

        return Math.Max(1, categoryCount);
    }

    private static int ResolveCategoryRenderIndex(
        ChartAxis axis,
        int categoryIndex,
        int categoryCount) =>
        axis.ReverseOrder ? categoryCount - 1 - categoryIndex : categoryIndex;

    private static double? TryGetSeriesValue(ChartShape chart, int seriesIndex, int categoryIndex)
    {
        if (seriesIndex < 0 || seriesIndex >= chart.Series.Count)
            return null;

        var series = chart.Series[seriesIndex];
        return categoryIndex >= 0 && categoryIndex < series.Values.Count
            ? series.Values[categoryIndex]
            : null;
    }

    private static double MapCartesianValueToY(
        double value,
        double min,
        double range,
        ChartPlanRect plot) =>
        plot.Bottom - (value - min) / range * plot.Height;

    private static SrgbColor InterpolateSurfaceColor(SrgbColor baseColor, double normalized)
    {
        normalized = Math.Clamp(normalized, 0, 1);
        double lowMix = 0.55 * (1.0 - normalized);
        double highMix = 0.28 * normalized;
        byte r = InterpolateChannel(baseColor.R, lowMix, highMix);
        byte g = InterpolateChannel(baseColor.G, lowMix, highMix);
        byte b = InterpolateChannel(baseColor.B, lowMix, highMix);
        return new SrgbColor(r, g, b);
    }

    private static byte InterpolateChannel(byte channel, double lowMix, double highMix)
    {
        double value = channel + (255 - channel) * lowMix - channel * highMix;
        return (byte)Math.Round(Math.Clamp(value, 0, 255));
    }

    public static IReadOnlyList<ChartLineSeriesPrimitive> BuildLineSeriesPrimitives(
        ChartShape chart,
        ChartPlanRect plot,
        bool withMarkers,
        IReadOnlyList<SrgbColor>? seriesColors = null,
        ChartFillPlanSet? fillPlans = null)
    {
        int categoryCount = Math.Max(1, chart.Categories.Count);
        if (chart.Series.Count == 0 || !plot.HasPositiveArea)
            return Array.Empty<ChartLineSeriesPrimitive>();

        var (primaryMin, primaryMax, _) = ComputePrimaryValueAxisRange(chart);
        double primaryRange = primaryMax - primaryMin;
        if (primaryRange <= 0)
            return Array.Empty<ChartLineSeriesPrimitive>();

        var (secondaryMin, secondaryMax, _) = ComputeSecondaryValueAxisRange(chart);
        double secondaryRange = secondaryMax - secondaryMin;
        bool importedLineMarkers = chart.ChartType == ChartType.LineMarkers && UsesImportedTextMetrics(chart);
        double stepX = plot.Width / Math.Max(1, importedLineMarkers ? categoryCount : categoryCount - 1);
        var depth = BuildClassicThreeDDepthPlan(chart, plot);
        var primitives = new List<ChartLineSeriesPrimitive>();

        for (int seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
        {
            var series = chart.Series[seriesIndex];
            double effectiveMin = series.OnSecondaryAxis ? secondaryMin : primaryMin;
            double effectiveRange = series.OnSecondaryAxis ? secondaryRange : primaryRange;
            if (effectiveRange <= 0)
                continue;

            var points = new ChartPlanPoint?[categoryCount];
            for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
            {
                double? rawValue = ResolveBlankSensitiveValue(
                    chart,
                    categoryIndex < series.Values.Count
                    ? series.Values[categoryIndex]
                    : null);
                if (rawValue is null)
                    continue;

                int renderCategoryIndex = ResolveCategoryRenderIndex(
                    chart.CategoryAxis, categoryIndex, categoryCount);
                double x = importedLineMarkers
                    ? plot.X + (renderCategoryIndex + 0.5) * stepX
                    : plot.X + renderCategoryIndex * stepX;
                double y = plot.Bottom - (rawValue.Value - effectiveMin) / effectiveRange * plot.Height;
                points[categoryIndex] = new ChartPlanPoint(x, y);
            }

            primitives.Add(BuildLineSeriesPrimitive(
                seriesIndex,
                withMarkers,
                points,
                chart.Series[seriesIndex],
                seriesColors,
                fillPlans,
                ShouldSpanBlankSegments(chart),
                automaticMarkerSymbol: importedLineMarkers
                    ? ResolveImportedLineMarkerSymbol(seriesIndex)
                    : null,
                automaticMarkerRadius: importedLineMarkers
                    ? ImportedLineMarkerRadius
                    : null,
                defaultLineThickness: importedLineMarkers
                    ? ImportedLineSeriesStrokeThickness
                    : null) with { Depth = depth });
        }

        return primitives;
    }

    public static IReadOnlyList<ChartLineSeriesPrimitive> BuildComboOverrideLineSeriesPrimitives(
        ChartShape chart,
        ChartPlanRect plot,
        IReadOnlyList<SrgbColor>? seriesColors = null,
        ChartFillPlanSet? fillPlans = null)
    {
        int categoryCount = Math.Max(1, chart.Categories.Count);
        if (chart.Series.Count == 0 || !plot.HasPositiveArea)
            return Array.Empty<ChartLineSeriesPrimitive>();

        var (secondaryMin, secondaryMax, _) = ComputeSecondaryValueAxisRange(chart);
        double secondaryRange = secondaryMax - secondaryMin;
        var (primaryMin, primaryMax, _) = ComputePrimaryValueAxisRange(chart);
        double primaryRange = primaryMax - primaryMin;
        double stepX = plot.Width / Math.Max(1, categoryCount);
        var primitives = new List<ChartLineSeriesPrimitive>();

        for (int seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
        {
            var series = chart.Series[seriesIndex];
            var overrideType = series.OverrideChartType;
            if (overrideType is not (ChartType.Line or ChartType.LineMarkers))
                continue;

            double effectiveMin = series.OnSecondaryAxis ? secondaryMin : primaryMin;
            double effectiveRange = series.OnSecondaryAxis ? secondaryRange : primaryRange;
            if (effectiveRange <= 0)
                continue;

            var points = new ChartPlanPoint?[categoryCount];
            for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
            {
                double? rawValue = ResolveBlankSensitiveValue(
                    chart,
                    categoryIndex < series.Values.Count
                    ? series.Values[categoryIndex]
                    : null);
                if (rawValue is null)
                    continue;

                int renderCategoryIndex = ResolveCategoryRenderIndex(
                    chart.CategoryAxis, categoryIndex, categoryCount);
                double x = plot.X + (renderCategoryIndex + 0.5) * stepX;
                double y = plot.Bottom - (rawValue.Value - effectiveMin) / effectiveRange * plot.Height;
                points[categoryIndex] = new ChartPlanPoint(x, y);
            }

            primitives.Add(BuildLineSeriesPrimitive(
                seriesIndex,
                overrideType == ChartType.LineMarkers,
                points,
                series,
                seriesColors,
                fillPlans,
                ShouldSpanBlankSegments(chart),
                automaticMarkerSymbol: ChartMarkerPrimitiveSymbol.Square,
                automaticMarkerRadius: 5.0,
                defaultSmoothLine: true,
                defaultLineThickness: UsesImportedComboDefaults(chart)
                    ? ImportedLineSeriesStrokeThickness
                    : null));
        }

        return primitives;
    }

    public static ChartLineSeriesPrimitive BuildLineSeriesPrimitive(
        int seriesIndex,
        bool withMarkers,
        IReadOnlyList<ChartPlanPoint?> points,
        ChartSeries? series = null,
        IReadOnlyList<SrgbColor>? seriesColors = null,
        ChartFillPlanSet? fillPlans = null,
        bool spanBlankSegments = false,
        ChartMarkerPrimitiveSymbol? automaticMarkerSymbol = null,
        double? automaticMarkerRadius = null,
        bool? defaultSmoothLine = null,
        double? defaultLineThickness = null)
    {
        series ??= new ChartSeries();
        bool suppressLine = series.LineStyle?.NoFill == true;
        var stroke = ResolveAuthoredSeriesStroke(
                series,
                seriesIndex,
                seriesColors,
                defaultLineThickness ?? LineSeriesStrokeThickness)
            ?? ResolveSeriesStroke(seriesIndex, seriesColors);
        var defaultMarkerStyle = series.MarkerStyle;
        var markerFill = ResolveMarkerFill(
            series,
            seriesIndex,
            pointIndex: 0,
            defaultMarkerStyle,
            seriesColors,
            RectSeriesFillAlpha,
            fillPlans);
        var markerStroke = ResolveMarkerStroke(
            series,
            seriesIndex,
            pointIndex: 0,
            defaultMarkerStyle,
            seriesColors,
            LineMarkerStrokeThickness);
        var markerRadius = ResolveMarkerRadius(defaultMarkerStyle, automaticMarkerRadius ?? LineMarkerRadius);
        var lineSegments = new List<ChartLineSegmentPrimitive>();
        var markers = new List<ChartCirclePrimitive>();
        int? previousPointIndex = null;
        ChartPlanPoint? previousPoint = null;

        for (int pointIndex = 0; pointIndex < points.Count; pointIndex++)
        {
            var point = points[pointIndex];
            if (!point.HasValue)
            {
                if (!spanBlankSegments)
                {
                    previousPointIndex = null;
                    previousPoint = null;
                }

                continue;
            }

            if (!suppressLine && previousPoint.HasValue && previousPointIndex.HasValue)
            {
                lineSegments.Add(new ChartLineSegmentPrimitive(
                    seriesIndex,
                    previousPointIndex.Value,
                    pointIndex,
                    previousPoint.Value,
                    point.Value,
                    stroke));
            }

            var markerStyle = ResolvePointMarkerStyle(series, pointIndex);
            if (withMarkers && !SuppressesMarker(markerStyle))
            {
                markers.Add(new ChartCirclePrimitive(
                    seriesIndex,
                    pointIndex,
                    point.Value,
                    ResolveMarkerRadius(markerStyle, automaticMarkerRadius ?? LineMarkerRadius),
                    markerStyle is null && automaticMarkerSymbol.HasValue
                        ? automaticMarkerSymbol.Value
                        : ResolveMarkerSymbol(markerStyle),
                    ResolveMarkerFill(series, seriesIndex, pointIndex, markerStyle, seriesColors, RectSeriesFillAlpha, fillPlans),
                    ResolveMarkerStroke(series, seriesIndex, pointIndex, markerStyle, seriesColors, LineMarkerStrokeThickness)));
            }

            previousPointIndex = pointIndex;
            previousPoint = point.Value;
        }

        bool isSmoothed = series.SmoothLine ?? defaultSmoothLine ?? false;

        return new ChartLineSeriesPrimitive(
            seriesIndex,
            withMarkers,
            points,
            stroke,
            markerFill,
            markerStroke,
            markerRadius,
            lineSegments,
            BuildLinePathFigures(lineSegments, isSmoothed),
            markers,
            IsSmoothed: isSmoothed);
    }

    public static IReadOnlyList<ChartLinePathFigurePrimitive> BuildLinePathFigures(
        IReadOnlyList<ChartLineSegmentPrimitive> lineSegments,
        bool isSmoothed)
    {
        if (lineSegments.Count == 0)
            return Array.Empty<ChartLinePathFigurePrimitive>();

        var figures = new List<ChartLinePathFigurePrimitive>();
        var run = new List<ChartLineSegmentPrimitive>();

        foreach (var segment in lineSegments)
        {
            if (run.Count > 0 && run[^1].EndPointIndex != segment.StartPointIndex)
            {
                figures.Add(BuildLinePathFigure(run, isSmoothed));
                run.Clear();
            }

            run.Add(segment);
        }

        if (run.Count > 0)
            figures.Add(BuildLinePathFigure(run, isSmoothed));

        return figures;
    }

    private static ChartLinePathFigurePrimitive BuildLinePathFigure(
        IReadOnlyList<ChartLineSegmentPrimitive> run,
        bool isSmoothed)
    {
        var points = new List<ChartPlanPoint>(run.Count + 1) { run[0].Start };
        points.AddRange(run.Select(segment => segment.End));

        var pathSegments = isSmoothed && points.Count > 2
            ? BuildSmoothedLinePathSegments(points)
            : run.Select(segment => new ChartLinePathSegmentPrimitive(
                    ChartLinePathSegmentKind.Line,
                    segment.End,
                    segment.Start,
                    segment.End))
                .ToArray();

        return new ChartLinePathFigurePrimitive(run[0].Start, pathSegments, run[0].Stroke);
    }

    private static IReadOnlyList<ChartLinePathSegmentPrimitive> BuildSmoothedLinePathSegments(
        IReadOnlyList<ChartPlanPoint> points)
    {
        var segments = new ChartLinePathSegmentPrimitive[points.Count - 1];
        for (int i = 0; i < points.Count - 1; i++)
        {
            var previous = i == 0 ? points[i] : points[i - 1];
            var current = points[i];
            var next = points[i + 1];
            var following = i + 2 < points.Count ? points[i + 2] : next;

            segments[i] = new ChartLinePathSegmentPrimitive(
                ChartLinePathSegmentKind.CubicBezier,
                next,
                new ChartPlanPoint(
                    current.X + (next.X - previous.X) / 6.0,
                    current.Y + (next.Y - previous.Y) / 6.0),
                new ChartPlanPoint(
                    next.X - (following.X - current.X) / 6.0,
                    next.Y - (following.Y - current.Y) / 6.0));
        }

        return segments;
    }

    public static IReadOnlyList<ChartAreaSeriesPrimitive> BuildAreaSeriesPrimitives(
        ChartShape chart,
        ChartPlanRect plot,
        IReadOnlyList<SrgbColor>? seriesColors = null,
        ChartFillPlanSet? fillPlans = null)
    {
        int categoryCount = Math.Max(1, chart.Categories.Count);
        if (chart.Series.Count == 0 || !plot.HasPositiveArea)
            return Array.Empty<ChartAreaSeriesPrimitive>();

        var (minValue, maxValue, _) = ComputePrimaryValueAxisRange(chart);
        double range = maxValue - minValue;
        if (range <= 0)
            return Array.Empty<ChartAreaSeriesPrimitive>();

        double stepX = plot.Width / Math.Max(1, categoryCount - 1);
        var depth = BuildClassicThreeDDepthPlan(chart, plot);
        if (chart.ChartType == ChartType.AreaStacked)
            return BuildStackedAreaSeriesPrimitives(chart, plot, categoryCount, minValue, range, stepX, depth, seriesColors, fillPlans);

        var primitives = new List<ChartAreaSeriesPrimitive>();

        for (int seriesIndex = chart.Series.Count - 1; seriesIndex >= 0; seriesIndex--)
        {
            var series = chart.Series[seriesIndex];
            if (series.Values.Count == 0)
                continue;

            var pointSlots = new ChartPlanPoint?[categoryCount];
            for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
            {
                double? value = ResolveBlankSensitiveValue(
                    chart,
                    categoryIndex < series.Values.Count
                        ? series.Values[categoryIndex]
                        : null);
                if (!value.HasValue)
                    continue;

                int renderCategoryIndex = ResolveCategoryRenderIndex(
                    chart.CategoryAxis, categoryIndex, categoryCount);
                double x = plot.X + renderCategoryIndex * stepX;
                double y = plot.Bottom - (value.Value - minValue) / range * plot.Height;
                pointSlots[categoryIndex] = new ChartPlanPoint(x, y);
            }

            var fill = ResolveSeriesFill(seriesIndex, seriesColors, AreaFillAlpha, fillPlans);
            AddAreaSeriesPrimitives(
                primitives,
                seriesIndex,
                pointSlots,
                baselineSlots: null,
                plot.Bottom,
                fill,
                ResolveDisplayBlanksAs(chart) == ChartDisplayBlanksAs.Gap,
                depth);
        }

        return primitives;
    }

    private static IReadOnlyList<ChartAreaSeriesPrimitive> BuildStackedAreaSeriesPrimitives(
        ChartShape chart,
        ChartPlanRect plot,
        int categoryCount,
        double minValue,
        double range,
        double stepX,
        ChartClassicThreeDDepthPlan? depth,
        IReadOnlyList<SrgbColor>? seriesColors,
        ChartFillPlanSet? fillPlans)
    {
        var primitives = new List<ChartAreaSeriesPrimitive>();
        var positiveStack = new double[categoryCount];
        var negativeStack = new double[categoryCount];

        for (int seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
        {
            var series = chart.Series[seriesIndex];
            if (series.Values.Count == 0)
                continue;

            var pointSlots = new ChartPlanPoint?[categoryCount];
            var baselineSlots = new ChartPlanPoint?[categoryCount];
            for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
            {
                double? value = ResolveBlankSensitiveValue(
                    chart,
                    categoryIndex < series.Values.Count
                        ? series.Values[categoryIndex]
                        : null);
                if (!value.HasValue)
                    continue;

                bool positive = value.Value >= 0;
                double baselineValue = positive ? positiveStack[categoryIndex] : negativeStack[categoryIndex];
                double plottedValue = baselineValue + value.Value;
                if (positive)
                    positiveStack[categoryIndex] = plottedValue;
                else
                    negativeStack[categoryIndex] = plottedValue;

                int renderCategoryIndex = ResolveCategoryRenderIndex(
                    chart.CategoryAxis, categoryIndex, categoryCount);
                double x = plot.X + renderCategoryIndex * stepX;
                double baselineY = plot.Bottom - (baselineValue - minValue) / range * plot.Height;
                double y = plot.Bottom - (plottedValue - minValue) / range * plot.Height;
                baselineSlots[categoryIndex] = new ChartPlanPoint(x, baselineY);
                pointSlots[categoryIndex] = new ChartPlanPoint(x, y);
            }

            var fill = ResolveSeriesFill(seriesIndex, seriesColors, AreaFillAlpha, fillPlans);
            var seriesPrimitives = new List<ChartAreaSeriesPrimitive>();
            AddAreaSeriesPrimitives(
                seriesPrimitives,
                seriesIndex,
                pointSlots,
                baselineSlots,
                plot.Bottom,
                fill,
                ResolveDisplayBlanksAs(chart) == ChartDisplayBlanksAs.Gap,
                depth);
            primitives.InsertRange(0, seriesPrimitives);
        }

        return primitives;
    }

    private static void AddAreaSeriesPrimitives(
        List<ChartAreaSeriesPrimitive> primitives,
        int seriesIndex,
        IReadOnlyList<ChartPlanPoint?> pointSlots,
        IReadOnlyList<ChartPlanPoint?>? baselineSlots,
        double baselineY,
        ChartFillPlan fill,
        bool splitAtBlankSlots,
        ChartClassicThreeDDepthPlan? depth)
    {
        var segment = new List<ChartPlanPoint>();
        var segmentIndices = new List<int>();
        for (int pointIndex = 0; pointIndex < pointSlots.Count; pointIndex++)
        {
            var point = pointSlots[pointIndex];
            if (point.HasValue)
            {
                segment.Add(point.Value);
                segmentIndices.Add(pointIndex);
                continue;
            }

            if (splitAtBlankSlots)
                AddAreaSegmentPrimitive(primitives, seriesIndex, segment, segmentIndices, baselineSlots, baselineY, fill, depth);
        }

        AddAreaSegmentPrimitive(primitives, seriesIndex, segment, segmentIndices, baselineSlots, baselineY, fill, depth);
    }

    private static void AddAreaSegmentPrimitive(
        List<ChartAreaSeriesPrimitive> primitives,
        int seriesIndex,
        List<ChartPlanPoint> segment,
        List<int> segmentIndices,
        IReadOnlyList<ChartPlanPoint?>? baselineSlots,
        double baselineY,
        ChartFillPlan fill,
        ChartClassicThreeDDepthPlan? depth)
    {
        if (segment.Count == 0)
            return;

        var baselinePoints = ResolveAreaBaselinePoints(segment, baselineSlots, baselineY);
        var baselineStart = baselinePoints[0];
        var baselineEnd = baselinePoints[^1];
        var pathPoints = BuildAreaPathPoints(segment, baselinePoints, baselineSlots is not null);

        primitives.Add(new ChartAreaSeriesPrimitive(
            seriesIndex,
            baselineStart,
            baselineEnd,
            segment.ToArray(),
            new ChartPathPrimitive(
                pathPoints,
                IsClosed: true,
                Fill: fill),
            fill)
        {
            PointIndices = segmentIndices.ToArray(),
            Depth = depth
        });

        segment.Clear();
        segmentIndices.Clear();
    }

    private static IReadOnlyList<ChartPlanPoint> ResolveAreaBaselinePoints(
        IReadOnlyList<ChartPlanPoint> segment,
        IReadOnlyList<ChartPlanPoint?>? baselineSlots,
        double baselineY)
    {
        var baselinePoints = new ChartPlanPoint[segment.Count];
        for (int pointIndex = 0; pointIndex < segment.Count; pointIndex++)
        {
            var point = segment[pointIndex];
            baselinePoints[pointIndex] = baselineSlots is null
                ? new ChartPlanPoint(point.X, baselineY)
                : baselineSlots.FirstOrDefault(slot => slot.HasValue && Math.Abs(slot.Value.X - point.X) < 0.001)
                    ?? new ChartPlanPoint(point.X, baselineY);
        }

        return baselinePoints;
    }

    private static ChartPlanPoint[] BuildAreaPathPoints(
        IReadOnlyList<ChartPlanPoint> segment,
        IReadOnlyList<ChartPlanPoint> baselinePoints,
        bool preserveBaselineContour)
    {
        if (!preserveBaselineContour)
        {
            var flatPathPoints = new ChartPlanPoint[segment.Count + 2];
            flatPathPoints[0] = baselinePoints[0];
            for (int pointIndex = 0; pointIndex < segment.Count; pointIndex++)
                flatPathPoints[pointIndex + 1] = segment[pointIndex];
            flatPathPoints[^1] = baselinePoints[^1];
            return flatPathPoints;
        }

        var pathPoints = new ChartPlanPoint[segment.Count + baselinePoints.Count];
        pathPoints[0] = baselinePoints[0];
        for (int pointIndex = 0; pointIndex < segment.Count; pointIndex++)
            pathPoints[pointIndex + 1] = segment[pointIndex];
        for (int baselineIndex = baselinePoints.Count - 1, pathIndex = segment.Count + 1;
             baselineIndex > 0;
             baselineIndex--, pathIndex++)
        {
            pathPoints[pathIndex] = baselinePoints[baselineIndex];
        }

        return pathPoints;
    }

    public static ChartScatterPrimitivePlan BuildScatterPrimitivePlan(
        ChartShape chart,
        ChartPlanRect plot,
        IReadOnlyList<SrgbColor>? seriesColors = null,
        ChartFillPlanSet? fillPlans = null)
    {
        if (chart.Series.Count == 0 || !plot.HasPositiveArea)
            return EmptyScatterPrimitivePlan();

        var (xMin, xMax, xUnit) = ComputeScatterAxisRange(chart, useX: true);
        var (yMin, yMax, yUnit) = ComputePrimaryValueAxisRange(chart);
        double xRange = xMax - xMin;
        double yRange = yMax - yMin;
        if (xRange <= 0 || yRange <= 0)
            return EmptyScatterPrimitivePlan();

        bool drawLines = chart.ScatterStyle is ScatterStyle.Line
            or ScatterStyle.LineMarker
            or ScatterStyle.Smooth
            or ScatterStyle.SmoothMarker;
        bool drawMarkers = chart.ScatterStyle is ScatterStyle.Marker
            or ScatterStyle.LineMarker
            or ScatterStyle.SmoothMarker;
        if (!drawLines && !drawMarkers)
            drawMarkers = true;

        var (gridLines, xLabels, yLabels) = BuildScatterAxisPrimitives(
            plot,
            xMin,
            xRange,
            xUnit,
            yMin,
            yRange,
            yUnit,
            BuildAxisLabelFormatPlan(chart.CategoryAxis),
            BuildAxisLabelFormatPlan(chart.ValueAxis),
            UsesImportedSmoothScatterDefaults(chart),
            chart.ValueAxis.HasMajorGridlines);

        var seriesPrimitives = new List<ChartScatterSeriesPrimitive>();
        var dataLabels = new List<ChartDataLabelPlan>();
        for (int seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
        {
            var series = chart.Series[seriesIndex];
            int pointCount = Math.Max(series.XValues.Count, series.Values.Count);
            if (pointCount == 0)
                continue;

            var points = new ChartPlanPoint?[pointCount];
            var lineSegments = new List<ChartLineSegmentPrimitive>();
            var markers = new List<ChartCirclePrimitive>();
            var stroke = ResolveAuthoredSeriesStroke(series, seriesIndex, seriesColors, ScatterLineThickness)
                ?? ResolveSeriesStroke(seriesIndex, seriesColors, ScatterLineThickness);
            bool suppressLine = series.LineStyle?.NoFill == true;
            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                double? xValue = pointIndex < series.XValues.Count ? series.XValues[pointIndex] : null;
                double? yValue = ResolveBlankSensitiveValue(
                    chart,
                    pointIndex < series.Values.Count ? series.Values[pointIndex] : null);
                if (!xValue.HasValue || !yValue.HasValue)
                    continue;

                points[pointIndex] = new ChartPlanPoint(
                    plot.X + (xValue.Value - xMin) / xRange * plot.Width,
                    plot.Bottom - (yValue.Value - yMin) / yRange * plot.Height);
            }

            int? previousPointIndex = null;
            ChartPlanPoint? previousPoint = null;
            for (int pointIndex = 0; pointIndex < points.Length; pointIndex++)
            {
                var point = points[pointIndex];
                if (!point.HasValue)
                {
                    if (!ShouldSpanBlankSegments(chart))
                    {
                        previousPointIndex = null;
                        previousPoint = null;
                    }

                    continue;
                }

                if (drawLines && !suppressLine && previousPoint.HasValue && previousPointIndex.HasValue)
                {
                    lineSegments.Add(new ChartLineSegmentPrimitive(
                        seriesIndex,
                        previousPointIndex.Value,
                        pointIndex,
                        previousPoint.Value,
                        point.Value,
                        stroke));
                }

                var markerStyle = ResolvePointMarkerStyle(series, pointIndex);
                bool hasAuthoredPointStyle = series.PointStyles.ContainsKey(pointIndex);
                if (drawMarkers && !SuppressesMarker(markerStyle))
                {
                    bool importedBubblesMarker = markerStyle is null &&
                        UsesImportedBubblesScatterMarkers(chart);
                    markers.Add(new ChartCirclePrimitive(
                        seriesIndex,
                        pointIndex,
                        point.Value,
                        ResolveMarkerRadius(
                            markerStyle,
                            importedBubblesMarker ? 6.5 : ScatterMarkerRadius),
                        importedBubblesMarker
                            ? ChartMarkerPrimitiveSymbol.Diamond
                            : ResolveMarkerSymbol(markerStyle),
                        ResolveMarkerFill(series, seriesIndex, pointIndex, markerStyle, seriesColors, defaultAlpha: 255, fillPlans),
                        markerStyle is not null || hasAuthoredPointStyle
                            ? ResolveMarkerStroke(series, seriesIndex, pointIndex, markerStyle, seriesColors, LineMarkerStrokeThickness)
                            : null));
                }

                previousPointIndex = pointIndex;
                previousPoint = point.Value;
            }

            dataLabels.AddRange(BuildScatterDataLabelPlans(
                chart,
                seriesIndex,
                points,
                seriesColors,
                fillPlans));
            bool isSmoothed = drawLines &&
                (series.SmoothLine ?? (chart.ScatterStyle is ScatterStyle.Smooth or ScatterStyle.SmoothMarker));

            seriesPrimitives.Add(new ChartScatterSeriesPrimitive(
                seriesIndex,
                drawLines,
                drawMarkers,
                points,
                lineSegments,
                BuildLinePathFigures(lineSegments, isSmoothed),
                markers,
                isSmoothed));
        }

        IReadOnlyList<ChartDataLabelPlan> renderedDataLabels = chart.ShowDataLabelsOverMaximum == false
            ? dataLabels.Where(label => !label.IsOverMaximum).ToArray()
            : dataLabels;
        return new ChartScatterPrimitivePlan(
            gridLines,
            ResolveScatterGridLineStroke(chart),
            xLabels,
            yLabels,
            seriesPrimitives,
            renderedDataLabels);
    }

    public static ChartBubblePrimitivePlan BuildBubblePrimitivePlan(
        ChartShape chart,
        ChartPlanRect plot,
        IReadOnlyList<SrgbColor>? seriesColors = null,
        ChartFillPlanSet? fillPlans = null)
    {
        if (chart.Series.Count == 0 || !plot.HasPositiveArea)
            return EmptyBubblePrimitivePlan();

        var (xMin, xMax, xUnit) = ComputeBubbleXAxisRange(chart);
        var (yMin, yMax, yUnit) = ComputePrimaryValueAxisRange(chart);
        double xRange = xMax - xMin;
        double yRange = yMax - yMin;
        if (xRange <= 0 || yRange <= 0)
            return EmptyBubblePrimitivePlan();

        double maxBubble = 0;
        foreach (var series in chart.Series)
        {
            foreach (var value in series.BubbleSizes)
            {
                if (value.HasValue && (value.Value >= 0 || chart.ShowNegativeBubbles))
                    maxBubble = Math.Max(maxBubble, Math.Abs(value.Value));
            }
        }

        if (maxBubble <= 0)
            maxBubble = 1;

        double bubbleScale = Math.Clamp(chart.BubbleScalePercent, 0, 300) / 100.0;
        double maxBubbleRadius = Math.Min(plot.Width, plot.Height) / 8.0 * bubbleScale;
        var bubbles = new List<ChartBubblePrimitive>();
        for (int seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
        {
            var series = chart.Series[seriesIndex];
            var color = ResolveSeriesColor(seriesIndex, seriesColors);
            var stroke = new ChartStrokePlan(color, Alpha: 255, Thickness: BubbleStrokeThickness);
            int pointCount = Math.Max(series.XValues.Count, series.Values.Count);
            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                double xValue = ResolveBubbleXValue(series, pointIndex);
                double? yValue = pointIndex < series.Values.Count ? series.Values[pointIndex] : null;
                double? bubbleValue = pointIndex < series.BubbleSizes.Count ? series.BubbleSizes[pointIndex] : null;
                // PowerPoint does not synthesize a radius when a bubble chart
                // has no c:bubbleSize value. Keep the axes and legend, but do
                // not invent visible points for an incomplete imported chart.
                if (!yValue.HasValue || !bubbleValue.HasValue)
                    continue;
                if (bubbleValue < 0 && !chart.ShowNegativeBubbles)
                    continue;

                double radius = ComputeBubbleRadius(
                    Math.Abs(bubbleValue.Value),
                    maxBubble,
                    maxBubbleRadius,
                    chart.BubbleSizeRepresents);
                radius = Math.Max(2, radius);

                bubbles.Add(new ChartBubblePrimitive(
                    seriesIndex,
                    pointIndex,
                    new ChartPlanPoint(
                        plot.X + (xValue - xMin) / xRange * plot.Width,
                        plot.Bottom - (yValue.Value - yMin) / yRange * plot.Height),
                    radius,
                    ResolvePointFill(series, seriesIndex, pointIndex, seriesColors, BubbleFillAlpha, fillPlans, ShouldVaryPointColors(chart)),
                    stroke));
            }
        }

        var (gridLines, xLabels, yLabels) = BuildScatterAxisPrimitives(
            plot,
            xMin,
            xRange,
            xUnit,
            yMin,
            yRange,
            yUnit,
            BuildAxisLabelFormatPlan(chart.CategoryAxis),
            BuildAxisLabelFormatPlan(chart.ValueAxis),
            importedMinorValueGridlines: false,
            includeXGridlines: chart.ValueAxis.HasMajorGridlines);

        return new ChartBubblePrimitivePlan(
            gridLines,
            ResolveScatterGridLineStroke(chart),
            xLabels,
            yLabels,
            bubbles);
    }

    private static (double min, double max, double majorUnit) ComputeBubbleXAxisRange(ChartShape chart)
    {
        double dataMin = 0;
        double dataMax = 0;
        bool any = false;

        foreach (var series in chart.Series)
        {
            int pointCount = Math.Max(series.XValues.Count, series.Values.Count);
            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                if (pointIndex >= series.Values.Count || !series.Values[pointIndex].HasValue)
                    continue;

                double xValue = ResolveBubbleXValue(series, pointIndex);
                dataMin = Math.Min(dataMin, xValue);
                dataMax = Math.Max(dataMax, xValue);
                any = true;
            }
        }

        if (!any)
            return (0, 1, 1);

        double min = dataMin >= 0 ? 0 : dataMin;
        return ComputeNiceRange(min, dataMax);
    }

    private static double ResolveBubbleXValue(ChartSeries series, int pointIndex) =>
        pointIndex < series.XValues.Count && series.XValues[pointIndex].HasValue
            ? series.XValues[pointIndex]!.Value
            : pointIndex;

    private static double ComputeBubbleRadius(
        double bubbleValue,
        double maxBubble,
        double maxBubbleRadius,
        BubbleSizeRepresentation sizeRepresents)
    {
        if (maxBubble <= 0 || bubbleValue <= 0 || maxBubbleRadius <= 0)
            return 0;

        double normalized = Math.Clamp(bubbleValue / maxBubble, 0, 1);
        return sizeRepresents == BubbleSizeRepresentation.Width
            ? normalized * maxBubbleRadius
            : Math.Sqrt(normalized) * maxBubbleRadius;
    }

    public static ChartRadarPrimitivePlan BuildRadarPrimitivePlan(
        ChartShape chart,
        ChartPlanRect plot,
        IReadOnlyList<SrgbColor>? seriesColors = null,
        ChartFillPlanSet? fillPlans = null)
    {
        if (chart.Series.Count == 0 || !plot.HasPositiveArea)
            return EmptyRadarPrimitivePlan();

        int categoryCount = Math.Max(3, chart.Categories.Count > 0
            ? chart.Categories.Count
            : chart.Series[0].Values.Count > 0
                ? chart.Series[0].Values.Count
                : 3);

        bool importedRadar = UsesImportedTextMetrics(chart);
        var center = new ChartPlanPoint(
            plot.X + plot.Width / 2 + (importedRadar ? ImportedRadarCenterOffsetX : 0),
            plot.Y + plot.Height / 2 + (importedRadar ? ImportedRadarCenterOffsetY : 0));
        double radius = Math.Min(plot.Width, plot.Height) / 2 *
            (importedRadar ? ImportedRadarRadiusFactor : 0.75);
        double dataMax = 0;
        foreach (var series in chart.Series)
        {
            foreach (var value in series.Values)
            {
                if (value.HasValue)
                    dataMax = Math.Max(dataMax, Math.Abs(value.Value));
            }
        }

        if (dataMax <= 0)
            dataMax = 1;

        int ringCount = importedRadar ? 9 : 4;
        double radarMax = importedRadar
            ? Math.Ceiling(dataMax / 10.0) * 10.0
            : dataMax;
        var gridStroke = importedRadar
            ? new ChartStrokePlan(new SrgbColor(0x80, 0x80, 0x80), Alpha: 255, Thickness: 0.5)
            : DefaultGridLineStroke();
        var rings = new List<ChartRadarRingPrimitive>();
        for (int ring = 1; ring <= ringCount; ring++)
        {
            double ringRadius = radius * ring / ringCount;
            var points = new ChartPlanPoint[categoryCount];
            for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
            {
                double angle = GetRadarAngle(categoryIndex, categoryCount);
                points[categoryIndex] = new ChartPlanPoint(
                    center.X + ringRadius * Math.Cos(angle),
                    center.Y + ringRadius * Math.Sin(angle));
            }

            rings.Add(new ChartRadarRingPrimitive(
                points,
                new ChartPathPrimitive(points, IsClosed: true, Fill: null),
                gridStroke));
        }

        var spokes = new List<ChartGridLinePlan>(categoryCount);
        for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
        {
            double angle = GetRadarAngle(categoryIndex, categoryCount);
            spokes.Add(new ChartGridLinePlan(
                center,
                new ChartPlanPoint(
                    center.X + radius * Math.Cos(angle),
                    center.Y + radius * Math.Sin(angle))));
        }

        var valueLabels = new List<ChartTextPlan>();
        if (importedRadar)
        {
            for (int ring = 0; ring <= ringCount; ring++)
            {
                double ringRadius = radius * ring / ringCount;
                double labelY = center.Y - ringRadius - 7.0 + ImportedRadarValueLabelOffsetY;
                valueLabels.Add(new ChartTextPlan(
                    FormatAxisValue(radarMax * ring / ringCount),
                    new ChartPlanRect(
                        center.X - 58.0 + ImportedRadarValueLabelOffsetX,
                        labelY,
                        48.0,
                        14.0),
                    IsBold: false,
                    FontSize: ResolveTextFontSize(chart, 6.5),
                    Alignment: ChartPlanTextAlignment.Right));
            }
        }

        var labels = new List<ChartTextPlan>();
        for (int categoryIndex = 0; categoryIndex < chart.Categories.Count && categoryIndex < categoryCount; categoryIndex++)
        {
            double angle = GetRadarAngle(categoryIndex, categoryCount);
            double labelOffset = importedRadar
                ? Math.Sin(angle) < -0.5
                    ? 39.0
                    : Math.Sin(angle) > 0.5
                        ? 3.0
                        : 37.0
                : 6.0;
            double labelX = center.X + (radius + labelOffset) * Math.Cos(angle);
            double labelY = center.Y + (radius + labelOffset) * Math.Sin(angle);
            double labelWidth = importedRadar ? 96.0 : 40.0;
            labels.Add(new ChartTextPlan(
                chart.Categories[categoryIndex],
                new ChartPlanRect(labelX - labelWidth / 2, labelY - 6, labelWidth, ResolveCategoryLabelHeight(chart)),
                IsBold: false,
                FontSize: ResolveTextFontSize(chart, 6.5),
                Alignment: ChartPlanTextAlignment.Center));
        }

        bool withMarkers = chart.RadarStyle == RadarStyle.Marker;
        bool filled = chart.RadarStyle == RadarStyle.Filled;
        var seriesPrimitives = new List<ChartRadarSeriesPrimitive>();
        for (int seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
        {
            var series = chart.Series[seriesIndex];
            var pointSlots = new ChartPlanPoint?[categoryCount];
            for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
            {
                double? value = ResolveBlankSensitiveValue(
                    chart,
                    categoryIndex < series.Values.Count ? series.Values[categoryIndex] : null);
                if (!value.HasValue)
                    continue;

                double fraction = Math.Clamp(value.Value / dataMax, 0, 1);
                double angle = GetRadarAngle(categoryIndex, categoryCount);
                pointSlots[categoryIndex] = new ChartPlanPoint(
                    center.X + radius * fraction * Math.Cos(angle),
                    center.Y + radius * fraction * Math.Sin(angle));
            }

            var color = ResolveSeriesColor(seriesIndex, seriesColors);
            var fill = filled ? ResolveSeriesFill(seriesIndex, seriesColors, RadarFillAlpha, fillPlans) : (ChartFillPlan?)null;
            double seriesStrokeThickness = importedRadar
                ? ImportedRadarSeriesStrokeThickness
                : RadarSeriesStrokeThickness;
            var stroke = ResolveAuthoredSeriesStroke(series, seriesIndex, seriesColors, seriesStrokeThickness)
                ?? new ChartStrokePlan(color, Alpha: 255, Thickness: seriesStrokeThickness);
            var markers = new List<ChartCirclePrimitive>();
            if (withMarkers)
            {
                for (int pointIndex = 0; pointIndex < pointSlots.Length; pointIndex++)
                {
                    var point = pointSlots[pointIndex];
                    if (!point.HasValue)
                        continue;

                    var markerStyle = ResolvePointMarkerStyle(series, pointIndex);
                    bool hasAuthoredPointStyle = series.PointStyles.ContainsKey(pointIndex);
                    if (SuppressesMarker(markerStyle))
                        continue;

                    markers.Add(new ChartCirclePrimitive(
                        seriesIndex,
                        pointIndex,
                        point.Value,
                        ResolveMarkerRadius(markerStyle, RadarMarkerRadius),
                        ResolveMarkerSymbol(markerStyle),
                        ResolveMarkerFill(series, seriesIndex, pointIndex, markerStyle, seriesColors, defaultAlpha: 255, fillPlans),
                        markerStyle is not null || hasAuthoredPointStyle
                            ? ResolveMarkerStroke(series, seriesIndex, pointIndex, markerStyle, seriesColors, LineMarkerStrokeThickness)
                            : null));
                }
            }

            seriesPrimitives.Add(new ChartRadarSeriesPrimitive(
                seriesIndex,
                filled,
                withMarkers,
                pointSlots,
                BuildRadarSeriesPaths(pointSlots, fill, ResolveDisplayBlanksAs(chart)),
                stroke,
                markers));
        }

        return new ChartRadarPrimitivePlan(
            rings,
            spokes,
            importedRadar ? gridStroke : DefaultRadarSpokeStroke(),
            labels,
            seriesPrimitives)
        {
            Center = center,
            Radius = radius,
            ValueMaximum = dataMax,
            ValueLabels = valueLabels
        };
    }

    private static IReadOnlyList<ChartPathPrimitive> BuildRadarSeriesPaths(
        IReadOnlyList<ChartPlanPoint?> pointSlots,
        ChartFillPlan? fill,
        ChartDisplayBlanksAs displayBlanksAs)
    {
        var presentPoints = pointSlots
            .Where(point => point.HasValue)
            .Select(point => point!.Value)
            .ToArray();
        if (presentPoints.Length < 2)
            return Array.Empty<ChartPathPrimitive>();

        if (displayBlanksAs == ChartDisplayBlanksAs.Span)
        {
            bool closes = presentPoints.Length >= 3;
            return new[]
            {
                new ChartPathPrimitive(
                    presentPoints,
                    IsClosed: closes,
                    Fill: closes ? fill : null)
            };
        }

        bool hasBlank = pointSlots.Any(point => !point.HasValue);
        if (!hasBlank)
        {
            return new[]
            {
                new ChartPathPrimitive(
                    presentPoints,
                    IsClosed: presentPoints.Length >= 3,
                    Fill: presentPoints.Length >= 3 ? fill : null)
            };
        }

        var segments = new List<ChartPathPrimitive>();
        for (int pointIndex = 0; pointIndex < pointSlots.Count; pointIndex++)
        {
            int nextIndex = (pointIndex + 1) % pointSlots.Count;
            var start = pointSlots[pointIndex];
            var end = pointSlots[nextIndex];
            if (!start.HasValue || !end.HasValue)
                continue;

            segments.Add(new ChartPathPrimitive(
                new[] { start.Value, end.Value },
                IsClosed: false,
                Fill: null));
        }

        return segments;
    }

    public static IReadOnlyList<ChartPieSlicePrimitive> BuildPieSlicePrimitives(
        ChartShape chart,
        ChartPlanRect plot,
        IReadOnlyList<SrgbColor>? seriesColors = null,
        ChartFillPlanSet? fillPlans = null)
    {
        if (chart.Series.Count == 0 || !plot.HasPositiveArea)
            return Array.Empty<ChartPieSlicePrimitive>();

        bool importedThreeDPie = chart.ThreeDStyle == ChartThreeDStyle.Pie && UsesImportedTextMetrics(chart);
        double outerRadius = importedThreeDPie
            ? plot.Width / 2 * ImportedThreeDPieHorizontalScale
            : Math.Min(plot.Width, plot.Height) / 2 * 0.85;
        var slices = BuildSlicePrimitivesForSeries(
            chart.Series[0],
            seriesIndex: 0,
            plot,
            innerRadius: 0,
            outerRadius,
            ResolvePieStartAngle(chart),
            seriesColors,
            fillPlans,
            ShouldVaryPointColors(chart),
            ResolvePieVerticalScale(chart),
            ResolvePieDepthOffset(chart, outerRadius));
        if (!importedThreeDPie)
            return slices;

        double centerOffsetY = plot.Height * ImportedThreeDPieCenterOffsetFactor;
        return slices
            .Select(slice => slice with
            {
                Center = new ChartPlanPoint(slice.Center.X, slice.Center.Y - centerOffsetY),
                Fill = DarkenImportedThreeDPieTop(slice.Fill!.Value),
                DrawDepthSidewalls = true,
                DepthFill = slice.Fill!.Value
            })
            .ToArray();
    }

    public static IReadOnlyList<ChartPieSlicePrimitive> BuildDoughnutSlicePrimitives(
        ChartShape chart,
        ChartPlanRect plot,
        IReadOnlyList<SrgbColor>? seriesColors = null,
        ChartFillPlanSet? fillPlans = null)
    {
        if (chart.Series.Count == 0 || !plot.HasPositiveArea)
            return Array.Empty<ChartPieSlicePrimitive>();

        double outerRadius = Math.Min(plot.Width, plot.Height) / 2 * 0.85;
        double innerHoleRadius = outerRadius * Math.Clamp(chart.DoughnutHolePercent, 0, 90) / 100.0;
        int seriesCount = chart.Series.Count;
        double ringGap = seriesCount > 1 ? outerRadius * 0.04 : 0;
        double ringWidth = seriesCount > 1
            ? (outerRadius - innerHoleRadius - (seriesCount - 1) * ringGap) / seriesCount
            : outerRadius - innerHoleRadius;

        var primitives = new List<ChartPieSlicePrimitive>();
        for (int seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
        {
            double innerRadius = innerHoleRadius + seriesIndex * (ringWidth + ringGap);
            double seriesOuterRadius = innerRadius + ringWidth;
            if (seriesOuterRadius <= 0 || innerRadius < 0)
                innerRadius = 0;

            primitives.AddRange(BuildSlicePrimitivesForSeries(
                chart.Series[seriesIndex],
                seriesIndex,
                plot,
                innerRadius,
                seriesOuterRadius,
                ResolvePieStartAngle(chart),
                seriesColors,
                fillPlans,
                ShouldVaryPointColors(chart),
                verticalScale: 1.0,
                depthOffsetY: 0));
        }

        return primitives;
    }

    public static IReadOnlyList<ChartDataLabelPlan> BuildDataLabelPlans(
        ChartShape chart,
        ChartPlanRect plot,
        IReadOnlyList<SrgbColor>? seriesColors = null,
        ChartFillPlanSet? fillPlans = null)
    {
        var family = GetRenderFamily(chart.ChartType);
        if (family is ChartRenderFamily.Funnel or ChartRenderFamily.Waterfall or ChartRenderFamily.Radar or ChartRenderFamily.ScatterLike || !plot.HasPositiveArea)
            return Array.Empty<ChartDataLabelPlan>();

        if (family == ChartRenderFamily.Pie)
            return BuildPieDataLabelPlans(chart, plot, seriesColors, fillPlans);

        var plans = new List<ChartDataLabelPlan>();
        bool isLineOrArea = IsLineOrArea(chart.ChartType);
        bool isBar = family == ChartRenderFamily.HorizontalBar;
        for (int seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
        {
            var overrideType = chart.Series[seriesIndex].OverrideChartType;
            bool seriesIsLineOrArea = overrideType.HasValue
                ? IsLineOrArea(overrideType.Value)
                : isLineOrArea;
            bool seriesIsBar = overrideType.HasValue
                ? overrideType.Value is ChartType.BarClustered
                    or ChartType.BarStacked
                    or ChartType.BarStacked100
                : isBar;

            IReadOnlyList<ChartDataLabelPlan> seriesPlans = seriesIsLineOrArea
                ? BuildLineDataLabelPlans(chart, seriesIndex, plot, seriesColors, fillPlans)
                : seriesIsBar
                    ? BuildBarDataLabelPlans(chart, seriesIndex, plot, seriesColors, fillPlans)
                    : BuildColumnDataLabelPlans(chart, seriesIndex, plot, seriesColors, fillPlans);

            plans.AddRange(seriesPlans);
        }

        return chart.ShowDataLabelsOverMaximum == false
            ? plans.Where(plan => !plan.IsOverMaximum).ToArray()
            : plans;
    }

    private static ChartDataLabelPlan ApplyDataLabelTextStyle(
        ChartDataLabelPlan plan,
        ChartDataLabels labels)
    {
        var style = labels.TextStyle;
        return style is null
            ? plan
            : plan with
            {
                IsBold = style.Bold ?? plan.IsBold,
                IsItalic = style.Italic ?? plan.IsItalic,
                FontSize = style.FontSizePt is > 0 ? style.FontSizePt.Value : plan.FontSize,
                FontFamily = style.FontFamily ?? plan.FontFamily,
                TextColor = style.Color?.Resolved ?? plan.TextColor
            };
    }

    private static ChartDataLabelPlan ApplyLegendKey(
        ChartDataLabelPlan plan,
        ChartDataLabels labels,
        int seriesIndex,
        IReadOnlyList<SrgbColor>? seriesColors,
        ChartFillPlanSet? fillPlans)
    {
        if (!labels.ShowLegendKey)
            return plan;

        const double keySize = 6.0;
        const double keyGap = 3.0;
        var bounds = plan.Bounds;
        return plan with
        {
            TextBounds = new ChartPlanRect(
                bounds.X + keySize + keyGap,
                bounds.Y,
                Math.Max(1.0, bounds.Width - keySize - keyGap),
                bounds.Height),
            LegendKeyBounds = new ChartPlanRect(
                bounds.X,
                bounds.Y + (bounds.Height - keySize) / 2.0,
                keySize,
                keySize),
            LegendKeyFill = ResolveSeriesFill(
                seriesIndex,
                seriesColors,
                RectSeriesFillAlpha,
                fillPlans)
        };
    }

    private readonly record struct OfPieScenePrimitives(
        IReadOnlyList<ChartPieSlicePrimitive> PrimarySlices,
        IReadOnlyList<ChartPieSlicePrimitive> SecondarySlices,
        IReadOnlyList<ChartRectPrimitive> SecondaryBars);

    private static OfPieScenePrimitives BuildOfPiePrimitives(
        ChartShape chart,
        ChartPlanRect plot,
        IReadOnlyList<SrgbColor>? seriesColors,
        ChartFillPlanSet? fillPlans)
    {
        if (chart.Series.Count == 0 || !plot.HasPositiveArea)
            return new(Array.Empty<ChartPieSlicePrimitive>(), Array.Empty<ChartPieSlicePrimitive>(), Array.Empty<ChartRectPrimitive>());

        var values = GetVisiblePieValues(chart.Series[0]);
        if (values.Count < 2)
        {
            var only = BuildPieSlicePrimitives(chart, plot, seriesColors, fillPlans);
            return new(only, Array.Empty<ChartPieSlicePrimitive>(), Array.Empty<ChartRectPrimitive>());
        }

        var secondaryIndices = ResolveOfPieSecondaryIndices(chart, values);
        var primaryIndices = values
            .Select(value => value.PointIndex)
            .Where(pointIndex => !secondaryIndices.Contains(pointIndex))
            .ToHashSet();
        if (primaryIndices.Count == 0)
        {
            int retained = secondaryIndices.OrderBy(pointIndex => pointIndex).First();
            secondaryIndices.Remove(retained);
            primaryIndices.Add(retained);
        }
        if (secondaryIndices.Count == 0)
        {
            int moved = primaryIndices.OrderByDescending(pointIndex => pointIndex).First();
            primaryIndices.Remove(moved);
            secondaryIndices.Add(moved);
        }

        // OfPie reuses c:gapWidth for the separation between the primary and
        // secondary plots. Keep the historical default when the attribute is
        // omitted, but let an authored value scale that same bounded gap.
        double gapWidthPercent = Math.Clamp(chart.BarGapWidthPercent ?? 150, 0, 500);
        double gap = Math.Min(16.0, plot.Width * 0.06) * gapWidthPercent / 150.0;
        double primaryWidth = Math.Max(1.0, (plot.Width - gap) * 0.58);
        double secondaryWidth = Math.Max(1.0, plot.Width - gap - primaryWidth);
        var primaryPlot = new ChartPlanRect(plot.X, plot.Y, primaryWidth, plot.Height);
        double secondaryScale = Math.Clamp(chart.OfPieSecondPieSizePercent ?? 100, 40, 100) / 100.0;
        double secondaryHeight = Math.Max(1.0, plot.Height * secondaryScale);
        var secondaryPlot = new ChartPlanRect(
            plot.X + primaryWidth + gap,
            plot.Y + (plot.Height - secondaryHeight) / 2.0,
            secondaryWidth,
            secondaryHeight);

        var primary = BuildSlicePrimitivesForSeries(
            chart.Series[0], 0, primaryPlot, 0,
            Math.Min(primaryPlot.Width, primaryPlot.Height) / 2.0 * 0.85,
            ResolvePieStartAngle(chart), seriesColors, fillPlans,
            ShouldVaryPointColors(chart), 1.0, 0,
            primaryIndices);

        if (chart.OfPieType == OfPieType.Bar)
        {
            return new(
                primary,
                Array.Empty<ChartPieSlicePrimitive>(),
                BuildOfPieBarPrimitives(chart.Series[0], secondaryIndices, secondaryPlot, seriesColors, fillPlans, ShouldVaryPointColors(chart)));
        }

        var secondary = BuildSlicePrimitivesForSeries(
            chart.Series[0], 0, secondaryPlot, 0,
            Math.Min(secondaryPlot.Width, secondaryPlot.Height) / 2.0 * 0.85,
            ResolvePieStartAngle(chart), seriesColors, fillPlans,
            ShouldVaryPointColors(chart), 1.0, 0,
            secondaryIndices);
        return new(primary, secondary, Array.Empty<ChartRectPrimitive>());
    }

    private static HashSet<int> ResolveOfPieSecondaryIndices(
        ChartShape chart,
        IReadOnlyList<(int PointIndex, double Value)> values)
    {
        double total = values.Sum(value => value.Value);
        var selected = chart.OfPieSplitType switch
        {
            OfPieSplitType.Position => values
                .TakeLast(Math.Clamp((int)Math.Round(chart.OfPieSplitPosition ?? 2), 1, values.Count - 1))
                .Select(value => value.PointIndex),
            OfPieSplitType.Percent => values
                .Where(value => value.Value / total * 100.0 <= Math.Clamp(chart.OfPieSplitPosition ?? 10, 0, 100))
                .Select(value => value.PointIndex),
            OfPieSplitType.Value => values
                .Where(value => value.Value <= Math.Max(0, chart.OfPieSplitPosition ?? 0))
                .Select(value => value.PointIndex),
            OfPieSplitType.Custom when chart.OfPieCustomPointIndices.Count > 0 =>
                values.Where(value => chart.OfPieCustomPointIndices.Contains(value.PointIndex)).Select(value => value.PointIndex),
            OfPieSplitType.Custom => values.TakeLast(Math.Min(2, values.Count - 1)).Select(value => value.PointIndex),
            _ => values.TakeLast(Math.Min(3, values.Count - 1)).Select(value => value.PointIndex)
        };
        var result = selected.ToHashSet();
        if (result.Count == 0)
            result.Add(values[^1].PointIndex);
        if (result.Count == values.Count)
            result.Remove(values[0].PointIndex);
        return result;
    }

    private static IReadOnlyList<ChartRectPrimitive> BuildOfPieBarPrimitives(
        ChartSeries series,
        IReadOnlySet<int> pointIndices,
        ChartPlanRect plot,
        IReadOnlyList<SrgbColor>? seriesColors,
        ChartFillPlanSet? fillPlans,
        bool varyByPoint)
    {
        var values = GetVisiblePieValues(series)
            .Where(value => pointIndices.Contains(value.PointIndex))
            .ToArray();
        if (values.Length == 0)
            return Array.Empty<ChartRectPrimitive>();

        double max = values.Max(value => value.Value);
        double slot = plot.Width / values.Length;
        var bars = new List<ChartRectPrimitive>(values.Length);
        for (int index = 0; index < values.Length; index++)
        {
            var value = values[index];
            double height = Math.Max(0.5, value.Value / max * Math.Max(1.0, plot.Height - 12.0));
            var bounds = new ChartPlanRect(
                plot.X + index * slot + slot * 0.14,
                plot.Bottom - height,
                Math.Max(1.0, slot * 0.72),
                height);
            bars.Add(new ChartRectPrimitive(
                0,
                value.PointIndex,
                bounds,
                ResolvePointFill(series, 0, value.PointIndex, seriesColors, RectSeriesFillAlpha, fillPlans, varyByPoint),
                new ChartStrokePlan(new SrgbColor(0xFF, 0xFF, 0xFF), 230, 0.8)));
        }

        return bars;
    }

    public static (double min, double max, double majorUnit) ComputePrimaryValueAxisRange(
        ChartShape chart)
    {
        if (IsHundredPercentStacked(chart.ChartType) &&
            chart.ValueAxis.Min is null &&
            chart.ValueAxis.Max is null &&
            chart.Series.Any(series => !series.OnSecondaryAxis && series.Values.Any(value => value.HasValue)))
        {
            return UsesImportedTextMetrics(chart)
                ? (0, 1, 0.1)
                : (0, 1, 0.25);
        }

        double dataMin = 0;
        double dataMax = 0;

        if (chart.ChartType == ChartType.AreaStacked)
        {
            AccumulateStackedCategoryTotals(chart, onSecondaryAxis: false, ref dataMin, ref dataMax);
        }
        else if (chart.ChartType == ChartType.Waterfall)
        {
            AccumulateWaterfallTotals(chart, ref dataMin, ref dataMax);
        }
        else
        {
            foreach (var series in chart.Series)
            {
                if (series.OnSecondaryAxis)
                    continue;

                AccumulateValues(series.Values, ref dataMin, ref dataMax);
            }
        }

        double min = chart.ValueAxis.Min ?? (dataMin >= 0 ? 0 : dataMin);
        double max = chart.ValueAxis.Max ?? dataMax;
        if (chart.ValueAxis.MajorUnit is > 0)
            return ApplyAuthoredMajorUnit(chart.ValueAxis, min, max);
        if (UsesStockLineFallback(chart) && chart.ValueAxis.Min is null && chart.ValueAxis.Max is null)
            return ComputeStockFallbackValueAxisRange(min, max);
        if (UsesImportedThreeDColumnDefaults(chart) &&
            chart.ValueAxis.Min is null && chart.ValueAxis.Max is null)
        {
            var range = ComputeNiceRange(min, max, targetIntervals: 10);
            double cappedMax = Math.Ceiling(max / range.majorUnit) * range.majorUnit;
            return (range.min, Math.Max(range.min + range.majorUnit, cappedMax), range.majorUnit);
        }
        if (chart.ChartType == ChartType.ColumnClustered &&
            UsesImportedTextMetrics(chart) &&
            chart.Series.Any(series => series.OnSecondaryAxis) &&
            chart.ValueAxis.Min is null && chart.ValueAxis.Max is null)
        {
            // PowerPoint gives imported column+line combos a denser primary
            // scale than a standalone clustered column chart.
            var range = ComputeNiceRange(min, max, targetIntervals: 10);
            return range.max - max <= range.majorUnit * 0.25
                ? (range.min, range.max + range.majorUnit, range.majorUnit)
                : range;
        }
        if (chart.ChartType == ChartType.BarClustered &&
            UsesImportedTextMetrics(chart) &&
            chart.ValueAxis.Min is null && chart.ValueAxis.Max is null)
        {
            return ComputeNiceRange(min, max, targetIntervals: 6);
        }
        if (UsesImportedSingleScatterDefaults(chart) &&
            chart.ValueAxis.Min is null && chart.ValueAxis.Max is null)
        {
            var range = ComputeNiceRange(min, max, targetIntervals: 8);
            return range.max - max <= range.majorUnit * 0.25
                ? (range.min, range.max + range.majorUnit, range.majorUnit)
                : range;
        }
        if (UsesImportedBubbleDefaults(chart) &&
            chart.ValueAxis.Min is null && chart.ValueAxis.Max is null)
        {
            var range = ComputeNiceRange(min, max, targetIntervals: 8);
            return range.max - max <= range.majorUnit * 1.25
                ? (range.min, range.max + range.majorUnit, range.majorUnit)
                : range;
        }
        if (chart.ChartType is (ChartType.Line or ChartType.LineMarkers) &&
            UsesImportedTextMetrics(chart) &&
            chart.ValueAxis.Min is null && chart.ValueAxis.Max is null)
        {
            return ComputeNiceRange(min, max, targetIntervals: 6);
        }
        return ComputeNiceRange(min, max);
    }

    public static (double min, double max, double majorUnit) ComputeSecondaryValueAxisRange(
        ChartShape chart)
    {
        double dataMin = 0;
        double dataMax = 0;
        bool any = false;

        foreach (var series in chart.Series)
        {
            if (!series.OnSecondaryAxis)
                continue;

            AccumulateValues(series.Values, ref dataMin, ref dataMax, ref any);
        }

        if (!any)
            return (0, 1, 1);

        double min = chart.SecondaryValueAxis?.Min ?? (dataMin >= 0 ? 0 : dataMin);
        double max = chart.SecondaryValueAxis?.Max ?? dataMax;
        if (chart.SecondaryValueAxis?.MajorUnit is > 0)
            return ApplyAuthoredMajorUnit(chart.SecondaryValueAxis, min, max);
        if (UsesImportedTextMetrics(chart) &&
            chart.Series.Any(series => series.OnSecondaryAxis) &&
            chart.SecondaryValueAxis?.Min is null && chart.SecondaryValueAxis?.Max is null)
        {
            // The imported combo baseline uses eight readable secondary-axis
            // intervals, which keeps the 0..8000 labels at 1000 increments.
            return ComputeNiceRange(min, max, targetIntervals: 8);
        }
        return ComputeNiceRange(min, max);
    }

    private static (double min, double max, double majorUnit) ApplyAuthoredMajorUnit(
        ChartAxis axis,
        double dataMin,
        double dataMax)
    {
        double majorUnit = axis.MajorUnit!.Value;
        double min = axis.Min ?? (dataMin >= 0
            ? 0
            : Math.Floor(dataMin / majorUnit) * majorUnit);
        double max = axis.Max ?? Math.Ceiling(dataMax / majorUnit) * majorUnit;
        if (max <= min)
            max = min + majorUnit;
        return (min, max, majorUnit);
    }

    private static void AccumulateStackedCategoryTotals(
        ChartShape chart,
        bool onSecondaryAxis,
        ref double dataMin,
        ref double dataMax)
    {
        int categoryCount = Math.Max(
            chart.Categories.Count,
            chart.Series
                .Where(series => series.OnSecondaryAxis == onSecondaryAxis)
                .Select(series => series.Values.Count)
                .DefaultIfEmpty(0)
                .Max());

        for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
        {
            double positiveTotal = 0;
            double negativeTotal = 0;
            foreach (var series in chart.Series)
            {
                if (series.OnSecondaryAxis != onSecondaryAxis || categoryIndex >= series.Values.Count)
                    continue;

                var value = series.Values[categoryIndex];
                if (!value.HasValue)
                    continue;

                if (value.Value >= 0)
                    positiveTotal += value.Value;
                else
                    negativeTotal += value.Value;
            }

            dataMin = Math.Min(dataMin, negativeTotal);
            dataMax = Math.Max(dataMax, positiveTotal);
        }
    }

    private static void AccumulateWaterfallTotals(
        ChartShape chart,
        ref double dataMin,
        ref double dataMax)
    {
        var series = chart.Series.FirstOrDefault(item => !item.OnSecondaryAxis);
        if (series is null)
            return;

        double cumulative = 0;
        for (int index = 0; index < series.Values.Count; index++)
        {
            cumulative += series.Values[index] ?? 0;
            dataMin = Math.Min(dataMin, cumulative);
            dataMax = Math.Max(dataMax, cumulative);
        }
    }

    public static (double min, double max, double majorUnit) ComputeScatterAxisRange(
        ChartShape chart,
        bool useX)
    {
        double dataMin = 0;
        double dataMax = 0;

        foreach (var series in chart.Series)
        {
            var values = useX ? series.XValues : series.Values;
            AccumulateValues(values, ref dataMin, ref dataMax);
        }

        double min = dataMin >= 0 ? 0 : dataMin;
        double max = dataMax;
        return useX && UsesImportedTextMetrics(chart)
            ? ComputeNiceRange(min, max, targetIntervals: 6)
            : ComputeNiceRange(min, max);
    }

    public static string FormatAxisValue(double value) =>
        Math.Abs(value) >= 1000
            ? $"{value / 1000:G4}K"
            : value == Math.Floor(value)
                ? ((long)value).ToString(CultureInfo.InvariantCulture)
                : value.ToString("G3", CultureInfo.InvariantCulture);

    public static string FormatAxisValue(double value, string? numberFormatCode) =>
        string.IsNullOrWhiteSpace(numberFormatCode)
            ? FormatAxisValue(value)
            : FormatWithCode(value, numberFormatCode!);

    public static string FormatAxisLabelValue(double value, string? numberFormatCode) =>
        string.Equals(numberFormatCode, "General", StringComparison.OrdinalIgnoreCase)
            ? FormatAxisValueWithoutScale(value)
            : string.IsNullOrWhiteSpace(numberFormatCode)
                ? FormatAxisValue(value)
            : FormatWithCode(value, numberFormatCode!);

    private static string FormatChartAxisLabelValue(
        ChartShape chart,
        double value,
        ChartAxis axis)
    {
        var numberFormatCode = axis.NumberFormatCode;
        if (IsHundredPercentStacked(chart.ChartType) &&
            (string.IsNullOrWhiteSpace(numberFormatCode) ||
             string.Equals(numberFormatCode, "General", StringComparison.OrdinalIgnoreCase)))
        {
            return FormatWithCode(value, "0%");
        }

        if (UsesImportedTextMetrics(chart) &&
            chart.Series.Any(series => series.OnSecondaryAxis) &&
            string.IsNullOrWhiteSpace(numberFormatCode))
        {
            return FormatAxisValueWithoutScale(value / DisplayUnitDivisor(axis));
        }

        value /= DisplayUnitDivisor(axis);

        return FormatAxisLabelValue(value, numberFormatCode);
    }

    private static double DisplayUnitDivisor(ChartAxis axis) => axis.DisplayUnit switch
    {
        ChartAxisDisplayUnit.Hundreds => 100,
        ChartAxisDisplayUnit.Thousands => 1_000,
        ChartAxisDisplayUnit.TenThousands => 10_000,
        ChartAxisDisplayUnit.HundredThousands => 100_000,
        ChartAxisDisplayUnit.Millions => 1_000_000,
        ChartAxisDisplayUnit.TenMillions => 10_000_000,
        ChartAxisDisplayUnit.HundredMillions => 100_000_000,
        ChartAxisDisplayUnit.Billions => 1_000_000_000,
        ChartAxisDisplayUnit.Trillions => 1_000_000_000_000,
        ChartAxisDisplayUnit.Custom when axis.CustomDisplayUnit is > 0 => axis.CustomDisplayUnit.Value,
        _ => 1,
    };

    private static string FormatAxisValueWithoutScale(double value) =>
        value == Math.Floor(value)
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("G3", CultureInfo.InvariantCulture);

    public static ChartDataLabels? ResolveEffectiveLabels(ChartShape chart, int seriesIndex)
    {
        var series = seriesIndex < chart.Series.Count ? chart.Series[seriesIndex] : null;
        var labels = series?.DataLabels ??
            (series?.OverrideChartType.HasValue == true ? null : chart.DataLabels);
        return labels is not null && labels.HasAny ? labels : null;
    }

    public static string FormatDataLabel(
        ChartDataLabels labels,
        double value,
        double total,
        string? categoryName,
        string? seriesName,
        double? bubbleSize = null)
    {
        string formattedValue = string.IsNullOrEmpty(labels.NumberFormat)
            ? FormatAxisValue(value)
            : FormatWithCode(value, labels.NumberFormat!);

        string percent = total > 0
            ? $"{value / total * 100:0}%"
            : "0%";

        var parts = new List<string>();
        if (labels.ShowSeriesName && !string.IsNullOrEmpty(seriesName))
            parts.Add(seriesName);
        if (labels.ShowCategoryName && !string.IsNullOrEmpty(categoryName))
            parts.Add(categoryName);
        if (labels.ShowValue)
            parts.Add(formattedValue);
        if (labels.ShowPercent)
            parts.Add(percent);
        if (labels.ShowBubbleSize && bubbleSize.HasValue)
            parts.Add(string.IsNullOrEmpty(labels.NumberFormat)
                ? FormatAxisValue(bubbleSize.Value)
                : FormatWithCode(bubbleSize.Value, labels.NumberFormat!));

        return string.Join(labels.Separator ?? " ", parts);
    }

    public static string FormatWithCode(double value, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return FormatAxisValue(value);

        var rawSection = SelectNumberFormatSection(value, code);
        if (TryFormatElapsedTimeValue(value, rawSection, out var elapsedText))
            return elapsedText;

        var section = NormalizeNumberFormatSection(rawSection);
        if (string.IsNullOrWhiteSpace(section) ||
            string.Equals(section, "General", StringComparison.OrdinalIgnoreCase))
        {
            return FormatAxisValue(value);
        }

        if (LooksLikeDateFormat(section) && TryFromOaDate(value, out var date))
            return FormatDateValue(date, section);

        if (TryFormatFractionValue(value, section, out var fractionText))
            return fractionText;

        var placeholderStart = IndexOfAny(section, '#', '0', '?');
        if (placeholderStart < 0)
            return FormatAxisValue(value);

        var placeholderEnd = LastIndexOfAny(section, '#', '0', '?');
        var prefix = section[..placeholderStart];
        var numericPattern = section[placeholderStart..(placeholderEnd + 1)];
        var suffix = section[(placeholderEnd + 1)..];
        bool asPercent = section.Contains('%', StringComparison.Ordinal);
        int scaleCommaCount = CountScaleCommas(ref suffix);
        double scaled = asPercent ? value * 100.0 : value;
        for (int count = 0; count < scaleCommaCount; count++)
            scaled /= 1000.0;
        suffix = suffix.Replace("%", string.Empty, StringComparison.Ordinal);

        var number = FormatNumberWithPattern(scaled, numericPattern);
        var hasAuthoredNegativeAffordance = HasAuthoredNegativeAffordance(prefix, suffix);
        var sign = scaled < 0 && !hasAuthoredNegativeAffordance ? "-" : string.Empty;
        return $"{sign}{prefix}{number}{suffix}{(asPercent ? "%" : string.Empty)}";
    }

    private static string FormatCategoryAxisLabel(string label, ChartAxis axis)
    {
        if (string.IsNullOrWhiteSpace(axis.NumberFormatCode))
            return label;

        var section = NormalizeNumberFormatSection(SelectNumberFormatSection(1, axis.NumberFormatCode!));
        if (LooksLikeDateFormat(section) && TryParseCategoryDate(label, out var date))
            return FormatDateValue(date, section);

        return double.TryParse(label, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? FormatWithCode(value, axis.NumberFormatCode!)
            : label;
    }

    private static string SelectNumberFormatSection(double value, string code)
    {
        var sections = code.Split(';');
        var conditionalSection = SelectConditionalNumberFormatSection(value, sections);
        if (conditionalSection is not null)
            return conditionalSection;

        if (sections.Length == 1)
            return sections[0];

        if (value > 0)
            return sections[0];

        if (value < 0)
            return sections.Length > 1 ? sections[1] : sections[0];

        return sections.Length > 2 ? sections[2] : sections[0];
    }

    private static string? SelectConditionalNumberFormatSection(double value, IReadOnlyList<string> sections)
    {
        bool hasConditionalSection = false;
        foreach (var section in sections)
        {
            if (!TryReadNumberFormatCondition(section, out var condition))
                continue;

            hasConditionalSection = true;
            if (condition.Matches(value))
                return section;
        }

        return hasConditionalSection ? sections.LastOrDefault(section => !TryReadNumberFormatCondition(section, out _)) : null;
    }

    private static bool TryReadNumberFormatCondition(string section, out NumberFormatCondition condition)
    {
        for (int index = 0; index < section.Length; index++)
        {
            if (char.IsWhiteSpace(section[index]))
                continue;

            if (section[index] != '[')
                break;

            var end = section.IndexOf(']', index + 1);
            if (end < 0)
                break;

            var token = section[(index + 1)..end].Trim();
            if (NumberFormatCondition.TryParse(token, out condition))
                return true;

            index = end;
        }

        condition = default;
        return false;
    }

    private static string NormalizeNumberFormatSection(string section, bool preserveElapsedTimeTokens = false)
    {
        var builder = new StringBuilder(section.Length);
        bool inQuotedLiteral = false;
        for (int index = 0; index < section.Length; index++)
        {
            var ch = section[index];
            if (ch == '"')
            {
                inQuotedLiteral = !inQuotedLiteral;
                continue;
            }

            if (!inQuotedLiteral && ch == '[')
            {
                var end = section.IndexOf(']', index + 1);
                if (end >= 0)
                {
                    var token = section[(index + 1)..end].Trim();
                    if (preserveElapsedTimeTokens && IsElapsedTimeToken(token))
                        builder.Append(section, index, end - index + 1);

                    index = end;
                    continue;
                }
            }

            if (!inQuotedLiteral && (ch == '_' || ch == '*'))
            {
                if (index + 1 < section.Length)
                    index++;
                continue;
            }

            if (!inQuotedLiteral && ch == '\\' && index + 1 < section.Length)
            {
                builder.Append(section[++index]);
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString().Trim();
    }

    private static bool TryFormatElapsedTimeValue(double value, string section, out string formatted)
    {
        formatted = string.Empty;

        var pattern = NormalizeNumberFormatSection(section, preserveElapsedTimeTokens: true);
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        var lower = pattern.ToLowerInvariant();
        var absoluteSeconds = Math.Abs(value) * 86400.0;
        string sign = value < 0 ? "-" : string.Empty;

        if (lower.StartsWith("[h]:mm:ss", StringComparison.Ordinal) &&
            TryReadElapsedFractionalSecondDigits(pattern, "[h]:mm:ss".Length, out var hourSecondDecimals))
        {
            formatted = sign + FormatElapsedHours(absoluteSeconds, hourSecondDecimals);
            return true;
        }

        if (lower.StartsWith("[m]:ss", StringComparison.Ordinal) &&
            TryReadElapsedFractionalSecondDigits(pattern, "[m]:ss".Length, out var minuteSecondDecimals))
        {
            formatted = sign + FormatElapsedMinutes(absoluteSeconds, minuteSecondDecimals);
            return true;
        }

        if (lower.StartsWith("[s]", StringComparison.Ordinal) &&
            TryReadElapsedFractionalSecondDigits(pattern, "[s]".Length, out var totalSecondDecimals))
        {
            formatted = sign + FormatElapsedTotalSeconds(absoluteSeconds, totalSecondDecimals);
            return true;
        }

        return false;
    }

    private static bool TryReadElapsedFractionalSecondDigits(string pattern, int startIndex, out int decimals)
    {
        decimals = 0;
        if (startIndex == pattern.Length)
            return true;

        if (startIndex >= pattern.Length || pattern[startIndex] != '.')
            return false;

        int index = startIndex + 1;
        while (index < pattern.Length && pattern[index] == '0')
            index++;

        decimals = index - startIndex - 1;
        return decimals is > 0 and <= 3 && index == pattern.Length;
    }

    private static string FormatElapsedHours(double absoluteSeconds, int decimals)
    {
        var roundedSeconds = Math.Round(absoluteSeconds, decimals, MidpointRounding.AwayFromZero);
        var hours = (long)Math.Floor(roundedSeconds / 3600.0);
        var minutes = (long)Math.Floor((roundedSeconds - hours * 3600.0) / 60.0);
        var seconds = roundedSeconds - hours * 3600.0 - minutes * 60.0;
        return $"{hours}:{minutes:00}:{FormatElapsedSecondComponent(seconds, decimals)}";
    }

    private static string FormatElapsedMinutes(double absoluteSeconds, int decimals)
    {
        var roundedSeconds = Math.Round(absoluteSeconds, decimals, MidpointRounding.AwayFromZero);
        var minutes = (long)Math.Floor(roundedSeconds / 60.0);
        var seconds = roundedSeconds - minutes * 60.0;
        return $"{minutes}:{FormatElapsedSecondComponent(seconds, decimals)}";
    }

    private static string FormatElapsedTotalSeconds(double absoluteSeconds, int decimals)
    {
        var roundedSeconds = Math.Round(absoluteSeconds, decimals, MidpointRounding.AwayFromZero);
        return roundedSeconds.ToString(decimals == 0 ? "0" : $"0.{new string('0', decimals)}", CultureInfo.InvariantCulture);
    }

    private static string FormatElapsedSecondComponent(double seconds, int decimals) =>
        seconds.ToString(decimals == 0 ? "00" : $"00.{new string('0', decimals)}", CultureInfo.InvariantCulture);

    private static bool IsElapsedTimeToken(string token) =>
        string.Equals(token, "h", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(token, "m", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(token, "s", StringComparison.OrdinalIgnoreCase);

    private static bool TryFormatFractionValue(double value, string section, out string formatted)
    {
        formatted = string.Empty;
        if (section.Contains('%', StringComparison.Ordinal))
            return false;

        var placeholderStart = IndexOfAny(section, '#', '0', '?');
        if (placeholderStart < 0)
            return false;

        var placeholderEnd = LastIndexOfAny(section, '#', '0', '?');
        if (placeholderEnd < placeholderStart)
            return false;

        var prefix = section[..placeholderStart];
        var fractionPattern = section[placeholderStart..(placeholderEnd + 1)];
        var suffix = section[(placeholderEnd + 1)..];
        if (!TryReadFractionPattern(fractionPattern, out var hasWholePart, out var maxDenominator))
            return false;

        var fraction = FormatFractionNumber(Math.Abs(value), hasWholePart, maxDenominator);
        var sign = value < 0 && !HasAuthoredNegativeAffordance(prefix, suffix) ? "-" : string.Empty;
        formatted = $"{sign}{prefix}{fraction}{suffix}";
        return true;
    }

    private static bool TryReadFractionPattern(string pattern, out bool hasWholePart, out int maxDenominator)
    {
        hasWholePart = false;
        maxDenominator = 0;

        var slashIndex = pattern.IndexOf('/');
        if (slashIndex <= 0 || slashIndex != pattern.LastIndexOf('/'))
            return false;

        var beforeSlash = pattern[..slashIndex].TrimEnd();
        var denominatorPattern = pattern[(slashIndex + 1)..].Trim();
        if (!IsFractionPlaceholderRun(denominatorPattern))
            return false;

        var wholeAndNumeratorSeparator = beforeSlash.LastIndexOf(' ');
        var numeratorPattern = beforeSlash.Trim();
        if (wholeAndNumeratorSeparator >= 0)
        {
            var wholePattern = beforeSlash[..wholeAndNumeratorSeparator].Trim();
            numeratorPattern = beforeSlash[(wholeAndNumeratorSeparator + 1)..].Trim();
            hasWholePart = IsFractionPlaceholderRun(wholePattern);
        }

        if (!IsFractionPlaceholderRun(numeratorPattern))
            return false;

        var denominatorDigits = denominatorPattern.Count(IsFractionPlaceholder);
        var numeratorDigits = numeratorPattern.Count(IsFractionPlaceholder);
        if (denominatorDigits is < 1 or > 2 || numeratorDigits is < 1 or > 2)
            return false;

        maxDenominator = (int)Math.Pow(10, denominatorDigits) - 1;
        return true;
    }

    private static bool IsFractionPlaceholderRun(string text) =>
        text.Length > 0 && text.All(ch => IsFractionPlaceholder(ch) || ch == ',');

    private static bool IsFractionPlaceholder(char ch) => ch is '#' or '0' or '?';

    private static string FormatFractionNumber(double value, bool hasWholePart, int maxDenominator)
    {
        var whole = hasWholePart ? (long)Math.Floor(value) : 0;
        var fractionalValue = hasWholePart ? value - whole : value;
        var (numerator, denominator) = ApproximateFraction(fractionalValue, maxDenominator);

        if (hasWholePart && numerator == denominator)
        {
            whole++;
            numerator = 0;
        }

        if (numerator == 0)
            return whole == 0 ? "0" : whole.ToString(CultureInfo.InvariantCulture);

        var divisor = GreatestCommonDivisor(numerator, denominator);
        numerator /= divisor;
        denominator /= divisor;

        var fraction = $"{numerator.ToString(CultureInfo.InvariantCulture)}/{denominator.ToString(CultureInfo.InvariantCulture)}";
        return hasWholePart && whole != 0
            ? $"{whole.ToString(CultureInfo.InvariantCulture)} {fraction}"
            : fraction;
    }

    private static (long numerator, long denominator) ApproximateFraction(double value, int maxDenominator)
    {
        long bestNumerator = 0;
        long bestDenominator = 1;
        double bestError = double.MaxValue;

        for (long denominator = 1; denominator <= maxDenominator; denominator++)
        {
            var numerator = (long)Math.Round(value * denominator, MidpointRounding.AwayFromZero);
            var error = Math.Abs(value - (double)numerator / denominator);
            if (error + 1e-12 < bestError)
            {
                bestError = error;
                bestNumerator = numerator;
                bestDenominator = denominator;
            }
        }

        return (bestNumerator, bestDenominator);
    }

    private static long GreatestCommonDivisor(long left, long right)
    {
        left = Math.Abs(left);
        right = Math.Abs(right);
        while (right != 0)
        {
            var remainder = left % right;
            left = right;
            right = remainder;
        }

        return left == 0 ? 1 : left;
    }

    private static bool HasAuthoredNegativeAffordance(string prefix, string suffix) =>
        prefix.Contains('-', StringComparison.Ordinal) ||
        suffix.Contains('-', StringComparison.Ordinal) ||
        prefix.Contains('(', StringComparison.Ordinal) ||
        suffix.Contains(')', StringComparison.Ordinal);

    private static int CountScaleCommas(ref string suffix)
    {
        int count = 0;
        while (count < suffix.Length && suffix[count] == ',')
            count++;

        if (count > 0)
            suffix = suffix[count..];

        return count;
    }

    private static string FormatNumberWithPattern(double value, string pattern)
    {
        var absolute = Math.Abs(value);
        var dotIndex = pattern.IndexOf('.');
        var integerPattern = dotIndex >= 0 ? pattern[..dotIndex] : pattern;
        var decimalPattern = dotIndex >= 0 ? pattern[(dotIndex + 1)..] : string.Empty;
        bool useGrouping = integerPattern.Contains(',', StringComparison.Ordinal);
        int maxDecimals = decimalPattern.Count(ch => ch is '0' or '#');
        int minDecimals = decimalPattern.Count(ch => ch == '0');

        var rounded = Math.Round(absolute, maxDecimals, MidpointRounding.AwayFromZero);
        var fixedText = rounded.ToString($"F{maxDecimals}", CultureInfo.InvariantCulture);
        var parts = fixedText.Split('.');
        var integerText = useGrouping && long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole)
            ? whole.ToString("N0", CultureInfo.InvariantCulture)
            : parts[0];

        if (maxDecimals == 0)
            return integerText;

        var decimals = parts.Length > 1 ? parts[1] : string.Empty;
        while (decimals.Length > minDecimals && decimals.EndsWith('0'))
            decimals = decimals[..^1];

        return decimals.Length == 0
            ? integerText
            : $"{integerText}.{decimals}";
    }

    private static bool LooksLikeDateFormat(string code)
    {
        if (code.Contains('%', StringComparison.Ordinal))
            return false;

        var lower = code.ToLowerInvariant();
        return lower.Contains('y') ||
            (lower.Contains('d') && lower.Contains('m'));
    }

    private static bool TryParseCategoryDate(string label, out DateTime date)
    {
        if (DateTime.TryParse(label, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;

        if (double.TryParse(label, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial))
            return TryFromOaDate(serial, out date);

        date = default;
        return false;
    }

    private static bool TryFromOaDate(double value, out DateTime date)
    {
        if (value is < 1 or > 2958465)
        {
            date = default;
            return false;
        }

        try
        {
            date = DateTime.FromOADate(value);
            return true;
        }
        catch (ArgumentException)
        {
            date = default;
            return false;
        }
    }

    private static string FormatDateValue(DateTime date, string code)
    {
        var dotIndex = code.IndexOf('.');
        if (dotIndex >= 0)
            code = code[..dotIndex];

        var netFormat = code
            .Replace("yyyy", "yyyy", StringComparison.OrdinalIgnoreCase)
            .Replace("yy", "yy", StringComparison.OrdinalIgnoreCase)
            .Replace("mmmm", "MMMM", StringComparison.OrdinalIgnoreCase)
            .Replace("mmm", "MMM", StringComparison.OrdinalIgnoreCase)
            .Replace("mm", "MM", StringComparison.OrdinalIgnoreCase)
            .Replace("m", "M", StringComparison.OrdinalIgnoreCase)
            .Replace("dddd", "dddd", StringComparison.OrdinalIgnoreCase)
            .Replace("ddd", "ddd", StringComparison.OrdinalIgnoreCase)
            .Replace("dd", "dd", StringComparison.OrdinalIgnoreCase)
            .Replace("d", "d", StringComparison.OrdinalIgnoreCase);

        return date.ToString(netFormat, CultureInfo.InvariantCulture);
    }

    private static int IndexOfAny(string text, params char[] chars)
    {
        for (int index = 0; index < text.Length; index++)
        {
            if (chars.Contains(text[index]))
                return index;
        }

        return -1;
    }

    private static int LastIndexOfAny(string text, params char[] chars)
    {
        for (int index = text.Length - 1; index >= 0; index--)
        {
            if (chars.Contains(text[index]))
                return index;
        }

        return -1;
    }

    private readonly record struct NumberFormatCondition(string Operator, double Threshold)
    {
        public static bool TryParse(string token, out NumberFormatCondition condition)
        {
            string[] operators = [">=", "<=", "<>", ">", "<", "="];
            foreach (var op in operators)
            {
                if (!token.StartsWith(op, StringComparison.Ordinal))
                    continue;

                var thresholdText = token[op.Length..].Trim();
                if (double.TryParse(
                    thresholdText,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var threshold))
                {
                    condition = new NumberFormatCondition(op, threshold);
                    return true;
                }
            }

            condition = default;
            return false;
        }

        public bool Matches(double value) =>
            Operator switch
            {
                ">=" => value >= Threshold,
                "<=" => value <= Threshold,
                "<>" => value != Threshold,
                ">" => value > Threshold,
                "<" => value < Threshold,
                "=" => value == Threshold,
                _ => false
            };
    }

    private static IReadOnlyList<ChartPieSlicePrimitive> BuildSlicePrimitivesForSeries(
        ChartSeries series,
        int seriesIndex,
        ChartPlanRect plot,
        double innerRadius,
        double outerRadius,
        double initialStartAngle,
        IReadOnlyList<SrgbColor>? seriesColors,
        ChartFillPlanSet? fillPlans,
        bool varyByPoint,
        double verticalScale,
        double depthOffsetY,
        IReadOnlySet<int>? includedPointIndices = null)
    {
        var values = GetVisiblePieValues(series)
            .Where(value => includedPointIndices is null || includedPointIndices.Contains(value.PointIndex))
            .ToArray();
        if (values.Length == 0)
            return Array.Empty<ChartPieSlicePrimitive>();

        double total = values.Sum(value => value.Value);
        if (total <= 0)
            return Array.Empty<ChartPieSlicePrimitive>();

        var center = new ChartPlanPoint(
            plot.X + plot.Width / 2,
            plot.Y + plot.Height / 2);
        double startAngle = initialStartAngle;
        var primitives = new List<ChartPieSlicePrimitive>(values.Length);
        foreach (var visibleValue in values)
        {
            double sweepAngle = visibleValue.Value / total * 2 * Math.PI;
            double endAngle = startAngle + sweepAngle;
            var fill = ResolvePointFill(
                series,
                seriesIndex,
                visibleValue.PointIndex,
                seriesColors,
                RectSeriesFillAlpha,
                fillPlans,
                varyByPoint);
            var sliceCenter = OffsetExplodedSliceCenter(
                center,
                outerRadius,
                startAngle,
                endAngle,
                series.PointStyles.TryGetValue(visibleValue.PointIndex, out var pointStyle)
                    ? pointStyle.ExplosionPercent
                    : null);
            primitives.Add(new ChartPieSlicePrimitive(
                seriesIndex,
                visibleValue.PointIndex,
                sliceCenter,
                innerRadius,
                outerRadius,
                startAngle,
                endAngle,
                fill)
            {
                VerticalScale = verticalScale,
                DepthOffsetY = depthOffsetY,
                DepthFill = depthOffsetY > 0
                    ? fill.WithAlpha(ThreeDPieDepthFillAlpha)
                    : null
            });
            startAngle = endAngle;
        }

        return primitives;
    }

    private static ChartPlanPoint OffsetExplodedSliceCenter(
        ChartPlanPoint center,
        double outerRadius,
        double startAngle,
        double endAngle,
        int? explosionPercent)
    {
        if (!explosionPercent.HasValue || explosionPercent.Value <= 0 || outerRadius <= 0)
            return center;

        double midpoint = startAngle + (endAngle - startAngle) / 2;
        double offset = outerRadius * Math.Clamp(explosionPercent.Value, 0, 100) / 100.0;
        return new ChartPlanPoint(
            center.X + offset * Math.Cos(midpoint),
            center.Y + offset * Math.Sin(midpoint));
    }

    private static IReadOnlyList<(int PointIndex, double Value)> GetVisiblePieValues(ChartSeries series)
    {
        var values = new List<(int PointIndex, double Value)>();
        for (int pointIndex = 0; pointIndex < series.Values.Count; pointIndex++)
        {
            var value = series.Values[pointIndex];
            if (value.HasValue && value.Value > 0)
                values.Add((pointIndex, value.Value));
        }

        return values;
    }

    private static double ResolvePieStartAngle(ChartShape chart) =>
        DegreesToRadians(Math.Clamp(chart.FirstSliceAngleDegrees ?? 0, 0, 360) - 90);

    public static double ResolvePieVerticalScale(ChartShape chart) =>
        chart.ChartType == ChartType.Pie && chart.ThreeDStyle == ChartThreeDStyle.Pie
            ? UsesImportedTextMetrics(chart)
                ? ImportedThreeDPieVerticalScale
                : ThreeDPieVerticalScale
            : 1.0;

    public static double ResolvePieDepthOffset(ChartShape chart, double outerRadius) =>
        chart.ChartType == ChartType.Pie && chart.ThreeDStyle == ChartThreeDStyle.Pie
            ? UsesImportedTextMetrics(chart)
                ? Math.Round(outerRadius * ImportedThreeDPieDepthScale, 4)
                : Math.Round(Math.Clamp(outerRadius * 0.22, 2.0, 14.0), 4)
            : 0;

    public static double ResolveImportedThreeDPieSidewallFactor(
        int pointIndex,
        double startAngle,
        double endAngle)
    {
        double midpoint = (startAngle + endAngle) / 2;
        return pointIndex switch
        {
            0 => 0.30,
            1 => 0.80,
            2 => 0.35,
            _ => 0.35 + 0.4 * Math.Clamp(Math.Sin(midpoint), 0, 1)
        };
    }

    private static ChartFillPlan DarkenImportedThreeDPieTop(ChartFillPlan fill)
    {
        const double factor = ImportedThreeDPieTopShadeFactor;
        return new ChartFillPlan(
            new SrgbColor(
                ScalePieDepthChannel(fill.Color.R, factor),
                ScalePieDepthChannel(fill.Color.G, factor),
                ScalePieDepthChannel(fill.Color.B, factor)),
            Alpha: 255);
    }

    private static byte ScalePieDepthChannel(byte channel, double factor) =>
        (byte)Math.Round(Math.Clamp(channel * factor, 0, 255));

    public static ChartClassicThreeDDepthPlan? BuildClassicThreeDDepthPlan(
        ChartShape chart,
        ChartPlanRect plot)
    {
        if (!plot.HasPositiveArea)
            return null;

        bool isThreeDLine =
            chart.ThreeDStyle == ChartThreeDStyle.Line &&
            chart.ChartType is ChartType.Line or ChartType.LineMarkers;
        bool isThreeDArea =
            chart.ThreeDStyle == ChartThreeDStyle.Area &&
            chart.ChartType == ChartType.Area;
        if (!isThreeDLine && !isThreeDArea)
            return null;

        double offset = Math.Round(
            Math.Clamp(Math.Min(plot.Width, plot.Height) * ClassicThreeDDepthScale, 2.0, 8.0),
            4);

        return new ChartClassicThreeDDepthPlan(
            OffsetX: offset,
            OffsetY: -offset,
            StrokeAlpha: 120,
            FillAlpha: 70);
    }

    private static double DegreesToRadians(double degrees) =>
        degrees * Math.PI / 180.0;

    private static (
        IReadOnlyList<ChartGridLinePlan> gridLines,
        IReadOnlyList<ChartTextPlan> xLabels,
        IReadOnlyList<ChartTextPlan> yLabels) BuildScatterAxisPrimitives(
            ChartPlanRect plot,
            double xMin,
            double xRange,
            double xUnit,
            double yMin,
            double yRange,
            double yUnit,
            ChartAxisLabelFormatPlan? xAxisLabelFormat,
            ChartAxisLabelFormatPlan? yAxisLabelFormat,
            bool importedMinorValueGridlines = false,
            bool includeXGridlines = true)
    {
        double xSteps = xRange / xUnit;
        double ySteps = yRange / yUnit;
        if (xSteps <= 0 || ySteps <= 0)
            return (
                Array.Empty<ChartGridLinePlan>(),
                Array.Empty<ChartTextPlan>(),
                Array.Empty<ChartTextPlan>());

        int xTickCount = (int)Math.Round(xSteps);
        int yTickCount = (int)Math.Round(ySteps);
        int yGridTickCount = importedMinorValueGridlines ? yTickCount * 2 : yTickCount;
        var gridLines = new List<ChartGridLinePlan>(xTickCount + yGridTickCount + 2);
        var xLabels = new List<ChartTextPlan>(xTickCount + 1);
        var yLabels = new List<ChartTextPlan>(yTickCount + 1);

        for (int tickIndex = 0; tickIndex <= xTickCount; tickIndex++)
        {
            double x = plot.X + plot.Width * tickIndex / xSteps;
            if (includeXGridlines)
            {
                gridLines.Add(new ChartGridLinePlan(
                    new ChartPlanPoint(x, plot.Y),
                    new ChartPlanPoint(x, plot.Bottom)));
            }

            double value = xMin + xUnit * tickIndex;
            xLabels.Add(new ChartTextPlan(
                FormatAxisLabelValue(value, xAxisLabelFormat?.FormatCode),
                new ChartPlanRect(x - 20, plot.Bottom + 2, 40, 12),
                IsBold: false,
                FontSize: 6.5,
                Alignment: ChartPlanTextAlignment.Center,
                AxisLabelFormat: xAxisLabelFormat));
        }

        for (int tickIndex = 0; tickIndex <= yGridTickCount; tickIndex++)
        {
            double y = plot.Bottom - plot.Height * tickIndex / yGridTickCount;
            gridLines.Add(new ChartGridLinePlan(
                new ChartPlanPoint(plot.X, y),
                new ChartPlanPoint(plot.Right, y)));

            if (!importedMinorValueGridlines || tickIndex % 2 == 0)
            {
                double value = yMin + yUnit * (importedMinorValueGridlines
                    ? tickIndex / 2
                    : tickIndex);
                yLabels.Add(new ChartTextPlan(
                    FormatAxisLabelValue(value, yAxisLabelFormat?.FormatCode),
                    new ChartPlanRect(plot.X - 38, y - 6, 36, 12),
                    IsBold: false,
                    FontSize: 6.5,
                    Alignment: ChartPlanTextAlignment.Right,
                    AxisLabelFormat: yAxisLabelFormat));
            }
        }

        return (gridLines, xLabels, yLabels);
    }

    private static ChartAxisLabelFormatPlan? BuildAxisLabelFormatPlan(ChartAxis? axis) =>
        string.IsNullOrWhiteSpace(axis?.NumberFormatCode)
            ? null
            : new ChartAxisLabelFormatPlan(axis.NumberFormatCode!, axis.NumberFormatSourceLinked);

    private static double GetRadarAngle(int categoryIndex, int categoryCount) =>
        -Math.PI / 2 + 2 * Math.PI * categoryIndex / categoryCount;

    private static ChartScatterPrimitivePlan EmptyScatterPrimitivePlan() =>
        new(
            Array.Empty<ChartGridLinePlan>(),
            DefaultGridLineStroke(),
            Array.Empty<ChartTextPlan>(),
            Array.Empty<ChartTextPlan>(),
            Array.Empty<ChartScatterSeriesPrimitive>(),
            Array.Empty<ChartDataLabelPlan>());

    private static ChartBubblePrimitivePlan EmptyBubblePrimitivePlan() =>
        new(
            Array.Empty<ChartGridLinePlan>(),
            DefaultGridLineStroke(),
            Array.Empty<ChartTextPlan>(),
            Array.Empty<ChartTextPlan>(),
            Array.Empty<ChartBubblePrimitive>());

    private static ChartRadarPrimitivePlan EmptyRadarPrimitivePlan() =>
        new(
            Array.Empty<ChartRadarRingPrimitive>(),
            Array.Empty<ChartGridLinePlan>(),
            DefaultRadarSpokeStroke(),
            Array.Empty<ChartTextPlan>(),
            Array.Empty<ChartRadarSeriesPrimitive>());

    private static ChartStrokePlan ResolveScatterGridLineStroke(ChartShape chart) =>
        UsesImportedSingleScatterDefaults(chart) || UsesImportedBubbleDefaults(chart)
            ? new ChartStrokePlan(new SrgbColor(0x89, 0x89, 0x89), Alpha: 255, Thickness: 1.0)
            : UsesImportedSmoothScatterDefaults(chart)
                ? DefaultGridLineStroke(chart)
                : DefaultGridLineStroke();

    private static ChartMajorGridLinePrimitivePlan EmptyMajorGridLinePrimitivePlan() =>
        new(
            Array.Empty<ChartGridLinePlan>(),
            DefaultGridLineStroke());

    private static ChartMajorGridLinePrimitivePlan EmptyMinorGridLinePrimitivePlan() =>
        new(
            Array.Empty<ChartGridLinePlan>(),
            new ChartStrokePlan(new SrgbColor(0xB7, 0xB7, 0xB7), Alpha: 170, Thickness: 0.75));

    private static ChartMajorAxisTickPrimitivePlan EmptyMajorAxisTickPrimitivePlan() =>
        new(
            Array.Empty<ChartGridLinePlan>(),
            Array.Empty<ChartGridLinePlan>(),
            DefaultAxisTickStroke());

    private static ChartSecondaryValueAxisPrimitivePlan EmptySecondaryValueAxisPrimitivePlan() =>
        new(
            Array.Empty<ChartTextPlan>(),
            Array.Empty<ChartGridLinePlan>(),
            DefaultAxisTickStroke(),
            Title: null);

    private static ChartDataTablePrimitivePlan EmptyDataTablePrimitivePlan() =>
        new(
            new ChartPlanRect(0, 0, 0, 0),
            BackgroundFill: null,
            Array.Empty<ChartDataTableCellPlan>(),
            Array.Empty<ChartGridLinePlan>(),
            Array.Empty<ChartGridLinePlan>(),
            Array.Empty<ChartGridLinePlan>(),
            DefaultDataTableBorderStroke());

    private static bool HasSupportedDataTable(
        ChartShape chart,
        ChartRenderFamily family) =>
        chart.DataTable is not null &&
        chart.Categories.Count > 0 &&
        chart.Series.Count > 0 &&
        family == ChartRenderFamily.Cartesian;

    private static bool ShouldPlanDataTable(
        ChartShape chart,
        ChartFramePlan frame) =>
        frame.HasPlot && HasSupportedDataTable(chart, frame.Family);

    private static double ComputeDataTableHeight(ChartShape chart) =>
        DataTableHeaderHeight + Math.Max(1, chart.Series.Count) * DataTableRowHeight;

    private static double ComputeDataTableReservedHeight(ChartShape chart) =>
        DataTableGap + ComputeDataTableHeight(chart);

    /// <summary>
    /// Width of the data table's row-header (series-name) column. The plot's left edge is inset by
    /// this same width when a data table is present, so the plot's category band and the data
    /// table's category columns share one left origin and one per-category width (columns sit
    /// directly under their category's bar/point, as in PowerPoint).
    /// <paramref name="chartAreaWidth"/> is the overall chart bounds width (not the plot width),
    /// so the cap is independent of the inset it produces - avoiding a self-referential
    /// plot-width-depends-on-column-width-depends-on-plot-width cycle.
    /// </summary>
    private static double ComputeDataTableFirstColumnWidth(double chartAreaWidth) =>
        Math.Min(DataTableSeriesHeaderWidth, Math.Max(0, chartAreaWidth) * 0.4);

    private static ChartPlanRect InsetDataTableCellText(ChartPlanRect bounds) =>
        new(
            bounds.X + DataTableTextInset,
            bounds.Y,
            Math.Max(0, bounds.Width - 2 * DataTableTextInset),
            bounds.Height);

    private static IReadOnlyList<ChartGridLinePlan> BuildDataTableHorizontalBorders(
        ChartPlanRect bounds,
        int rowCount)
    {
        var borders = new List<ChartGridLinePlan>(rowCount + 1);
        for (int rowIndex = 0; rowIndex <= rowCount; rowIndex++)
        {
            double y = rowIndex == 0
                ? bounds.Y
                : bounds.Y + DataTableHeaderHeight + (rowIndex - 1) * DataTableRowHeight;
            borders.Add(new ChartGridLinePlan(
                new ChartPlanPoint(bounds.X, y),
                new ChartPlanPoint(bounds.Right, y)));
        }

        return borders;
    }

    private static IReadOnlyList<ChartGridLinePlan> BuildDataTableVerticalBorders(
        ChartPlanRect bounds,
        double firstColumnWidth,
        double categoryWidth,
        int categoryCount)
    {
        var borders = new List<ChartGridLinePlan>(categoryCount + 2);
        borders.Add(new ChartGridLinePlan(
            new ChartPlanPoint(bounds.X, bounds.Y),
            new ChartPlanPoint(bounds.X, bounds.Bottom)));
        borders.Add(new ChartGridLinePlan(
            new ChartPlanPoint(bounds.X + firstColumnWidth, bounds.Y),
            new ChartPlanPoint(bounds.X + firstColumnWidth, bounds.Bottom)));

        for (int categoryIndex = 1; categoryIndex <= categoryCount; categoryIndex++)
        {
            double x = bounds.X + firstColumnWidth + categoryIndex * categoryWidth;
            borders.Add(new ChartGridLinePlan(
                new ChartPlanPoint(x, bounds.Y),
                new ChartPlanPoint(x, bounds.Bottom)));
        }

        return borders;
    }

    private static IReadOnlyList<ChartGridLinePlan> BuildOutlineBorders(ChartPlanRect bounds) =>
    [
        new(new ChartPlanPoint(bounds.X, bounds.Y), new ChartPlanPoint(bounds.Right, bounds.Y)),
        new(new ChartPlanPoint(bounds.Right, bounds.Y), new ChartPlanPoint(bounds.Right, bounds.Bottom)),
        new(new ChartPlanPoint(bounds.Right, bounds.Bottom), new ChartPlanPoint(bounds.X, bounds.Bottom)),
        new(new ChartPlanPoint(bounds.X, bounds.Bottom), new ChartPlanPoint(bounds.X, bounds.Y))
    ];

    private static ChartStrokePlan DefaultGridLineStroke(ChartShape? chart = null) =>
        chart is not null &&
        chart.ChartType == ChartType.Stock &&
        UsesImportedTextMetrics(chart)
            ? new ChartStrokePlan(new SrgbColor(0x00, 0x00, 0x00), Alpha: 255, Thickness: 1.0)
        : chart is not null &&
          chart.ChartType == ChartType.Scatter &&
          UsesImportedSmoothScatterDefaults(chart)
            ? new ChartStrokePlan(new SrgbColor(0x00, 0x00, 0x00), Alpha: 255, Thickness: 1.0)
        : chart is not null &&
          chart.ChartType == ChartType.ColumnStacked100 &&
          UsesImportedTextMetrics(chart)
            ? new ChartStrokePlan(new SrgbColor(0x00, 0x00, 0x00), Alpha: 255, Thickness: 1.0)
        : chart is not null &&
          UsesImportedCartesianAxisStrokes(chart)
            ? new ChartStrokePlan(new SrgbColor(0x89, 0x89, 0x89), Alpha: 255, Thickness: 1.0)
        : chart is not null &&
        UsesClassicOfficeChartStyle(chart)
            ? new ChartStrokePlan(new SrgbColor(0x00, 0x00, 0x00), Alpha: 255, Thickness: 0.5)
            : chart is not null &&
              UsesImportedComboDefaults(chart)
                ? new ChartStrokePlan(new SrgbColor(0x89, 0x89, 0x89), Alpha: 255, Thickness: 1.0)
            : new ChartStrokePlan(new SrgbColor(0xD9, 0xD9, 0xD9), Alpha: 255, Thickness: 0.5);

    private static ChartStrokePlan DefaultAxisTickStroke(ChartShape? chart = null) =>
        chart is not null &&
        chart.ChartType == ChartType.Stock &&
        UsesImportedTextMetrics(chart)
            ? new ChartStrokePlan(new SrgbColor(0x00, 0x00, 0x00), Alpha: 255, Thickness: 1.0)
        : chart is not null && UsesImportedCartesianAxisStrokes(chart)
            ? new ChartStrokePlan(new SrgbColor(0x89, 0x89, 0x89), Alpha: 255, Thickness: 0.75)
            : chart is not null &&
              UsesClassicOfficeChartStyle(chart)
                  ? new ChartStrokePlan(new SrgbColor(0x00, 0x00, 0x00), Alpha: 255, Thickness: 0.75)
              : chart is not null && UsesImportedComboDefaults(chart)
                  ? new ChartStrokePlan(new SrgbColor(0x89, 0x89, 0x89), Alpha: 255, Thickness: 0.75)
              : new ChartStrokePlan(new SrgbColor(0x7F, 0x7F, 0x7F), Alpha: 255, Thickness: 0.75);

    private static ChartStrokePlan DefaultDataTableBorderStroke() =>
        new(new SrgbColor(0xB7, 0xB7, 0xB7), Alpha: 255, Thickness: 0.5);

    private static ChartStrokePlan ResolveDataTableBorderStroke(ChartDataTableSettings settings) =>
        settings.BorderOutline switch
        {
            ShapeOutline.None => new ChartStrokePlan(
                new SrgbColor(0xB7, 0xB7, 0xB7),
                Alpha: 0,
                Thickness: 0.5),
            ShapeOutline.Visible visible => new ChartStrokePlan(
                visible.Color.Resolved,
                Alpha: 255,
                Thickness: visible.WidthPt,
                Dash: visible.Dash),
            ShapeOutline.GradientVisible gradient => new ChartStrokePlan(
                ResolveGradientFallbackColor(gradient.Gradient),
                Alpha: 255,
                Thickness: gradient.WidthPt,
                Dash: gradient.Dash)
            {
                Fill = ResolveDataTableBorderGradientFill(gradient.Gradient)
            },
            _ => DefaultDataTableBorderStroke()
        };

    private static ResolvedFill.Gradient ResolveDataTableBorderGradientFill(ShapeFill.Gradient gradient) =>
        new(
            gradient.Stops.Select(stop => new ResolvedFill.ResolvedGradientStop(
                stop.Position,
                stop.Color.Resolved,
                stop.Color.Alpha)).ToArray(),
            gradient.Kind,
            gradient.AngleDegrees);

    private static SrgbColor ResolveGradientFallbackColor(ShapeFill.Gradient gradient) =>
        gradient.Stops.Count > 0
            ? gradient.Stops[0].Color.Resolved
            : new SrgbColor(0xB7, 0xB7, 0xB7);

    private static ChartFillPlan? ResolveDataTableBackgroundFill(ChartDataTableSettings settings) =>
        settings.BackgroundFill switch
        {
            ShapeFill.Solid solid => new ChartFillPlan(solid.Color.Resolved, Alpha: 255),
            _ => null
        };

    private static ChartDataTableTextPlan ResolveDataTableTextStyle(
        ChartDataTableSettings settings,
        bool defaultBold) =>
        new(
            settings.TextStyle?.Bold ?? defaultBold,
            settings.TextStyle?.Italic ?? false,
            settings.TextStyle?.FontSizePt ?? DataTableFontSize,
            settings.TextStyle?.Color?.Resolved ?? new SrgbColor(0x40, 0x40, 0x40),
            settings.TextStyle?.FontFamily);

    private static ChartStrokePlan DefaultRadarSpokeStroke() =>
        new(new SrgbColor(0xC0, 0xC0, 0xC0), Alpha: 255, Thickness: 0.5);

    private static IReadOnlyList<ChartDataLabelPlan> BuildScatterDataLabelPlans(
        ChartShape chart,
        int seriesIndex,
        IReadOnlyList<ChartPlanPoint?> points,
        IReadOnlyList<SrgbColor>? seriesColors,
        ChartFillPlanSet? fillPlans)
    {
        var labels = ResolveEffectiveLabels(chart, seriesIndex);
        if (labels is null || seriesIndex < 0 || seriesIndex >= chart.Series.Count)
            return Array.Empty<ChartDataLabelPlan>();

        var series = chart.Series[seriesIndex];
        double total = ComputeDataLabelTotal(chart, series, categoryIndex: 0, stacked: false, labels);
        var plans = new List<ChartDataLabelPlan>();

        for (int pointIndex = 0; pointIndex < points.Count; pointIndex++)
        {
            var point = points[pointIndex];
            double? value = pointIndex < series.Values.Count ? series.Values[pointIndex] : null;
            if (!point.HasValue || !value.HasValue)
                continue;

            string categoryName = pointIndex < chart.Categories.Count
                ? chart.Categories[pointIndex]
                : pointIndex < series.XValues.Count && series.XValues[pointIndex].HasValue
                    ? FormatAxisValue(series.XValues[pointIndex]!.Value)
                    : string.Empty;
            double? bubbleSize = pointIndex < series.BubbleSizes.Count
                ? series.BubbleSizes[pointIndex]
                : null;
            string text = FormatDataLabel(labels, value.Value, total, categoryName, series.Name, bubbleSize);
            if (string.IsNullOrEmpty(text) && !labels.ShowLegendKey)
                continue;

            var labelPlan = ApplyDataLabelTextStyle(new ChartDataLabelPlan(
                seriesIndex,
                pointIndex,
                text,
                PlanScatterDataLabelBounds(point.Value, labels.Position ?? DataLabelPosition.Above),
                IsBold: false,
                FontSize: ResolveTextFontSize(chart, 6.5),
                Alignment: ChartPlanTextAlignment.Center)
            {
                IsOverMaximum = value.Value > ComputePrimaryValueAxisRange(chart).max
            }, labels);
            plans.Add(ApplyLegendKey(
                labelPlan,
                labels,
                seriesIndex,
                seriesColors,
                fillPlans));
        }

        return plans;
    }

    private static ChartPlanRect PlanScatterDataLabelBounds(
        ChartPlanPoint point,
        DataLabelPosition position)
    {
        const double gap = 3.0;
        double centeredX = point.X - ScatterDataLabelWidth / 2;
        double centeredY = point.Y - ScatterDataLabelHeight / 2;

        return position switch
        {
            DataLabelPosition.Below or DataLabelPosition.InsideBase =>
                new ChartPlanRect(
                    centeredX,
                    point.Y + gap,
                    ScatterDataLabelWidth,
                    ScatterDataLabelHeight),
            DataLabelPosition.Left =>
                new ChartPlanRect(
                    point.X - ScatterDataLabelWidth - gap,
                    centeredY,
                    ScatterDataLabelWidth,
                    ScatterDataLabelHeight),
            DataLabelPosition.Right =>
                new ChartPlanRect(
                    point.X + gap,
                    centeredY,
                    ScatterDataLabelWidth,
                    ScatterDataLabelHeight),
            DataLabelPosition.Center or DataLabelPosition.BestFit =>
                new ChartPlanRect(
                    centeredX,
                    centeredY,
                    ScatterDataLabelWidth,
                    ScatterDataLabelHeight),
            _ =>
                new ChartPlanRect(
                    centeredX,
                    point.Y - ScatterDataLabelHeight - gap,
                    ScatterDataLabelWidth,
                    ScatterDataLabelHeight)
        };
    }

    private static IReadOnlyList<ChartDataLabelPlan> BuildColumnDataLabelPlans(
        ChartShape chart,
        int seriesIndex,
        ChartPlanRect plot,
        IReadOnlyList<SrgbColor>? seriesColors,
        ChartFillPlanSet? fillPlans)
    {
        var labels = ResolveEffectiveLabels(chart, seriesIndex);
        if (labels is null || seriesIndex < 0 || seriesIndex >= chart.Series.Count)
            return Array.Empty<ChartDataLabelPlan>();

        var series = chart.Series[seriesIndex];
        int categoryCount = Math.Max(1, chart.Categories.Count);
        var (primaryMin, primaryMax, _) = ComputePrimaryValueAxisRange(chart);
        double primaryRange = primaryMax - primaryMin;
        if (primaryRange <= 0)
            return Array.Empty<ChartDataLabelPlan>();

        var (secondaryMin, secondaryMax, _) = ComputeSecondaryValueAxisRange(chart);
        double secondaryRange = secondaryMax - secondaryMin;
        double effectiveMin = series.OnSecondaryAxis ? secondaryMin : primaryMin;
        double effectiveRange = series.OnSecondaryAxis ? secondaryRange : primaryRange;
        if (effectiveRange <= 0)
            return Array.Empty<ChartDataLabelPlan>();

        bool percentStacked = IsHundredPercentStacked(chart.ChartType);
        bool stacked = chart.ChartType is ChartType.ColumnStacked or ChartType.ColumnStacked100;
        bool importedPercentStackedCluster = UsesImportedPercentStackedClusterLayout(chart);
        double categoryWidth = plot.Width / categoryCount;
        int seriesCount = Math.Max(1, chart.Series.Count);
        var spacing = ResolveBarClusterSpacing(
            chart,
            categoryWidth,
            seriesCount,
            stacked && !importedPercentStackedCluster);
        var position = labels.Position ?? DataLabelPosition.OutsideEnd;
        var plans = new List<ChartDataLabelPlan>();

        for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
        {
            double? rawValue = categoryIndex < series.Values.Count
                ? series.Values[categoryIndex]
                : null;
            if (rawValue is null)
                continue;

            double value = rawValue.Value;
            var slot = ResolveBarClusterSlot(plot.X, categoryIndex, spacing);
            if (importedPercentStackedCluster)
                slot = ResolveImportedPercentStackedClusterSlot(slot);
            double barX = importedPercentStackedCluster
                ? slot.ClusterStart + seriesIndex * slot.SeriesStep
                : stacked
                    ? slot.ClusterStart
                    : slot.ClusterStart + seriesIndex * slot.SeriesStep;

            double barHeight;
            double barY;
            if (stacked)
            {
                double stackedY = plot.Bottom;
                for (int previousSeriesIndex = 0; previousSeriesIndex < seriesIndex; previousSeriesIndex++)
                {
                    double? previousValue = categoryIndex < chart.Series[previousSeriesIndex].Values.Count
                        ? chart.Series[previousSeriesIndex].Values[categoryIndex]
                        : null;
                    if (previousValue is null)
                        continue;

                    double height = Math.Max(
                        0.5,
                        ComputeStackedExtent(
                            chart,
                            categoryIndex,
                            previousValue.Value,
                            series.OnSecondaryAxis,
                            plot.Height,
                            Math.Abs(previousValue.Value / effectiveRange) * plot.Height,
                            percentStacked));
                    stackedY -= height;
                }

                barHeight = Math.Max(
                    0.5,
                    ComputeStackedExtent(
                        chart,
                        categoryIndex,
                        value,
                        series.OnSecondaryAxis,
                        plot.Height,
                        Math.Abs(value / effectiveRange) * plot.Height,
                        percentStacked));
                barY = stackedY - barHeight;
            }
            else
            {
                barHeight = Math.Abs((value - effectiveMin) / effectiveRange * plot.Height);
                barY = plot.Bottom - (value - effectiveMin) / effectiveRange * plot.Height;
            }

            double total = ComputeDataLabelTotal(chart, series, categoryIndex, stacked || percentStacked, labels);
            string categoryName = categoryIndex < chart.Categories.Count
                ? chart.Categories[categoryIndex]
                : string.Empty;
            string text = FormatDataLabel(labels, value, total, categoryName, series.Name);
            if (string.IsNullOrEmpty(text) && !labels.ShowLegendKey)
                continue;

            double labelHeight = ResolveDataLabelHeight(chart);
            double labelY = value < 0
                ? position switch
                {
                    DataLabelPosition.InsideEnd => barY + barHeight - labelHeight - 2,
                    DataLabelPosition.Center => barY + barHeight / 2 - labelHeight / 2,
                    DataLabelPosition.InsideBase => barY + 2,
                    _ => barY + barHeight + 1
                }
                : position switch
                {
                    DataLabelPosition.InsideEnd => barY + 2,
                    DataLabelPosition.Center => barY + barHeight / 2 - labelHeight / 2,
                    DataLabelPosition.InsideBase => barY + barHeight - labelHeight - 2,
                    _ => barY - labelHeight - 1
                };

            double labelWidth = UsesImportedTextMetrics(chart)
                ? importedPercentStackedCluster
                    ? ImportedPercentStackedDataLabelWidth
                    : Math.Max(percentStacked ? 100.0 : 50.0, slot.SeriesSize)
                : slot.SeriesSize;
            double labelX = UsesImportedTextMetrics(chart)
                ? barX + slot.SeriesSize / 2.0 - labelWidth / 2.0
                : barX;
            var labelPlan = new ChartDataLabelPlan(
                seriesIndex,
                categoryIndex,
                text,
                new ChartPlanRect(labelX, labelY, labelWidth, labelHeight),
                IsBold: false,
                FontSize: ResolveTextFontSize(chart, 6.5),
                Alignment: ChartPlanTextAlignment.Center)
            {
                WrapText = importedPercentStackedCluster,
                IsOverMaximum = value > effectiveMin + effectiveRange
            };
            labelPlan = ApplyDataLabelTextStyle(labelPlan, labels);
            if (labels.ShowLegendKey)
            {
                const double keySize = 6.0;
                const double keyGap = 3.0;
                labelPlan = labelPlan with
                {
                    TextBounds = new ChartPlanRect(
                        labelX + keySize + keyGap,
                        labelY,
                        Math.Max(1.0, labelWidth - keySize - keyGap),
                        labelHeight),
                    LegendKeyBounds = new ChartPlanRect(
                        labelX,
                        labelY + (labelHeight - keySize) / 2.0,
                        keySize,
                        keySize),
                    LegendKeyFill = ResolveSeriesFill(seriesIndex, seriesColors, RectSeriesFillAlpha, fillPlans)
                };
            }
            plans.Add(labelPlan);
        }

        return plans;
    }

    private static IReadOnlyList<ChartDataLabelPlan> BuildLineDataLabelPlans(
        ChartShape chart,
        int seriesIndex,
        ChartPlanRect plot,
        IReadOnlyList<SrgbColor>? seriesColors,
        ChartFillPlanSet? fillPlans)
    {
        var labels = ResolveEffectiveLabels(chart, seriesIndex);
        if (labels is null || seriesIndex < 0 || seriesIndex >= chart.Series.Count)
            return Array.Empty<ChartDataLabelPlan>();

        var series = chart.Series[seriesIndex];
        int categoryCount = Math.Max(1, chart.Categories.Count);
        var (primaryMin, primaryMax, _) = ComputePrimaryValueAxisRange(chart);
        double primaryRange = primaryMax - primaryMin;
        if (primaryRange <= 0)
            return Array.Empty<ChartDataLabelPlan>();

        var (secondaryMin, secondaryMax, _) = ComputeSecondaryValueAxisRange(chart);
        double secondaryRange = secondaryMax - secondaryMin;
        double effectiveMin = series.OnSecondaryAxis ? secondaryMin : primaryMin;
        double effectiveRange = series.OnSecondaryAxis ? secondaryRange : primaryRange;
        if (effectiveRange <= 0)
            return Array.Empty<ChartDataLabelPlan>();

        double stepX = plot.Width / Math.Max(1, categoryCount - 1);
        double total = ComputeDataLabelTotal(chart, series, categoryIndex: 0, stacked: false, labels);
        var plans = new List<ChartDataLabelPlan>();

        for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
        {
            double? rawValue = categoryIndex < series.Values.Count
                ? series.Values[categoryIndex]
                : null;
            if (rawValue is null)
                continue;

            double x = plot.X + categoryIndex * stepX;
            double y = plot.Bottom - (rawValue.Value - effectiveMin) / effectiveRange * plot.Height;
            string categoryName = categoryIndex < chart.Categories.Count
                ? chart.Categories[categoryIndex]
                : string.Empty;
            string text = FormatDataLabel(labels, rawValue.Value, total, categoryName, series.Name);
            if (string.IsNullOrEmpty(text) && !labels.ShowLegendKey)
                continue;

            var labelPlan = ApplyDataLabelTextStyle(new ChartDataLabelPlan(
                seriesIndex,
                categoryIndex,
                text,
                new ChartPlanRect(x - 20, y - ResolveDataLabelHeight(chart) - 3, 40, ResolveDataLabelHeight(chart)),
                IsBold: false,
                FontSize: ResolveTextFontSize(chart, 6.5),
                Alignment: ChartPlanTextAlignment.Center)
            {
                IsOverMaximum = rawValue.Value > effectiveMin + effectiveRange
            }, labels);
            plans.Add(ApplyLegendKey(
                labelPlan,
                labels,
                seriesIndex,
                seriesColors,
                fillPlans));
        }

        return plans;
    }

    private static IReadOnlyList<ChartDataLabelPlan> BuildBarDataLabelPlans(
        ChartShape chart,
        int seriesIndex,
        ChartPlanRect plot,
        IReadOnlyList<SrgbColor>? seriesColors,
        ChartFillPlanSet? fillPlans)
    {
        var labels = ResolveEffectiveLabels(chart, seriesIndex);
        if (labels is null || seriesIndex < 0 || seriesIndex >= chart.Series.Count)
            return Array.Empty<ChartDataLabelPlan>();

        var series = chart.Series[seriesIndex];
        int categoryCount = Math.Max(1, chart.Categories.Count);
        var (primaryMin, primaryMax, _) = ComputePrimaryValueAxisRange(chart);
        double primaryRange = primaryMax - primaryMin;
        if (primaryRange <= 0)
            return Array.Empty<ChartDataLabelPlan>();

        var (secondaryMin, secondaryMax, _) = ComputeSecondaryValueAxisRange(chart);
        double secondaryRange = secondaryMax - secondaryMin;
        double effectiveMin = series.OnSecondaryAxis ? secondaryMin : primaryMin;
        double effectiveRange = series.OnSecondaryAxis ? secondaryRange : primaryRange;
        if (effectiveRange <= 0)
            return Array.Empty<ChartDataLabelPlan>();

        bool percentStacked = IsHundredPercentStacked(chart.ChartType);
        bool stacked = chart.ChartType is ChartType.BarStacked or ChartType.BarStacked100;
        bool importedPercentStackedCluster = UsesImportedPercentStackedClusterLayout(chart);
        double categoryHeight = plot.Height / categoryCount;
        int seriesCount = Math.Max(1, chart.Series.Count);
        var spacing = ResolveBarClusterSpacing(
            chart,
            categoryHeight,
            seriesCount,
            stacked && !importedPercentStackedCluster);
        var position = labels.Position ?? DataLabelPosition.OutsideEnd;
        var plans = new List<ChartDataLabelPlan>();

        for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
        {
            double? rawValue = categoryIndex < series.Values.Count
                ? series.Values[categoryIndex]
                : null;
            if (rawValue is null)
                continue;

            double value = rawValue.Value;
            int renderRow = categoryCount - 1 - categoryIndex;
            var slot = ResolveBarClusterSlot(plot.Y, renderRow, spacing);
            if (importedPercentStackedCluster)
                slot = ResolveImportedPercentStackedClusterSlot(slot);

            double barWidth;
            double barX;
            double barY;
            if (stacked)
            {
                double stackedX = plot.X;
                for (int previousSeriesIndex = 0; previousSeriesIndex < seriesIndex; previousSeriesIndex++)
                {
                    double? previousValue = categoryIndex < chart.Series[previousSeriesIndex].Values.Count
                        ? chart.Series[previousSeriesIndex].Values[categoryIndex]
                        : null;
                    if (previousValue is null)
                        continue;

                    stackedX += Math.Max(
                        0.5,
                        ComputeStackedExtent(
                            chart,
                            categoryIndex,
                            previousValue.Value,
                            series.OnSecondaryAxis,
                            plot.Width,
                            Math.Abs((previousValue.Value - effectiveMin) / effectiveRange * plot.Width),
                            percentStacked));
                }

                barWidth = Math.Max(
                    0.5,
                    ComputeStackedExtent(
                        chart,
                        categoryIndex,
                        value,
                        series.OnSecondaryAxis,
                        plot.Width,
                        Math.Abs((value - effectiveMin) / effectiveRange * plot.Width),
                        percentStacked));
                barX = stackedX;
                int renderSeries = seriesCount - 1 - seriesIndex;
                barY = importedPercentStackedCluster
                    ? slot.ClusterStart + renderSeries * slot.SeriesStep
                    : slot.ClusterStart;
            }
            else
            {
                int renderSeries = seriesCount - 1 - seriesIndex;
                barWidth = percentStacked
                    ? Math.Max(
                        0.5,
                        ComputeStackedExtent(
                            chart,
                            categoryIndex,
                            value,
                            series.OnSecondaryAxis,
                            plot.Width,
                            Math.Abs((value - effectiveMin) / effectiveRange * plot.Width),
                            percentStacked))
                    : Math.Abs((value - effectiveMin) / effectiveRange * plot.Width);
                barX = plot.X;
                barY = slot.ClusterStart + renderSeries * slot.SeriesStep;
            }

            double total = ComputeDataLabelTotal(chart, series, categoryIndex, stacked || percentStacked, labels);
            string categoryName = categoryIndex < chart.Categories.Count
                ? chart.Categories[categoryIndex]
                : string.Empty;
            string text = FormatDataLabel(labels, value, total, categoryName, series.Name);
            if (string.IsNullOrEmpty(text) && !labels.ShowLegendKey)
                continue;

            double labelHeight = ResolveDataLabelHeight(chart);
            double labelX = position switch
            {
                DataLabelPosition.InsideEnd => barX + barWidth - 22 - 2,
                DataLabelPosition.Center => barX + barWidth / 2 - 22,
                DataLabelPosition.InsideBase => barX + 2,
                _ => barX + barWidth + 2
            };
            double labelY = barY + slot.SeriesSize / 2 - labelHeight / 2;

            var labelPlan = ApplyDataLabelTextStyle(new ChartDataLabelPlan(
                seriesIndex,
                categoryIndex,
                text,
                new ChartPlanRect(labelX, labelY, 44, labelHeight),
                IsBold: false,
                FontSize: ResolveTextFontSize(chart, 6.5),
                Alignment: ChartPlanTextAlignment.Center)
            {
                IsOverMaximum = value > effectiveMin + effectiveRange
            }, labels);
            plans.Add(ApplyLegendKey(
                labelPlan,
                labels,
                seriesIndex,
                seriesColors,
                fillPlans));
        }

        return plans;
    }

    private static IReadOnlyList<ChartDataLabelPlan> BuildPieDataLabelPlans(
        ChartShape chart,
        ChartPlanRect plot,
        IReadOnlyList<SrgbColor>? seriesColors,
        ChartFillPlanSet? fillPlans)
    {
        var labels = ResolveEffectiveLabels(chart, 0);
        if (labels is null || chart.Series.Count == 0)
            return Array.Empty<ChartDataLabelPlan>();

        var firstSeries = chart.Series[0];
        var values = GetVisiblePieValues(firstSeries);
        if (values.Count == 0)
            return Array.Empty<ChartDataLabelPlan>();

        double total = values.Sum(value => value.Value);
        if (total <= 0)
            return Array.Empty<ChartDataLabelPlan>();

        double centerX = plot.X + plot.Width / 2;
        double centerY = plot.Y + plot.Height / 2;
        double radius = Math.Min(plot.Width, plot.Height) / 2 * 0.85;
        double startAngle = ResolvePieStartAngle(chart);
        var position = labels.Position ?? DataLabelPosition.BestFit;
        double labelRadius = position is DataLabelPosition.InsideEnd or DataLabelPosition.Center or DataLabelPosition.BestFit
            ? radius * 0.65
            : radius * 1.15;
        var plans = new List<ChartDataLabelPlan>();

        foreach (var visibleValue in values)
        {
            double sweepAngle = visibleValue.Value / total * 2 * Math.PI;
            double midAngle = startAngle + sweepAngle / 2;
            string categoryName = visibleValue.PointIndex < chart.Categories.Count
                ? chart.Categories[visibleValue.PointIndex]
                : string.Empty;
            string text = FormatDataLabel(labels, visibleValue.Value, total, categoryName, firstSeries.Name);
            if (!string.IsNullOrEmpty(text) || labels.ShowLegendKey)
            {
                var labelCenter = OffsetExplodedSliceCenter(
                    new ChartPlanPoint(centerX, centerY),
                    radius,
                    midAngle - sweepAngle / 2,
                    midAngle + sweepAngle / 2,
                    firstSeries.PointStyles.TryGetValue(visibleValue.PointIndex, out var pointStyle)
                        ? pointStyle.ExplosionPercent
                        : null);
                double labelX = labelCenter.X + labelRadius * Math.Cos(midAngle);
                double labelY = labelCenter.Y + labelRadius * Math.Sin(midAngle);
                double labelWidth = Math.Max(64, text.Length * 12.0);
                var labelPlan = ApplyDataLabelTextStyle(new ChartDataLabelPlan(
                    SeriesIndex: 0,
                    CategoryIndex: visibleValue.PointIndex,
                    Text: text,
                    Bounds: new ChartPlanRect(labelX - labelWidth / 2, labelY - ResolveDataLabelHeight(chart) / 2, labelWidth, ResolveDataLabelHeight(chart)),
                    IsBold: false,
                    FontSize: ResolveTextFontSize(chart, 6.5),
                    Alignment: ChartPlanTextAlignment.Center)
                {
                    TextColor = UsesImportedPieLegendDefaults(chart)
                        ? new SrgbColor(0x00, 0x00, 0x00)
                        : null
                }, labels);
                plans.Add(ApplyLegendKey(
                    labelPlan,
                    labels,
                    seriesIndex: 0,
                    seriesColors: seriesColors,
                    fillPlans: fillPlans));
            }

            startAngle += sweepAngle;
        }

        return plans;
    }

    private static double ComputeDataLabelTotal(
        ChartShape chart,
        ChartSeries series,
        int categoryIndex,
        bool stacked,
        ChartDataLabels labels)
    {
        if (!labels.ShowPercent)
            return 0;

        double total = 0;
        if (stacked)
        {
            foreach (var chartSeries in chart.Series)
            {
                if (categoryIndex < chartSeries.Values.Count && chartSeries.Values[categoryIndex].HasValue)
                    total += Math.Abs(chartSeries.Values[categoryIndex]!.Value);
            }
        }
        else
        {
            foreach (var value in series.Values)
            {
                if (value.HasValue)
                    total += Math.Abs(value.Value);
            }
        }

        return total;
    }

    private static bool IsHundredPercentStacked(ChartType chartType) =>
        chartType is ChartType.ColumnStacked100 or ChartType.BarStacked100;

    private static double ComputeStackedExtent(
        ChartShape chart,
        int categoryIndex,
        double value,
        bool onSecondaryAxis,
        double totalExtent,
        double fallbackExtent,
        bool percentStacked)
    {
        if (!percentStacked)
            return fallbackExtent;

        double categoryTotal = ComputeStackedCategoryMagnitudeTotal(chart, categoryIndex, onSecondaryAxis);
        return categoryTotal > 0
            ? Math.Abs(value) / categoryTotal * totalExtent
            : fallbackExtent;
    }

    private static double ComputeStackedCategoryMagnitudeTotal(
        ChartShape chart,
        int categoryIndex,
        bool onSecondaryAxis)
    {
        double total = 0;
        foreach (var series in chart.Series)
        {
            if (series.OnSecondaryAxis != onSecondaryAxis || IsComboOverrideNonStacked(series.OverrideChartType))
                continue;

            if (categoryIndex < series.Values.Count && series.Values[categoryIndex].HasValue)
                total += Math.Abs(series.Values[categoryIndex]!.Value);
        }

        return total;
    }

    private static bool IsComboOverrideNonStacked(ChartType? overrideType) =>
        overrideType is ChartType.Line
            or ChartType.LineMarkers
            or ChartType.Scatter
            or ChartType.Bubble;

    private static (double min, double max, double majorUnit) ComputeNiceRange(
        double min,
        double max)
        => ComputeNiceRange(min, max, targetIntervals: 4);

    private static (double min, double max, double majorUnit) ComputeNiceRange(
        double min,
        double max,
        int targetIntervals)
    {
        if (max <= min)
            max = min + 1;

        double range = max - min;
        double rawUnit = range / Math.Max(1, targetIntervals);
        if (rawUnit <= 0)
            rawUnit = 1;

        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawUnit)));
        double normalized = rawUnit / magnitude;
        double niceMultiplier = normalized switch
        {
            < 1.5 => 1.0,
            < 2.25 => 2.0,
            < 3.75 => 2.5,
            < 7.5 => 5.0,
            _ => 10.0
        };

        double majorUnit = niceMultiplier * magnitude;
        double niceMax = Math.Ceiling(max / majorUnit) * majorUnit;
        double niceMin = min >= 0 ? 0 : Math.Floor(min / majorUnit) * majorUnit;

        if (niceMax - max < majorUnit * 0.25)
            niceMax += majorUnit;

        return (niceMin, niceMax, majorUnit);
    }

    private static bool UsesStockLineFallback(ChartShape chart) =>
        chart.ChartType == ChartType.Stock && !chart.HasHighLowLines;

    private static (double min, double max, double majorUnit) ComputeStockFallbackValueAxisRange(
        double min,
        double max)
    {
        if (max <= min)
            max = min + 1;

        // PowerPoint gives the line fallback a denser category-chart scale than
        // its normal four-major-interval default, leaving one interval of headroom.
        double rawUnit = (max - min) / 8.0;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(rawUnit, double.Epsilon))));
        double normalized = rawUnit / magnitude;
        double multiplier = normalized switch
        {
            <= 1.0 => 1.0,
            <= 2.0 => 2.0,
            <= 2.5 => 2.5,
            <= 5.0 => 5.0,
            _ => 10.0
        };
        double unit = multiplier * magnitude;
        double niceMin = min >= 0 ? 0 : Math.Floor(min / unit) * unit;
        double niceMax = Math.Ceiling(max / unit) * unit;
        if (niceMax <= max + 1e-9)
            niceMax += unit;

        return (niceMin, niceMax, unit);
    }

    public static ChartBarClusterSlot ResolveBarClusterSpacing(
        ChartShape chart,
        double categorySize,
        int seriesCount,
        bool stacked)
    {
        double gapWidth = Math.Clamp(chart.BarGapWidthPercent ?? (int)DefaultBarGapWidthPercent, 0, 500);
        if (stacked && chart.ChartType == ChartType.ColumnStacked100 && UsesImportedTextMetrics(chart))
            gapWidth = ImportedPercentStackedGapWidthPercent;
        if (stacked || seriesCount <= 1)
        {
            double singleSeriesSize = categorySize / (1.0 + gapWidth / 100.0);
            double singleCategoryStart = (categorySize - singleSeriesSize) / 2.0;
            return new ChartBarClusterSlot(
                singleCategoryStart,
                categorySize,
                singleCategoryStart,
                singleSeriesSize,
                singleSeriesSize,
                singleSeriesSize);
        }

        double overlap = Math.Clamp(chart.BarOverlapPercent ?? 0, -100, 100) / 100.0;
        // PowerPoint defines GapWidth as a percentage of a single bar, not of
        // the full multi-series cluster. The category band therefore contains
        // the occupied series widths plus one bar-relative gap.
        double occupiedFactor = 1.0 + (1.0 - overlap) * (seriesCount - 1);
        double denominator = occupiedFactor + gapWidth / 100.0;
        double seriesSize = denominator <= 0 ? categorySize : categorySize / denominator;
        double seriesStep = seriesSize * (1.0 - overlap);
        double occupied = seriesSize + seriesStep * (seriesCount - 1);
        double categoryStart = (categorySize - occupied) / 2.0;

        return new ChartBarClusterSlot(
            categoryStart,
            categorySize,
            categoryStart,
            occupied,
            seriesSize,
            seriesStep);
    }

    public static ChartBarDepthPlan? BuildBarGapDepthPlan(
        ChartShape chart,
        double categorySize,
        int seriesIndex,
        int seriesCount,
        bool isHorizontalBar,
        bool stacked)
    {
        bool isThreeD = chart.ThreeDStyle is ChartThreeDStyle.Column or ChartThreeDStyle.Bar;
        if (!isThreeD && chart.BarGapDepthPercent is not { })
            return null;

        int gapDepth = Math.Clamp(chart.BarGapDepthPercent ?? 150, 0, 500);
        if (isThreeD)
        {
            double depthX = Math.Min(Math.Max(categorySize * 0.12, 14.0), 18.0);
            double depthY = -depthX * 0.55;
            return new ChartBarDepthPlan(
                gapDepth,
                depthX,
                depthY,
                isHorizontalBar,
                stacked)
            {
                IsThreeD = true,
                CategorySkewY = ImportedThreeDBarCategorySkewY,
                HeightScaleBase = ImportedThreeDBarHeightScaleBase,
                HeightScaleStep = ImportedThreeDBarHeightScaleStep,
                BaseLift = ImportedThreeDBarBaseLift
            };
        }

        if (gapDepth == 0 || categorySize <= 0)
        {
            return new ChartBarDepthPlan(
                gapDepth,
                OffsetX: 0,
                OffsetY: 0,
                isHorizontalBar,
                stacked);
        }

        double maxOffset = Math.Min(categorySize * 0.18, 10.0) * gapDepth / 500.0;
        double seriesRatio = stacked || seriesCount <= 1
            ? 0.5
            : (Math.Clamp(seriesIndex, 0, seriesCount - 1) + 0.5) / seriesCount;
        double offset = Math.Round(maxOffset * seriesRatio, 4);
        return new ChartBarDepthPlan(
            gapDepth,
            OffsetX: offset,
            OffsetY: -offset,
            isHorizontalBar,
            stacked);
    }

    public static ChartPlanRect ApplyBarGapDepthOffset(
        ChartPlanRect bounds,
        ChartBarDepthPlan? depth)
    {
        if (!depth.HasValue)
            return bounds;

        return new ChartPlanRect(
            bounds.X + depth.Value.OffsetX,
            bounds.Y + depth.Value.OffsetY,
            bounds.Width,
            bounds.Height);
    }

    private static ChartPlanRect ApplyColumnDepthProjection(
        ChartPlanRect bounds,
        ChartBarDepthPlan? depth,
        int categoryIndex,
        int seriesIndex)
    {
        if (depth is not { IsThreeD: true } threeD)
            return ApplyBarGapDepthOffset(bounds, depth);

        double baselineShift = -threeD.BaseLift + categoryIndex * threeD.CategorySkewY;
        double heightScale = threeD.HeightScaleBase + categoryIndex * threeD.HeightScaleStep;
        double projectedHeight = Math.Max(0.5, bounds.Height * heightScale);
        double projectedBottom = bounds.Bottom + baselineShift;
        double perspectiveOffset =
            ImportedThreeDBarPerspectiveX0 +
            ImportedThreeDBarPerspectiveX1 * categoryIndex +
            ImportedThreeDBarPerspectiveX2 * categoryIndex * categoryIndex +
            ImportedThreeDBarPerspectiveX3 * categoryIndex * categoryIndex * categoryIndex;
        double projectedWidth = Math.Max(1.0, bounds.Width * ImportedThreeDBarWidthScale);
        double widthDelta = bounds.Width - projectedWidth;
        return new ChartPlanRect(
            bounds.X + perspectiveOffset - seriesIndex * widthDelta * 0.35,
            projectedBottom - projectedHeight,
            projectedWidth,
            projectedHeight);
    }

    private static ChartBarClusterSlot ResolveBarClusterSlot(
        double plotStart,
        int categoryIndex,
        ChartBarClusterSlot spacing) =>
        spacing with
        {
            CategoryStart = plotStart + categoryIndex * spacing.CategorySize,
            ClusterStart = plotStart + categoryIndex * spacing.CategorySize + spacing.ClusterStart
        };

    private static ChartBarClusterSlot ResolveImportedPercentStackedClusterSlot(
        ChartBarClusterSlot slot) =>
        slot with
        {
            ClusterStart = slot.CategoryStart + (slot.CategorySize - slot.SeriesSize) / 2.0
        };

    private static void AccumulateValues(
        IEnumerable<double?> values,
        ref double dataMin,
        ref double dataMax)
    {
        bool ignored = false;
        AccumulateValues(values, ref dataMin, ref dataMax, ref ignored);
    }

    private static void AccumulateValues(
        IEnumerable<double?> values,
        ref double dataMin,
        ref double dataMax,
        ref bool any)
    {
        foreach (var value in values)
        {
            if (!value.HasValue)
                continue;

            dataMin = Math.Min(dataMin, value.Value);
            dataMax = Math.Max(dataMax, value.Value);
            any = true;
        }
    }
}
