using System.Collections.Generic;
using System.Linq;

namespace FreeW.Core.Model;

// MODEL-DESIGN CHOICE (roadmap item W3, basic DrawingML charts):
// A chart is modelled as an OPTIONAL INLINE RUN MARK (Run.Chart), mirroring Run.Equation / Run.Image and
// every other inline run feature. This lets a chart flow through the existing run sequence, table cells,
// headers/footers and hyperlink/comment/revision wrapping with zero new plumbing, and — like images — it
// round-trips through docx as a separate part referenced by an inline w:drawing. A Chart holds only the
// self-contained, cache-based data the roadmap calls for: a kind (bar/column/line/pie), an optional title,
// string category labels, and one or more numeric series. Size is kept in points to match the rest of the
// FreeW unit model (the writer converts to EMU). Values are stored as a flat numeric list per series; the
// writer emits literal c:strCache / c:numCache so no embedded workbook is required.

/// <summary>
/// The kind of a <see cref="Chart"/>. <see cref="Bar"/> and <see cref="Column"/> both serialise as an OOXML
/// <c>c:barChart</c> (differing only by <c>c:barDir</c> — bar = horizontal, col = vertical);
/// <see cref="Line"/> is a <c>c:lineChart</c>, <see cref="Pie"/> a <c>c:pieChart</c>, <see cref="Area"/> a
/// <c>c:areaChart</c>, <see cref="Doughnut"/> a <c>c:doughnutChart</c> and <see cref="Scatter"/> a
/// <c>c:scatterChart</c> (whose series carry <c>c:xVal</c>/<c>c:yVal</c> instead of <c>c:cat</c>/<c>c:val</c>).
/// </summary>
public enum ChartKind
{
    /// <summary>A vertical bar chart (OOXML c:barChart with c:barDir val="col").</summary>
    Column,

    /// <summary>A horizontal bar chart (OOXML c:barChart with c:barDir val="bar").</summary>
    Bar,

    /// <summary>A line chart (OOXML c:lineChart).</summary>
    Line,

    /// <summary>A pie chart (OOXML c:pieChart).</summary>
    Pie,

    /// <summary>An XY scatter chart (OOXML c:scatterChart; series carry c:xVal/c:yVal). Categories supply x-values.</summary>
    Scatter,

    /// <summary>An area chart (OOXML c:areaChart).</summary>
    Area,

    /// <summary>A doughnut chart (OOXML c:doughnutChart — a pie with a hole; uses the first series).</summary>
    Doughnut
}

/// <summary>
/// One data series of a <see cref="Chart"/>: an optional display <see cref="Name"/> and an ordered list of
/// numeric <see cref="Values"/> aligned with the chart's category labels. Immutable so it round-trips
/// cleanly. The writer emits the name as a <c>c:tx</c>/<c>c:strRef</c> cache and the values as a
/// <c>c:val</c>/<c:numRef</c>/<c>c:numCache</c> (literal caches — no embedded workbook).
/// </summary>
public sealed class ChartSeries
{
    /// <summary>The series display name (legend label), or null when unnamed.</summary>
    public string? Name { get; set; }

    /// <summary>The numeric values, aligned positionally with the chart's category labels.</summary>
    public List<double> Values { get; } = [];

    public ChartSeries() { }

    /// <summary>Creates a named series from an ordered set of values.</summary>
    public ChartSeries(string? name, IEnumerable<double> values)
    {
        Name = name;
        Values.AddRange(values);
    }
}

/// <summary>
/// Visual switches explicitly serialized by an imported chart part.  Native chart-style ids are
/// family-specific themes, so their presence alone is not a reliable substitute for these elements.
/// </summary>
public sealed record ChartNativeVisualSettings(
    bool ShowGridlines,
    bool HasPlotAreaFill,
    bool ShowDataLabels,
    bool ScatterConnectsPoints);

// ─── Chart Gallery catalogs ───────────────────────────────────────────────────
//
// Three curated sets mirror Word's Chart Design ribbon galleries:
//   • ChartStyle     — visual treatment (gridlines, fills, marker style, data labels)
//   • ChartColorScheme — series/slice colour palette (colorful or monochrome)
//   • ChartQuickLayout — which chart elements are visible (title/legend/axes/labels/gridlines)
//
// Each catalog is a static IReadOnlyList<T> (mirrors DocumentTheme.Catalog, DocumentFontSet.Catalog, …).
// Ids are stable integer keys (1-based) that match Word's internal chart style numbering where possible
// so round-trip through c:style is natural.

/// <summary>
/// A named visual style for a chart, controlling gridline visibility, plot-area fill,
/// marker/line treatment, and whether data labels are shown. Maps to Word's Chart Style
/// gallery (numbers 1–11 in Word 2016+). Persisted in the chart part as <c>c:style</c>.
/// </summary>
public sealed record ChartStyle(
    /// <summary>Stable id (1-based). Persisted as <c>c:style/@val</c>.</summary>
    int Id,
    /// <summary>Gallery display name.</summary>
    string Name,
    /// <summary>Whether horizontal value gridlines are drawn.</summary>
    bool ShowGridlines,
    /// <summary>Whether the plot area has a background fill (vs. transparent).</summary>
    bool PlotAreaFill,
    /// <summary>Whether circular markers are drawn at line/scatter data points.</summary>
    bool ShowMarkers,
    /// <summary>Whether data-value labels are shown on each data point.</summary>
    bool ShowDataLabels)
{
    /// <summary>All built-in chart styles, in Word-gallery display order (Style 1 … Style 8).</summary>
    public static readonly IReadOnlyList<ChartStyle> Catalog =
    [
        new(1,  "Style 1",  ShowGridlines: true,  PlotAreaFill: false, ShowMarkers: false, ShowDataLabels: false),
        new(2,  "Style 2",  ShowGridlines: false, PlotAreaFill: true,  ShowMarkers: false, ShowDataLabels: false),
        new(3,  "Style 3",  ShowGridlines: true,  PlotAreaFill: true,  ShowMarkers: false, ShowDataLabels: false),
        new(4,  "Style 4",  ShowGridlines: true,  PlotAreaFill: false, ShowMarkers: true,  ShowDataLabels: false),
        new(5,  "Style 5",  ShowGridlines: true,  PlotAreaFill: false, ShowMarkers: false, ShowDataLabels: true),
        new(6,  "Style 6",  ShowGridlines: false, PlotAreaFill: false, ShowMarkers: false, ShowDataLabels: true),
        new(7,  "Style 7",  ShowGridlines: true,  PlotAreaFill: true,  ShowMarkers: true,  ShowDataLabels: true),
        new(8,  "Style 8",  ShowGridlines: false, PlotAreaFill: true,  ShowMarkers: true,  ShowDataLabels: true),
    ];

    /// <summary>Default style (Style 1 — gridlines on, no fill, no labels).</summary>
    public static ChartStyle Default => Catalog[0];

    /// <summary>Find by stable id, or null.</summary>
    public static ChartStyle? FindById(int id) =>
        Catalog.FirstOrDefault(s => s.Id == id);
}

/// <summary>
/// A named series colour palette for a chart (the "Change Colors" gallery in Word's Chart Design tab).
/// Each palette is an ordered array of six hex colours applied cyclically to series/slices.
/// Persisted via a FreeW extension element in the chart part.
/// </summary>
public sealed record ChartColorScheme(
    /// <summary>Stable string id, persisted in the FreeW extension.</summary>
    string Id,
    /// <summary>Gallery display name.</summary>
    string Name,
    /// <summary>Six hex colours (#RRGGBB) in series/slice order.</summary>
    IReadOnlyList<string> Colors)
{
    /// <summary>All built-in colour schemes, grouped like Word's gallery (Colorful then Monochromatic).</summary>
    public static readonly IReadOnlyList<ChartColorScheme> Catalog =
    [
        // ── Colorful ──
        new("colorful1", "Colorful Palette 1",
            ["#4472C4", "#ED7D31", "#A5A5A5", "#FFC000", "#5B9BD5", "#70AD47"]),
        new("colorful2", "Colorful Palette 2",
            ["#ED7D31", "#FFC000", "#4472C4", "#70AD47", "#255E91", "#9E480E"]),
        new("colorful3", "Colorful Palette 3",
            ["#264478", "#FF0000", "#FFC000", "#70AD47", "#4472C4", "#ED7D31"]),
        new("colorful4", "Colorful Palette 4",
            ["#636363", "#FF0000", "#FFC000", "#4472C4", "#70AD47", "#ED7D31"]),
        // ── Monochromatic (blue shades, orange shades, grey shades) ──
        new("mono-blue",   "Monochromatic Blue",
            ["#214A82", "#2E5FAA", "#4472C4", "#6C8FD1", "#A9C1E7", "#D6E4F4"]),
        new("mono-orange", "Monochromatic Orange",
            ["#833C00", "#AD4F00", "#ED7D31", "#F3A06B", "#F7C09D", "#FBDFD1"]),
        new("mono-grey",   "Monochromatic Grey",
            ["#262626", "#404040", "#808080", "#A6A6A6", "#BFBFBF", "#D9D9D9"]),
    ];

    /// <summary>Default colour scheme (Colorful Palette 1 — the classic Office palette).</summary>
    public static ChartColorScheme Default => Catalog[0];

    /// <summary>Find by id (case-insensitive), or null.</summary>
    public static ChartColorScheme? FindById(string id) =>
        Catalog.FirstOrDefault(s => string.Equals(s.Id, id, System.StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A Quick Layout preset — a named combination of which chart elements are visible (title,
/// legend, axis titles, data labels, gridlines). Maps to Word's Quick Layout gallery (Layout 1–9).
/// Persisted via a FreeW extension element in the chart part.
/// </summary>
public sealed record ChartQuickLayout(
    /// <summary>Stable integer id (1-based, matching Word's Quick Layout numbering).</summary>
    int Id,
    /// <summary>Gallery display name.</summary>
    string Name,
    /// <summary>Whether the chart title is shown.</summary>
    bool ShowTitle,
    /// <summary>Whether the legend is shown.</summary>
    bool ShowLegend,
    /// <summary>Whether axis titles are shown (category + value).</summary>
    bool ShowAxisTitles,
    /// <summary>Whether data value labels are shown on each data point.</summary>
    bool ShowDataLabels,
    /// <summary>Whether gridlines are shown.</summary>
    bool ShowGridlines)
{
    /// <summary>All built-in quick layouts, in Word-gallery display order (Layout 1 … Layout 9).</summary>
    public static readonly IReadOnlyList<ChartQuickLayout> Catalog =
    [
        new(1, "Layout 1",  ShowTitle: false, ShowLegend: false, ShowAxisTitles: false, ShowDataLabels: false, ShowGridlines: true),
        new(2, "Layout 2",  ShowTitle: true,  ShowLegend: false, ShowAxisTitles: false, ShowDataLabels: false, ShowGridlines: true),
        new(3, "Layout 3",  ShowTitle: true,  ShowLegend: true,  ShowAxisTitles: false, ShowDataLabels: false, ShowGridlines: true),
        new(4, "Layout 4",  ShowTitle: true,  ShowLegend: true,  ShowAxisTitles: true,  ShowDataLabels: false, ShowGridlines: true),
        new(5, "Layout 5",  ShowTitle: true,  ShowLegend: true,  ShowAxisTitles: false, ShowDataLabels: true,  ShowGridlines: true),
        new(6, "Layout 6",  ShowTitle: true,  ShowLegend: false, ShowAxisTitles: false, ShowDataLabels: true,  ShowGridlines: false),
        new(7, "Layout 7",  ShowTitle: false, ShowLegend: false, ShowAxisTitles: false, ShowDataLabels: true,  ShowGridlines: false),
        new(8, "Layout 8",  ShowTitle: false, ShowLegend: true,  ShowAxisTitles: false, ShowDataLabels: false, ShowGridlines: false),
        new(9, "Layout 9",  ShowTitle: true,  ShowLegend: true,  ShowAxisTitles: true,  ShowDataLabels: true,  ShowGridlines: true),
    ];

    /// <summary>Default quick layout (Layout 1 — no title/legend, gridlines only).</summary>
    public static ChartQuickLayout Default => Catalog[0];

    /// <summary>Find by stable id, or null.</summary>
    public static ChartQuickLayout? FindById(int id) =>
        Catalog.FirstOrDefault(l => l.Id == id);
}

/// <summary>
/// A basic, self-contained DrawingML chart carried by a <see cref="Run"/> via <see cref="Run.Chart"/>. Holds
/// a <see cref="Kind"/>, an optional <see cref="Title"/>, the shared category labels
/// (<see cref="Categories"/>) and one or more numeric <see cref="Series"/>. On save it serialises as a
/// separate chart part (<c>word/charts/chartN.xml</c>) referenced by an inline <c>w:drawing</c>; the data is
/// embedded as literal caches so no companion workbook is needed. Modelled as an inline run mark — mirroring
/// <see cref="Run.Image"/> and <see cref="Run.Equation"/> — so charts round-trip through the existing run
/// flow without a new block type.
/// </summary>
public sealed class Chart
{
    /// <summary>The chart kind (column/bar/line/pie).</summary>
    public ChartKind Kind { get; set; } = ChartKind.Column;

    /// <summary>The chart title, or null when the chart has no title.</summary>
    public string? Title { get; set; }

    /// <summary>The category (x-axis / slice) labels, shared by every series.</summary>
    public List<string> Categories { get; } = [];

    /// <summary>The data series (at least one). Bar/column/line/area charts may carry several; pie/doughnut use the first.</summary>
    public List<ChartSeries> Series { get; } = [];

    /// <summary>
    /// Whether the chart displays a legend (OOXML <c>c:legend</c>). Defaults to <c>false</c> so existing
    /// output (no legend) is preserved byte-for-byte; set <c>true</c> to emit a bottom-positioned legend.
    /// </summary>
    public bool ShowLegend { get; set; }

    /// <summary>
    /// An optional title for the category (x) axis (OOXML <c>c:title</c> on <c>c:catAx</c>). Null — the
    /// default — emits no axis title, preserving existing output. Ignored for pie/doughnut (axis-less) charts.
    /// </summary>
    public string? CategoryAxisTitle { get; set; }

    /// <summary>
    /// An optional title for the value (y) axis (OOXML <c>c:title</c> on <c>c:valAx</c>). Null — the
    /// default — emits no axis title, preserving existing output. Ignored for pie/doughnut (axis-less) charts.
    /// </summary>
    public string? ValueAxisTitle { get; set; }

    /// <summary>The rendered width in points (converted to EMU on save). Defaults to a Word-typical 5in.</summary>
    public double WidthPt { get; set; } = 360;

    /// <summary>The rendered height in points (converted to EMU on save). Defaults to a Word-typical 3in.</summary>
    public double HeightPt { get; set; } = 216;

    public double RotationAngle { get; set; }
    public bool FlipH { get; set; }
    public bool FlipV { get; set; }

    /// <summary>
    /// Floating-position state. Null (the default) means the chart is inline.
    /// Set <see cref="FloatingPlacement.Wrapping"/> to any non-Inline value to make it float.
    /// </summary>
    public FloatingPlacement? Placement { get; set; }

    /// <summary>True when this chart is floating (non-null Placement with Wrapping != Inline).</summary>
    public bool IsFloating => Placement?.IsFloating ?? false;

    // ── Chart Design gallery selections ──────────────────────────────────────

    /// <summary>
    /// The active <see cref="ChartStyle"/> id (1-based). 0 (the default) means "no explicit style"
    /// — the renderer uses <see cref="ChartStyle.Default"/>. Persisted as <c>c:style/@val</c> in the
    /// chart part; 0 is omitted on write (Word defaults to its own baseline).
    /// </summary>
    public int StyleId { get; set; }

    /// <summary>
    /// The active <see cref="ChartColorScheme"/> id (e.g. "colorful1", "mono-blue"). Null (the default)
    /// means "no explicit scheme" — the renderer uses <see cref="ChartColorScheme.Default"/>.
    /// Persisted via a FreeW extension element in the chart part.
    /// </summary>
    public string? ColorSchemeId { get; set; }

    /// <summary>
    /// The active <see cref="ChartQuickLayout"/> id (1-based). 0 (the default) means "no explicit
    /// layout" — the renderer honours the individual ShowLegend / Title / axis-title properties as-is.
    /// When set to a non-zero id the quick-layout overrides those individual toggles for rendering
    /// (they are preserved on the model but the render ignores them). Persisted via a FreeW extension.
    /// </summary>
    public int QuickLayoutId { get; set; }

    /// <summary>
    /// Source-authoritative visual elements recovered from an imported chart part. Null keeps the
    /// FreeW gallery-style defaults for charts authored in the model.
    /// </summary>
    public ChartNativeVisualSettings? NativeVisualSettings { get; set; }

    public Chart() { }

    /// <summary>
    /// Convenience factory: a single-series chart from category labels + values (lengths should match),
    /// with an optional series name and title.
    /// </summary>
    public static Chart Create(
        ChartKind kind,
        IEnumerable<string> categories,
        IEnumerable<double> values,
        string? seriesName = null,
        string? title = null)
    {
        var chart = new Chart { Kind = kind, Title = title };
        chart.Categories.AddRange(categories);
        chart.Series.Add(new ChartSeries(seriesName, values));
        return chart;
    }

    /// <summary>Creates an independent copy for document merge and undo snapshots.</summary>
    public Chart Clone()
    {
        var clone = new Chart
        {
            Kind = Kind,
            Title = Title,
            ShowLegend = ShowLegend,
            CategoryAxisTitle = CategoryAxisTitle,
            ValueAxisTitle = ValueAxisTitle,
            WidthPt = WidthPt,
            HeightPt = HeightPt,
            RotationAngle = RotationAngle,
            FlipH = FlipH,
            FlipV = FlipV,
            Placement = Placement?.Clone(),
            StyleId = StyleId,
            ColorSchemeId = ColorSchemeId,
            QuickLayoutId = QuickLayoutId,
            NativeVisualSettings = NativeVisualSettings
        };
        clone.Categories.AddRange(Categories);
        foreach (var series in Series)
            clone.Series.Add(new ChartSeries(series.Name, series.Values));
        return clone;
    }
}
