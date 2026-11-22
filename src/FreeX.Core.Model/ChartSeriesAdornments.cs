namespace FreeX.Core.Model;

/// <summary>
/// One series' trendline, the way Excel stores it (<c>&lt;c:ser&gt;/&lt;c:trendline&gt;</c>): every series
/// carries its own trendline (or none), with its own type, forecast, readout and line formatting.
/// Init-only members so the record stays binary-compatible when options are added.
/// <para>
/// The chart-wide <c>ChartModel.ShowLinearTrendline</c>/<c>Trendline*</c> members are kept as the
/// "apply to all series" form used by old files and legacy commands: while
/// <see cref="ChartModel.SeriesTrendlines"/> is empty they describe a trendline that every series
/// carries; once it holds entries, the entries are authoritative (see
/// <see cref="ChartSeriesAdornmentExtensions.ResolveTrendline"/>).
/// </para>
/// </summary>
public sealed record ChartSeriesTrendline
{
    public int SeriesIndex { get; init; }
    public string? Name { get; init; }
    public ChartTrendlineType Type { get; init; } = ChartTrendlineType.Linear;
    public int Period { get; init; } = 2;
    public int Order { get; init; } = 2;
    public double? Forward { get; init; }
    public double? Backward { get; init; }
    public double? Intercept { get; init; }
    public bool ShowEquation { get; init; }
    public bool ShowRSquared { get; init; }
    public string? LabelNumberFormatCode { get; init; }
    public bool? LabelNumberFormatSourceLinked { get; init; }
    public ChartManualLayoutModel? LabelLayout { get; init; }
    public CellColor? LabelFillColor { get; init; }
    public WorkbookThemeColorReference? LabelFillThemeColor { get; init; }
    public CellColor? LabelBorderColor { get; init; }
    public WorkbookThemeColorReference? LabelBorderThemeColor { get; init; }
    public double? LabelBorderThickness { get; init; }
    public CellColor? LabelTextColor { get; init; }
    public WorkbookThemeColorReference? LabelTextThemeColor { get; init; }
    public double? LabelFontSize { get; init; }
    public double? LabelAngle { get; init; }
    public CellColor? Color { get; init; }
    public WorkbookThemeColorReference? ThemeColor { get; init; }
    public double Thickness { get; init; } = 1.5;
    public ChartLineDashStyle DashStyle { get; init; } = ChartLineDashStyle.Dash;

    /// <summary>Captures the chart-wide trendline members as the entry for <paramref name="seriesIndex"/>.</summary>
    public static ChartSeriesTrendline FromChart(ChartModel chart, int seriesIndex)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new ChartSeriesTrendline
        {
            SeriesIndex = seriesIndex,
            Name = chart.TrendlineName,
            Type = chart.TrendlineType,
            Period = chart.TrendlinePeriod,
            Order = chart.TrendlineOrder,
            Forward = chart.TrendlineForward,
            Backward = chart.TrendlineBackward,
            Intercept = chart.TrendlineIntercept,
            ShowEquation = chart.ShowTrendlineEquation,
            ShowRSquared = chart.ShowTrendlineRSquared,
            LabelNumberFormatCode = chart.TrendlineLabelNumberFormatCode,
            LabelNumberFormatSourceLinked = chart.TrendlineLabelNumberFormatSourceLinked,
            LabelLayout = chart.TrendlineLabelLayout,
            LabelFillColor = chart.TrendlineLabelFillColor,
            LabelFillThemeColor = chart.TrendlineLabelFillThemeColor,
            LabelBorderColor = chart.TrendlineLabelBorderColor,
            LabelBorderThemeColor = chart.TrendlineLabelBorderThemeColor,
            LabelBorderThickness = chart.TrendlineLabelBorderThickness,
            LabelTextColor = chart.TrendlineLabelTextColor,
            LabelTextThemeColor = chart.TrendlineLabelTextThemeColor,
            LabelFontSize = chart.TrendlineLabelFontSize,
            LabelAngle = chart.TrendlineLabelAngle,
            Color = chart.TrendlineColor,
            ThemeColor = chart.TrendlineThemeColor,
            Thickness = chart.TrendlineThickness,
            DashStyle = chart.TrendlineDashStyle,
        };
    }

    /// <summary>Writes this entry's settings into the chart-wide trendline members of <paramref name="chart"/>.</summary>
    public void ApplyTo(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        chart.TrendlineName = Name;
        chart.TrendlineType = Type;
        chart.TrendlinePeriod = Period;
        chart.TrendlineOrder = Order;
        chart.TrendlineForward = Forward;
        chart.TrendlineBackward = Backward;
        chart.TrendlineIntercept = Intercept;
        chart.ShowTrendlineEquation = ShowEquation;
        chart.ShowTrendlineRSquared = ShowRSquared;
        chart.TrendlineLabelNumberFormatCode = LabelNumberFormatCode;
        chart.TrendlineLabelNumberFormatSourceLinked = LabelNumberFormatSourceLinked;
        chart.TrendlineLabelLayout = LabelLayout;
        chart.TrendlineLabelFillColor = LabelFillColor;
        chart.TrendlineLabelFillThemeColor = LabelFillThemeColor;
        chart.TrendlineLabelBorderColor = LabelBorderColor;
        chart.TrendlineLabelBorderThemeColor = LabelBorderThemeColor;
        chart.TrendlineLabelBorderThickness = LabelBorderThickness;
        chart.TrendlineLabelTextColor = LabelTextColor;
        chart.TrendlineLabelTextThemeColor = LabelTextThemeColor;
        chart.TrendlineLabelFontSize = LabelFontSize;
        chart.TrendlineLabelAngle = LabelAngle;
        chart.TrendlineColor = Color;
        chart.TrendlineThemeColor = ThemeColor;
        chart.TrendlineThickness = Thickness;
        chart.TrendlineDashStyle = DashStyle;
    }
}

/// <summary>
/// One series' error bars (<c>&lt;c:ser&gt;/&lt;c:errBars&gt;</c>). See <see cref="ChartSeriesTrendline"/>
/// for how this relates to the chart-wide <c>ShowErrorBars</c>/<c>ErrorBar*</c> members.
/// </summary>
public sealed record ChartSeriesErrorBars
{
    public int SeriesIndex { get; init; }
    public ChartErrorBarKind Kind { get; init; } = ChartErrorBarKind.StandardError;
    public ChartErrorBarAxisDirection AxisDirection { get; init; } = ChartErrorBarAxisDirection.Y;
    public ChartErrorBarDirection Direction { get; init; } = ChartErrorBarDirection.Both;
    public double Value { get; init; } = 5;
    public string? PlusRangeFormula { get; init; }
    public string? MinusRangeFormula { get; init; }
    public bool EndCaps { get; init; } = true;
    public CellColor? Color { get; init; }
    public WorkbookThemeColorReference? ThemeColor { get; init; }
    public string? PlusRangeCacheXml { get; init; }
    public string? MinusRangeCacheXml { get; init; }
    public double Thickness { get; init; } = 1;
    public ChartLineDashStyle DashStyle { get; init; } = ChartLineDashStyle.Solid;

    /// <summary>Captures the chart-wide error-bar members as the entry for <paramref name="seriesIndex"/>.</summary>
    public static ChartSeriesErrorBars FromChart(ChartModel chart, int seriesIndex)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new ChartSeriesErrorBars
        {
            SeriesIndex = seriesIndex,
            Kind = chart.ErrorBarKind,
            AxisDirection = chart.ErrorBarAxisDirection,
            Direction = chart.ErrorBarDirection,
            Value = chart.ErrorBarValue,
            PlusRangeFormula = chart.ErrorBarPlusRangeFormula,
            MinusRangeFormula = chart.ErrorBarMinusRangeFormula,
            EndCaps = chart.ErrorBarEndCaps,
            Color = chart.ErrorBarColor,
            ThemeColor = chart.ErrorBarThemeColor,
            PlusRangeCacheXml = chart.ErrorBarPlusRangeCacheXml,
            MinusRangeCacheXml = chart.ErrorBarMinusRangeCacheXml,
            Thickness = chart.ErrorBarThickness,
            DashStyle = chart.ErrorBarDashStyle,
        };
    }

    /// <summary>Writes this entry's settings into the chart-wide error-bar members of <paramref name="chart"/>.</summary>
    public void ApplyTo(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        chart.ErrorBarKind = Kind;
        chart.ErrorBarAxisDirection = AxisDirection;
        chart.ErrorBarDirection = Direction;
        chart.ErrorBarValue = Value;
        chart.ErrorBarPlusRangeFormula = PlusRangeFormula;
        chart.ErrorBarMinusRangeFormula = MinusRangeFormula;
        chart.ErrorBarEndCaps = EndCaps;
        chart.ErrorBarColor = Color;
        chart.ErrorBarThemeColor = ThemeColor;
        chart.ErrorBarPlusRangeCacheXml = PlusRangeCacheXml;
        chart.ErrorBarMinusRangeCacheXml = MinusRangeCacheXml;
        chart.ErrorBarThickness = Thickness;
        chart.ErrorBarDashStyle = DashStyle;
    }
}

/// <summary>
/// Per-series trendline / error-bar resolution shared by every renderer, the XLSX writer and the editing
/// planners, so "which series carries what" has exactly one definition.
/// </summary>
public static class ChartSeriesAdornmentExtensions
{
    /// <summary>
    /// The series indexes the chart plots (the <c>c:idx</c> values): the column mappings when the chart
    /// has them, otherwise <c>0..seriesCount-1</c>.
    /// </summary>
    public static IReadOnlyList<int> EnumerateSeriesIndexes(this ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        if (chart.SeriesColumnMappings.Count > 0)
            return chart.SeriesColumnMappings.Select(m => m.SeriesXmlIndex).Distinct().Order().ToList();

        var count = ChartTypeSupport.GetDataSeriesCount(chart);
        return Enumerable.Range(0, Math.Max(0, count)).ToList();
    }

    /// <summary>
    /// The trendline settings carried by <paramref name="seriesIndex"/> as a read-only projection of the
    /// chart (<see cref="ChartModel.ShowLinearTrendline"/> is true and the <c>Trendline*</c> members hold
    /// that series' settings), or null when the series has no trendline. While the chart has no
    /// per-series entries, the chart-wide settings apply to every series.
    /// </summary>
    public static ChartModel? ResolveTrendline(this ChartModel chart, int seriesIndex)
    {
        ArgumentNullException.ThrowIfNull(chart);
        if (!chart.ShowLinearTrendline || !ChartTypeSupport.SupportsTrendlines(chart.Type))
            return null;

        if (chart.SeriesTrendlines.Count == 0)
            return chart;

        var entry = chart.SeriesTrendlines.FirstOrDefault(t => t.SeriesIndex == seriesIndex);
        if (entry is null)
            return null;

        var projection = chart.ShallowClone();
        entry.ApplyTo(projection);
        projection.TrendlineSeriesIndex = seriesIndex;
        return projection;
    }

    /// <summary>Error-bar counterpart of <see cref="ResolveTrendline"/>.</summary>
    public static ChartModel? ResolveErrorBars(this ChartModel chart, int seriesIndex)
    {
        ArgumentNullException.ThrowIfNull(chart);
        if (!chart.ShowErrorBars || !ChartTypeSupport.SupportsTrendlines(chart.Type))
            return null;

        if (chart.SeriesErrorBars.Count == 0)
            return chart;

        var entry = chart.SeriesErrorBars.FirstOrDefault(t => t.SeriesIndex == seriesIndex);
        if (entry is null)
            return null;

        var projection = chart.ShallowClone();
        entry.ApplyTo(projection);
        projection.ErrorBarSeriesIndex = seriesIndex;
        return projection;
    }

    public static bool HasTrendline(this ChartModel chart, int seriesIndex) =>
        chart.ResolveTrendline(seriesIndex) is not null;

    public static bool HasErrorBars(this ChartModel chart, int seriesIndex) =>
        chart.ResolveErrorBars(seriesIndex) is not null;

    /// <summary>True when any series carries a trendline.</summary>
    public static bool HasAnyTrendline(this ChartModel chart) =>
        chart.ShowLinearTrendline && ChartTypeSupport.SupportsTrendlines(chart.Type);

    /// <summary>True when any series carries error bars.</summary>
    public static bool HasAnyErrorBars(this ChartModel chart) =>
        chart.ShowErrorBars && ChartTypeSupport.SupportsTrendlines(chart.Type);

    /// <summary>
    /// Turns the legacy chart-wide trendline (applies to every series) into explicit per-series entries so
    /// a single series can then be edited or removed on its own. No-op when entries already exist or the
    /// chart has no trendline.
    /// </summary>
    public static List<ChartSeriesTrendline> MaterializeTrendlines(this ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        if (chart.SeriesTrendlines.Count > 0 || !chart.ShowLinearTrendline)
            return chart.SeriesTrendlines.ToList();

        return chart.EnumerateSeriesIndexes()
            .Select(index => ChartSeriesTrendline.FromChart(chart, index))
            .ToList();
    }

    /// <summary>Error-bar counterpart of <see cref="MaterializeTrendlines"/>.</summary>
    public static List<ChartSeriesErrorBars> MaterializeErrorBars(this ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        if (chart.SeriesErrorBars.Count > 0 || !chart.ShowErrorBars)
            return chart.SeriesErrorBars.ToList();

        return chart.EnumerateSeriesIndexes()
            .Select(index => ChartSeriesErrorBars.FromChart(chart, index))
            .ToList();
    }

    /// <summary>
    /// Remaps per-series adornments after a series was removed: drops the removed series' entries and
    /// shifts higher indexes down.
    /// </summary>
    public static List<T> RemapAfterSeriesRemoval<T>(
        IEnumerable<T> entries,
        Func<T, int> getIndex,
        Func<T, int, T> withIndex,
        int removedSeriesIndex) =>
        entries
            .Where(e => getIndex(e) != removedSeriesIndex)
            .Select(e => getIndex(e) > removedSeriesIndex ? withIndex(e, getIndex(e) - 1) : e)
            .ToList();
}
