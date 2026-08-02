namespace FreeX.Core.Model;

public enum ChartType
{
    Column,
    StackedColumn,
    PercentStackedColumn,
    Line,
    Pie,
    ThreeDPie,
    Doughnut,
    Bar,
    StackedBar,
    PercentStackedBar,
    Scatter,
    Bubble,
    Area,
    StackedArea,
    PercentStackedArea,
    Radar,
    Stock,
    Surface,
    Treemap,
    Sunburst,
    Histogram,
    Pareto,
    BoxAndWhisker,
    Waterfall,
    Funnel,
    Map,
    ThreeDColumn,
    ThreeDBar,
    ThreeDArea,
    ThreeDLine,
    ThreeDSurface
}

// R62-io-chart-legend-datalabels-6-2: TopRight added (at the end, preserving the existing members'
// ordinal values) so a legend explicitly placed at the top-right corner (<c:legendPos val="tr"/>,
// a real ST_LegendPos value reachable from Excel's Format Legend pane, most commonly on pie charts)
// round-trips as a compact corner legend instead of collapsing into a full-height right-side legend.
public enum ChartLegendPosition { None, Left, Right, Top, Bottom, TopRight }

// R51-io-chart-datalabel-3-1: Left/Right/Top/Bottom added (at the end, preserving the existing
// members' ordinal values) so the family of chart types whose data labels support OOXML
// c:dLblPos val="l"/"r"/"t"/"b" (Line, 3-D Line, Scatter, Bubble) can round-trip that position
// instead of every reader/writer site being forced to collapse it to BestFit/Center.
public enum ChartDataLabelPosition
{
    BestFit,
    Center,
    InsideEnd,
    OutsideEnd,
    InsideBase,
    Left,
    Right,
    Top,
    Bottom
}

/// <summary>
/// <see cref="Custom"/> means the source file used a literal separator string (e.g. Excel's
/// "Period" data-label separator choice) that doesn't map to any of the other members; the raw
/// text itself is preserved out-of-band (see <see cref="ChartModel.DataLabelSeparatorText"/> for
/// the chart-wide default, or <c>SeparatorText</c> on the per-series/per-point override records,
/// which already store the literal text directly).
/// </summary>
public enum ChartDataLabelSeparator { Comma, Semicolon, NewLine, Space, Custom }

public enum ChartDataLabelNumberFormat { General, Number, Currency, Percent }

public enum ChartTrendlineType { Linear, Exponential, Logarithmic, Power, MovingAverage, Polynomial }

public enum ChartLineDashStyle { Solid, Dash, Dot }

/// <summary>
/// A single &lt;c:legendEntry&gt; override: hiding one legend key (<see cref="IsDeleted"/>),
/// applying per-entry text formatting (bold/italic/size/color) via a &lt;c:txPr&gt; child, or
/// both together in the same element.
/// </summary>
/// <remarks>
/// R45-io-chart-datatable-legend-3-1: real Excel writes a &lt;c:legendEntry&gt; with a
/// &lt;c:txPr&gt; but NO &lt;c:delete&gt; child when the user selects a single legend key and
/// changes only its font (the entry is never hidden). That entry must still round-trip -- it
/// must not be discarded just because <see cref="IsDeleted"/> is null.
/// </remarks>
public sealed record ChartLegendEntryModel(
    int Index,
    bool? IsDeleted,
    bool? TextBold = null,
    bool? TextItalic = null,
    double? TextFontSize = null,
    CellColor? TextColor = null,
    WorkbookThemeColorReference? TextThemeColor = null)
{
    /// <summary>Whether this entry carries any per-entry text-formatting override.</summary>
    public bool HasTextFormatting =>
        TextBold is not null || TextItalic is not null || TextFontSize is not null ||
        TextColor is not null || TextThemeColor is not null;
}

/// <summary>
/// Maps a chart series (identified by its chart-XML <c>&lt;c:idx&gt;</c>) to the worksheet
/// column that supplies its values. Populated from each series' <c>&lt;c:val&gt;</c> range so the
/// renderer can draw exactly the columns the chart references — in their declared idx order —
/// rather than assuming every column inside <see cref="ChartModel.DataRange"/> is a series.
/// This is required for charts whose series skip columns (e.g. a combo chart that plots columns
/// B, D, E but not C) or list series out of column order.
/// <para>
/// <paramref name="ValueColumn"/> is an absolute worksheet column index. When the list is empty
/// the renderer falls back to its legacy positional column scan.
/// </para>
/// </summary>
public sealed record ChartSeriesColumnMapping(int SeriesXmlIndex, uint ValueColumn);

/// <summary>
/// Verbatim formula strings for a single chart series, preserved from the source XML.
/// Used to round-trip multi-area series formulas that cannot be represented as a
/// single rectangular <see cref="GridRange"/>.
/// <para>
/// For a Scatter/Bubble series (which has no <c>cat</c>/<c>val</c> containers), the reader
/// repurposes <see cref="CatFormula"/> to carry the series' <c>xVal</c> formula and
/// <see cref="ValFormula"/> to carry its <c>yVal</c> formula — mirroring how
/// <c>XlsxChartXmlWriter.BuildScatterChartSeries</c> already consumes them on write. A Bubble
/// series additionally may need to preserve its <c>bubbleSize</c> formula, which has no
/// standard-chart equivalent to repurpose, hence the dedicated <see cref="BubbleSizeFormula"/>.
/// </para>
/// <para>
/// R103-io-chart-series-verbatim-cache: <see cref="ValCacheXml"/>/<see cref="CatCacheXml"/>/
/// <see cref="BubbleSizeCacheXml"/> hold the source file's own &lt;c:numCache&gt;/&lt;c:strCache&gt;
/// element (serialized verbatim, root-element name preserved) for the matching unparsable
/// container, when the source actually had one. Real Excel always pairs a series formula —
/// including a named-range/multi-area/external-link one — with a cache of its last-computed
/// values, so a manual-calculation workbook or non-recalculating consumer still shows the chart's
/// last-known data. Without this, <c>XlsxChartXmlWriter</c> had no cache data available to
/// re-emit for a verbatim series (only the formula text was ever captured) and unconditionally
/// wrote no cache at all, even when the source file had one. These are null when the source
/// container had no cache to begin with (e.g. a full-column named range with no computed value),
/// matching real Excel, which also omits the cache in that case.
/// </para>
/// </summary>
public sealed record ChartSeriesVerbatimFormulas(
    int SeriesIndex,
    string? ValFormula,
    string? CatFormula,
    string? TxFormula,
    string? BubbleSizeFormula = null,
    string? ValCacheXml = null,
    string? CatCacheXml = null,
    string? BubbleSizeCacheXml = null);

/// <summary>
/// Embedded data values for a single chart series, extracted from the
/// <c>&lt;c:numCache&gt;</c> / <c>&lt;c:strCache&gt;</c> elements in the chart XML.
/// Used as a fallback when the series data range formula is an unresolvable named range
/// (e.g. <c>Sheet1!rngMyData</c>) so FreeX can still render the chart without recalc.
/// <para>
/// R117-io-chart-embedded-bubble-size-1: <see cref="SizeValues"/> carries a Bubble series'
/// cached <c>&lt;c:bubbleSize&gt;</c> numCache (aligned by point index with <see cref="Values"/>,
/// which for Bubble already holds the cached <c>&lt;c:yVal&gt;</c>). It is null for every other
/// chart type/family, including Stock: a Stock chart's Open/High/Low/Close are each their own
/// classic <c>&lt;c:ser&gt;</c> (with their own <c>&lt;c:val&gt;</c> cache), so the reader
/// (<c>XlsxChartSeriesRangeReader.TryReadEmbeddedSeriesData</c>) already captures each dimension
/// as its own list entry (in the fixed Open/High/Low/Close document order OOXML requires) -- no
/// extra field is needed on this record for Stock, only a consumer that knows to re-merge that
/// ordered list back into one High/Low/Open/Close-shaped series (see
/// <c>ChartRenderer.BuildEmbeddedCellLookup</c> and
/// <c>ChartLayoutRequestBuilder.BuildFromEmbeddedData</c>).
/// </para>
/// </summary>
public sealed record ChartEmbeddedSeriesData(
    int SeriesIndex,
    string? SeriesName,
    IReadOnlyList<string> Categories,
    IReadOnlyList<double?> Values,
    IReadOnlyList<double?>? SizeValues = null);

public enum ChartBubbleSizeRepresents { Area, Width }

public enum ChartAxisTickStyle { None, Inside, Outside, Cross }

public enum ChartAxisTickLabelPosition { NextTo, Low, High }

public enum ChartAxisPosition { Bottom, Top, Left, Right }

public enum ChartAxisCrosses { AutoZero, Minimum, Maximum, Custom }

public enum ChartAxisCrossBetween { Between, MidCategory }

public enum ChartAxisLabelAlignment { Center, Left, Right }

public enum ChartDateAxisUnit { Days, Months, Years }

public enum ChartAxisDisplayUnit
{
    Hundreds,
    Thousands,
    TenThousands,
    HundredThousands,
    Millions,
    TenMillions,
    HundredMillions,
    Billions,
    Trillions
}

public enum ChartDrawingAnchorKind { Absolute, OneCell, TwoCell }

// R65-default-fallback-swallow-sweep-2: X/Star/Plus/Dot/Dash/Auto added so ST_MarkerStyle's
// remaining values round-trip distinctly instead of collapsing to Circle on read and "circle" on
// write.
public enum ChartMarkerStyle { None, Circle, Square, Diamond, Triangle, X, Star, Plus, Dot, Dash, Auto }

// R41-io-chart-errorbars-trendline-3-1: StdDev (Standard Deviation, with a user-configurable
// multiplier) added at the end so it round-trips distinctly from StandardError (Standard Error,
// no multiplier) instead of the two being conflated.
public enum ChartErrorBarKind { StandardError, Percentage, FixedValue, Custom, StdDev }

public enum ChartErrorBarAxisDirection { Y, X }

public enum ChartErrorBarDirection { Both, Plus, Minus }

public enum ChartBlankDisplayMode { Gap, Span, Zero }

public enum StockChartSubtype
{
    HighLowClose,
    OpenHighLowClose,
    VolumeHighLowClose,
    VolumeOpenHighLowClose
}

public sealed class ChartProtectionModel
{
    public bool? ChartObject { get; set; }
    public bool? Data { get; set; }
    public bool? Formatting { get; set; }
    public bool? Selection { get; set; }
    public bool? UserInterface { get; set; }
}

public sealed class ChartPrintSettingsModel
{
    public ChartPageMarginsModel? PageMargins { get; set; }
    public ChartPageSetupModel? PageSetup { get; set; }
    public ChartHeaderFooterModel? HeaderFooter { get; set; }
}

public sealed class ChartHeaderFooterModel
{
    public bool? DifferentOddEven { get; set; }
    public bool? DifferentFirst { get; set; }
    public bool? AlignWithMargins { get; set; }
    public string? OddHeader { get; set; }
    public string? OddFooter { get; set; }
    public string? EvenHeader { get; set; }
    public string? EvenFooter { get; set; }
    public string? FirstHeader { get; set; }
    public string? FirstFooter { get; set; }
}

public sealed class ChartColorMapOverrideModel
{
    public bool UseMasterColorMapping { get; set; }
    public Dictionary<string, string> OverrideMappings { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ChartExternalDataModel
{
    public string? RelationshipId { get; set; }
    public string? RelationshipType { get; set; }
    public string? Target { get; set; }
    public string? TargetMode { get; set; }
    public bool? AutoUpdate { get; set; }
}

public sealed class ChartUserShapesModel
{
    public string? RelationshipId { get; set; }
    public string? RelationshipType { get; set; }
    public string? Target { get; set; }
    public string? TargetMode { get; set; }
}

public sealed class ChartManualLayoutModel
{
    public string? LayoutTarget { get; set; }
    public string? XMode { get; set; }
    public string? YMode { get; set; }
    public string? WidthMode { get; set; }
    public string? HeightMode { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
}

public sealed class ChartPageMarginsModel
{
    public double? Left { get; set; }
    public double? Right { get; set; }
    public double? Top { get; set; }
    public double? Bottom { get; set; }
    public double? Header { get; set; }
    public double? Footer { get; set; }
}

public sealed class ChartPageSetupModel
{
    public string? PaperSize { get; set; }
    public string? Orientation { get; set; }
    public int? Copies { get; set; }
    public bool? UsePrinterDefaults { get; set; }
    public int? FirstPageNumber { get; set; }
    public bool? UseFirstPageNumber { get; set; }
    public int? HorizontalDpi { get; set; }
    public int? VerticalDpi { get; set; }
    public bool? BlackAndWhite { get; set; }
    public bool? Draft { get; set; }
}

public sealed class ChartDataTableModel
{
    public bool? ShowHorizontalBorder { get; set; }
    public bool? ShowVerticalBorder { get; set; }
    public bool? ShowOutline { get; set; }
    public bool? ShowLegendKeys { get; set; }
    public CellColor? FillColor { get; set; }
    public WorkbookThemeColorReference? FillThemeColor { get; set; }
    public CellColor? BorderColor { get; set; }
    public WorkbookThemeColorReference? BorderThemeColor { get; set; }
    public double? BorderThickness { get; set; }
    public CellColor? TextColor { get; set; }
    public WorkbookThemeColorReference? TextThemeColor { get; set; }
    public double? FontSize { get; set; }
}

public sealed class Chart3DViewModel
{
    public int? RotationX { get; set; }
    public int? HeightPercent { get; set; }
    public int? RotationY { get; set; }
    public int? DepthPercent { get; set; }
    public bool? RightAngleAxes { get; set; }
    public int? Perspective { get; set; }
}

public sealed class ChartSurfaceFormatModel
{
    public CellColor? FillColor { get; set; }
    public WorkbookThemeColorReference? FillThemeColor { get; set; }
    public CellColor? BorderColor { get; set; }
    public WorkbookThemeColorReference? BorderThemeColor { get; set; }
    public double? BorderThickness { get; set; }
}

/// <summary>
/// Per-data-point fill color override for pie/doughnut slices, read from
/// <c>&lt;c:dPt&gt;</c> elements with explicit <c>&lt;c:spPr&gt;</c> fills in the chart XML.
/// </summary>
public sealed record ChartPointFillFormat(
    int SeriesIndex,
    int PointIndex,
    CellColor? FillColor = null,
    WorkbookThemeColorReference? FillThemeColor = null)
{
    public CellColor? ResolveFillColor(WorkbookTheme theme) =>
        FillThemeColor?.Resolve(theme) ?? FillColor;
}

/// <summary>
/// An explicit &lt;c:order&gt; value captured for a series whose order diverges from its idx.
/// See <see cref="ChartModel.SeriesOrderOverrides"/> for the round-trip rationale.
/// </summary>
public sealed record ChartSeriesOrderOverride(int SeriesIndex, int Order);

/// <summary>
/// R103-io-chart-series-tx-1: a series' &lt;c:tx&gt;&lt;c:strRef&gt;&lt;c:f&gt; formula captured
/// verbatim from the source XML. See <see cref="ChartModel.SeriesNameOverrides"/> for the
/// round-trip rationale.
/// </summary>
public sealed record ChartSeriesNameOverride(int SeriesIndex, string Formula);

/// <summary>
/// Per-data-point marker override for a Line/Scatter data point, read from a &lt;c:dPt&gt;'s
/// &lt;c:marker&gt; child (Format Data Point &gt; Marker Options). See
/// <see cref="ChartModel.PointMarkerFormats"/> for the round-trip rationale.
/// </summary>
public sealed record ChartPointMarkerFormat(
    int SeriesIndex,
    int PointIndex,
    ChartMarkerStyle? MarkerStyle = null,
    double? MarkerSize = null,
    CellColor? FillColor = null,
    WorkbookThemeColorReference? FillThemeColor = null,
    CellColor? BorderColor = null,
    WorkbookThemeColorReference? BorderThemeColor = null,
    double? BorderThickness = null)
{
    public CellColor? ResolveFillColor(WorkbookTheme theme) =>
        FillThemeColor?.Resolve(theme) ?? FillColor;

    public CellColor? ResolveBorderColor(WorkbookTheme theme) =>
        BorderThemeColor?.Resolve(theme) ?? BorderColor;
}

/// <summary>
/// A literal data-label string supplied by a chart's "Value From Cells" feature
/// (OOXML <c>c15:datalabelsRange</c> under a series' <c>extLst</c>). The cached text
/// (e.g. <c>"👍 10%"</c>) is what Excel displays for the point, independent of the
/// series' numeric value. The renderer draws this verbatim above the point instead of
/// formatting the value.
/// </summary>
public sealed record ChartRangeDataLabel(int SeriesIndex, int PointIndex, string Text);

public sealed record ChartRangeDataLabelPoint(int PointIndex, string Text);

/// <summary>
/// Per-series "Value From Cells" data-label definition (OOXML <c>c15:datalabelsRange</c>),
/// capturing the source <c>c15:f</c> formula, the cached <c>c15:ptCount</c> point count, and the
/// cached per-point strings so the feature round-trips on XLSX and native (.fxl) save.
/// </summary>
public sealed record ChartSeriesRangeDataLabels(
    int SeriesIndex, string? Formula, int? PointCount, IReadOnlyList<ChartRangeDataLabelPoint> Points)
{
    public bool Equals(ChartSeriesRangeDataLabels? other) =>
        other is not null && SeriesIndex == other.SeriesIndex
        && string.Equals(Formula, other.Formula, StringComparison.Ordinal)
        && PointCount == other.PointCount && Points.SequenceEqual(other.Points);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SeriesIndex); hash.Add(Formula, StringComparer.Ordinal); hash.Add(PointCount);
        foreach (var p in Points) hash.Add(p);
        return hash.ToHashCode();
    }
}

public sealed record ChartSeriesFormat(
    int SeriesIndex,
    CellColor? FillColor = null,
    CellColor? StrokeColor = null,
    double? StrokeThickness = null,
    ChartLineDashStyle? DashStyle = null,
    ChartMarkerStyle? MarkerStyle = null,
    double? MarkerSize = null,
    WorkbookThemeColorReference? FillThemeColor = null,
    WorkbookThemeColorReference? StrokeThemeColor = null,
    bool? Smooth = null,
    CellColor? MarkerBorderColor = null,
    WorkbookThemeColorReference? MarkerBorderThemeColor = null,
    double? MarkerBorderThickness = null,
    bool? InvertIfNegative = null,
    bool NoFill = false,
    bool NoLine = false,
    // R91-render-chart-series-format-5-1: verbatim passthrough of an authored series fill
    // (<a:gradFill>/<a:pattFill>) that has no dedicated model representation. When set, the
    // writer re-emits this element as-is instead of synthesizing a fill from FillColor/
    // FillThemeColor/NoFill, so a gradient or pattern fill survives a FreeX save intact instead
    // of collapsing to the theme-palette solid default.
    string? RawFillXml = null,
    // R91-render-chart-series-format-5-4: the <a:alpha> transparency child of the series fill's
    // color element (<a:srgbClr>/<a:schemeClr>), as a 0..1 opacity fraction (1 = fully opaque,
    // the implicit default when no <a:alpha> is authored). CellColor itself has no alpha channel,
    // so this rides alongside FillColor/FillThemeColor rather than inside them.
    double? FillAlpha = null)
{
    public CellColor? ResolveFillColor(WorkbookTheme theme) =>
        FillThemeColor?.Resolve(theme) ?? FillColor;

    public CellColor? ResolveStrokeColor(WorkbookTheme theme) =>
        StrokeThemeColor?.Resolve(theme) ?? StrokeColor;
}

public sealed record ChartPointDataLabelFormat(
    int SeriesIndex,
    int PointIndex,
    CellColor? FillColor = null,
    CellColor? BorderColor = null,
    double? BorderThickness = null,
    CellColor? TextColor = null,
    double? FontSize = null,
    WorkbookThemeColorReference? FillThemeColor = null,
    WorkbookThemeColorReference? BorderThemeColor = null,
    WorkbookThemeColorReference? TextThemeColor = null,
    bool? IsDeleted = null,
    ChartDataLabelPosition? Position = null,
    bool? ShowValue = null,
    bool? ShowCategoryName = null,
    bool? ShowSeriesName = null,
    bool? ShowLegendKey = null,
    bool? ShowPercentage = null,
    bool? ShowBubbleSize = null,
    string? NumberFormatCode = null,
    bool? NumberFormatSourceLinked = null,
    string? SeparatorText = null,
    // Per-point manual layout (e.g. a data label dragged away from its computed position), read/
    // written from the <c:dLbl>'s own <c:layout> child. Distinct from any chart/plot-area/title/
    // legend layout - this only ever applies to this one point.
    ChartManualLayoutModel? Layout = null,
    // Verbatim <c:tx> XML (namespace-qualified) for a per-point custom label-text override
    // (Excel's "type over the label text" feature). Preserved verbatim rather than modeled as
    // plain text so multi-run rich formatting inside the override survives round-trip.
    string? CustomTextXml = null)
{
    public CellColor? ResolveFillColor(WorkbookTheme theme) =>
        FillThemeColor?.Resolve(theme) ?? FillColor;

    public CellColor? ResolveBorderColor(WorkbookTheme theme) =>
        BorderThemeColor?.Resolve(theme) ?? BorderColor;

    public CellColor? ResolveTextColor(WorkbookTheme theme) =>
        TextThemeColor?.Resolve(theme) ?? TextColor;
}

public sealed record ChartSeriesDataLabelFormat(
    int SeriesIndex,
    CellColor? FillColor = null,
    CellColor? BorderColor = null,
    double? BorderThickness = null,
    CellColor? TextColor = null,
    double? FontSize = null,
    WorkbookThemeColorReference? FillThemeColor = null,
    WorkbookThemeColorReference? BorderThemeColor = null,
    WorkbookThemeColorReference? TextThemeColor = null,
    // R62-io-chart-legend-datalabels-6-1: models the series-level <c:dLbls><c:delete val="1"/></c:dLbls>
    // override (CT_DLbls' delete|Group_DLbls choice) -- Excel writes this when the user hides just
    // this series' data labels, overriding a chart-wide showVal=1 default. Like
    // ChartPointDataLabelFormat.IsDeleted, this must be modeled even when every other field on this
    // record is null, otherwise the per-series suppression is silently discarded on open.
    bool? IsDeleted = null,
    ChartDataLabelPosition? Position = null,
    bool? ShowValue = null,
    bool? ShowCategoryName = null,
    bool? ShowSeriesName = null,
    bool? ShowLegendKey = null,
    bool? ShowPercentage = null,
    bool? ShowBubbleSize = null,
    string? NumberFormatCode = null,
    bool? NumberFormatSourceLinked = null,
    string? SeparatorText = null)
{
    public CellColor? ResolveFillColor(WorkbookTheme theme) =>
        FillThemeColor?.Resolve(theme) ?? FillColor;

    public CellColor? ResolveBorderColor(WorkbookTheme theme) =>
        BorderThemeColor?.Resolve(theme) ?? BorderColor;

    public CellColor? ResolveTextColor(WorkbookTheme theme) =>
        TextThemeColor?.Resolve(theme) ?? TextColor;
}

