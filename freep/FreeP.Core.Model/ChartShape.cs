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
    /// <summary>Radar (spider/polar) chart. Each series is a closed polygon on N spokes.</summary>
    Radar,
    /// <summary>Bubble chart. Like scatter but each point has a BubbleSizes list in its series.</summary>
    Bubble,
    Unknown
}

/// <summary>Scatter/bubble style read from c:scatterStyle or similar.</summary>
public enum ScatterStyle { Marker, LineMarker, Line, Smooth, SmoothMarker }

/// <summary>Radar style read from c:radarStyle.</summary>
public enum RadarStyle { Standard, Marker, Filled }

/// <summary>Position of the chart legend relative to the plot area.</summary>
public enum LegendPosition { Right, Left, Top, Bottom }

/// <summary>A single data series within a <see cref="ChartShape"/>.</summary>
public sealed class ChartSeries
{
    /// <summary>Series name (from c:tx cache).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Default series fill color. Null means use the theme accent cycle.</summary>
    public ThemeAwareColor? FillColor { get; set; }

    /// <summary>Data point values, one per category. Null entries represent missing/gap points.</summary>
    public List<double?> Values { get; } = new();

    /// <summary>Per-point fill color overrides (keyed by zero-based point index). Used primarily for pie charts.</summary>
    public Dictionary<int, ThemeAwareColor> PointColors { get; } = new();

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
}

/// <summary>Configuration for one chart axis (category or value).</summary>
public sealed class ChartAxis
{
    /// <summary>Axis title text. Null if no title is set.</summary>
    public string? Title { get; set; }

    /// <summary>Explicit minimum scale value. Null = auto.</summary>
    public double? Min { get; set; }

    /// <summary>Explicit maximum scale value. Null = auto.</summary>
    public double? Max { get; set; }

    /// <summary>Whether major gridlines are shown on this axis.</summary>
    public bool HasMajorGridlines { get; set; } = true;

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

    /// <summary>Chart title text, or null if no title.</summary>
    public string? Title { get; set; }

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

    // ── Type-specific auxiliary fields ───────────────────────────────────────────

    /// <summary>
    /// Inner hole radius as a percentage [0..100] for doughnut charts (from c:holeSize).
    /// Default 50 (PowerPoint default). Ignored for non-doughnut chart types.
    /// </summary>
    public int DoughnutHolePercent { get; set; } = 50;

    /// <summary>
    /// Scatter/bubble style (marker, line+marker, smooth line, etc.).
    /// Populated for Scatter and Bubble chart types.
    /// </summary>
    public ScatterStyle ScatterStyle { get; set; } = ScatterStyle.Marker;

    /// <summary>
    /// Radar chart style (standard, marker, filled).
    /// Populated for Radar chart type.
    /// </summary>
    public RadarStyle RadarStyle { get; set; } = RadarStyle.Standard;
}
