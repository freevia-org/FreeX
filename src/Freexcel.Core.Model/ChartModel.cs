namespace Freexcel.Core.Model;

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

public enum ChartDataLabelPosition { BestFit, Center, InsideEnd, OutsideEnd }

public enum ChartDataLabelSeparator { Comma, Semicolon, NewLine, Space }

public enum ChartDataLabelNumberFormat { General, Number, Currency, Percent }

public enum ChartTrendlineType { Linear, Exponential, Logarithmic, Power, MovingAverage, Polynomial }

public enum ChartLineDashStyle { Solid, Dash, Dot }

public enum ChartBubbleSizeRepresents { Area, Width }

public enum ChartAxisTickStyle { None, Inside, Outside, Cross }

public enum ChartMarkerStyle { None, Circle, Square, Diamond, Triangle }

public enum ChartErrorBarKind { StandardError, Percentage, FixedValue }

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
    public bool? BlackAndWhite { get; set; }
    public bool? Draft { get; set; }
}

public sealed class ChartDataTableModel
{
    public bool? ShowHorizontalBorder { get; set; }
    public bool? ShowVerticalBorder { get; set; }
    public bool? ShowOutline { get; set; }
    public bool? ShowLegendKeys { get; set; }
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
    double? MarkerBorderThickness = null)
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
    public string? Name { get; set; }
    public ChartType Type { get; set; } = ChartType.Column;
    public GridRange DataRange { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool IsPivotChart { get; set; }
    public string? PivotSourceSheetName { get; set; }
    public string? PivotTableName { get; set; }
    public int? PivotCacheId { get; set; }
    public string? PivotFormatsXml { get; set; }
    public bool ShowPivotChartFieldButtons { get; set; } = true;
    public bool ShowPivotChartReportFilterButtons { get; set; } = true;
    public bool ShowPivotChartAxisFieldButtons { get; set; } = true;
    public bool ShowPivotChartValueFieldButtons { get; set; } = true;
    public bool Uses1904DateSystem { get; set; }
    public string? Language { get; set; }
    public int? ChartStyleId { get; set; }
    public ChartColorMapOverrideModel? ColorMapOverride { get; set; }
    public ChartExternalDataModel? ExternalData { get; set; }
    public ChartManualLayoutModel? PlotAreaLayout { get; set; }
    public ChartManualLayoutModel? LegendLayout { get; set; }
    public bool RoundedCorners { get; set; }
    public ChartBlankDisplayMode BlankDisplayMode { get; set; } = ChartBlankDisplayMode.Gap;
    public bool ShowDataLabelsOverMaximum { get; set; }
    public bool AutoTitleDeleted { get; set; }
    public bool ShowDataInHiddenRowsAndColumns { get; set; }
    public ChartProtectionModel? Protection { get; set; }
    public ChartPrintSettingsModel? PrintSettings { get; set; }
    public ChartDataTableModel? DataTable { get; set; }
    public Chart3DViewModel? ThreeDView { get; set; }
    public ChartSurfaceFormatModel? FloorFormat { get; set; }
    public ChartSurfaceFormatModel? SideWallFormat { get; set; }
    public ChartSurfaceFormatModel? BackWallFormat { get; set; }
    public int? BarGapWidth { get; set; }
    public int? BarOverlap { get; set; }
    public bool? VaryColorsByPoint { get; set; }
    public int BubbleScale { get; set; } = 100;
    public bool ShowNegativeBubbles { get; set; }
    public ChartBubbleSizeRepresents BubbleSizeRepresents { get; set; } = ChartBubbleSizeRepresents.Area;
    public StockChartSubtype StockSubtype { get; set; } = StockChartSubtype.HighLowClose;
    public bool FirstRowIsHeader { get; set; } = true;
    public bool FirstColIsCategories { get; set; } = true;
    public string? Title { get; set; }
    public string? XAxisTitle { get; set; }
    public string? YAxisTitle { get; set; }
    public CellColor? ChartTitleTextColor { get; set; }
    public WorkbookThemeColorReference? ChartTitleTextThemeColor { get; set; }
    public double ChartTitleFontSize { get; set; } = 16;
    public CellColor? AxisTitleTextColor { get; set; }
    public WorkbookThemeColorReference? AxisTitleTextThemeColor { get; set; }
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
    public WorkbookThemeColorReference? XAxisLabelTextThemeColor { get; set; }
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
    public WorkbookThemeColorReference? YAxisLabelTextThemeColor { get; set; }
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
    public bool ShowErrorBars { get; set; }
    public ChartErrorBarKind ErrorBarKind { get; set; } = ChartErrorBarKind.StandardError;
    public ChartErrorBarDirection ErrorBarDirection { get; set; } = ChartErrorBarDirection.Both;
    public double ErrorBarValue { get; set; } = 5;
    public bool ErrorBarEndCaps { get; set; } = true;
    public CellColor? ErrorBarColor { get; set; }
    public WorkbookThemeColorReference? ErrorBarThemeColor { get; set; }
    public double ErrorBarThickness { get; set; } = 1;
    public ChartLineDashStyle ErrorBarDashStyle { get; set; } = ChartLineDashStyle.Solid;
    public bool ShowDropLines { get; set; }
    public CellColor? DropLineColor { get; set; }
    public WorkbookThemeColorReference? DropLineThemeColor { get; set; }
    public double DropLineThickness { get; set; } = 1;
    public ChartLineDashStyle DropLineDashStyle { get; set; } = ChartLineDashStyle.Solid;
    public bool ShowHighLowLines { get; set; }
    public CellColor? HighLowLineColor { get; set; }
    public WorkbookThemeColorReference? HighLowLineThemeColor { get; set; }
    public double HighLowLineThickness { get; set; } = 1;
    public ChartLineDashStyle HighLowLineDashStyle { get; set; } = ChartLineDashStyle.Solid;
    public bool ShowUpDownBars { get; set; }
    public int? UpDownBarGapWidth { get; set; }
    public CellColor? UpBarFillColor { get; set; }
    public WorkbookThemeColorReference? UpBarFillThemeColor { get; set; }
    public CellColor? UpBarBorderColor { get; set; }
    public WorkbookThemeColorReference? UpBarBorderThemeColor { get; set; }
    public double? UpBarBorderThickness { get; set; }
    public CellColor? DownBarFillColor { get; set; }
    public WorkbookThemeColorReference? DownBarFillThemeColor { get; set; }
    public CellColor? DownBarBorderColor { get; set; }
    public WorkbookThemeColorReference? DownBarBorderThemeColor { get; set; }
    public double? DownBarBorderThickness { get; set; }
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

    public CellColor? ResolveChartTitleTextColor(WorkbookTheme theme) =>
        ChartTitleTextThemeColor?.Resolve(theme) ?? ChartTitleTextColor;

    public CellColor? ResolveAxisTitleTextColor(WorkbookTheme theme) =>
        AxisTitleTextThemeColor?.Resolve(theme) ?? AxisTitleTextColor;

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

    public CellColor? ResolveXAxisLabelTextColor(WorkbookTheme theme) =>
        XAxisLabelTextThemeColor?.Resolve(theme) ?? XAxisLabelTextColor;

    public CellColor? ResolveYAxisLabelTextColor(WorkbookTheme theme) =>
        YAxisLabelTextThemeColor?.Resolve(theme) ?? YAxisLabelTextColor;

    public CellColor? ResolveTrendlineColor(WorkbookTheme theme) =>
        TrendlineThemeColor?.Resolve(theme) ?? TrendlineColor;
}
