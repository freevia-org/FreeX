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

public readonly record struct ChartTextPlan(
    string Text,
    ChartPlanRect Bounds,
    bool IsBold,
    double FontSize,
    ChartPlanTextAlignment Alignment);

public readonly record struct ChartRectPrimitive(
    int SeriesIndex,
    int CategoryIndex,
    ChartPlanRect Bounds,
    ChartFillPlan Fill,
    ChartStrokePlan? Stroke);

public readonly record struct ChartLineSeriesPrimitive(
    int SeriesIndex,
    bool WithMarkers,
    IReadOnlyList<ChartPlanPoint?> Points,
    ChartStrokePlan Stroke,
    ChartFillPlan? MarkerFill,
    ChartStrokePlan? MarkerStroke,
    double MarkerRadius,
    IReadOnlyList<ChartLineSegmentPrimitive> LineSegments,
    IReadOnlyList<ChartCirclePrimitive> Markers);

public readonly record struct ChartAreaSeriesPrimitive(
    int SeriesIndex,
    ChartPlanPoint BaselineStart,
    ChartPlanPoint BaselineEnd,
    IReadOnlyList<ChartPlanPoint> Points,
    ChartPathPrimitive AreaPath,
    ChartFillPlan Fill);

public readonly record struct ChartScatterSeriesPrimitive(
    int SeriesIndex,
    bool DrawLines,
    bool DrawMarkers,
    IReadOnlyList<ChartPlanPoint?> Points,
    IReadOnlyList<ChartLineSegmentPrimitive> LineSegments,
    IReadOnlyList<ChartCirclePrimitive> Markers);

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
    IReadOnlyList<ChartPlanPoint> Points,
    ChartPathPrimitive Path,
    ChartStrokePlan Stroke,
    IReadOnlyList<ChartCirclePrimitive> Markers);

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
    public ChartPlanPoint OuterStart => PointOnCircle(OuterRadius, StartAngle);
    public ChartPlanPoint OuterEnd => PointOnCircle(OuterRadius, EndAngle);
    public ChartPlanPoint InnerEnd => PointOnCircle(InnerRadius, EndAngle);
    public ChartPlanPoint InnerStart => PointOnCircle(InnerRadius, StartAngle);

    private ChartPlanPoint PointOnCircle(double radius, double angle) =>
        new(Center.X + radius * Math.Cos(angle), Center.Y + radius * Math.Sin(angle));
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
    public const byte BubbleFillAlpha = 180;
    public const double BubbleStrokeThickness = 0.8;
    public const byte RadarFillAlpha = 80;
    public const double RadarSeriesStrokeThickness = 1.5;
    public const double RadarMarkerRadius = 3.0;
    private const double DipPerPoint = 96.0 / 72.0;

    private static readonly SrgbColor[] FallbackSeriesColors =
    [
        new(0x4F, 0x81, 0xBD),
        new(0xC0, 0x50, 0x4D),
        new(0x9B, 0xBB, 0x59),
        new(0x80, 0x64, 0xA2),
        new(0x4B, 0xAC, 0xC6),
        new(0xF7, 0x96, 0x46)
    ];

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

        var color = series.LineStyle?.Color?.Resolved
            ?? series.FillColor?.Resolved
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
            ?? pointColorOverride
            ?? series.FillColor?.Resolved
            ?? ResolveSeriesColor(seriesIndex, seriesColors);
        return new ChartFillPlan(color, defaultAlpha);
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

    public static ChartFramePlan BuildFramePlan(ChartShape chart, ChartPlanRect bounds)
    {
        double titleAreaHeight = chart.Title is not null ? TitleHeight + Margin : 0;
        bool hasLegend = chart.Legend.HasValue;
        bool legendRight = chart.Legend is LegendPosition.Right or LegendPosition.Left;
        double legendAreaWidth = hasLegend && legendRight
            ? Math.Min(90, bounds.Width * 0.20)
            : 0;
        double legendAreaHeight = hasLegend && !legendRight
            ? LegendHeight + Margin
            : 0;

        var family = GetRenderFamily(chart.ChartType);
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
        bool hasSecondaryValueAxis = reservesAxes &&
            !isBar &&
            chart.SecondaryValueAxis is { Delete: false };
        bool hasSecondaryValueAxisTitle = hasSecondaryValueAxis &&
            !string.IsNullOrWhiteSpace(chart.SecondaryValueAxis?.Title);
        double secondaryAxisAreaWidth = hasSecondaryValueAxis
            ? AxisLabelWidth + (hasSecondaryValueAxisTitle ? SecondaryAxisTitleGap + AxisTitleBand : 0)
            : 0;
        bool hasDataTable = HasSupportedDataTable(chart, family);
        double categoryBandHeight = hasDataTable
            ? ComputeDataTableReservedHeight(chart)
            : CategoryLabelHeight;
        double plotLeft = bounds.X + Margin
            + (reservesAxes ? (isBar ? BarCategoryLabelWidth : AxisLabelWidth) : 0)
            + (hasValueAxisTitle && !isBar ? AxisTitleBand : 0)
            + (hasCategoryAxisTitle && isBar ? AxisTitleBand : 0);
        double plotTop = bounds.Y + Margin + titleAreaHeight;
        double plotRight = bounds.X + bounds.Width - Margin - legendAreaWidth - secondaryAxisAreaWidth;
        double plotBottom = bounds.Y + bounds.Height - Margin - legendAreaHeight
            - (reservesAxes ? (isBar ? AxisLabelWidth : categoryBandHeight) : 0)
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
        ChartPlanRect? titleBounds = chart.Title is not null
            ? new ChartPlanRect(
                bounds.X + Margin,
                bounds.Y + Margin,
                bounds.Width - 2 * Margin,
                TitleHeight)
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
            _ => ChartRenderFamily.Cartesian
        };

    public static IReadOnlyList<ChartLegendItemPlan> BuildLegendItemPlans(
        ChartShape chart,
        ChartFramePlan frame,
        IReadOnlyList<SrgbColor>? seriesColors,
        ChartFillPlanSet? fillPlans = null)
    {
        if (!frame.HasLegend || !frame.HasPlot || chart.Series.Count == 0)
            return Array.Empty<ChartLegendItemPlan>();

        var plot = frame.Plot;
        double legendX;
        double legendY;
        double legendWidth;
        if (frame.LegendRight)
        {
            legendX = frame.Bounds.X + frame.Bounds.Width - frame.LegendAreaWidth - Margin / 2;
            legendY = plot.Y;
            legendWidth = frame.LegendAreaWidth - Margin / 2;
        }
        else
        {
            legendX = plot.X;
            legendY = frame.Bounds.Y + frame.Bounds.Height - frame.LegendAreaHeight - Margin / 2;
            legendWidth = plot.Width;
        }

        int itemCount = frame.IsPie
            ? chart.Categories.Count > 0
                ? chart.Categories.Count
                : chart.Series[0].Values.Count > 0
                    ? chart.Series[0].Values.Count
                    : 0
            : chart.Series.Count;
        int maxItems = (int)Math.Max(1, frame.LegendRight ? plot.Height / LegendHeight : legendWidth / 80);
        int itemsToShow = Math.Min(itemCount, maxItems);
        if (itemsToShow == 0)
            return Array.Empty<ChartLegendItemPlan>();

        var items = new List<ChartLegendItemPlan>(itemsToShow);
        for (int itemIndex = 0; itemIndex < itemsToShow; itemIndex++)
        {
            double itemX = frame.LegendRight ? legendX : legendX + itemIndex * 80.0;
            double itemY = frame.LegendRight ? legendY + itemIndex * LegendHeight : legendY;
            double textWidth = frame.LegendRight ? legendWidth - 10 : 70;
            string text = frame.IsPie
                ? itemIndex < chart.Categories.Count
                    ? chart.Categories[itemIndex]
                    : $"Point {itemIndex + 1}"
                : chart.Series[itemIndex].Name;
            var color = seriesColors is not null && itemIndex < seriesColors.Count
                ? seriesColors[itemIndex]
                : FallbackSeriesColors[0];

            items.Add(new ChartLegendItemPlan(
                new ChartPlanRect(itemX, itemY + 3, 8, 8),
                new ChartTextPlan(
                    text,
                    new ChartPlanRect(itemX + 10, itemY, textWidth, LegendHeight),
                    IsBold: false,
                    FontSize: 7.0,
                    Alignment: ChartPlanTextAlignment.Left),
                fillPlans is not null
                    ? ResolveSeriesFill(itemIndex, seriesColors, alpha: 255, fillPlans)
                    : new ChartFillPlan(color, Alpha: 255)));
        }

        return items;
    }

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

        return new ChartMajorGridLinePrimitivePlan(
            lines,
            DefaultGridLineStroke());
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
            DefaultAxisTickStroke());
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
                    chart.Categories[categoryIndex],
                    new ChartPlanRect(plot.X - BarCategoryLabelWidth, y, BarCategoryLabelWidth - 4, categoryStep),
                    IsBold: false,
                    FontSize: 6.5,
                    Alignment: ChartPlanTextAlignment.Right));
            }
        }
        else
        {
            double categoryStep = plot.Width / Math.Max(1, chart.Categories.Count);
            for (int categoryIndex = 0; categoryIndex < chart.Categories.Count; categoryIndex++)
            {
                double x = plot.X + categoryIndex * categoryStep;
                labels.Add(new ChartTextPlan(
                    chart.Categories[categoryIndex],
                    new ChartPlanRect(x, plot.Bottom + 2, categoryStep, CategoryLabelHeight),
                    IsBold: false,
                    FontSize: 7.0,
                    Alignment: ChartPlanTextAlignment.Center));
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
                    FormatAxisValue(value),
                    new ChartPlanRect(x - AxisLabelWidth / 2, plot.Bottom + 2, AxisLabelWidth, CategoryLabelHeight),
                    IsBold: false,
                    FontSize: 6.5,
                    Alignment: ChartPlanTextAlignment.Center));
            }
            else
            {
                double y = plot.Bottom - plot.Height * tickIndex / steps;
                labels.Add(new ChartTextPlan(
                    FormatAxisValue(value),
                    new ChartPlanRect(plot.X - AxisLabelWidth, y - 6, AxisLabelWidth - GridlinePad, 12),
                    IsBold: false,
                    FontSize: 6.5,
                    Alignment: ChartPlanTextAlignment.Right));
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
                FormatAxisValue(value),
                new ChartPlanRect(labelX, y - 6, labelWidth, 12),
                IsBold: false,
                FontSize: 6.5,
                Alignment: ChartPlanTextAlignment.Left));
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
        const double gapRatio = 1.5;
        double categoryWidth = plot.Width / categoryCount;
        double clusterWidth = categoryWidth / (1.0 + gapRatio);
        double halfGap = (categoryWidth - clusterWidth) / 2.0;
        int seriesCount = Math.Max(1, chart.Series.Count);
        double seriesWidth = stacked ? clusterWidth : clusterWidth / seriesCount;
        bool varyByPoint = ShouldVaryPointColors(chart);

        var primitives = new List<ChartRectPrimitive>();
        for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
        {
            double categoryLeft = plot.X + categoryIndex * categoryWidth + halfGap;
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

                double? rawValue = categoryIndex < series.Values.Count
                    ? series.Values[categoryIndex]
                    : null;
                if (rawValue is null)
                    continue;

                double effectiveMin = series.OnSecondaryAxis ? secondaryMin : primaryMin;
                double effectiveRange = series.OnSecondaryAxis ? secondaryRange : primaryRange;
                if (effectiveRange <= 0)
                    continue;

                double x = stacked ? categoryLeft : categoryLeft + seriesIndex * seriesWidth;
                double drawWidth = Math.Max(1, stacked ? seriesWidth : seriesWidth - 1);
                if (stacked)
                {
                    double height = Math.Max(0.5, Math.Abs(rawValue.Value / effectiveRange) * plot.Height);
                    primitives.Add(new ChartRectPrimitive(
                        seriesIndex,
                        categoryIndex,
                        new ChartPlanRect(x, stackedY - height, drawWidth, height),
                        ResolvePointFill(series, seriesIndex, categoryIndex, seriesColors, RectSeriesFillAlpha, fillPlans, varyByPoint),
                        Stroke: null));
                    stackedY -= height;
                }
                else
                {
                    double height = Math.Max(0.5, Math.Abs((rawValue.Value - effectiveMin) / effectiveRange * plot.Height));
                    double y = plot.Bottom - (rawValue.Value - effectiveMin) / effectiveRange * plot.Height;
                    primitives.Add(new ChartRectPrimitive(
                        seriesIndex,
                        categoryIndex,
                        new ChartPlanRect(x, y, drawWidth, height),
                        ResolvePointFill(series, seriesIndex, categoryIndex, seriesColors, RectSeriesFillAlpha, fillPlans, varyByPoint),
                        Stroke: null));
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
        const double gapRatio = 1.5;
        double categoryHeight = plot.Height / categoryCount;
        double clusterHeight = categoryHeight / (1.0 + gapRatio);
        double halfGap = (categoryHeight - clusterHeight) / 2.0;
        int seriesCount = Math.Max(1, chart.Series.Count);
        double seriesHeight = stacked ? clusterHeight : clusterHeight / seriesCount;
        bool varyByPoint = ShouldVaryPointColors(chart);

        var primitives = new List<ChartRectPrimitive>();
        for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
        {
            int renderRow = categoryCount - 1 - categoryIndex;
            double categoryTop = plot.Y + renderRow * categoryHeight + halfGap;
            double stackedX = plot.X;

            for (int seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
            {
                var series = chart.Series[seriesIndex];
                double? rawValue = categoryIndex < series.Values.Count
                    ? series.Values[categoryIndex]
                    : null;
                if (rawValue is null)
                    continue;

                double effectiveMin = series.OnSecondaryAxis ? secondaryMin : primaryMin;
                double effectiveRange = series.OnSecondaryAxis ? secondaryRange : primaryRange;
                if (effectiveRange <= 0)
                    continue;

                double width = Math.Max(0.5, Math.Abs((rawValue.Value - effectiveMin) / effectiveRange * plot.Width));
                int renderSeries = stacked ? seriesIndex : seriesCount - 1 - seriesIndex;
                double y = stacked ? categoryTop : categoryTop + renderSeries * seriesHeight;
                double x = stacked ? stackedX : plot.X;
                double height = Math.Max(1, stacked ? seriesHeight : seriesHeight - 1);

                primitives.Add(new ChartRectPrimitive(
                    seriesIndex,
                    categoryIndex,
                    new ChartPlanRect(x, y, width, height),
                    ResolvePointFill(series, seriesIndex, categoryIndex, seriesColors, RectSeriesFillAlpha, fillPlans, varyByPoint),
                    Stroke: null));

                if (stacked)
                    stackedX += width;
            }
        }

        return primitives;
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
                double? rawValue = categoryIndex < series.Values.Count
                    ? series.Values[categoryIndex]
                    : null;
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
                fillPlans));
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
                double? rawValue = categoryIndex < series.Values.Count
                    ? series.Values[categoryIndex]
                    : null;
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
                fillPlans));
        }

        return primitives;
    }

    public static ChartLineSeriesPrimitive BuildLineSeriesPrimitive(
        int seriesIndex,
        bool withMarkers,
        IReadOnlyList<ChartPlanPoint?> points,
        ChartSeries? series = null,
        IReadOnlyList<SrgbColor>? seriesColors = null,
        ChartFillPlanSet? fillPlans = null)
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
                previousPointIndex = null;
                previousPoint = null;
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
                    ResolveMarkerSymbol(markerStyle),
                    ResolveMarkerFill(series, seriesIndex, pointIndex, markerStyle, seriesColors, RectSeriesFillAlpha, fillPlans),
                    ResolveMarkerStroke(series, seriesIndex, pointIndex, markerStyle, seriesColors, LineMarkerStrokeThickness)));
            }

            previousPointIndex = pointIndex;
            previousPoint = point.Value;
        }

        return new ChartLineSeriesPrimitive(
            seriesIndex,
            withMarkers,
            points,
            stroke,
            markerFill,
            markerStroke,
            markerRadius,
            lineSegments,
            markers);
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
        var baselineStart = new ChartPlanPoint(plot.X, plot.Bottom);
        var baselineEnd = new ChartPlanPoint(plot.Right, plot.Bottom);
        var primitives = new List<ChartAreaSeriesPrimitive>();

        for (int seriesIndex = chart.Series.Count - 1; seriesIndex >= 0; seriesIndex--)
        {
            var series = chart.Series[seriesIndex];
            if (series.Values.Count == 0)
                continue;

            var points = new ChartPlanPoint[categoryCount];
            for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
            {
                double value = categoryIndex < series.Values.Count
                    ? series.Values[categoryIndex] ?? 0
                    : 0;
                double x = plot.X + categoryIndex * stepX;
                double y = plot.Bottom - (value - minValue) / range * plot.Height;
                points[categoryIndex] = new ChartPlanPoint(x, y);
            }

            var fill = ResolveSeriesFill(seriesIndex, seriesColors, AreaFillAlpha, fillPlans);
            var pathPoints = new ChartPlanPoint[categoryCount + 2];
            pathPoints[0] = baselineStart;
            for (int pointIndex = 0; pointIndex < points.Length; pointIndex++)
                pathPoints[pointIndex + 1] = points[pointIndex];
            pathPoints[^1] = baselineEnd;

            primitives.Add(new ChartAreaSeriesPrimitive(
                seriesIndex,
                baselineStart,
                baselineEnd,
                points,
                new ChartPathPrimitive(
                    pathPoints,
                    IsClosed: true,
                    Fill: fill),
                fill));
        }

        return primitives;
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
            yUnit);

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
                double? yValue = pointIndex < series.Values.Count ? series.Values[pointIndex] : null;
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
                    previousPointIndex = null;
                    previousPoint = null;
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

            seriesPrimitives.Add(new ChartScatterSeriesPrimitive(
                seriesIndex,
                drawLines,
                drawMarkers,
                points,
                lineSegments,
                markers));
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

        var (xMin, xMax, xUnit) = ComputeScatterAxisRange(chart, useX: true);
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
                if (value.HasValue)
                    maxBubble = Math.Max(maxBubble, value.Value);
            }
        }

        if (maxBubble <= 0)
            maxBubble = 1;

        double maxBubbleRadius = Math.Min(plot.Width, plot.Height) / 8.0;
        var bubbles = new List<ChartBubblePrimitive>();
        for (int seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
        {
            var series = chart.Series[seriesIndex];
            var color = ResolveSeriesColor(seriesIndex, seriesColors);
            var stroke = new ChartStrokePlan(color, Alpha: 255, Thickness: BubbleStrokeThickness);
            int pointCount = Math.Max(series.XValues.Count, series.Values.Count);
            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                double? xValue = pointIndex < series.XValues.Count ? series.XValues[pointIndex] : null;
                double? yValue = pointIndex < series.Values.Count ? series.Values[pointIndex] : null;
                double? bubbleValue = pointIndex < series.BubbleSizes.Count ? series.BubbleSizes[pointIndex] : null;
                if (!xValue.HasValue || !yValue.HasValue)
                    continue;

                double radius = bubbleValue.HasValue
                    ? Math.Sqrt(bubbleValue.Value / maxBubble) * maxBubbleRadius
                    : maxBubbleRadius * 0.3;
                radius = Math.Max(2, radius);

                bubbles.Add(new ChartBubblePrimitive(
                    seriesIndex,
                    pointIndex,
                    new ChartPlanPoint(
                        plot.X + (xValue.Value - xMin) / xRange * plot.Width,
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
            yUnit);

        return new ChartBubblePrimitivePlan(
            gridLines,
            DefaultGridLineStroke(),
            xLabels,
            yLabels,
            bubbles);
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
                new ChartPlanRect(labelX - 20, labelY - 6, 40, 12),
                IsBold: false,
                FontSize: 6.5,
                Alignment: ChartPlanTextAlignment.Center));
        }

        bool withMarkers = chart.RadarStyle == RadarStyle.Marker;
        bool filled = chart.RadarStyle == RadarStyle.Filled;
        var seriesPrimitives = new List<ChartRadarSeriesPrimitive>();
        for (int seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
        {
            var series = chart.Series[seriesIndex];
            var points = new ChartPlanPoint[categoryCount];
            for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
            {
                double? value = categoryIndex < series.Values.Count ? series.Values[categoryIndex] : null;
                double fraction = Math.Clamp((value ?? 0) / dataMax, 0, 1);
                double angle = GetRadarAngle(categoryIndex, categoryCount);
                points[categoryIndex] = new ChartPlanPoint(
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
                for (int pointIndex = 0; pointIndex < points.Length; pointIndex++)
                {
                    var markerStyle = ResolvePointMarkerStyle(series, pointIndex);
                    bool hasAuthoredPointStyle = series.PointStyles.ContainsKey(pointIndex);
                    if (SuppressesMarker(markerStyle))
                        continue;

                    markers.Add(new ChartCirclePrimitive(
                        seriesIndex,
                        pointIndex,
                        points[pointIndex],
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
                points,
                new ChartPathPrimitive(points, IsClosed: true, fill),
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

    public static IReadOnlyList<ChartPieSlicePrimitive> BuildPieSlicePrimitives(
        ChartShape chart,
        ChartPlanRect plot,
        IReadOnlyList<SrgbColor>? seriesColors = null,
        ChartFillPlanSet? fillPlans = null)
    {
        if (chart.Series.Count == 0 || !plot.HasPositiveArea)
            return Array.Empty<ChartPieSlicePrimitive>();

        return BuildSlicePrimitivesForSeries(
            chart.Series[0],
            seriesIndex: 0,
            plot,
            innerRadius: 0,
            outerRadius: Math.Min(plot.Width, plot.Height) / 2 * 0.85,
            seriesColors,
            fillPlans,
            ShouldVaryPointColors(chart));
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
                seriesColors,
                fillPlans,
                ShouldVaryPointColors(chart)));
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
        double dataMin = 0;
        double dataMax = 0;

        foreach (var series in chart.Series)
        {
            if (series.OnSecondaryAxis)
                continue;

            AccumulateValues(series.Values, ref dataMin, ref dataMax);
        }

        double min = chart.ValueAxis.Min ?? (dataMin >= 0 ? 0 : dataMin);
        double max = chart.ValueAxis.Max ?? dataMax;
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

    public static ChartDataLabels? ResolveEffectiveLabels(ChartShape chart, int seriesIndex)
    {
        var series = seriesIndex < chart.Series.Count ? chart.Series[seriesIndex] : null;
        var labels = series?.DataLabels ?? chart.DataLabels;
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
        if (code.Contains('%'))
        {
            double percent = value * 100.0;
            int dotPosition = code.IndexOf('.');
            int decimals = dotPosition >= 0 ? code.LastIndexOf('%') - dotPosition - 1 : 0;
            string format = decimals > 0 ? $"F{decimals}" : "F0";
            return percent.ToString(format, CultureInfo.InvariantCulture) + "%";
        }

        if (code.Contains(','))
            return value.ToString("N0", CultureInfo.InvariantCulture);

        int dotIndex = code.IndexOf('.');
        if (dotIndex >= 0)
        {
            int decimals = code.Length - dotIndex - 1;
            return value.ToString($"F{decimals}", CultureInfo.InvariantCulture);
        }

        return FormatAxisValue(value);
    }

    private static IReadOnlyList<ChartPieSlicePrimitive> BuildSlicePrimitivesForSeries(
        ChartSeries series,
        int seriesIndex,
        ChartPlanRect plot,
        double innerRadius,
        double outerRadius,
        IReadOnlyList<SrgbColor>? seriesColors,
        ChartFillPlanSet? fillPlans,
        bool varyByPoint)
    {
        var values = series.Values
            .Where(value => value.HasValue && value.Value > 0)
            .Select(value => value!.Value)
            .ToList();
        if (values.Count == 0)
            return Array.Empty<ChartPieSlicePrimitive>();

        double total = values.Sum();
        if (total <= 0)
            return Array.Empty<ChartPieSlicePrimitive>();

        var center = new ChartPlanPoint(
            plot.X + plot.Width / 2,
            plot.Y + plot.Height / 2);
        double startAngle = -Math.PI / 2;
        var primitives = new List<ChartPieSlicePrimitive>(values.Count);
        for (int pointIndex = 0; pointIndex < values.Count; pointIndex++)
        {
            double sweepAngle = values[pointIndex] / total * 2 * Math.PI;
            double endAngle = startAngle + sweepAngle;
            primitives.Add(new ChartPieSlicePrimitive(
                seriesIndex,
                pointIndex,
                center,
                innerRadius,
                outerRadius,
                startAngle,
                endAngle,
                ResolvePointFill(series, seriesIndex, pointIndex, seriesColors, RectSeriesFillAlpha, fillPlans, varyByPoint)));
            startAngle = endAngle;
        }

        return primitives;
    }

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
            double yUnit)
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
                FormatAxisValue(value),
                new ChartPlanRect(x - 20, plot.Bottom + 2, 40, 12),
                IsBold: false,
                FontSize: 6.5,
                Alignment: ChartPlanTextAlignment.Center));
        }

        for (int tickIndex = 0; tickIndex <= yTickCount; tickIndex++)
        {
            double y = plot.Bottom - plot.Height * tickIndex / ySteps;
            gridLines.Add(new ChartGridLinePlan(
                new ChartPlanPoint(plot.X, y),
                new ChartPlanPoint(plot.Right, y)));

            double value = yMin + yUnit * tickIndex;
            yLabels.Add(new ChartTextPlan(
                FormatAxisValue(value),
                new ChartPlanRect(plot.X - 38, y - 6, 36, 12),
                IsBold: false,
                FontSize: 6.5,
                Alignment: ChartPlanTextAlignment.Right));
        }

        return (gridLines, xLabels, yLabels);
    }

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

    private static ChartStrokePlan DefaultGridLineStroke() =>
        new(new SrgbColor(0xD9, 0xD9, 0xD9), Alpha: 255, Thickness: 0.5);

    private static ChartStrokePlan DefaultAxisTickStroke() =>
        new(new SrgbColor(0x7F, 0x7F, 0x7F), Alpha: 255, Thickness: 0.75);

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
                stop.Color.Resolved)).ToArray(),
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
                FontSize: 6.5,
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
        const double gapRatio = 1.5;
        double categoryWidth = plot.Width / categoryCount;
        double clusterWidth = categoryWidth / (1.0 + gapRatio);
        double halfGap = (categoryWidth - clusterWidth) / 2.0;
        int seriesCount = Math.Max(1, chart.Series.Count);
        double seriesWidth = stacked ? clusterWidth : clusterWidth / seriesCount;
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
            double barX = stacked
                ? plot.X + categoryIndex * categoryWidth + halfGap
                : plot.X + categoryIndex * categoryWidth + halfGap + seriesIndex * seriesWidth;

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

                    double height = Math.Max(0.5, Math.Abs(previousValue.Value / effectiveRange) * plot.Height);
                    stackedY -= height;
                }

                barHeight = Math.Max(0.5, Math.Abs(value / effectiveRange) * plot.Height);
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

            const double labelHeight = 11.0;
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

            plans.Add(new ChartDataLabelPlan(
                seriesIndex,
                categoryIndex,
                text,
                new ChartPlanRect(barX, labelY, seriesWidth, labelHeight),
                IsBold: false,
                FontSize: 6.5,
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
                new ChartPlanRect(x - 20, y - 14, 40, 11),
                IsBold: false,
                FontSize: 6.5,
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
        const double gapRatio = 1.5;
        double categoryHeight = plot.Height / categoryCount;
        double clusterHeight = categoryHeight / (1.0 + gapRatio);
        double halfGap = (categoryHeight - clusterHeight) / 2.0;
        int seriesCount = Math.Max(1, chart.Series.Count);
        double seriesHeight = stacked ? clusterHeight : clusterHeight / seriesCount;
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
            double categoryTop = plot.Y + renderRow * categoryHeight + halfGap;

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

                    stackedX += Math.Max(0.5, Math.Abs((previousValue.Value - effectiveMin) / effectiveRange * plot.Width));
                }

                barWidth = Math.Max(0.5, Math.Abs((value - effectiveMin) / effectiveRange * plot.Width));
                barX = stackedX;
                barY = categoryTop;
            }
            else
            {
                int renderSeries = seriesCount - 1 - seriesIndex;
                barWidth = Math.Abs((value - effectiveMin) / effectiveRange * plot.Width);
                barX = plot.X;
                barY = categoryTop + renderSeries * seriesHeight;
            }

            double total = ComputeDataLabelTotal(chart, series, categoryIndex, stacked, labels);
            string categoryName = categoryIndex < chart.Categories.Count
                ? chart.Categories[categoryIndex]
                : string.Empty;
            string text = FormatDataLabel(labels, value, total, categoryName, series.Name);
            if (string.IsNullOrEmpty(text))
                continue;

            const double labelHeight = 11.0;
            double labelX = position switch
            {
                DataLabelPosition.InsideEnd => barX + barWidth - 22 - 2,
                DataLabelPosition.Center => barX + barWidth / 2 - 22,
                DataLabelPosition.InsideBase => barX + 2,
                _ => barX + barWidth + 2
            };
            double labelY = barY + seriesHeight / 2 - labelHeight / 2;

            plans.Add(new ChartDataLabelPlan(
                seriesIndex,
                categoryIndex,
                text,
                new ChartPlanRect(labelX, labelY, 44, labelHeight),
                IsBold: false,
                FontSize: 6.5,
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
        var values = firstSeries.Values
            .Where(value => value.HasValue && value.Value > 0)
            .Select(value => value!.Value)
            .ToList();
        if (values.Count == 0)
            return Array.Empty<ChartDataLabelPlan>();

        double total = values.Sum();
        if (total <= 0)
            return Array.Empty<ChartDataLabelPlan>();

        double centerX = plot.X + plot.Width / 2;
        double centerY = plot.Y + plot.Height / 2;
        double radius = Math.Min(plot.Width, plot.Height) / 2 * 0.85;
        double startAngle = -Math.PI / 2;
        var position = labels.Position ?? DataLabelPosition.BestFit;
        double labelRadius = position == DataLabelPosition.InsideEnd
            ? radius * 0.65
            : radius * 1.15;
        var plans = new List<ChartDataLabelPlan>();

        for (int valueIndex = 0; valueIndex < values.Count; valueIndex++)
        {
            double sweepAngle = values[valueIndex] / total * 2 * Math.PI;
            double midAngle = startAngle + sweepAngle / 2;
            string categoryName = valueIndex < chart.Categories.Count
                ? chart.Categories[valueIndex]
                : string.Empty;
            string text = FormatDataLabel(labels, values[valueIndex], total, categoryName, firstSeries.Name);
            if (!string.IsNullOrEmpty(text))
            {
                double labelX = centerX + labelRadius * Math.Cos(midAngle);
                double labelY = centerY + labelRadius * Math.Sin(midAngle);
                plans.Add(new ChartDataLabelPlan(
                    SeriesIndex: 0,
                    CategoryIndex: valueIndex,
                    Text: text,
                    Bounds: new ChartPlanRect(labelX - 22, labelY - 6, 44, 12),
                    IsBold: false,
                    FontSize: 6.5,
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

        if (Math.Abs(niceMax - max) < majorUnit * 1e-9)
            niceMax += majorUnit;

        return (niceMin, niceMax, majorUnit);
    }

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
