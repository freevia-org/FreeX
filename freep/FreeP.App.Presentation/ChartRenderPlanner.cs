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
    ScatterLike,
    Radar
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
    ChartAxisLabelFormatPlan? AxisLabelFormat = null);

public readonly record struct ChartBarDepthPlan(
    int GapDepthPercent,
    double OffsetX,
    double OffsetY,
    bool IsHorizontalBar,
    bool IsStacked);

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
    IReadOnlyList<ChartLineSegmentPrimitive> ContourSegments);

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

public readonly record struct ChartAreaSeriesPrimitive(
    int SeriesIndex,
    ChartPlanPoint BaselineStart,
    ChartPlanPoint BaselineEnd,
    IReadOnlyList<ChartPlanPoint> Points,
    ChartPathPrimitive AreaPath,
    ChartFillPlan Fill)
{
    public ChartClassicThreeDDepthPlan? Depth { get; init; }
}

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
    IReadOnlyList<ChartRadarSeriesPrimitive> Series);

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
    public double SweepAngle => EndAngle - StartAngle;
    public bool IsLargeArc => SweepAngle > Math.PI;
    public double VerticalScale { get; init; }
    public double DepthOffsetY { get; init; }
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
    ChartPlanTextAlignment Alignment);

public readonly record struct ChartLegendItemPlan(
    ChartPlanRect SwatchBounds,
    ChartTextPlan Label,
    ChartFillPlan Fill);

public readonly record struct ChartAxisTitlePlan(
    ChartTextPlan Label,
    ChartAxisTitleOrientation Orientation);

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
    public const double LineMarkerRadius = 3.0;
    public const double LineMarkerStrokeThickness = 0.75;
    public const double ScatterLineThickness = 1.5;
    public const double ScatterMarkerRadius = 3.5;
    public const double ScatterDataLabelWidth = 40.0;
    public const double ScatterDataLabelHeight = 11.0;
    public const double ImportedScatterPlotLeftInset = 10.5;
    public const double ImportedScatterPlotUpwardOffset = 10.5;
    public const double ImportedScatterPlotRightInset = 3.0;
    public const byte BubbleFillAlpha = 180;
    public const double BubbleStrokeThickness = 0.8;
    public const byte RadarFillAlpha = 80;
    public const double RadarSeriesStrokeThickness = 1.5;
    public const double RadarMarkerRadius = 3.0;
    public const double ThreeDPieVerticalScale = 0.72;
    public const byte ThreeDPieDepthFillAlpha = 140;
    public const double ClassicThreeDDepthScale = 0.045;
    public const double StockTickWidthFraction = 0.32;
    public const double StockVolumeBandHeightFraction = 0.28;
    public const double StockVolumeBarWidthFraction = 0.55;
    public const double SurfaceCellStrokeThickness = 0.4;
    public const double SurfaceFacetStrokeThickness = 0.55;
    public const double SurfaceWireframeStrokeThickness = 0.7;
    public const double SurfaceContourStrokeThickness = 0.9;
    private const double DipPerPoint = 96.0 / 72.0;
    private const double DefaultBarGapWidthPercent = 150.0;

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
        chart.TextStyle?.FontSizePt is > 0 ? chart.TextStyle.FontSizePt.Value : fallback;

    private static double ResolveFrameMargin(ChartShape chart) =>
        UsesImportedTextMetrics(chart) ? 20.0 : Margin;

    private static double ResolveAxisLabelWidth(ChartShape chart) =>
        UsesImportedTextMetrics(chart) ? 48.0 : AxisLabelWidth;

    private static double ResolveCategoryLabelHeight(ChartShape chart) =>
        UsesImportedTextMetrics(chart) ? 32.0 : CategoryLabelHeight;

    private static double ResolveBarCategoryLabelWidth(ChartShape chart) =>
        UsesImportedTextMetrics(chart) ? 60.0 : BarCategoryLabelWidth;

    private static double ResolveLegendLineHeight(ChartShape chart) =>
        UsesImportedTextMetrics(chart) ? 28.0 : LegendHeight;

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
        bool varyByPoint = false)
    {
        if (fillPlans?.TryGetPointFill(seriesIndex, pointIndex, alpha, out var pointFill) == true)
            return pointFill;

        var pointStyleColor = series.PointStyles.TryGetValue(pointIndex, out var pointStyle)
            ? pointStyle.FillColor?.Resolved
            : (SrgbColor?)null;
        var pointColorOverride = series.PointColors.TryGetValue(pointIndex, out var pointColor)
            ? pointColor.Resolved
            : (SrgbColor?)null;

        if (pointStyleColor is not null)
            return new ChartFillPlan(pointStyleColor.Value, alpha);

        if (pointColorOverride is not null)
            return new ChartFillPlan(pointColorOverride.Value, alpha);

        if (varyByPoint && series.FillColor is null && series.Fill is null)
            return new ChartFillPlan(ResolveSeriesColor(pointIndex, seriesColors), alpha);

        return ResolveSeriesFill(seriesIndex, seriesColors, alpha, fillPlans);
    }

    private static bool ShouldVaryPointColors(ChartShape chart) =>
        chart.VaryColors &&
        (chart.ChartType is ChartType.Pie or ChartType.Doughnut or ChartType.Bubble ||
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
        double titleHeight = UsesImportedTextMetrics(chart) ? 28.0 : TitleHeight;
        double titleAreaHeight = chart.Title is not null ? titleHeight + margin : 0;
        var family = GetRenderFamily(chart.ChartType);
        bool hasSecondaryValueAxis = family is not (ChartRenderFamily.Pie
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
                    : Math.Min(120, bounds.Width * 0.11)
                : Math.Min(90, bounds.Width * 0.20)
            : 0;
        double legendAreaHeight = legendReservesPlotSpace && !legendRight
            ? legendLineHeight + margin
            : 0;

        bool reservesAxes = family is not (ChartRenderFamily.Pie
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
        if (family == ChartRenderFamily.ScatterLike && UsesImportedTextMetrics(chart))
        {
            // PowerPoint's imported scatter layout moves the plot above the
            // baseline category-label band and reserves a compact left gutter.
            plot = new ChartPlanRect(
                plot.X + ImportedScatterPlotLeftInset,
                plot.Y - ImportedScatterPlotUpwardOffset,
                plot.Width - ImportedScatterPlotLeftInset - ImportedScatterPlotRightInset,
                plot.Height);
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
                bounds.Y + 57.0,
                bounds.Width - 120.0,
                bounds.Height - 99.0);
        }
        if (TryResolveManualLayoutRect(chart.PlotAreaManualLayout, bounds, out var manualPlot))
            plot = manualPlot;
        ChartPlanRect? titleBounds = chart.Title is not null
            ? new ChartPlanRect(
                bounds.X + margin,
                UsesStockLineFallback(chart) || chart.ChartType == ChartType.Surface3D
                    ? bounds.Y + 7.0
                    : bounds.Y + margin,
                bounds.Width - 2 * margin,
                titleHeight)
            : null;
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

    public static ChartRenderFamily GetRenderFamily(ChartType chartType) =>
        chartType switch
        {
            ChartType.Pie or ChartType.Doughnut => ChartRenderFamily.Pie,
            ChartType.BarClustered or ChartType.BarStacked or ChartType.BarStacked100 => ChartRenderFamily.HorizontalBar,
            ChartType.Scatter or ChartType.Bubble => ChartRenderFamily.ScatterLike,
            ChartType.Radar => ChartRenderFamily.Radar,
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

        bool hasManualLayout = TryResolveManualLayoutRect(chart.LegendManualLayout, frame.Bounds, out _);
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
        double legendLineHeight = ResolveLegendLineHeight(chart);
        int maxItems = (int)Math.Max(1, verticalLegend ? legendBounds.Height / legendLineHeight : legendWidth / 80);
        int itemsToShow = Math.Min(itemCount, maxItems);
        if (itemsToShow == 0)
            return Array.Empty<ChartLegendItemPlan>();

        var items = new List<ChartLegendItemPlan>(itemsToShow);
        double firstItemY = verticalLegend && !hasManualLayout
            ? legendBounds.Y + Math.Max(0, (legendBounds.Height - itemsToShow * legendLineHeight) / 2)
            : legendBounds.Y;
        for (int itemIndex = 0; itemIndex < itemsToShow; itemIndex++)
        {
            int sourceItemIndex = frame.IsPie
                ? itemIndex
                : frame.IsBar
                    ? itemCount - itemIndex - 1
                    : itemIndex;
            double itemX = verticalLegend ? legendBounds.X : legendBounds.X + itemIndex * 80.0;
            double itemY = verticalLegend ? firstItemY + itemIndex * legendLineHeight : legendBounds.Y;
            double textWidth = verticalLegend
                ? Math.Max(0, legendWidth - 10)
                : Math.Min(70, Math.Max(0, legendBounds.Right - itemX - 10));
            string text = frame.IsPie
                ? sourceItemIndex < chart.Categories.Count
                    ? chart.Categories[sourceItemIndex]
                    : $"Point {sourceItemIndex + 1}"
                : chart.Series[sourceItemIndex].Name;
            var color = seriesColors is not null && sourceItemIndex < seriesColors.Count
                ? seriesColors[sourceItemIndex]
                : FallbackSeriesColors[0];

            items.Add(new ChartLegendItemPlan(
                new ChartPlanRect(itemX, itemY + 3, 8, 8),
                new ChartTextPlan(
                    text,
                    new ChartPlanRect(itemX + 10, itemY, textWidth, legendLineHeight),
                    IsBold: false,
                    FontSize: ResolveTextFontSize(chart, 7.0),
                    Alignment: ChartPlanTextAlignment.Left),
                fillPlans is not null
                    ? frame.IsPie
                        ? ResolvePointFill(chart.Series[0], 0, sourceItemIndex, seriesColors, alpha: 255, fillPlans,
                            ShouldVaryPointColors(chart))
                        : ResolveSeriesFill(sourceItemIndex, seriesColors, alpha: 255, fillPlans)
                    : new ChartFillPlan(color, Alpha: 255)));
        }

        return items;
    }

    private static ChartPlanRect ResolveLegendBounds(ChartShape chart, ChartFramePlan frame)
    {
        if (TryResolveManualLayoutRect(chart.LegendManualLayout, frame.Bounds, out var manualLegend))
            return manualLegend;

        var plot = frame.Plot;
        if (frame.LegendRight)
        {
            double legendAreaWidth = frame.LegendAreaWidth > 0
                ? frame.LegendAreaWidth
                : Math.Min(90, frame.Bounds.Width * 0.20);
            bool importedTextMetrics = UsesImportedTextMetrics(chart);
            if (importedTextMetrics && chart.SecondaryValueAxis is { Delete: false })
            {
                double legendX = Math.Min(frame.Bounds.Right, plot.Right + 50.0);
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
        ChartPlanRect parent,
        out ChartPlanRect rect)
    {
        rect = default;
        if (!HasResolvableManualLayout(layout))
            return false;

        double x = ResolveManualLayoutStart(parent.X, parent.Width, layout!.X!.Value);
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
                double y = plot.Bottom - plot.Height * index / steps;
                lines.Add(new ChartGridLinePlan(
                    new ChartPlanPoint(plot.X, y),
                    new ChartPlanPoint(plot.Right, y)));
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

    public static ChartMajorAxisTickPrimitivePlan BuildMajorAxisTickPrimitivePlan(
        ChartShape chart,
        ChartFramePlan frame)
    {
        if (!frame.HasPlot || frame.IsPie || frame.IsRadar || frame.IsScatterLike)
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

        var labels = new List<ChartTextPlan>(chart.Categories.Count);
        var plot = frame.Plot;
        if (frame.IsBar)
        {
            int categoryCount = chart.Categories.Count;
            double categoryStep = plot.Height / Math.Max(1, categoryCount);
            for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
            {
                int renderRow = categoryCount - 1 - categoryIndex;
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
            for (int categoryIndex = 0; categoryIndex < chart.Categories.Count; categoryIndex++)
            {
                double x = plot.X + categoryIndex * categoryStep;
                labels.Add(new ChartTextPlan(
                    FormatCategoryAxisLabel(chart.Categories[categoryIndex], chart.CategoryAxis),
                    new ChartPlanRect(x, plot.Bottom + 2, categoryStep, ResolveCategoryLabelHeight(chart)),
                    IsBold: false,
                    FontSize: ResolveTextFontSize(chart, 7.0),
                    Alignment: ChartPlanTextAlignment.Center,
                    AxisLabelFormat: BuildAxisLabelFormatPlan(chart.CategoryAxis)));
            }
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
                    FormatAxisValue(value, chart.ValueAxis.NumberFormatCode),
                    new ChartPlanRect(x - ResolveAxisLabelWidth(chart) / 2, plot.Bottom + 2, ResolveAxisLabelWidth(chart), ResolveCategoryLabelHeight(chart)),
                    IsBold: false,
                    FontSize: ResolveTextFontSize(chart, 6.5),
                    Alignment: ChartPlanTextAlignment.Center,
                    AxisLabelFormat: BuildAxisLabelFormatPlan(chart.ValueAxis)));
            }
            else
            {
                double y = plot.Bottom - plot.Height * tickIndex / steps;
                labels.Add(new ChartTextPlan(
                    FormatAxisValue(value, chart.ValueAxis.NumberFormatCode),
                    new ChartPlanRect(plot.X - ResolveAxisLabelWidth(chart), y - 6, ResolveAxisLabelWidth(chart) - GridlinePad, ResolveCategoryLabelHeight(chart)),
                    IsBold: false,
                    FontSize: ResolveTextFontSize(chart, 6.5),
                    Alignment: ChartPlanTextAlignment.Right,
                    AxisLabelFormat: BuildAxisLabelFormatPlan(chart.ValueAxis)));
            }
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
                double x = plot.X + categoryIndex * categoryStep + categoryStep / 2.0;
                ticks.Add(new ChartGridLinePlan(
                    new ChartPlanPoint(x, plot.Bottom),
                    new ChartPlanPoint(x, plot.Bottom + AxisMajorTickLength)));
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

        if (!chart.ValueAxis.Delete && !string.IsNullOrWhiteSpace(chart.ValueAxis.Title))
        {
            if (frame.IsBar)
            {
                plans.Add(new ChartAxisTitlePlan(
                    new ChartTextPlan(
                        chart.ValueAxis.Title!,
                        new ChartPlanRect(
                            plot.X,
                            plot.Bottom + CategoryLabelHeight + 2,
                            plot.Width,
                            AxisTitleBand),
                        IsBold: false,
                        FontSize: AxisTitleFontSize,
                        Alignment: ChartPlanTextAlignment.Center),
                    ChartAxisTitleOrientation.Horizontal));
            }
            else
            {
                plans.Add(new ChartAxisTitlePlan(
                    new ChartTextPlan(
                        chart.ValueAxis.Title!,
                        new ChartPlanRect(
                            frame.Bounds.X + Margin,
                            plot.Y,
                            AxisTitleBand,
                            plot.Height),
                        IsBold: false,
                        FontSize: AxisTitleFontSize,
                        Alignment: ChartPlanTextAlignment.Center),
                    ChartAxisTitleOrientation.VerticalCounterclockwise));
            }
        }

        if (!chart.CategoryAxis.Delete && !string.IsNullOrWhiteSpace(chart.CategoryAxis.Title))
        {
            if (frame.IsBar)
            {
                plans.Add(new ChartAxisTitlePlan(
                    new ChartTextPlan(
                        chart.CategoryAxis.Title!,
                        new ChartPlanRect(
                            frame.Bounds.X + Margin,
                            plot.Y,
                            AxisTitleBand,
                            plot.Height),
                        IsBold: false,
                        FontSize: AxisTitleFontSize,
                        Alignment: ChartPlanTextAlignment.Center),
                    ChartAxisTitleOrientation.VerticalCounterclockwise));
            }
            else
            {
                double categoryTitleOffset = ShouldPlanDataTable(chart, frame)
                    ? ComputeDataTableReservedHeight(chart) + 2
                    : CategoryLabelHeight + 2;
                plans.Add(new ChartAxisTitlePlan(
                    new ChartTextPlan(
                        chart.CategoryAxis.Title!,
                        new ChartPlanRect(
                            plot.X,
                            plot.Bottom + categoryTitleOffset,
                            plot.Width,
                            AxisTitleBand),
                        IsBold: false,
                        FontSize: AxisTitleFontSize,
                        Alignment: ChartPlanTextAlignment.Center),
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
        double labelX = plot.Right + AxisMajorTickLength + GridlinePad;
        double labelWidth = Math.Max(1, AxisLabelWidth - AxisMajorTickLength - GridlinePad);
        for (int tickIndex = 0; tickIndex <= tickCount; tickIndex++)
        {
            double value = niceMin + majorUnit * tickIndex;
            double y = plot.Bottom - plot.Height * tickIndex / steps;
            ticks.Add(new ChartGridLinePlan(
                new ChartPlanPoint(plot.Right, y),
                new ChartPlanPoint(plot.Right + AxisMajorTickLength, y)));
            labels.Add(new ChartTextPlan(
                FormatAxisValue(value, chart.SecondaryValueAxis.NumberFormatCode),
                new ChartPlanRect(labelX, y - 6, labelWidth, UsesImportedTextMetrics(chart) ? 32.0 : 12.0),
                IsBold: false,
                FontSize: ResolveTextFontSize(chart, 6.5),
                Alignment: ChartPlanTextAlignment.Left,
                AxisLabelFormat: BuildAxisLabelFormatPlan(chart.SecondaryValueAxis)));
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
            DefaultAxisTickStroke(),
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
        bool stacked = chart.ChartType is ChartType.ColumnStacked or ChartType.ColumnStacked100;
        bool percentStacked = IsHundredPercentStacked(chart.ChartType);
        double categoryWidth = plot.Width / categoryCount;
        int seriesCount = Math.Max(1, chart.Series.Count);
        var spacing = ResolveBarClusterSpacing(chart, categoryWidth, seriesCount, stacked);
        bool varyByPoint = ShouldVaryPointColors(chart);

        var primitives = new List<ChartRectPrimitive>();
        for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
        {
            var slot = ResolveBarClusterSlot(plot.X, categoryIndex, spacing);
            double stackedY = plot.Bottom;

            for (int seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
            {
                var series = chart.Series[seriesIndex];
                if (series.OverrideChartType.HasValue &&
                    series.OverrideChartType.Value is ChartType.Line
                        or ChartType.LineMarkers
                        or ChartType.Scatter
                        or ChartType.Bubble)
                {
                    continue;
                }

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

                double x = stacked
                    ? slot.ClusterStart
                    : slot.ClusterStart + seriesIndex * slot.SeriesStep;
                double drawWidth = Math.Max(1, slot.SeriesSize - (stacked ? 0 : 1));
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
                        seriesIndex,
                        seriesCount,
                        isHorizontalBar: false,
                        stacked);
                    var bounds = ApplyBarGapDepthOffset(
                        new ChartPlanRect(x, stackedY - height, drawWidth, height),
                        depth);
                    primitives.Add(new ChartRectPrimitive(
                        seriesIndex,
                        categoryIndex,
                        bounds,
                        ResolvePointFill(series, seriesIndex, categoryIndex, seriesColors, RectSeriesFillAlpha, fillPlans, varyByPoint),
                        Stroke: null)
                    {
                        Depth = depth
                    });
                    stackedY -= height;
                }
                else
                {
                    double height = Math.Max(0.5, Math.Abs((rawValue.Value - effectiveMin) / effectiveRange * plot.Height));
                    double y = plot.Bottom - (rawValue.Value - effectiveMin) / effectiveRange * plot.Height;
                    var depth = BuildBarGapDepthPlan(
                        chart,
                        categoryWidth,
                        seriesIndex,
                        seriesCount,
                        isHorizontalBar: false,
                        stacked);
                    var bounds = ApplyBarGapDepthOffset(new ChartPlanRect(x, y, drawWidth, height), depth);
                    primitives.Add(new ChartRectPrimitive(
                        seriesIndex,
                        categoryIndex,
                        bounds,
                        ResolvePointFill(series, seriesIndex, categoryIndex, seriesColors, RectSeriesFillAlpha, fillPlans, varyByPoint),
                        Stroke: null)
                    {
                        Depth = depth
                    });
                }
            }
        }

        return primitives;
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
        bool stacked = chart.ChartType is ChartType.BarStacked or ChartType.BarStacked100;
        bool percentStacked = IsHundredPercentStacked(chart.ChartType);
        double categoryHeight = plot.Height / categoryCount;
        int seriesCount = Math.Max(1, chart.Series.Count);
        var spacing = ResolveBarClusterSpacing(chart, categoryHeight, seriesCount, stacked);
        bool varyByPoint = ShouldVaryPointColors(chart);

        var primitives = new List<ChartRectPrimitive>();
        for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
        {
            int renderRow = categoryCount - 1 - categoryIndex;
            var slot = ResolveBarClusterSlot(plot.Y, renderRow, spacing);
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
                        : Math.Abs((rawValue.Value - effectiveMin) / effectiveRange * plot.Width));
                int renderSeries = stacked ? seriesIndex : seriesCount - 1 - seriesIndex;
                double y = stacked
                    ? slot.ClusterStart
                    : slot.ClusterStart + renderSeries * slot.SeriesStep;
                double x = stacked ? stackedX : plot.X;
                double height = Math.Max(1, slot.SeriesSize - (stacked ? 0 : 1));

                var depth = BuildBarGapDepthPlan(
                    chart,
                    categoryHeight,
                    seriesIndex,
                    seriesCount,
                    isHorizontalBar: true,
                    stacked);
                var bounds = ApplyBarGapDepthOffset(new ChartPlanRect(x, y, width, height), depth);

                primitives.Add(new ChartRectPrimitive(
                    seriesIndex,
                    categoryIndex,
                    bounds,
                    ResolvePointFill(series, seriesIndex, categoryIndex, seriesColors, RectSeriesFillAlpha, fillPlans, varyByPoint),
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

            double x = plot.X + (categoryIndex + 0.5) * categoryWidth;
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
                StockFallbackMarkerSymbols[seriesIndex % StockFallbackMarkerSymbols.Length]));
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
            double x = plot.X + categoryIndex * categoryWidth + (categoryWidth - barWidth) / 2.0;
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
                double? value = TryGetSeriesValue(chart, seriesIndex, categoryIndex);
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
                    chart.ChartType == ChartType.Surface3D),
                cell.Value,
                cell.NormalizedValue);
            pointsByKey[(cell.SeriesIndex, cell.CategoryIndex)] = point;
        }

        var points = pointsByKey.Values
            .OrderBy(point => point.SeriesIndex)
            .ThenBy(point => point.CategoryIndex)
            .ToArray();

        var wireframe = BuildSurfaceWireframeSegments(pointsByKey, seriesCount, categoryCount);
        var facets = BuildSurfaceFacetPrimitives(chart, pointsByKey, seriesCount, categoryCount, seriesColors);
        var contours = BuildSurfaceContourSegments(pointsByKey, seriesCount, categoryCount);

        return new ChartSurfaceGeometryPlan(cells, points, facets, wireframe, contours);
    }

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
        bool isThreeD)
    {
        double categoryT = categoryCount <= 1 ? 0 : categoryIndex / (double)(categoryCount - 1);
        double seriesT = seriesCount <= 1 ? 0 : seriesIndex / (double)(seriesCount - 1);

        if (!isThreeD)
        {
            return new ChartPlanPoint(
                Math.Round(plot.X + categoryT * plot.Width, 4),
                Math.Round(plot.Bottom - seriesT * plot.Height, 4));
        }

        double depthX = Math.Min(plot.Width * 0.18, 72.0);
        double depthY = Math.Min(plot.Height * 0.26, 52.0);
        double categorySlopeY = Math.Min(plot.Height * 0.20, 40.0);
        double lift = Math.Min(plot.Height * 0.50, 88.0);
        double drawableWidth = Math.Max(1, plot.Width - depthX);
        double x = plot.X + categoryT * drawableWidth + seriesT * depthX;
        double y = plot.Bottom + categoryT * categorySlopeY - seriesT * depthY - normalized * lift;

        return new ChartPlanPoint(Math.Round(x, 4), Math.Round(y, 4));
    }

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
        IReadOnlyList<SrgbColor>? seriesColors)
    {
        var facets = new List<ChartSurfaceFacetPrimitive>();
        var stroke = new ChartStrokePlan(
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

                double averageValue = facetPoints.Average(point => point.Value);
                double averageNormalized = facetPoints.Average(point => point.NormalizedValue);
                var color = ResolveSurfaceFacetColor(
                    chart,
                    seriesIndex,
                    seriesColors,
                    averageNormalized);

                facets.Add(new ChartSurfaceFacetPrimitive(
                    seriesIndex,
                    categoryIndex,
                    facetPoints.Select(point => point.Point).ToArray(),
                    new ChartFillPlan(color, Alpha: chart.ChartType == ChartType.Surface3D ? (byte)220 : (byte)185),
                    stroke,
                    averageValue,
                    averageNormalized));
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
        IReadOnlyList<SrgbColor>? seriesColors,
        double normalized)
    {
        if (chart.VaryColors)
            return ResolveSurfaceVaryColor(normalized);

        return InterpolateSurfaceColor(
            ResolveSeriesColor(seriesIndex, seriesColors),
            normalized);
    }

    private static SrgbColor ResolveSurfaceVaryColor(double normalized)
    {
        normalized = Math.Clamp(normalized, 0, 1);
        double position = normalized * (SurfaceVaryColors.Length - 1);
        int lowerIndex = (int)Math.Floor(position);
        int upperIndex = Math.Min(lowerIndex + 1, SurfaceVaryColors.Length - 1);
        double fraction = position - lowerIndex;
        var lower = SurfaceVaryColors[lowerIndex];
        var upper = SurfaceVaryColors[upperIndex];
        return new SrgbColor(
            (byte)Math.Round(lower.R + (upper.R - lower.R) * fraction),
            (byte)Math.Round(lower.G + (upper.G - lower.G) * fraction),
            (byte)Math.Round(lower.B + (upper.B - lower.B) * fraction));
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
        double stepX = plot.Width / Math.Max(1, categoryCount - 1);
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

                double x = plot.X + categoryIndex * stepX;
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
                ShouldSpanBlankSegments(chart)) with { Depth = depth });
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
        double stepX = categoryCount > 1 ? plot.Width / (categoryCount - 1) : plot.Width / 2;
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

                double x = categoryCount == 1
                    ? plot.X + plot.Width / 2
                    : plot.X + categoryIndex * stepX;
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
                ShouldSpanBlankSegments(chart)));
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
        ChartMarkerPrimitiveSymbol? automaticMarkerSymbol = null)
    {
        series ??= new ChartSeries();
        bool suppressLine = series.LineStyle?.NoFill == true;
        var stroke = ResolveAuthoredSeriesStroke(series, seriesIndex, seriesColors, LineSeriesStrokeThickness)
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
        var markerRadius = ResolveMarkerRadius(defaultMarkerStyle, LineMarkerRadius);
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
                    ResolveMarkerRadius(markerStyle, LineMarkerRadius),
                    markerStyle is null && automaticMarkerSymbol.HasValue
                        ? automaticMarkerSymbol.Value
                        : ResolveMarkerSymbol(markerStyle),
                    ResolveMarkerFill(series, seriesIndex, pointIndex, markerStyle, seriesColors, RectSeriesFillAlpha, fillPlans),
                    ResolveMarkerStroke(series, seriesIndex, pointIndex, markerStyle, seriesColors, LineMarkerStrokeThickness)));
            }

            previousPointIndex = pointIndex;
            previousPoint = point.Value;
        }

        bool isSmoothed = series.SmoothLine == true;

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

                double x = plot.X + categoryIndex * stepX;
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

                double x = plot.X + categoryIndex * stepX;
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
        for (int pointIndex = 0; pointIndex < pointSlots.Count; pointIndex++)
        {
            var point = pointSlots[pointIndex];
            if (point.HasValue)
            {
                segment.Add(point.Value);
                continue;
            }

            if (splitAtBlankSlots)
                AddAreaSegmentPrimitive(primitives, seriesIndex, segment, baselineSlots, baselineY, fill, depth);
        }

        AddAreaSegmentPrimitive(primitives, seriesIndex, segment, baselineSlots, baselineY, fill, depth);
    }

    private static void AddAreaSegmentPrimitive(
        List<ChartAreaSeriesPrimitive> primitives,
        int seriesIndex,
        List<ChartPlanPoint> segment,
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
            Depth = depth
        });

        segment.Clear();
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
            BuildAxisLabelFormatPlan(chart.ValueAxis));

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
                    markers.Add(new ChartCirclePrimitive(
                        seriesIndex,
                        pointIndex,
                        point.Value,
                        ResolveMarkerRadius(markerStyle, ScatterMarkerRadius),
                        ResolveMarkerSymbol(markerStyle),
                        ResolveMarkerFill(series, seriesIndex, pointIndex, markerStyle, seriesColors, defaultAlpha: 255, fillPlans),
                        markerStyle is not null || hasAuthoredPointStyle
                            ? ResolveMarkerStroke(series, seriesIndex, pointIndex, markerStyle, seriesColors, LineMarkerStrokeThickness)
                            : null));
                }

                previousPointIndex = pointIndex;
                previousPoint = point.Value;
            }

            dataLabels.AddRange(BuildScatterDataLabelPlans(chart, seriesIndex, points));
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

        return new ChartScatterPrimitivePlan(
            gridLines,
            DefaultGridLineStroke(),
            xLabels,
            yLabels,
            seriesPrimitives,
            dataLabels);
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
                if (!yValue.HasValue)
                    continue;
                if (bubbleValue < 0 && !chart.ShowNegativeBubbles)
                    continue;

                double radius = bubbleValue.HasValue
                    ? ComputeBubbleRadius(Math.Abs(bubbleValue.Value), maxBubble, maxBubbleRadius, chart.BubbleSizeRepresents)
                    : maxBubbleRadius * 0.3;
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
            BuildAxisLabelFormatPlan(chart.ValueAxis));

        return new ChartBubblePrimitivePlan(
            gridLines,
            DefaultGridLineStroke(),
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

        var center = new ChartPlanPoint(plot.X + plot.Width / 2, plot.Y + plot.Height / 2);
        double radius = Math.Min(plot.Width, plot.Height) / 2 * 0.75;
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

        var rings = new List<ChartRadarRingPrimitive>();
        for (int ring = 1; ring <= 4; ring++)
        {
            double ringRadius = radius * ring / 4;
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
                DefaultGridLineStroke()));
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

        var labels = new List<ChartTextPlan>();
        for (int categoryIndex = 0; categoryIndex < chart.Categories.Count && categoryIndex < categoryCount; categoryIndex++)
        {
            double angle = GetRadarAngle(categoryIndex, categoryCount);
            double labelX = center.X + (radius + 6) * Math.Cos(angle);
            double labelY = center.Y + (radius + 6) * Math.Sin(angle);
            labels.Add(new ChartTextPlan(
                chart.Categories[categoryIndex],
                new ChartPlanRect(labelX - 20, labelY - 6, 40, ResolveCategoryLabelHeight(chart)),
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
            var stroke = ResolveAuthoredSeriesStroke(series, seriesIndex, seriesColors, RadarSeriesStrokeThickness)
                ?? new ChartStrokePlan(color, Alpha: 255, Thickness: RadarSeriesStrokeThickness);
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
            DefaultRadarSpokeStroke(),
            labels,
            seriesPrimitives);
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

        double outerRadius = Math.Min(plot.Width, plot.Height) / 2 * 0.85;
        return BuildSlicePrimitivesForSeries(
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
        ChartPlanRect plot)
    {
        var family = GetRenderFamily(chart.ChartType);
        if (family is ChartRenderFamily.Radar or ChartRenderFamily.ScatterLike || !plot.HasPositiveArea)
            return Array.Empty<ChartDataLabelPlan>();

        if (family == ChartRenderFamily.Pie)
            return BuildPieDataLabelPlans(chart, plot);

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
                ? BuildLineDataLabelPlans(chart, seriesIndex, plot)
                : seriesIsBar
                    ? BuildBarDataLabelPlans(chart, seriesIndex, plot)
                    : BuildColumnDataLabelPlans(chart, seriesIndex, plot);

            plans.AddRange(seriesPlans);
        }

        return plans;
    }

    public static (double min, double max, double majorUnit) ComputePrimaryValueAxisRange(
        ChartShape chart)
    {
        if (IsHundredPercentStacked(chart.ChartType) &&
            chart.ValueAxis.Min is null &&
            chart.ValueAxis.Max is null &&
            chart.Series.Any(series => !series.OnSecondaryAxis && series.Values.Any(value => value.HasValue)))
        {
            return (0, 1, 0.25);
        }

        double dataMin = 0;
        double dataMax = 0;

        if (chart.ChartType == ChartType.AreaStacked)
        {
            AccumulateStackedCategoryTotals(chart, onSecondaryAxis: false, ref dataMin, ref dataMax);
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
        if (UsesStockLineFallback(chart) && chart.ValueAxis.Min is null && chart.ValueAxis.Max is null)
            return ComputeStockFallbackValueAxisRange(min, max);
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
        return ComputeNiceRange(min, max);
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
        return ComputeNiceRange(min, max);
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
        string? seriesName)
    {
        string formattedValue = string.IsNullOrEmpty(labels.NumberFormat)
            ? FormatAxisValue(value)
            : FormatWithCode(value, labels.NumberFormat!);

        string percent = total > 0
            ? $"{value / total * 100:0}%"
            : "0%";

        var parts = new StringBuilder();
        if (labels.ShowSeriesName && !string.IsNullOrEmpty(seriesName))
            parts.Append(seriesName).Append(' ');
        if (labels.ShowCategoryName && !string.IsNullOrEmpty(categoryName))
            parts.Append(categoryName).Append(' ');
        if (labels.ShowValue)
            parts.Append(formattedValue).Append(' ');
        if (labels.ShowPercent)
            parts.Append(percent).Append(' ');

        return parts.ToString().Trim();
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
        double depthOffsetY)
    {
        var values = GetVisiblePieValues(series);
        if (values.Count == 0)
            return Array.Empty<ChartPieSlicePrimitive>();

        double total = values.Sum(value => value.Value);
        if (total <= 0)
            return Array.Empty<ChartPieSlicePrimitive>();

        var center = new ChartPlanPoint(
            plot.X + plot.Width / 2,
            plot.Y + plot.Height / 2);
        double startAngle = initialStartAngle;
        var primitives = new List<ChartPieSlicePrimitive>(values.Count);
        foreach (var visibleValue in values)
        {
            double sweepAngle = visibleValue.Value / total * 2 * Math.PI;
            double endAngle = startAngle + sweepAngle;
            primitives.Add(new ChartPieSlicePrimitive(
                seriesIndex,
                visibleValue.PointIndex,
                center,
                innerRadius,
                outerRadius,
                startAngle,
                endAngle,
                ResolvePointFill(
                    series,
                    seriesIndex,
                    visibleValue.PointIndex,
                    seriesColors,
                    RectSeriesFillAlpha,
                    fillPlans,
                    varyByPoint))
            {
                VerticalScale = verticalScale,
                DepthOffsetY = depthOffsetY
            });
            startAngle = endAngle;
        }

        return primitives;
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
            ? ThreeDPieVerticalScale
            : 1.0;

    public static double ResolvePieDepthOffset(ChartShape chart, double outerRadius) =>
        chart.ChartType == ChartType.Pie && chart.ThreeDStyle == ChartThreeDStyle.Pie
            ? Math.Round(Math.Clamp(outerRadius * 0.22, 2.0, 14.0), 4)
            : 0;

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
            ChartAxisLabelFormatPlan? yAxisLabelFormat)
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
        var gridLines = new List<ChartGridLinePlan>(xTickCount + yTickCount + 2);
        var xLabels = new List<ChartTextPlan>(xTickCount + 1);
        var yLabels = new List<ChartTextPlan>(yTickCount + 1);

        for (int tickIndex = 0; tickIndex <= xTickCount; tickIndex++)
        {
            double x = plot.X + plot.Width * tickIndex / xSteps;
            gridLines.Add(new ChartGridLinePlan(
                new ChartPlanPoint(x, plot.Y),
                new ChartPlanPoint(x, plot.Bottom)));

            double value = xMin + xUnit * tickIndex;
            xLabels.Add(new ChartTextPlan(
                FormatAxisValue(value, xAxisLabelFormat?.FormatCode),
                new ChartPlanRect(x - 20, plot.Bottom + 2, 40, 12),
                IsBold: false,
                FontSize: 6.5,
                Alignment: ChartPlanTextAlignment.Center,
                AxisLabelFormat: xAxisLabelFormat));
        }

        for (int tickIndex = 0; tickIndex <= yTickCount; tickIndex++)
        {
            double y = plot.Bottom - plot.Height * tickIndex / ySteps;
            gridLines.Add(new ChartGridLinePlan(
                new ChartPlanPoint(plot.X, y),
                new ChartPlanPoint(plot.Right, y)));

            double value = yMin + yUnit * tickIndex;
            yLabels.Add(new ChartTextPlan(
                FormatAxisValue(value, yAxisLabelFormat?.FormatCode),
                new ChartPlanRect(plot.X - 38, y - 6, 36, 12),
                IsBold: false,
                FontSize: 6.5,
                Alignment: ChartPlanTextAlignment.Right,
                AxisLabelFormat: yAxisLabelFormat));
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

    private static ChartMajorGridLinePrimitivePlan EmptyMajorGridLinePrimitivePlan() =>
        new(
            Array.Empty<ChartGridLinePlan>(),
            DefaultGridLineStroke());

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
        chart is not null && UsesClassicOfficeChartStyle(chart)
            ? new ChartStrokePlan(new SrgbColor(0x00, 0x00, 0x00), Alpha: 255, Thickness: 0.5)
            : new ChartStrokePlan(new SrgbColor(0xD9, 0xD9, 0xD9), Alpha: 255, Thickness: 0.5);

    private static ChartStrokePlan DefaultAxisTickStroke(ChartShape? chart = null) =>
        chart is not null && UsesClassicOfficeChartStyle(chart)
            ? new ChartStrokePlan(new SrgbColor(0x00, 0x00, 0x00), Alpha: 255, Thickness: 0.75)
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
        IReadOnlyList<ChartPlanPoint?> points)
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
            string text = FormatDataLabel(labels, value.Value, total, categoryName, series.Name);
            if (string.IsNullOrEmpty(text))
                continue;

            plans.Add(new ChartDataLabelPlan(
                seriesIndex,
                pointIndex,
                text,
                PlanScatterDataLabelBounds(point.Value, labels.Position ?? DataLabelPosition.Above),
                IsBold: false,
                FontSize: ResolveTextFontSize(chart, 6.5),
                Alignment: ChartPlanTextAlignment.Center));
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
        ChartPlanRect plot)
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

        bool stacked = chart.ChartType is ChartType.ColumnStacked or ChartType.ColumnStacked100;
        bool percentStacked = IsHundredPercentStacked(chart.ChartType);
        double categoryWidth = plot.Width / categoryCount;
        int seriesCount = Math.Max(1, chart.Series.Count);
        var spacing = ResolveBarClusterSpacing(chart, categoryWidth, seriesCount, stacked);
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
            double barX = stacked
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

            double total = ComputeDataLabelTotal(chart, series, categoryIndex, stacked, labels);
            string categoryName = categoryIndex < chart.Categories.Count
                ? chart.Categories[categoryIndex]
                : string.Empty;
            string text = FormatDataLabel(labels, value, total, categoryName, series.Name);
            if (string.IsNullOrEmpty(text))
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
                ? Math.Max(50.0, slot.SeriesSize)
                : slot.SeriesSize;
            double labelX = UsesImportedTextMetrics(chart)
                ? barX + slot.SeriesSize / 2.0 - labelWidth / 2.0
                : barX;
            plans.Add(new ChartDataLabelPlan(
                seriesIndex,
                categoryIndex,
                text,
                new ChartPlanRect(labelX, labelY, labelWidth, labelHeight),
                IsBold: false,
                FontSize: ResolveTextFontSize(chart, 6.5),
                Alignment: ChartPlanTextAlignment.Center));
        }

        return plans;
    }

    private static IReadOnlyList<ChartDataLabelPlan> BuildLineDataLabelPlans(
        ChartShape chart,
        int seriesIndex,
        ChartPlanRect plot)
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
            if (string.IsNullOrEmpty(text))
                continue;

            plans.Add(new ChartDataLabelPlan(
                seriesIndex,
                categoryIndex,
                text,
                new ChartPlanRect(x - 20, y - ResolveDataLabelHeight(chart) - 3, 40, ResolveDataLabelHeight(chart)),
                IsBold: false,
                FontSize: ResolveTextFontSize(chart, 6.5),
                Alignment: ChartPlanTextAlignment.Center));
        }

        return plans;
    }

    private static IReadOnlyList<ChartDataLabelPlan> BuildBarDataLabelPlans(
        ChartShape chart,
        int seriesIndex,
        ChartPlanRect plot)
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

        bool stacked = chart.ChartType is ChartType.BarStacked or ChartType.BarStacked100;
        bool percentStacked = IsHundredPercentStacked(chart.ChartType);
        double categoryHeight = plot.Height / categoryCount;
        int seriesCount = Math.Max(1, chart.Series.Count);
        var spacing = ResolveBarClusterSpacing(chart, categoryHeight, seriesCount, stacked);
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
                barY = slot.ClusterStart;
            }
            else
            {
                int renderSeries = seriesCount - 1 - seriesIndex;
                barWidth = Math.Abs((value - effectiveMin) / effectiveRange * plot.Width);
                barX = plot.X;
                barY = slot.ClusterStart + renderSeries * slot.SeriesStep;
            }

            double total = ComputeDataLabelTotal(chart, series, categoryIndex, stacked, labels);
            string categoryName = categoryIndex < chart.Categories.Count
                ? chart.Categories[categoryIndex]
                : string.Empty;
            string text = FormatDataLabel(labels, value, total, categoryName, series.Name);
            if (string.IsNullOrEmpty(text))
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

            plans.Add(new ChartDataLabelPlan(
                seriesIndex,
                categoryIndex,
                text,
                new ChartPlanRect(labelX, labelY, 44, labelHeight),
                IsBold: false,
                FontSize: ResolveTextFontSize(chart, 6.5),
                Alignment: ChartPlanTextAlignment.Center));
        }

        return plans;
    }

    private static IReadOnlyList<ChartDataLabelPlan> BuildPieDataLabelPlans(
        ChartShape chart,
        ChartPlanRect plot)
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
            if (!string.IsNullOrEmpty(text))
            {
                double labelX = centerX + labelRadius * Math.Cos(midAngle);
                double labelY = centerY + labelRadius * Math.Sin(midAngle);
                double labelWidth = Math.Max(64, text.Length * 12.0);
                plans.Add(new ChartDataLabelPlan(
                    SeriesIndex: 0,
                    CategoryIndex: visibleValue.PointIndex,
                    Text: text,
                    Bounds: new ChartPlanRect(labelX - labelWidth / 2, labelY - ResolveDataLabelHeight(chart) / 2, labelWidth, ResolveDataLabelHeight(chart)),
                    IsBold: false,
                    FontSize: ResolveTextFontSize(chart, 6.5),
                    Alignment: ChartPlanTextAlignment.Center));
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
    {
        if (max <= min)
            max = min + 1;

        double range = max - min;
        double rawUnit = range / 4.0;
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
        double clusterSize = categorySize * 100.0 / (100.0 + gapWidth);
        clusterSize = Math.Clamp(clusterSize, 1.0, categorySize);
        double categoryStart = (categorySize - clusterSize) / 2.0;

        if (stacked || seriesCount <= 1)
        {
            return new ChartBarClusterSlot(
                categoryStart,
                categorySize,
                categoryStart,
                clusterSize,
                clusterSize,
                clusterSize);
        }

        double overlap = Math.Clamp(chart.BarOverlapPercent ?? 0, -100, 100) / 100.0;
        double denominator = seriesCount - overlap * (seriesCount - 1);
        double seriesSize = denominator <= 0 ? clusterSize : clusterSize / denominator;
        double seriesStep = seriesSize * (1.0 - overlap);
        double occupied = seriesSize + seriesStep * (seriesCount - 1);
        double clusterStart = categoryStart + (clusterSize - occupied) / 2.0;

        return new ChartBarClusterSlot(
            categoryStart,
            categorySize,
            clusterStart,
            clusterSize,
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
        if (chart.BarGapDepthPercent is not { } rawDepth)
            return null;

        int gapDepth = Math.Clamp(rawDepth, 0, 500);
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

    private static ChartBarClusterSlot ResolveBarClusterSlot(
        double plotStart,
        int categoryIndex,
        ChartBarClusterSlot spacing) =>
        spacing with
        {
            CategoryStart = plotStart + categoryIndex * spacing.CategorySize,
            ClusterStart = plotStart + categoryIndex * spacing.CategorySize + spacing.ClusterStart
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
