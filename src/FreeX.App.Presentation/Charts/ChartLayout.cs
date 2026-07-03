using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts;

/// <summary>A single axis tick: its data value, its pixel position, and its formatted label.</summary>
public readonly record struct AxisTick(double Value, double Position, string Label);

/// <summary>
/// The laid-out geometry for one chart axis: its side, screen line position, and major ticks. The
/// desktop hosts draw the axis line, tick marks, and labels from this; all positioning math is done.
/// </summary>
public sealed class AxisLayout
{
    public required AxisSide Side { get; init; }
    public string? Title { get; init; }

    /// <summary>The fixed pixel coordinate of the axis line (X for vertical axes, Y for horizontal).</summary>
    public double LinePosition { get; init; }

    public required IReadOnlyList<AxisTick> Ticks { get; init; }

    /// <summary>The underlying scale, exposed so consumers can map extra values if needed.</summary>
    public required AxisScale Scale { get; init; }

    /// <summary>
    /// Clockwise rotation angle (degrees) for tick labels, as set on the chart axis.
    /// 0 = horizontal (default). Negative values rotate counter-clockwise (e.g. -45 for diagonal labels).
    /// The shell renderers apply this to each tick-label element.
    /// </summary>
    public double LabelAngle { get; init; }
}

/// <summary>The kind of geometry a series produced, so consumers can pick the right draw path.</summary>
public enum SeriesGeometryKind
{
    Columns,
    Bars,
    Line,
    Area,
    ScatterPoints,
    PieSlices,
    Bubbles,
    RadarPolyline,
    StockBars,
    /// <summary>Box-and-whisker overlay lines (median + whisker segments). Rendered as paired line segments.</summary>
    BoxWhiskers,
    /// <summary>Treemap tiles: solid colored rectangles with labels centered inside each tile.</summary>
    TreemapTiles,
    /// <summary>
    /// Surface/heatmap cells: a grid of solid colored rectangles where each cell's fill is mapped
    /// from its z-value through a min→max color gradient (blue at minimum, yellow at maximum).
    /// Rows correspond to series, columns to categories. Rendered by the shell renderers.
    /// </summary>
    SurfaceCells,
}

/// <summary>The fit a trendline overlay was computed with, so consumers can label/style it.</summary>
public enum TrendlineFitKind
{
    Linear,
    Exponential,
    Logarithmic,
    Power,
    MovingAverage,
    Polynomial
}

/// <summary>
/// One laid-out data point of a marker/line/area/scatter series: the data value and its plot-space
/// pixel position. For line/area series the points are in category order.
/// </summary>
public readonly record struct SeriesPoint(int PointIndex, double DataX, double DataY, LayoutPoint Position);

/// <summary>A laid-out rectangular bar/column with its source value and category index.</summary>
/// <param name="FillColorOverride">
/// Optional per-bar fill override. When non-null the renderer uses this color instead of the
/// series-level palette fill (used for waterfall increase/decrease/total coloring).
/// </param>
public readonly record struct SeriesBar(int PointIndex, double Value, LayoutRect Rect, CellColor? FillColorOverride = null);

/// <summary>A laid-out pie/doughnut slice: its arc geometry, value, and share of the total.</summary>
public readonly record struct SeriesSlice(int PointIndex, double Value, double Fraction, string Label, LayoutArc Arc);

/// <summary>
/// A laid-out bubble: its center in plot space, the source x/y/size values, and the pixel radius the
/// desktop hosts draw the marker at (already scaled from the size dimension).
/// </summary>
public readonly record struct SeriesBubble(int PointIndex, double DataX, double DataY, double SizeValue, LayoutPoint Center, double Radius);

/// <summary>
/// A laid-out stock (high-low-close / open-high-low-close) element for one category: the shared pixel
/// X, the high/low pixel Y of the vertical line, and the open/close pixel Y of the tick/box edges.
/// <see cref="HasOpen"/> is false for plain high-low-close series (no open tick / candle box).
/// </summary>
public readonly record struct StockElement(
    int PointIndex,
    double X,
    double HighY,
    double LowY,
    double OpenY,
    double CloseY,
    double OpenValue,
    double HighValue,
    double LowValue,
    double CloseValue,
    bool HasOpen)
{
    /// <summary>True when the close is at or above the open (an up bar), for up/down colouring.</summary>
    public bool IsUp => CloseValue >= OpenValue;
}

/// <summary>
/// One radar (spider) axis radiating from the plot center: the category index it represents, the
/// angle in degrees (clockwise from 12 o'clock), and the spoke's outer endpoint at the axis maximum.
/// </summary>
public readonly record struct RadarSpoke(int CategoryIndex, double AngleDegrees, LayoutPoint Outer);

/// <summary>
/// A laid-out trendline overlay for a series: the fit kind and the polyline of plot-space points the
/// desktop hosts stroke. The points are already mapped through the same value/category scales as the
/// series, so consumers draw them directly. Empty when no trendline (or an undefined fit).
/// </summary>
public sealed class TrendlineLayout
{
    public required TrendlineFitKind Fit { get; init; }
    public required IReadOnlyList<LayoutPoint> Points { get; init; }

    /// <summary>
    /// The equation and/or R-squared annotation text lines, when
    /// <see cref="ChartModel.ShowTrendlineEquation"/> or <see cref="ChartModel.ShowTrendlineRSquared"/>
    /// is set; otherwise empty. Consumers join these with a newline and draw them near
    /// <see cref="AnnotationAnchor"/>.
    /// </summary>
    public IReadOnlyList<string> AnnotationLines { get; init; } = [];

    /// <summary>
    /// The plot-space anchor point for the annotation text box (top-left corner), already mapped
    /// through the same scales as <see cref="Points"/>. Mirrors the source renderer's placement at
    /// the source data's (min X, max Y). Meaningless when <see cref="AnnotationLines"/> is empty.
    /// </summary>
    public LayoutPoint AnnotationAnchor { get; init; }
}

/// <summary>
/// The laid-out geometry for a single series. Only the collections relevant to
/// <see cref="Kind"/> are populated.
/// </summary>
public sealed record SeriesLayout
{
    public required int SeriesIndex { get; init; }
    public string? Name { get; init; }
    public required SeriesGeometryKind Kind { get; init; }

    public IReadOnlyList<SeriesBar> Bars { get; init; } = [];
    public IReadOnlyList<SeriesPoint> Points { get; init; } = [];
    public IReadOnlyList<SeriesSlice> Slices { get; init; } = [];

    /// <summary>For bubble series, the laid-out bubbles (center + pixel radius). Empty otherwise.</summary>
    public IReadOnlyList<SeriesBubble> Bubbles { get; init; } = [];

    /// <summary>For stock series, the per-category high-low(-open)-close elements. Empty otherwise.</summary>
    public IReadOnlyList<StockElement> StockElements { get; init; } = [];

    /// <summary>For area series, the baseline pixel Y the fill drops to (the zero line).</summary>
    public double AreaBaseline { get; init; }

    /// <summary>For surface/heatmap series, the laid-out grid cells (row × col, each pre-colored). Empty otherwise.</summary>
    public IReadOnlyList<SurfaceCell> SurfaceCells { get; init; } = [];

    /// <summary>
    /// The trendline overlay for this series, when the chart requests one and the fit is defined;
    /// otherwise null. Additive: existing series have no trendline.
    /// </summary>
    public TrendlineLayout? Trendline { get; init; }

    /// <summary>
    /// True when this series is plotted against the chart's secondary value axis (combo charts).
    /// Consumers map the series with <see cref="ChartLayout.SecondaryValueAxis"/> when set.
    /// </summary>
    public bool UsesSecondaryAxis { get; init; }

    /// <summary>
    /// For waterfall series: the horizontal connector lines between adjacent bars. Each entry is a
    /// pair of pixel points forming a short horizontal segment at the running-total level after
    /// bar <em>i</em>. Empty for non-waterfall series.
    /// </summary>
    public IReadOnlyList<(LayoutPoint Left, LayoutPoint Right)> WaterfallConnectors { get; init; } = [];
}

/// <summary>
/// One laid-out surface/heatmap cell: the row (series) index, column (category) index, the source
/// z-value, its pixel rectangle, and the pre-computed gradient fill color.
/// </summary>
public readonly record struct SurfaceCell(int Row, int Col, double Value, LayoutRect Rect, CellColor FillColor);

/// <summary>A single laid-out legend entry: its swatch rectangle and its label box.</summary>
public readonly record struct LegendEntry(int SeriesIndex, string Label, LayoutRect SwatchRect, LayoutRect LabelRect);

/// <summary>The laid-out legend: its bounding box and per-series entries (empty when no legend).</summary>
public sealed class LegendLayout
{
    public ChartLegendPosition Position { get; init; }
    public LayoutRect Bounds { get; init; }
    public IReadOnlyList<LegendEntry> Entries { get; init; } = [];

    public static readonly LegendLayout None = new() { Position = ChartLegendPosition.None };
}

/// <summary>A laid-out data label: its text, anchor point, and bounding box.</summary>
public readonly record struct DataLabelBox(
    int SeriesIndex,
    int PointIndex,
    string Text,
    LayoutPoint Anchor,
    LayoutRect Bounds);

/// <summary>
/// The complete portable layout for a chart: the plot rectangle actually used, the axes, the
/// per-series geometry, the legend, and the data-label boxes. A desktop host can render the whole
/// chart from this without re-deriving any positioning math.
/// </summary>
public sealed class ChartLayout
{
    public required ChartType Type { get; init; }

    /// <summary>The plot rectangle the series were laid out inside (after legend gutter).</summary>
    public required LayoutRect PlotArea { get; init; }

    public AxisLayout? CategoryAxis { get; init; }
    public AxisLayout? ValueAxis { get; init; }

    /// <summary>
    /// The secondary value axis for combo charts, when the chart enables one and at least one series
    /// is assigned to it; otherwise null. Series with <see cref="SeriesLayout.UsesSecondaryAxis"/>
    /// are laid out against this axis. Additive: simple charts leave it null.
    /// </summary>
    public AxisLayout? SecondaryValueAxis { get; init; }

    /// <summary>
    /// For radar charts: the laid-out spokes (one per category) plus the plot center and outer radius.
    /// Null for non-radar charts. The series points themselves live on the per-series layouts.
    /// </summary>
    public RadarLayout? Radar { get; init; }

    public required IReadOnlyList<SeriesLayout> Series { get; init; }
    public LegendLayout Legend { get; init; } = LegendLayout.None;
    public IReadOnlyList<DataLabelBox> DataLabels { get; init; } = [];
}

/// <summary>
/// The shared geometry of a radar chart's category axes: the plot center, the outer radius at the
/// value-axis maximum, and the per-category spokes. Series polylines are mapped onto these spokes.
/// </summary>
public sealed class RadarLayout
{
    public required LayoutPoint Center { get; init; }
    public required double OuterRadius { get; init; }
    public required IReadOnlyList<RadarSpoke> Spokes { get; init; }
}
