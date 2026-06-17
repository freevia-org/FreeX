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
/// <see cref="Line"/> is a <c>c:lineChart</c> and <see cref="Pie"/> a <c>c:pieChart</c>.
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
    Pie
}

/// <summary>
/// One data series of a <see cref="Chart"/>: an optional display <see cref="Name"/> and an ordered list of
/// numeric <see cref="Values"/> aligned with the chart's category labels. Immutable so it round-trips
/// cleanly. The writer emits the name as a <c>c:tx</c>/<c>c:strRef</c> cache and the values as a
/// <c>c:val</c>/<c>c:numRef</c>/<c>c:numCache</c> (literal caches — no embedded workbook).
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

    /// <summary>The data series (at least one). Bar/column/line charts may carry several; a pie chart uses the first.</summary>
    public List<ChartSeries> Series { get; } = [];

    /// <summary>The rendered width in points (converted to EMU on save). Defaults to a Word-typical 5in.</summary>
    public double WidthPt { get; set; } = 360;

    /// <summary>The rendered height in points (converted to EMU on save). Defaults to a Word-typical 3in.</summary>
    public double HeightPt { get; set; } = 216;

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
}
