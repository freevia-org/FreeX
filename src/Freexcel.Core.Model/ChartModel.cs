namespace Freexcel.Core.Model;

public enum ChartType
{
    Column,
    StackedColumn,
    PercentStackedColumn,
    Line,
    Pie,
    Doughnut,
    Bar,
    StackedBar,
    PercentStackedBar,
    Scatter,
    Bubble,
    Area,
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
    ThreeDColumn
}

public enum ChartLegendPosition { None, Left, Right, Top, Bottom }

public enum ChartDataLabelPosition { BestFit, Center, InsideEnd, OutsideEnd }

public enum ChartDataLabelSeparator { Comma, Semicolon, NewLine, Space }

public enum ChartDataLabelNumberFormat { General, Number, Currency, Percent }

public enum ChartTrendlineType { Linear, Exponential, Logarithmic, Power, MovingAverage, Polynomial }

public enum ChartLineDashStyle { Solid, Dash, Dot }

public enum ChartAxisTickStyle { None, Inside, Outside, Cross }

public enum ChartMarkerStyle { None, Circle, Square, Diamond, Triangle }

public sealed record ChartSeriesFormat(
    int SeriesIndex,
    CellColor? FillColor = null,
    CellColor? StrokeColor = null,
    double? StrokeThickness = null,
    ChartLineDashStyle? DashStyle = null,
    ChartMarkerStyle? MarkerStyle = null,
    double? MarkerSize = null,
    WorkbookThemeColorReference? FillThemeColor = null,
    WorkbookThemeColorReference? StrokeThemeColor = null)
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
    WorkbookThemeColorReference? TextThemeColor = null)
{
    public CellColor? ResolveFillColor(WorkbookTheme theme) =>
        FillThemeColor?.Resolve(theme) ?? FillColor;

    public CellColor? ResolveBorderColor(WorkbookTheme theme) =>
        BorderThemeColor?.Resolve(theme) ?? BorderColor;

    public CellColor? ResolveTextColor(WorkbookTheme theme) =>
        TextThemeColor?.Resolve(theme) ?? TextColor;
}

/// <summary>Lightweight chart definition stored on a Sheet.</summary>
public sealed class ChartModel
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public ChartType Type { get; set; } = ChartType.Column;
    public GridRange DataRange { get; set; }
    public bool IsPivotChart { get; set; }
    public string? PivotSourceSheetName { get; set; }
    public string? PivotTableName { get; set; }
    public int? PivotCacheId { get; set; }
    public int? ChartStyleId { get; set; }
    public bool FirstRowIsHeader { get; set; } = true;
    public bool FirstColIsCategories { get; set; } = true;
    public string? Title { get; set; }
    public string? XAxisTitle { get; set; }
    public string? YAxisTitle { get; set; }
    public CellColor? ChartTitleTextColor { get; set; }
    public double ChartTitleFontSize { get; set; } = 16;
    public CellColor? AxisTitleTextColor { get; set; }
    public double AxisTitleFontSize { get; set; } = 12;
    public CellColor? ChartAreaFillColor { get; set; }
    public WorkbookThemeColorReference? ChartAreaFillThemeColor { get; set; }
    public CellColor? PlotAreaFillColor { get; set; }
    public WorkbookThemeColorReference? PlotAreaFillThemeColor { get; set; }
    public CellColor? PlotAreaBorderColor { get; set; }
    public WorkbookThemeColorReference? PlotAreaBorderThemeColor { get; set; }
    public double PlotAreaBorderThickness { get; set; } = 1;
    public CellColor? LegendTextColor { get; set; }
    public WorkbookThemeColorReference? LegendTextThemeColor { get; set; }
    public CellColor? LegendFillColor { get; set; }
    public WorkbookThemeColorReference? LegendFillThemeColor { get; set; }
    public CellColor? LegendBorderColor { get; set; }
    public WorkbookThemeColorReference? LegendBorderThemeColor { get; set; }
    public double LegendBorderThickness { get; set; }
    public double LegendFontSize { get; set; } = 12;
    public double DoughnutHoleSize { get; set; } = 0.55;
    public double FirstSliceAngle { get; set; }
    public int ExplodedSliceIndex { get; set; } = -1;
    public double ExplodedSliceDistance { get; set; } = 0.1;
    public double? XAxisMinimum { get; set; }
    public double? XAxisMaximum { get; set; }
    public double? XAxisMajorUnit { get; set; }
    public double? XAxisMinorUnit { get; set; }
    public bool XAxisLogScale { get; set; }
    public ChartDataLabelNumberFormat XAxisNumberFormat { get; set; } = ChartDataLabelNumberFormat.General;
    public bool ShowXAxisMajorGridlines { get; set; }
    public bool ShowXAxisMinorGridlines { get; set; }
    public CellColor? XAxisMajorGridlineColor { get; set; }
    public CellColor? XAxisMinorGridlineColor { get; set; }
    public double XAxisGridlineThickness { get; set; } = 1;
    public ChartAxisTickStyle XAxisMajorTickStyle { get; set; } = ChartAxisTickStyle.Outside;
    public ChartAxisTickStyle XAxisMinorTickStyle { get; set; } = ChartAxisTickStyle.None;
    public bool ShowXAxisLabels { get; set; } = true;
    public CellColor? XAxisLabelTextColor { get; set; }
    public double XAxisLabelFontSize { get; set; } = 11;
    public double XAxisLabelAngle { get; set; }
    public CellColor? XAxisLineColor { get; set; }
    public double XAxisLineThickness { get; set; } = 1;
    public double? YAxisMinimum { get; set; }
    public double? YAxisMaximum { get; set; }
    public double? YAxisMajorUnit { get; set; }
    public double? YAxisMinorUnit { get; set; }
    public bool YAxisLogScale { get; set; }
    public ChartDataLabelNumberFormat YAxisNumberFormat { get; set; } = ChartDataLabelNumberFormat.General;
    public bool ShowYAxisMajorGridlines { get; set; }
    public bool ShowYAxisMinorGridlines { get; set; }
    public CellColor? YAxisMajorGridlineColor { get; set; }
    public CellColor? YAxisMinorGridlineColor { get; set; }
    public double YAxisGridlineThickness { get; set; } = 1;
    public ChartAxisTickStyle YAxisMajorTickStyle { get; set; } = ChartAxisTickStyle.Outside;
    public ChartAxisTickStyle YAxisMinorTickStyle { get; set; } = ChartAxisTickStyle.None;
    public bool ShowYAxisLabels { get; set; } = true;
    public CellColor? YAxisLabelTextColor { get; set; }
    public double YAxisLabelFontSize { get; set; } = 11;
    public double YAxisLabelAngle { get; set; }
    public CellColor? YAxisLineColor { get; set; }
    public double YAxisLineThickness { get; set; } = 1;
    public ChartLegendPosition LegendPosition { get; set; } = ChartLegendPosition.Right;
    public bool LegendOverlay { get; set; }
    public bool ShowLegend { get; set; } = true;
    public bool ShowDataLabels { get; set; }
    public ChartDataLabelPosition DataLabelPosition { get; set; } = ChartDataLabelPosition.BestFit;
    public bool ShowDataLabelCategoryName { get; set; }
    public bool ShowDataLabelSeriesName { get; set; }
    public bool ShowDataLabelPercentage { get; set; }
    public ChartDataLabelSeparator DataLabelSeparator { get; set; } = ChartDataLabelSeparator.Comma;
    public ChartDataLabelNumberFormat DataLabelNumberFormat { get; set; } = ChartDataLabelNumberFormat.General;
    public bool ShowDataLabelCallouts { get; set; }
    public CellColor? DataLabelFillColor { get; set; }
    public WorkbookThemeColorReference? DataLabelFillThemeColor { get; set; }
    public CellColor? DataLabelBorderColor { get; set; }
    public WorkbookThemeColorReference? DataLabelBorderThemeColor { get; set; }
    public CellColor? DataLabelTextColor { get; set; }
    public WorkbookThemeColorReference? DataLabelTextThemeColor { get; set; }
    public double DataLabelBorderThickness { get; set; }
    public double DataLabelFontSize { get; set; } = 11;
    public double DataLabelAngle { get; set; }
    public bool ShowLinearTrendline { get; set; }
    public ChartTrendlineType TrendlineType { get; set; } = ChartTrendlineType.Linear;
    public int TrendlinePeriod { get; set; } = 2;
    public int TrendlineOrder { get; set; } = 2;
    public bool ShowTrendlineEquation { get; set; }
    public bool ShowTrendlineRSquared { get; set; }
    public CellColor? TrendlineColor { get; set; }
    public WorkbookThemeColorReference? TrendlineThemeColor { get; set; }
    public double TrendlineThickness { get; set; } = 1.5;
    public ChartLineDashStyle TrendlineDashStyle { get; set; } = ChartLineDashStyle.Dash;
    public bool ShowSecondaryAxis { get; set; }
    public List<int> SecondaryAxisSeriesIndexes { get; set; } = [];
    public List<int> ComboLineSeriesIndexes { get; set; } = [];
    public List<ChartSeriesFormat> SeriesFormats { get; set; } = [];
    public List<ChartPointDataLabelFormat> PointDataLabelFormats { get; set; } = [];
    public bool UseComboLineForSecondarySeries { get; set; }
    public double Left   { get; set; } = 50;
    public double Top    { get; set; } = 50;
    public double Width  { get; set; } = 400;
    public double Height { get; set; } = 300;

    public CellColor? ResolveChartAreaFillColor(WorkbookTheme theme) =>
        ChartAreaFillThemeColor?.Resolve(theme) ?? ChartAreaFillColor;

    public CellColor? ResolvePlotAreaFillColor(WorkbookTheme theme) =>
        PlotAreaFillThemeColor?.Resolve(theme) ?? PlotAreaFillColor;

    public CellColor? ResolvePlotAreaBorderColor(WorkbookTheme theme) =>
        PlotAreaBorderThemeColor?.Resolve(theme) ?? PlotAreaBorderColor;

    public CellColor? ResolveLegendTextColor(WorkbookTheme theme) =>
        LegendTextThemeColor?.Resolve(theme) ?? LegendTextColor;

    public CellColor? ResolveLegendFillColor(WorkbookTheme theme) =>
        LegendFillThemeColor?.Resolve(theme) ?? LegendFillColor;

    public CellColor? ResolveLegendBorderColor(WorkbookTheme theme) =>
        LegendBorderThemeColor?.Resolve(theme) ?? LegendBorderColor;

    public CellColor? ResolveDataLabelFillColor(WorkbookTheme theme) =>
        DataLabelFillThemeColor?.Resolve(theme) ?? DataLabelFillColor;

    public CellColor? ResolveDataLabelBorderColor(WorkbookTheme theme) =>
        DataLabelBorderThemeColor?.Resolve(theme) ?? DataLabelBorderColor;

    public CellColor? ResolveDataLabelTextColor(WorkbookTheme theme) =>
        DataLabelTextThemeColor?.Resolve(theme) ?? DataLabelTextColor;

    public CellColor? ResolveTrendlineColor(WorkbookTheme theme) =>
        TrendlineThemeColor?.Resolve(theme) ?? TrendlineColor;
}
