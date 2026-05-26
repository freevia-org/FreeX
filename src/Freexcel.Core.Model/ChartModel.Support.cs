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

public sealed record ChartLegendEntryModel(int Index, bool? IsDeleted);

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
    bool? InvertIfNegative = null)
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
    string? SeparatorText = null)
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
