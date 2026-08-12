using Free.Shared.AppServices;

namespace FreeX.App.Presentation.Charts.Editing;

public sealed record ChartQuickCommandDescriptor(
    ChartQuickCommand Command,
    string Label,
    string HostMissingSelectionMessageResourceKey,
    string? HostUnsupportedMessageResourceKey = null,
    string? UnsupportedStatusResourceKey = null)
{
    public string TitleResourceKey => Command switch
    {
        ChartQuickCommand.FirstSliceAngle => "MainWindow_TooltipTitle_FirstSliceAngle",
        ChartQuickCommand.DoughnutHoleSize => "MainWindow_TooltipTitle_DoughnutHoleSize",
        ChartQuickCommand.ExplodedSlice => "MainWindow_TooltipTitle_ExplodeSlice",
        ChartQuickCommand.DataLabelCategoryName => "MainWindow_TooltipTitle_CategoryName",
        ChartQuickCommand.DataLabelSeriesName => "MainWindow_TooltipTitle_SeriesName",
        ChartQuickCommand.DataLabelPercentage => "MainWindow_TooltipTitle_Percentage",
        ChartQuickCommand.DataLabelSeparator => "MainWindow_TooltipTitle_LabelSeparator",
        ChartQuickCommand.DataLabelNumberFormat => "MainWindow_TooltipTitle_LabelNumberFormat",
        ChartQuickCommand.DataLabelCallout => "MainWindow_TooltipTitle_DataCallout",
        ChartQuickCommand.DataLabelFill => "MainWindow_TooltipTitle_DataLabelFill",
        ChartQuickCommand.DataLabelTextColor => "MainWindow_TooltipTitle_DataLabelText",
        ChartQuickCommand.DataLabelBorder => "MainWindow_TooltipTitle_DataLabelBorder",
        ChartQuickCommand.DataLabelFontSize => "MainWindow_TooltipTitle_DataLabelSize",
        ChartQuickCommand.DataLabelAngle => "MainWindow_TooltipTitle_DataLabelAngle",
        ChartQuickCommand.PointDataLabel => "MainWindow_TooltipTitle_FormatDataPointLabel",
        ChartQuickCommand.ChartAreaFill => "MainWindow_TooltipTitle_ChartAreaFill",
        ChartQuickCommand.ChartTitleColor => "MainWindow_TooltipTitle_ChartTitleColor",
        ChartQuickCommand.ChartTitleFontSize => "MainWindow_TooltipTitle_ChartTitleSize",
        ChartQuickCommand.AxisTitleColor => "MainWindow_TooltipTitle_AxisTitleColor",
        ChartQuickCommand.AxisTitleFontSize => "MainWindow_TooltipTitle_AxisTitleSize",
        ChartQuickCommand.PlotAreaFill => "MainWindow_TooltipTitle_PlotAreaFill",
        ChartQuickCommand.PlotAreaBorder => "MainWindow_TooltipTitle_PlotAreaBorder",
        ChartQuickCommand.LegendTextColor => "MainWindow_TooltipTitle_LegendText",
        ChartQuickCommand.LegendFill => "MainWindow_TooltipTitle_LegendFill",
        ChartQuickCommand.LegendBorder => "MainWindow_TooltipTitle_LegendBorder",
        ChartQuickCommand.LegendFontSize => "MainWindow_TooltipTitle_LegendFontSize",
        ChartQuickCommand.LegendOverlay => "MainWindow_TooltipTitle_LegendOverlay",
        ChartQuickCommand.TrendlineMovingAveragePeriod => "MainWindow_TooltipTitle_MovingAveragePeriod",
        ChartQuickCommand.TrendlinePolynomialOrder => "MainWindow_TooltipTitle_PolynomialOrder",
        ChartQuickCommand.TrendlineEquation => "MainWindow_TooltipTitle_TrendlineEquation",
        ChartQuickCommand.TrendlineRSquared => "MainWindow_TooltipTitle_RSquared",
        ChartQuickCommand.TrendlineColor => "MainWindow_TooltipTitle_TrendlineColor",
        ChartQuickCommand.TrendlineDash => "MainWindow_TooltipTitle_TrendlineDash",
        ChartQuickCommand.TrendlineThickness => "MainWindow_TooltipTitle_TrendlineWidth",
        ChartQuickCommand.SecondaryAxisSeries => "MainWindow_TooltipTitle_SecondaryAxisSeries",
        ChartQuickCommand.ComboToggle => "MainWindow_TooltipTitle_ComboChart",
        ChartQuickCommand.ComboSeries => "MainWindow_TooltipTitle_ComboChartSeries",
        ChartQuickCommand.SeriesWidth => "MainWindow_TooltipTitle_SeriesWidth",
        ChartQuickCommand.SeriesDash => "MainWindow_TooltipTitle_SeriesDash",
        ChartQuickCommand.SeriesMarkerSize => "MainWindow_TooltipTitle_MarkerSize",
        _ => throw new ArgumentOutOfRangeException(nameof(Command), Command, null),
    };
}

/// <summary>
/// Shared labels and host resource keys for chart contextual-tab quick commands. Shells still own
/// selection, localization lookup, and command execution; this catalog keeps quick-command descriptors
/// beside the planner that owns their support gates and layout deltas.
/// </summary>
public static class ChartQuickCommandCatalog
{
    public const string DataLabelOptionsHostMissingSelectionMessageResourceKey =
        "MainWindowMessage_ChartSelectForDataLabelOptions";
    public const string ChartAreaFormattingHostMissingSelectionMessageResourceKey =
        "MainWindowMessage_ChartSelectForChartAreaFormatting";
    public const string TrendlineInformationHostMissingSelectionMessageResourceKey =
        "MainWindowMessage_ChartSelectForTrendlineInformation";
    public const string SeriesFormattingHostMissingSelectionMessageResourceKey =
        "MainWindowMessage_ChartSelectForSeriesFormatting";

    public static readonly ChartQuickCommandDescriptor FirstSliceAngle = new(
        ChartQuickCommand.FirstSliceAngle,
        "First Slice Angle",
        "MainWindowMessage_ChartSelectPieDoughnutForFirstSliceAngle",
        "MainWindowMessage_ChartFirstSliceAngleUnsupported");

    public static readonly ChartQuickCommandDescriptor DoughnutHoleSize = new(
        ChartQuickCommand.DoughnutHoleSize,
        "Doughnut Hole Size",
        "MainWindowMessage_ChartSelectDoughnutForHoleSize",
        "MainWindowMessage_ChartDoughnutHoleSizeUnsupported");

    public static readonly ChartQuickCommandDescriptor ExplodedSlice = new(
        ChartQuickCommand.ExplodedSlice,
        "Explode Slice",
        "MainWindowMessage_ChartSelectPieDoughnutForExplode",
        "MainWindowMessage_ChartExplodedSliceUnsupported");

    public static readonly ChartQuickCommandDescriptor DataLabelCategoryName = new(
        ChartQuickCommand.DataLabelCategoryName,
        "Category Name",
        DataLabelOptionsHostMissingSelectionMessageResourceKey);

    public static readonly ChartQuickCommandDescriptor DataLabelSeriesName = new(
        ChartQuickCommand.DataLabelSeriesName,
        "Series Name",
        DataLabelOptionsHostMissingSelectionMessageResourceKey);

    public static readonly ChartQuickCommandDescriptor DataLabelPercentage = new(
        ChartQuickCommand.DataLabelPercentage,
        "Percentage",
        DataLabelOptionsHostMissingSelectionMessageResourceKey);

    public static readonly ChartQuickCommandDescriptor DataLabelSeparator = new(
        ChartQuickCommand.DataLabelSeparator,
        "Label Separator",
        DataLabelOptionsHostMissingSelectionMessageResourceKey);

    public static readonly ChartQuickCommandDescriptor DataLabelNumberFormat = new(
        ChartQuickCommand.DataLabelNumberFormat,
        "Label Number Format",
        DataLabelOptionsHostMissingSelectionMessageResourceKey);

    public static readonly ChartQuickCommandDescriptor DataLabelCallout = new(
        ChartQuickCommand.DataLabelCallout,
        "Data Callout",
        DataLabelOptionsHostMissingSelectionMessageResourceKey);

    public static readonly ChartQuickCommandDescriptor DataLabelFill = new(
        ChartQuickCommand.DataLabelFill,
        "Data Label Fill",
        DataLabelOptionsHostMissingSelectionMessageResourceKey);

    public static readonly ChartQuickCommandDescriptor DataLabelTextColor = new(
        ChartQuickCommand.DataLabelTextColor,
        "Data Label Text",
        DataLabelOptionsHostMissingSelectionMessageResourceKey);

    public static readonly ChartQuickCommandDescriptor DataLabelBorder = new(
        ChartQuickCommand.DataLabelBorder,
        "Data Label Border",
        DataLabelOptionsHostMissingSelectionMessageResourceKey);

    public static readonly ChartQuickCommandDescriptor DataLabelFontSize = new(
        ChartQuickCommand.DataLabelFontSize,
        "Data Label Size",
        DataLabelOptionsHostMissingSelectionMessageResourceKey);

    public static readonly ChartQuickCommandDescriptor DataLabelAngle = new(
        ChartQuickCommand.DataLabelAngle,
        "Data Label Angle",
        DataLabelOptionsHostMissingSelectionMessageResourceKey);

    public static readonly ChartQuickCommandDescriptor PointDataLabel = new(
        ChartQuickCommand.PointDataLabel,
        "Format Data Point Label",
        "MainWindowMessage_ChartSelectForPointDataLabel",
        "MainWindowMessage_ChartPointDataLabelNeedsDataPoints");

    public static readonly ChartQuickCommandDescriptor ChartAreaFill = new(
        ChartQuickCommand.ChartAreaFill,
        "Chart Area Fill",
        ChartAreaFormattingHostMissingSelectionMessageResourceKey);

    public static readonly ChartQuickCommandDescriptor ChartTitleColor = new(
        ChartQuickCommand.ChartTitleColor,
        "Chart Title Color",
        ChartAreaFormattingHostMissingSelectionMessageResourceKey);

    public static readonly ChartQuickCommandDescriptor ChartTitleFontSize = new(
        ChartQuickCommand.ChartTitleFontSize,
        "Chart Title Size",
        ChartAreaFormattingHostMissingSelectionMessageResourceKey);

    public static readonly ChartQuickCommandDescriptor AxisTitleColor = new(
        ChartQuickCommand.AxisTitleColor,
        "Axis Title Color",
        ChartAreaFormattingHostMissingSelectionMessageResourceKey);

    public static readonly ChartQuickCommandDescriptor AxisTitleFontSize = new(
        ChartQuickCommand.AxisTitleFontSize,
        "Axis Title Size",
        ChartAreaFormattingHostMissingSelectionMessageResourceKey);

    public static readonly ChartQuickCommandDescriptor PlotAreaFill = new(
        ChartQuickCommand.PlotAreaFill,
        "Plot Area Fill",
        ChartAreaFormattingHostMissingSelectionMessageResourceKey);

    public static readonly ChartQuickCommandDescriptor PlotAreaBorder = new(
        ChartQuickCommand.PlotAreaBorder,
        "Plot Area Border",
        ChartAreaFormattingHostMissingSelectionMessageResourceKey);

    public static readonly ChartQuickCommandDescriptor LegendTextColor = new(
        ChartQuickCommand.LegendTextColor,
        "Legend Text",
        ChartAreaFormattingHostMissingSelectionMessageResourceKey);

    public static readonly ChartQuickCommandDescriptor LegendFill = new(
        ChartQuickCommand.LegendFill,
        "Legend Fill",
        ChartAreaFormattingHostMissingSelectionMessageResourceKey);

    public static readonly ChartQuickCommandDescriptor LegendBorder = new(
        ChartQuickCommand.LegendBorder,
        "Legend Border",
        ChartAreaFormattingHostMissingSelectionMessageResourceKey);

    public static readonly ChartQuickCommandDescriptor LegendFontSize = new(
        ChartQuickCommand.LegendFontSize,
        "Legend Font Size",
        ChartAreaFormattingHostMissingSelectionMessageResourceKey);

    public static readonly ChartQuickCommandDescriptor LegendOverlay = new(
        ChartQuickCommand.LegendOverlay,
        "Legend Overlay",
        ChartAreaFormattingHostMissingSelectionMessageResourceKey);

    public static readonly ChartQuickCommandDescriptor TrendlineMovingAveragePeriod = new(
        ChartQuickCommand.TrendlineMovingAveragePeriod,
        "Moving Average Period",
        "MainWindowMessage_ChartSelectForMovingAveragePeriod",
        "MainWindowMessage_ChartTrendlinesSupportedTypes");

    public static readonly ChartQuickCommandDescriptor TrendlinePolynomialOrder = new(
        ChartQuickCommand.TrendlinePolynomialOrder,
        "Polynomial Order",
        "MainWindowMessage_ChartSelectForPolynomialOrder",
        "MainWindowMessage_ChartTrendlinesSupportedTypes");

    public static readonly ChartQuickCommandDescriptor TrendlineEquation = new(
        ChartQuickCommand.TrendlineEquation,
        "Trendline Equation",
        TrendlineInformationHostMissingSelectionMessageResourceKey,
        "MainWindowMessage_ChartTrendlineInformationSupportedTypes");

    public static readonly ChartQuickCommandDescriptor TrendlineRSquared = new(
        ChartQuickCommand.TrendlineRSquared,
        "R-squared",
        TrendlineInformationHostMissingSelectionMessageResourceKey,
        "MainWindowMessage_ChartTrendlineInformationSupportedTypes");

    public static readonly ChartQuickCommandDescriptor TrendlineColor = new(
        ChartQuickCommand.TrendlineColor,
        "Trendline Color",
        TrendlineInformationHostMissingSelectionMessageResourceKey,
        "MainWindowMessage_ChartTrendlineInformationSupportedTypes");

    public static readonly ChartQuickCommandDescriptor TrendlineDash = new(
        ChartQuickCommand.TrendlineDash,
        "Trendline Dash",
        TrendlineInformationHostMissingSelectionMessageResourceKey,
        "MainWindowMessage_ChartTrendlineInformationSupportedTypes");

    public static readonly ChartQuickCommandDescriptor TrendlineThickness = new(
        ChartQuickCommand.TrendlineThickness,
        "Trendline Width",
        TrendlineInformationHostMissingSelectionMessageResourceKey,
        "MainWindowMessage_ChartTrendlineInformationSupportedTypes");

    public static readonly ChartQuickCommandDescriptor SecondaryAxisSeries = new(
        ChartQuickCommand.SecondaryAxisSeries,
        "Secondary Axis Series",
        "MainWindowMessage_ChartSelectForSecondaryAxisSeries",
        "MainWindowMessage_ChartSecondaryAxisUnsupported");

    public static readonly ChartQuickCommandDescriptor ComboToggle = new(
        ChartQuickCommand.ComboToggle,
        "Combo Chart",
        "MainWindowMessage_ChartSelectForComboOptions",
        "MainWindowMessage_ChartComboUnsupported");

    public static readonly ChartQuickCommandDescriptor ComboSeries = new(
        ChartQuickCommand.ComboSeries,
        "Combo Chart Series",
        "MainWindowMessage_ChartSelectForComboSeries",
        "MainWindowMessage_ChartComboUnsupported",
        "ChartLoc_ComboChartsNeed");

    public static readonly ChartQuickCommandDescriptor SeriesWidth = new(
        ChartQuickCommand.SeriesWidth,
        "Series Width",
        SeriesFormattingHostMissingSelectionMessageResourceKey,
        "MainWindowMessage_ChartSeriesFormattingNeedsDataSeries");

    public static readonly ChartQuickCommandDescriptor SeriesDash = new(
        ChartQuickCommand.SeriesDash,
        "Series Dash",
        SeriesFormattingHostMissingSelectionMessageResourceKey,
        "MainWindowMessage_ChartSeriesFormattingNeedsDataSeries",
        "ChartLoc_NoDataSeriesToFormat");

    public static readonly ChartQuickCommandDescriptor SeriesMarkerSize = new(
        ChartQuickCommand.SeriesMarkerSize,
        "Marker Size",
        SeriesFormattingHostMissingSelectionMessageResourceKey,
        "MainWindowMessage_ChartSeriesMarkersSupportedTypes",
        "ChartLoc_MarkersAvailableOn");

    private static readonly ChartQuickCommandDescriptor[] Commands =
    [
        FirstSliceAngle,
        DoughnutHoleSize,
        ExplodedSlice,
        DataLabelCategoryName,
        DataLabelSeriesName,
        DataLabelPercentage,
        DataLabelSeparator,
        DataLabelNumberFormat,
        DataLabelCallout,
        DataLabelFill,
        DataLabelTextColor,
        DataLabelBorder,
        DataLabelFontSize,
        DataLabelAngle,
        PointDataLabel,
        ChartAreaFill,
        ChartTitleColor,
        ChartTitleFontSize,
        AxisTitleColor,
        AxisTitleFontSize,
        PlotAreaFill,
        PlotAreaBorder,
        LegendTextColor,
        LegendFill,
        LegendBorder,
        LegendFontSize,
        LegendOverlay,
        TrendlineMovingAveragePeriod,
        TrendlinePolynomialOrder,
        TrendlineEquation,
        TrendlineRSquared,
        TrendlineColor,
        TrendlineDash,
        TrendlineThickness,
        SecondaryAxisSeries,
        ComboToggle,
        ComboSeries,
        SeriesWidth,
        SeriesDash,
        SeriesMarkerSize,
    ];

    public static IReadOnlyList<ChartQuickCommandDescriptor> All => Commands;

    public static ChartQuickCommandDescriptor Get(ChartQuickCommand command)
        => WorkflowCommandCatalogPolicy.GetById(Commands, command, descriptor => descriptor.Command);
}
