using System.Text.Json.Serialization;

namespace FreeP.Core.Model;

/// <summary>High-level chart type, covering the most common OOXML chart variants.</summary>
public enum ChartType
{
    ColumnClustered,
    ColumnStacked,
    ColumnStacked100,
    BarClustered,
    BarStacked,
    BarStacked100,
    Line,
    LineMarkers,
    Pie,
    Area,
    AreaStacked,
    Scatter,
    /// <summary>Doughnut (annular pie) chart. HoleSize in ChartShape.DoughnutHolePercent.</summary>
    Doughnut,
    /// <summary>Pie-of-pie or bar-of-pie chart backed by OOXML <c>c:ofPieChart</c>.</summary>
    OfPie,
    /// <summary>Radar (spider/polar) chart. Each series is a closed polygon on N spokes.</summary>
    Radar,
    /// <summary>Bubble chart. Like scatter but each point has a BubbleSizes list in its series.</summary>
    Bubble,
    /// <summary>Stock chart, including high-low stems and open/close ticks when those series are modeled.</summary>
    Stock,
    /// <summary>2-D surface chart, rendered as a value-colored matrix until full surface mesh geometry lands.</summary>
    Surface,
    /// <summary>3-D surface chart, rendered as a value-colored matrix until full surface mesh geometry lands.</summary>
    Surface3D,
    /// <summary>Funnel chart with one category/value series.</summary>
    Funnel,
    /// <summary>Waterfall chart with one category/value series.</summary>
    Waterfall,
    Unknown
}

/// <summary>Secondary plot kind for an OOXML <c>c:ofPieChart</c>.</summary>
public enum OfPieType { Pie, Bar }

/// <summary>Authored point-selection rule for an OOXML <c>c:ofPieChart</c>.</summary>
public enum OfPieSplitType { Auto, Custom, Percent, Position, Value }

/// <summary>Scatter/bubble style read from c:scatterStyle or similar.</summary>
public enum ScatterStyle { Marker, LineMarker, Line, Smooth, SmoothMarker }

/// <summary>PowerPoint bubble-size representation from <c>c:sizeRepresents</c>.</summary>
public enum BubbleSizeRepresentation { Area, Width }

/// <summary>Radar style read from c:radarStyle.</summary>
public enum RadarStyle { Standard, Marker, Filled }

/// <summary>Authored chart marker symbol, matching the common OOXML <c>c:marker/c:symbol</c> values.</summary>
public enum ChartMarkerSymbol
{
    Auto,
    Circle,
    Dash,
    Diamond,
    Dot,
    None,
    Picture,
    Plus,
    Square,
    Star,
    Triangle,
    X
}

/// <summary>Position of the chart legend relative to the plot area.</summary>
public enum LegendPosition { Right, Left, Top, Bottom }

/// <summary>Which chart surface receives an authored fill and outline.</summary>
public enum ChartAreaFormattingTarget { ChartArea, PlotArea }

/// <summary>Coordinate mode for OOXML chart <c>c:manualLayout</c> values.</summary>
public enum ChartManualLayoutMode { Factor, Edge, Unsupported }

/// <summary>How blank cells are displayed in a chart.</summary>
public enum ChartDisplayBlanksAs { Span, Gap, Zero }

/// <summary>Authored OOXML axis tick mark placement.</summary>
public enum ChartTickMark { None, Cross, In, Out }

/// <summary>Authored OOXML axis label placement.</summary>
public enum ChartTickLabelPosition { None, Low, High, NextTo }

/// <summary>Authored value-axis category-boundary placement.</summary>
public enum ChartCrossBetween { Between, MidCat }

/// <summary>Authored category-axis label alignment.</summary>
public enum ChartLabelAlignment { Left, Center, Right }

/// <summary>Axis direction for a series error-bar set.</summary>
public enum ChartErrorDirection { X, Y }

/// <summary>Which side(s) of a data point receive an error bar.</summary>
public enum ChartErrorBarType { Both, Minus, Plus }

/// <summary>How the authored error magnitude is interpreted.</summary>
public enum ChartErrorValueType { Fixed, Percentage }

/// <summary>Trendline regression family authored under a chart series.</summary>
public enum ChartTrendlineType { Linear, Exponential, Logarithmic, Polynomial, Power, MovingAverage }

/// <summary>Authored PowerPoint trendline settings from c:trendline.</summary>
public sealed class ChartTrendline
{
    public ChartTrendlineType Type { get; set; } = ChartTrendlineType.Linear;
    public int? PolynomialOrder { get; set; }
    public int? MovingAveragePeriod { get; set; }
    public double? Forward { get; set; }
    public double? Backward { get; set; }
    public bool DisplayEquation { get; set; }
    public bool DisplayRSquared { get; set; }
}

/// <summary>Authored series error-bar settings from c:errBars.</summary>
public sealed class ChartErrorBars
{
    public ChartErrorDirection Direction { get; set; } = ChartErrorDirection.Y;
    public ChartErrorBarType BarType { get; set; } = ChartErrorBarType.Both;
    public ChartErrorValueType ValueType { get; set; } = ChartErrorValueType.Fixed;
    public double Value { get; set; }
    public bool NoEndCap { get; set; }
}

/// <summary>Authored axis crossing mode from <c>c:crosses/@val</c>.</summary>
public enum ChartAxisCrossing { AutoZero, Min, Max }

/// <summary>Built-in value-axis display-unit choices from <c>c:dispUnits</c>.</summary>
public enum ChartAxisDisplayUnit
{
    None,
    Hundreds,
    Thousands,
    TenThousands,
    HundredThousands,
    Millions,
    TenMillions,
    HundredMillions,
    Billions,
    Trillions,
    Custom,
    Unsupported,
}

/// <summary>Authored classic 3-D chart family read from OOXML chart-type elements.</summary>
public enum ChartThreeDStyle { None, Pie, Line, Area, Column, Bar }

/// <summary>Authored chart camera and projection settings from <c>c:view3D</c>.</summary>
public sealed class Chart3DView
{
    /// <summary>Elevation in degrees from <c>c:rotX</c>.</summary>
    public int? RotationX { get; set; }
    /// <summary>Azimuth in degrees from <c>c:rotY</c>.</summary>
    public int? RotationY { get; set; }
    /// <summary>Whether the chart uses right-angle axes from <c>c:rAngAx</c>.</summary>
    public bool? RightAngleAxes { get; set; }
    /// <summary>Perspective strength percentage from <c>c:perspective</c>.</summary>
    public int? Perspective { get; set; }
    /// <summary>Chart height percentage from <c>c:hPercent</c>.</summary>
    public int? HeightPercent { get; set; }
    /// <summary>Chart depth percentage from <c>c:depthPercent</c>.</summary>
    public int? DepthPercent { get; set; }
}

/// <summary>Small modeled subset of OOXML chart <c>c:manualLayout</c>.</summary>
public sealed class ChartManualLayout
{
    /// <summary>Raw <c>c:layoutTarget/@val</c> value, such as <c>inner</c> or <c>outer</c>.</summary>
    public string? LayoutTarget { get; set; }

    public ChartManualLayoutMode XMode { get; set; } = ChartManualLayoutMode.Factor;
    public ChartManualLayoutMode YMode { get; set; } = ChartManualLayoutMode.Factor;
    public ChartManualLayoutMode WidthMode { get; set; } = ChartManualLayoutMode.Factor;
    public ChartManualLayoutMode HeightMode { get; set; } = ChartManualLayoutMode.Factor;

    /// <summary>
    /// Original <c>c:xMode/@val</c> token when it is not one of the modes FreeP models.
    /// The corresponding mode is <see cref="ChartManualLayoutMode.Unsupported"/>.
    /// </summary>
    public string? RawXModeToken { get; set; }

    /// <summary>Original <c>c:yMode/@val</c> token when it is not modeled.</summary>
    public string? RawYModeToken { get; set; }

    /// <summary>Original <c>c:wMode/@val</c> token when it is not modeled.</summary>
    public string? RawWidthModeToken { get; set; }

    /// <summary>Original <c>c:hMode/@val</c> token when it is not modeled.</summary>
    public string? RawHeightModeToken { get; set; }

    /// <summary>Manual x coordinate value from <c>c:x/@val</c>.</summary>
    public double? X { get; set; }

    /// <summary>Manual y coordinate value from <c>c:y/@val</c>.</summary>
    public double? Y { get; set; }

    /// <summary>Manual width value from <c>c:w/@val</c>.</summary>
    public double? Width { get; set; }

    /// <summary>Manual height value from <c>c:h/@val</c>.</summary>
    public double? Height { get; set; }

    /// <summary>True when v1 rendering can safely resolve this as a factor rectangle.</summary>
    [JsonIgnore]
    public bool IsCompleteFactorRectangle =>
        XMode == ChartManualLayoutMode.Factor &&
        YMode == ChartManualLayoutMode.Factor &&
        WidthMode == ChartManualLayoutMode.Factor &&
        HeightMode == ChartManualLayoutMode.Factor &&
        X.HasValue &&
        Y.HasValue &&
        Width.HasValue &&
        Height.HasValue;
}

/// <summary>Position of data labels relative to the data point.</summary>
public enum DataLabelPosition
{
    /// <summary>Best fit / auto-placed by the application.</summary>
    BestFit,
    /// <summary>Centered on the data point or bar.</summary>
    Center,
    /// <summary>Inside end of bar/column.</summary>
    InsideEnd,
    /// <summary>Outside end of bar/column (above column top, right of bar end).</summary>
    OutsideEnd,
    /// <summary>Inside base of bar/column.</summary>
    InsideBase,
    /// <summary>Above the data point (used for line/scatter).</summary>
    Above,
    /// <summary>Below the data point.</summary>
    Below,
    /// <summary>Left of the data point.</summary>
    Left,
    /// <summary>Right of the data point.</summary>
    Right,
}

/// <summary>
/// Data label configuration for a chart or series. Controls which label components are shown,
/// where labels are placed, and how numeric values are formatted.
/// </summary>
public sealed class ChartDataLabels
{
    /// <summary>Optional explicit delete token for a native point-label override.</summary>
    public bool? Delete { get; set; }

    /// <summary>Show the numeric value of the data point.</summary>
    public bool ShowValue { get; set; }

    /// <summary>Show the slice percentage (primarily pie/doughnut charts).</summary>
    public bool ShowPercent { get; set; }

    /// <summary>Show the category name alongside the value.</summary>
    public bool ShowCategoryName { get; set; }

    /// <summary>Show the series name alongside the value.</summary>
    public bool ShowSeriesName { get; set; }

    /// <summary>Show the legend key (color swatch) next to the label.</summary>
    public bool ShowLegendKey { get; set; }

    /// <summary>Show the bubble-size value for bubble-chart points.</summary>
    public bool ShowBubbleSize { get; set; }

    /// <summary>Whether pie/doughnut data labels use leader lines.</summary>
    public bool? ShowLeaderLines { get; set; }

    /// <summary>Label placement relative to the data point. Null means use the default for the chart type.</summary>
    public DataLabelPosition? Position { get; set; }

    /// <summary>Number format code (e.g. "0.00", "#,##0", "0%"). Null or empty = General.</summary>
    public string? NumberFormat { get; set; }

    /// <summary>Separator between composed label parts. Null keeps FreeP's compact space separator.</summary>
    public string? Separator { get; set; }

    /// <summary>Optional authored text properties from c:dLbls/c:txPr.</summary>
    public ChartTextStyle? TextStyle { get; set; }

    /// <summary>Returns true if any label component is enabled.</summary>
    public bool HasAny =>
        Delete.HasValue ||
        ShowValue ||
        ShowPercent ||
        ShowCategoryName ||
        ShowSeriesName ||
        ShowLegendKey ||
        ShowBubbleSize ||
        ShowLeaderLines.HasValue ||
        Position.HasValue ||
        NumberFormat is not null ||
        Separator is not null ||
        TextStyle is not null;
}

/// <summary>Data table settings for charts that render source values below the plot.</summary>
public sealed class ChartDataTableSettings
{
    /// <summary>Show horizontal row borders in the table.</summary>
    public bool ShowHorizontalBorder { get; set; } = true;

    /// <summary>Show vertical column borders in the table.</summary>
    public bool ShowVerticalBorder { get; set; } = true;

    /// <summary>Show the outside border around the table.</summary>
    public bool ShowOutlineBorder { get; set; } = true;

    /// <summary>Show series legend keys next to the series names.</summary>
    public bool ShowLegendKeys { get; set; }

    /// <summary>Optional table background fill from <c>c:dTable/c:spPr</c>.</summary>
    public ShapeFill? BackgroundFill { get; set; }

    /// <summary>Optional border line style from <c>c:dTable/c:spPr/a:ln</c>.</summary>
    public ShapeOutline? BorderOutline { get; set; }

    /// <summary>Optional default text properties from <c>c:dTable/c:txPr/a:p/a:pPr/a:defRPr</c>.</summary>
    public ChartTextStyle? TextStyle { get; set; }
}

/// <summary>Small chart text style subset shared by chart labels, data tables, and render planners.</summary>
public sealed class ChartTextStyle
{
    /// <summary>
    /// True when the reader synthesized the Office chart default because the source chart has no
    /// <c>c:chartSpace/c:txPr</c>. This must not override role-specific axis or label defaults,
    /// and it must not be serialized as an authored text-properties node.
    /// </summary>
    public bool IsImplicitDefault { get; set; }

    /// <summary>Font size in points, or null to use the chart renderer default.</summary>
    public double? FontSizePt { get; set; }

    /// <summary>Explicit bold state. Null means inherit/use renderer default.</summary>
    public bool? Bold { get; set; }

    /// <summary>Explicit italic state. Null means inherit/use renderer default.</summary>
    public bool? Italic { get; set; }

    /// <summary>Text color, or null to use the chart renderer default.</summary>
    public ThemeAwareColor? Color { get; set; }

    /// <summary>Font family/typeface name from <c>a:latin/@typeface</c>, or null to use the chart renderer default.</summary>
    public string? FontFamily { get; set; }
}

/// <summary>Authored chart line stroke style shared by IO and renderer-neutral chart planning.</summary>
public sealed class ChartLineStyle
{
    /// <summary>Stroke color. Null means use the chart's series color fallback.</summary>
    public ThemeAwareColor? Color { get; set; }

    /// <summary>Stroke width in points. Null means use the chart planner default.</summary>
    public double? WidthPt { get; set; }

    /// <summary>Stroke dash preset from <c>a:prstDash</c>.</summary>
    public OutlineDash Dash { get; set; } = OutlineDash.Solid;

    /// <summary>True when the authored chart explicitly suppresses the line.</summary>
    public bool NoFill { get; set; }
}

/// <summary>Authored chart marker style shared by IO and renderer-neutral chart planning.</summary>
public sealed class ChartMarkerStyle
{
    /// <summary>Marker symbol. Null means use the chart type default.</summary>
    public ChartMarkerSymbol? Symbol { get; set; }

    /// <summary>Marker size in points. Null means use the chart planner default.</summary>
    public double? SizePt { get; set; }

    /// <summary>Marker fill color. Null means use the series fill/color fallback.</summary>
    public ThemeAwareColor? FillColor { get; set; }

    /// <summary>Rich marker fill metadata. Used for gradient fills; solid compatibility remains in <see cref="FillColor"/>.</summary>
    public ShapeFill? Fill { get; set; }

    /// <summary>Marker stroke color. Null means use the series stroke/color fallback.</summary>
    public ThemeAwareColor? StrokeColor { get; set; }

    /// <summary>Marker stroke width in points. Null means use the chart planner default.</summary>
    public double? StrokeWidthPt { get; set; }

    /// <summary>True when the marker fill was authored as noFill.</summary>
    public bool NoFill { get; set; }

    /// <summary>True when the marker stroke was authored as noFill.</summary>
    public bool NoStroke { get; set; }
}

/// <summary>Point-level authored chart style override, keyed by zero-based point index.</summary>
public sealed class ChartPointStyle
{
    /// <summary>Pie or doughnut slice explosion percentage from <c>c:explosion</c>.</summary>
    public int? ExplosionPercent { get; set; }

    /// <summary>Optional data-label override for this individual point.</summary>
    public ChartDataLabels? DataLabels { get; set; }

    /// <summary>Point fill color. Null means inherit from the series marker/series fill.</summary>
    public ThemeAwareColor? FillColor { get; set; }

    /// <summary>Rich point fill metadata. Used for gradient fills; solid compatibility remains in <see cref="FillColor"/>.</summary>
    public ShapeFill? Fill { get; set; }

    /// <summary>Point stroke color. Null means inherit from the series marker/series line.</summary>
    public ThemeAwareColor? StrokeColor { get; set; }

    /// <summary>Point stroke width in points. Null means inherit from the marker/series stroke width.</summary>
    public double? StrokeWidthPt { get; set; }

    /// <summary>Point marker override. Null means inherit from the series marker style.</summary>
    public ChartMarkerStyle? Marker { get; set; }
}

/// <summary>Authored workbook formula references for a chart series.</summary>
public sealed class ChartSeriesFormulaReferences
{
    /// <summary>Formula backing the series name (<c>c:tx/c:strRef/c:f</c>).</summary>
    public string? SeriesName { get; set; }

    /// <summary>Formula backing category labels (<c>c:cat/c:strRef|numRef/c:f</c>).</summary>
    public string? Category { get; set; }

    /// <summary>Formula backing category-chart values (<c>c:val/c:numRef/c:f</c>).</summary>
    public string? Values { get; set; }

    /// <summary>Formula backing scatter/bubble X values (<c>c:xVal/c:numRef/c:f</c>).</summary>
    public string? XValues { get; set; }

    /// <summary>Formula backing scatter/bubble Y values (<c>c:yVal/c:numRef/c:f</c>).</summary>
    public string? YValues { get; set; }

    /// <summary>Formula backing bubble sizes (<c>c:bubbleSize/c:numRef/c:f</c>).</summary>
    public string? BubbleSizes { get; set; }

    public bool HasAny =>
        SeriesName is not null ||
        Category is not null ||
        Values is not null ||
        XValues is not null ||
        YValues is not null ||
        BubbleSizes is not null;
}

/// <summary>A single data series within a <see cref="ChartShape"/>.</summary>
public sealed class ChartSeries
{
    /// <summary>Series name (from c:tx cache).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Default series fill color. Null means use the theme accent cycle.</summary>
    public ThemeAwareColor? FillColor { get; set; }

    /// <summary>Rich series fill metadata. Used for gradient fills; solid compatibility remains in <see cref="FillColor"/>.</summary>
    public ShapeFill? Fill { get; set; }

    /// <summary>Authored line stroke style for line/scatter/radar series.</summary>
    public ChartLineStyle? LineStyle { get; set; }

    /// <summary>Authored marker style for line/scatter/radar series.</summary>
    public ChartMarkerStyle? MarkerStyle { get; set; }

    /// <summary>Authored smooth-line decision from <c>c:smooth</c>. Null means use PowerPoint's chart-type default.</summary>
    public bool? SmoothLine { get; set; }

    /// <summary>
    /// Whether negative values use the inverted series fill from OOXML
    /// <c>c:invertIfNegative</c>. Null preserves the chart application's default.
    /// </summary>
    public bool? InvertIfNegative { get; set; }

    /// <summary>Data point values, one per category. Null entries represent missing/gap points.</summary>
    public List<double?> Values { get; } = new();

    /// <summary>Per-point fill color overrides (keyed by zero-based point index). Used primarily for pie charts.</summary>
    public Dictionary<int, ThemeAwareColor> PointColors { get; } = new();

    /// <summary>Per-point style overrides (keyed by zero-based point index).</summary>
    public Dictionary<int, ChartPointStyle> PointStyles { get; } = new();

    // ── Scatter / Bubble extension fields ────────────────────────────────────────

    /// <summary>
    /// X-axis values for scatter/bubble charts (c:xVal).
    /// Parallel to <see cref="Values"/> (which holds Y values for scatter/bubble).
    /// Null/empty for category-based chart types.
    /// </summary>
    public List<double?> XValues { get; } = new();

    /// <summary>
    /// Bubble size values for bubble charts (c:bubbleSize). One per point.
    /// Empty for non-bubble chart types.
    /// </summary>
    public List<double?> BubbleSizes { get; } = new();

    /// <summary>Per-series data labels override. When null, the chart-level labels apply (if any).</summary>
    public ChartDataLabels? DataLabels { get; set; }

    /// <summary>Optional authored error bars for this series.</summary>
    public ChartErrorBars? ErrorBars { get; set; }

    /// <summary>Optional authored regression trendline for this series.</summary>
    public ChartTrendline? Trendline { get; set; }

    /// <summary>True if this series plots against the secondary value axis (right axis).</summary>
    public bool OnSecondaryAxis { get; set; }

    /// <summary>
    /// Per-series chart-type override for combo charts. Null means use the parent
    /// <see cref="ChartShape.ChartType"/>. Set by the IO reader when a secondary
    /// chart-type group (e.g. a lineChart nested inside a bar+line combo) overrides
    /// the render style for this series (e.g. Line vs ColumnClustered).
    /// </summary>
    public ChartType? OverrideChartType { get; set; }

    /// <summary>
    /// Source workbook formulas/ranges from OOXML <c>c:f</c> nodes. Preserved charts
    /// write these back; edited charts replace them with regenerated workbook ranges.
    /// </summary>
    public ChartSeriesFormulaReferences FormulaReferences { get; } = new();
}

/// <summary>Configuration for one chart axis (category or value).</summary>
public sealed class ChartAxis
{
    /// <summary>Axis title text. Null if no title is set.</summary>
    public string? Title { get; set; }

    /// <summary>Optional independent formatting for the axis title text.</summary>
    public ChartTextStyle? TitleStyle { get; set; }

    /// <summary>Authored axis label number/date format code from <c>c:numFmt/@formatCode</c>.</summary>
    public string? NumberFormatCode { get; set; }

    /// <summary>Authored axis label source-linked state from <c>c:numFmt/@sourceLinked</c>. Null means unspecified.</summary>
    public bool? NumberFormatSourceLinked { get; set; }

    /// <summary>Authored built-in display unit from <c>c:dispUnits/c:builtInUnit</c>.</summary>
    public ChartAxisDisplayUnit DisplayUnit { get; set; }

    /// <summary>Unknown <c>c:builtInUnit/@val</c> token retained losslessly.</summary>
    public string? RawDisplayUnitToken { get; set; }

    /// <summary>Authored custom display-unit divisor from <c>c:dispUnits/c:customUnit/@val</c>.</summary>
    public double? CustomDisplayUnit { get; set; }

    /// <summary>Explicit minimum scale value. Null = auto.</summary>
    public double? Min { get; set; }

    /// <summary>Explicit maximum scale value. Null = auto.</summary>
    public double? Max { get; set; }

    /// <summary>Authored major tick interval from <c>c:majorUnit/@val</c>.</summary>
    public double? MajorUnit { get; set; }

    /// <summary>Authored minor tick interval from <c>c:minorUnit/@val</c>.</summary>
    public double? MinorUnit { get; set; }

    /// <summary>Whether major gridlines are shown on this axis.</summary>
    public bool HasMajorGridlines { get; set; } = true;

    /// <summary>Whether minor gridlines are shown on this axis.</summary>
    public bool HasMinorGridlines { get; set; }

    /// <summary>Authored <c>c:majorTickMark/@val</c>; null means unspecified.</summary>
    public ChartTickMark? MajorTickMark { get; set; }

    /// <summary>Unknown <c>c:majorTickMark/@val</c> token retained losslessly.</summary>
    public string? RawMajorTickMarkToken { get; set; }

    /// <summary>Authored <c>c:minorTickMark/@val</c>; null means unspecified.</summary>
    public ChartTickMark? MinorTickMark { get; set; }

    /// <summary>Unknown <c>c:minorTickMark/@val</c> token retained losslessly.</summary>
    public string? RawMinorTickMarkToken { get; set; }

    /// <summary>Authored <c>c:tickLblPos/@val</c>; null means unspecified.</summary>
    public ChartTickLabelPosition? TickLabelPosition { get; set; }

    /// <summary>Unknown <c>c:tickLblPos/@val</c> token retained losslessly.</summary>
    public string? RawTickLabelPositionToken { get; set; }

    /// <summary>Authored category-axis label offset percentage from <c>c:lblOffset/@val</c>.</summary>
    public int? LabelOffsetPercent { get; set; }

    /// <summary>Authored category-axis multi-level-label suppression token.</summary>
    public bool? NoMultiLevelLabels { get; set; }

    /// <summary>Authored value-axis category-boundary placement from <c>c:crossBetween/@val</c>.</summary>
    public ChartCrossBetween? CrossBetween { get; set; }

    /// <summary>Unknown <c>c:crossBetween/@val</c> token retained losslessly.</summary>
    public string? RawCrossBetweenToken { get; set; }

    /// <summary>Authored category-axis automatic crossing state from <c>c:auto/@val</c>.</summary>
    public bool? AutoCrossing { get; set; }

    /// <summary>Authored category-axis label alignment from <c>c:lblAlgn/@val</c>.</summary>
    public ChartLabelAlignment? LabelAlignment { get; set; }

    /// <summary>Unknown <c>c:lblAlgn/@val</c> token retained losslessly.</summary>
    public string? RawLabelAlignmentToken { get; set; }

    /// <summary>Authored axis crossing mode. Null preserves the writer's existing chart-role default.</summary>
    public ChartAxisCrossing? Crosses { get; set; }

    /// <summary>Unknown <c>c:crosses/@val</c> token retained losslessly.</summary>
    public string? RawCrossesToken { get; set; }

    /// <summary>Authored numeric axis crossing value from <c>c:crossesAt/@val</c>.</summary>
    public double? CrossesAt { get; set; }

    /// <summary>Whether the axis orientation is authored as <c>maxMin</c> instead of <c>minMax</c>.</summary>
    public bool ReverseOrder { get; set; }

    /// <summary>True if the axis is deleted (hidden) in the chart XML.</summary>
    public bool Delete { get; set; }
}

/// <summary>
/// The chart payload attached to a <see cref="SlideShape"/> when <c>Kind == Chart</c>.
/// Contains parsed chart data suitable for rendering without needing to re-parse XML.
/// </summary>
public sealed class ChartShape
{
    /// <summary>Chart variant (column clustered, pie, line, etc.).</summary>
    public ChartType ChartType { get; set; } = ChartType.ColumnClustered;

    /// <summary>Whether an <c>ofPieChart</c> uses a second pie or a bar plot.</summary>
    public OfPieType OfPieType { get; set; } = OfPieType.Pie;

    /// <summary>Authored <c>ofPieChart/splitType</c>; null means the producer omitted it.</summary>
    public OfPieSplitType? OfPieSplitType { get; set; }

    /// <summary>Authored numeric <c>ofPieChart/splitPos</c>, when present.</summary>
    public double? OfPieSplitPosition { get; set; }

    /// <summary>Authored <c>ofPieChart/secondPieSize</c> percentage, when present.</summary>
    public int? OfPieSecondPieSizePercent { get; set; }

    /// <summary>
    /// Authored <c>ofPieChart/secondPiePt</c> category indices for a custom split.
    /// The list is empty when the producer uses an automatic split rule.
    /// </summary>
    public List<int> OfPieCustomPointIndices { get; set; } = [];

    /// <summary>Whether the source explicitly included <c>ofPieChart/serLines</c>.</summary>
    public bool OfPieSeriesLinesSpecified { get; set; }

    /// <summary>
    /// Whether the source explicitly included a chart-level <c>serLines</c> element.
    /// The OfPie-specific property is retained for the existing pie-options workflow;
    /// this broader flag preserves the same authored token on other chart families.
    /// </summary>
    public bool SeriesLinesSpecified { get; set; }

    /// <summary>
    /// Authored <c>c:serLines/c:spPr/a:ln</c> style for stacked-chart series
    /// connectors. Null preserves the chart-family default stroke.
    /// </summary>
    public ChartLineStyle? SeriesLineStyle { get; set; }

    /// <summary>
    /// Whether a pie-family chart explicitly included the chart-level
    /// <c>leaderLines</c> element. This is distinct from the data-label
    /// <c>showLeaderLines</c> option, which is the older modeled route.
    /// </summary>
    public bool LeaderLinesSpecified { get; set; }

    /// <summary>
    /// True when an imported stock chart authors <c>c:hiLowLines</c>. When false,
    /// PowerPoint renders the stock series as ordinary line-and-marker series.
    /// </summary>
    public bool HasHighLowLines { get; set; } = true;

    /// <summary>Whether an imported line-family chart authors <c>c:dropLines</c>.</summary>
    public bool ShowDropLines { get; set; }

    /// <summary>Whether an imported line-family chart authors <c>c:upDownBars</c>.</summary>
    public bool ShowUpDownBars { get; set; }

    /// <summary>Whether a waterfall chart draws the authored <c>c:showConnectorLines</c>.</summary>
    public bool ShowWaterfallConnectorLines { get; set; } = true;

    /// <summary>Authored <c>c:upDownBars/c:gapWidth</c> percentage, when present.</summary>
    public int? UpDownBarGapWidthPercent { get; set; }

    /// <summary>Authored <c>c:upDownBars/c:upBars/c:spPr</c> fill, when present.</summary>
    public ShapeFill? UpBarFill { get; set; }

    /// <summary>Authored <c>c:upDownBars/c:downBars/c:spPr</c> fill, when present.</summary>
    public ShapeFill? DownBarFill { get; set; }

    /// <summary>
    /// Authored chart style identifier from <c>c:chartSpace/c:style</c> or its
    /// newer compatibility extension. Null means the classic Office default style.
    /// </summary>
    public int? StyleId { get; set; }

    /// <summary>Chart title text, or null if no title.</summary>
    public string? Title { get; set; }

    /// <summary>
    /// Explicit PowerPoint chart-title overlay state from <c>c:title/c:overlay</c>.
    /// Null means the source did not author the token; false is the normal above-plot
    /// placement and true places the title over the plot area.
    /// </summary>
    public bool? TitleOverlay { get; set; }

    /// <summary>Optional independent formatting for the chart title text.</summary>
    public ChartTextStyle? TitleStyle { get; set; }

    /// <summary>Optional authored chart-area fill from <c>c:chartSpace/c:spPr</c>.</summary>
    public ShapeFill? ChartAreaFill { get; set; }

    /// <summary>Optional authored chart-area outline from <c>c:chartSpace/c:spPr/a:ln</c>.</summary>
    public ShapeOutline? ChartAreaOutline { get; set; }

    /// <summary>True when PowerPoint supplied <see cref="Title"/> as an automatic title.</summary>
    public bool HasAutomaticTitle { get; set; }

    /// <summary>
    /// Default chart text properties from <c>c:chartSpace/c:txPr</c>. These apply to
    /// chart-owned text such as axes, legends, and data labels when no more-specific
    /// text properties are authored.
    /// </summary>
    public ChartTextStyle? TextStyle { get; set; }

    /// <summary>Category labels, one per data point position.</summary>
    public List<string> Categories { get; } = new();

    /// <summary>Data series, in the order they appear in the XML.</summary>
    public List<ChartSeries> Series { get; } = new();

    /// <summary>Value axis (Y axis for column/line/area/scatter; X axis for bar charts).</summary>
    public ChartAxis ValueAxis { get; set; } = new();

    /// <summary>Category axis (X axis for column/line/area; Y axis for bar charts).</summary>
    public ChartAxis CategoryAxis { get; set; } = new();

    /// <summary>Legend position, or null if no legend is displayed.</summary>
    public LegendPosition? Legend { get; set; }

    /// <summary>Optional plot-area manual layout from <c>c:plotArea/c:layout/c:manualLayout</c>.</summary>
    public ChartManualLayout? PlotAreaManualLayout { get; set; }

    /// <summary>Optional authored plot-area fill from <c>c:plotArea/c:spPr</c>.</summary>
    public ShapeFill? PlotAreaFill { get; set; }

    /// <summary>Optional authored plot-area outline from <c>c:plotArea/c:spPr/a:ln</c>.</summary>
    public ShapeOutline? PlotAreaOutline { get; set; }

    /// <summary>Optional legend manual layout from <c>c:legend/c:layout/c:manualLayout</c>.</summary>
    public ChartManualLayout? LegendManualLayout { get; set; }

    /// <summary>Explicit <c>c:legend/c:overlay</c> value. Null means unspecified.</summary>
    public bool? LegendOverlay { get; set; }

    /// <summary>Optional independent formatting for legend text.</summary>
    public ChartTextStyle? LegendTextStyle { get; set; }

    /// <summary>True when OOXML <c>c:varyColors</c> asks chart points to use independent fallback colors.</summary>
    public bool VaryColors { get; set; }

    /// <summary>Authored OOXML <c>c:wireframe</c> value for Surface3D charts.</summary>
    public bool Wireframe { get; set; }

    /// <summary>True when the source package contained an explicit <c>c:wireframe</c> token.</summary>
    public bool WireframeSpecified { get; set; }

    /// <summary>Authored chart-level blank-cell display behavior from <c>c:chart/c:dispBlanksAs</c>.</summary>
    public ChartDisplayBlanksAs? DisplayBlanksAs { get; set; }

    /// <summary>
    /// Authored chart-level <c>c:plotVisOnly</c> flag. Null means the source omitted the
    /// token and PowerPoint's default (true) applies; false includes hidden worksheet data.
    /// </summary>
    public bool? PlotVisibleOnly { get; set; }

    /// <summary>
    /// Authored chart-level <c>c:roundedCorners</c> flag. Null means the source omitted the
    /// token and the host default applies.
    /// </summary>
    public bool? RoundedCorners { get; set; }

    /// <summary>Authored chart-level <c>c:showDLblsOverMax</c> flag. Null means unspecified.</summary>
    public bool? ShowDataLabelsOverMaximum { get; set; }

    /// <summary>
    /// Authored bar/column gap width percentage from <c>c:gapWidth/@val</c>.
    /// Null preserves the planner default.
    /// </summary>
    public int? BarGapWidthPercent { get; set; }

    /// <summary>
    /// Authored clustered bar/column series overlap percentage from <c>c:overlap/@val</c>.
    /// Null preserves the planner default.
    /// </summary>
    public int? BarOverlapPercent { get; set; }

    /// <summary>
    /// Authored 3-D bar/column gap depth percentage from <c>c:gapDepth/@val</c>.
    /// Null preserves the planner default.
    /// </summary>
    public int? BarGapDepthPercent { get; set; }

    /// <summary>
    /// Authored classic 3-D chart group kind for families whose visible chart type still maps to
    /// the existing 2-D model family. This preserves <c>pie3DChart</c>, <c>line3DChart</c>, and
    /// <c>area3DChart</c> without introducing renderer-specific policy.
    /// </summary>
    public ChartThreeDStyle ThreeDStyle { get; set; } = ChartThreeDStyle.None;

    /// <summary>Authored 3-D camera and projection settings, or null when c:view3D was absent.</summary>
    public Chart3DView? View3D { get; set; }

    // ── Type-specific auxiliary fields ───────────────────────────────────────────

    /// <summary>
    /// Inner hole radius as a percentage [0..100] for doughnut charts (from c:holeSize).
    /// Default 50 (PowerPoint default). Ignored for non-doughnut chart types.
    /// </summary>
    public int DoughnutHolePercent { get; set; } = 50;

    /// <summary>
    /// Authored first-slice angle in degrees for pie and doughnut charts (from <c>c:firstSliceAng</c>).
    /// Null preserves the app default start angle.
    /// </summary>
    public int? FirstSliceAngleDegrees { get; set; }

    /// <summary>
    /// Scatter/bubble style (marker, line+marker, smooth line, etc.).
    /// Populated for Scatter and Bubble chart types.
    /// </summary>
    public ScatterStyle ScatterStyle { get; set; } = ScatterStyle.Marker;

    /// <summary>
    /// Bubble chart scale percentage from <c>c:bubbleScale</c>.
    /// Default 100 (PowerPoint default); clamped by render and write paths to the OOXML UI range.
    /// </summary>
    public int BubbleScalePercent { get; set; } = 100;

    /// <summary>
    /// Whether bubble size values represent bubble area or width (<c>c:sizeRepresents</c>).
    /// PowerPoint defaults to area sizing.
    /// </summary>
    public BubbleSizeRepresentation BubbleSizeRepresents { get; set; } = BubbleSizeRepresentation.Area;

    /// <summary>
    /// True when negative bubble sizes should remain visible (<c>c:showNegBubbles</c>).
    /// PowerPoint hides negative bubbles by default.
    /// </summary>
    public bool ShowNegativeBubbles { get; set; }

    /// <summary>
    /// Radar chart style (standard, marker, filled).
    /// Populated for Radar chart type.
    /// </summary>
    public RadarStyle RadarStyle { get; set; } = RadarStyle.Standard;

    /// <summary>Chart-level data label configuration. Applies to all series unless overridden per-series.</summary>
    public ChartDataLabels? DataLabels { get; set; }

    /// <summary>Optional data table rendered below supported cartesian charts.</summary>
    public ChartDataTableSettings? DataTable { get; set; }

    /// <summary>Secondary value axis (right side for column/line; top for bar). Present when a combo chart has series on a second axis.</summary>
    public ChartAxis? SecondaryValueAxis { get; set; }

    /// <summary>
    /// True when modeled chart data changed after package load and the writer should emit
    /// fresh cached data/workbook sidecar instead of preserving the source chart workbook.
    /// </summary>
    public bool RegenerateWorkbookOnSave { get; set; }

    /// <summary>
    /// Original chart part path from the loaded PPTX package. Used only by PPTX IO so
    /// preserved workbook sidecars follow the source slide relationship, not chart count.
    /// </summary>
    [JsonIgnore]
    public string? SourcePartPath { get; set; }

    /// <summary>
    /// Authored <c>c:date1904</c> chart-space flag. Null means the source omitted the token
    /// and the workbook/application date system remains authoritative.
    /// </summary>
    public bool? ChartDate1904 { get; set; }

    /// <summary>Authored chart-space locale from <c>c:lang/@val</c>, when present.</summary>
    public string? ChartLanguage { get; set; }

    /// <summary>
    /// Verbatim <c>c:chartSpace/c:pivotSource</c> payload from the source chart.
    /// Pivot-chart identity is package metadata even when the current model does not
    /// expose pivot-specific editing or rendering controls.
    /// </summary>
    [JsonIgnore]
    public string? PreservedPivotSourceXml { get; set; }

    /// <summary>
    /// Verbatim <c>c:chartSpace/c:protection</c> payload. The modeled protection flags
    /// below drive the shared editing policy; this payload preserves any source tokens
    /// that are not modeled separately.
    /// </summary>
    [JsonIgnore]
    public string? PreservedChartProtectionXml { get; set; }

    /// <summary>Whether <c>c:protection/@chartObject</c> blocks chart edits.</summary>
    public bool? ChartObjectProtected { get; set; }

    /// <summary>Whether <c>c:protection/@data</c> blocks chart data edits.</summary>
    public bool? ChartDataProtected { get; set; }

    /// <summary>Whether <c>c:protection/@formatting</c> blocks chart formatting edits.</summary>
    public bool? ChartFormattingProtected { get; set; }

    /// <summary>Whether <c>c:protection/@selection</c> blocks editor selection of chart elements.</summary>
    public bool? ChartSelectionProtected { get; set; }

    /// <summary>
    /// Verbatim <c>c:chartSpace/c:extLst</c> payload from the source chart, retained so
    /// compatibility and producer-specific extensions survive a save or duplicate operation.
    /// Modeled chart fields remain authoritative for the generated chart body.
    /// </summary>
    [JsonIgnore]
    public string? PreservedChartSpaceExtensionsXml { get; set; }
}
