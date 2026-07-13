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

public enum ChartLegendPosition { None, Left, Right, Top, Bottom }

public enum ChartDataLabelPosition { BestFit, Center, InsideEnd, OutsideEnd, InsideBase }

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

public sealed record ChartLegendEntryModel(int Index, bool? IsDeleted);

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
/// </summary>
public sealed record ChartSeriesVerbatimFormulas(
    int SeriesIndex,
    string? ValFormula,
    string? CatFormula,
    string? TxFormula);

/// <summary>
/// Embedded data values for a single chart series, extracted from the
/// <c>&lt;c:numCache&gt;</c> / <c>&lt;c:strCache&gt;</c> elements in the chart XML.
/// Used as a fallback when the series data range formula is an unresolvable named range
/// (e.g. <c>Sheet1!rngMyData</c>) so FreeX can still render the chart without recalc.
/// </summary>
public sealed record ChartEmbeddedSeriesData(
    int SeriesIndex,
    string? SeriesName,
    IReadOnlyList<string> Categories,
    IReadOnlyList<double?> Values);

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

public enum ChartMarkerStyle { None, Circle, Square, Diamond, Triangle }

public enum ChartErrorBarKind { StandardError, Percentage, FixedValue, Custom }

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
    bool NoLine = false)
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

