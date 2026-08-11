using Free.Shared.AppServices;
using Free.Shared.Localization;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

public enum ChartWorkflowCommandId
{
    ChangeChartType,
    SelectDataSource,
    MoveChart,
    FormatChartArea,
    ChartTitles,
    FormatBarColumn,
    FormatBubbleChart,
    FormatPieDoughnut,
    FormatStockChart,
    FormatDataLabels,
    FormatTrendline,
    FormatErrorBars,
    FormatDataSeries,
    ComboChart,
    SecondaryAxis,
}

public sealed record ChartWorkflowCommandDescriptor(
    ChartWorkflowCommandId Id,
    string Label,
    string HostMissingSelectionMessageResourceKey,
    string? TitleResourceKey = null,
    string? HostUnsupportedMessageResourceKey = null,
    string? UnsupportedStatusResourceKey = null);

/// <summary>
/// Shared labels, resource keys, and support gates for chart contextual command workflows. Platform
/// renderers still own the dialogs and command execution; this catalog keeps cross-platform chart action
/// text and command availability policy in one place.
/// </summary>
public static class ChartWorkflowCommandCatalog
{
    public const string DefaultHostMissingSelectionMessageResourceKey = "MainWindowMessage_ChartSelectBeforeCommand";
    public const string SelectChartBeforeUsingStatusResourceKey = "ChartLoc_SelectChartBeforeUsing";
    public const string CommandAppliedStatusResourceKey = "ChartLoc_CommandApplied";
    public const string CommandFailedStatusResourceKey = "ChartLoc_CommandFailed";
    public const string CommandNotYetAvailableStatusResourceKey = "ChartLoc_CommandNotYetAvailable";

    public static readonly ChartWorkflowCommandDescriptor ChangeChartType = new(
        ChartWorkflowCommandId.ChangeChartType,
        "Change Chart Type",
        DefaultHostMissingSelectionMessageResourceKey);

    public static readonly ChartWorkflowCommandDescriptor SelectDataSource = new(
        ChartWorkflowCommandId.SelectDataSource,
        "Select Data Source",
        DefaultHostMissingSelectionMessageResourceKey);

    public static readonly ChartWorkflowCommandDescriptor MoveChart = new(
        ChartWorkflowCommandId.MoveChart,
        "Move Chart",
        DefaultHostMissingSelectionMessageResourceKey);

    public static readonly ChartWorkflowCommandDescriptor FormatChartArea = new(
        ChartWorkflowCommandId.FormatChartArea,
        "Format Chart Area",
        "MainWindowMessage_ChartSelectForChartAreaFormatting");

    public static readonly ChartWorkflowCommandDescriptor ChartTitles = new(
        ChartWorkflowCommandId.ChartTitles,
        "Chart Titles",
        "MainWindowMessage_ChartSelectForTitles");

    public static readonly ChartWorkflowCommandDescriptor FormatBarColumn = new(
        ChartWorkflowCommandId.FormatBarColumn,
        "Format Bar/Column",
        "MainWindowMessage_ChartSelectBarColumnForGapWidth",
        ChartBarFormatPlanner.TitleResourceKey,
        "MainWindowMessage_ChartGapWidthUnsupported",
        "ChartLoc_GapWidthOverlapAvailableOn");

    public static readonly ChartWorkflowCommandDescriptor FormatBubbleChart = new(
        ChartWorkflowCommandId.FormatBubbleChart,
        "Format Bubble Chart",
        "MainWindowMessage_ChartSelectBubbleForOptions",
        ChartBubbleFormatPlanner.TitleResourceKey,
        "MainWindowMessage_ChartBubbleOptionsUnsupported",
        "ChartLoc_OptionsAvailableBubble");

    public static readonly ChartWorkflowCommandDescriptor FormatPieDoughnut = new(
        ChartWorkflowCommandId.FormatPieDoughnut,
        "Format Pie/Doughnut",
        "MainWindowMessage_ChartSelectPieDoughnutForOptions",
        ChartPieFormatPlanner.TitleResourceKey,
        "MainWindowMessage_ChartPieOptionsUnsupported",
        "ChartLoc_OptionsAvailablePieDoughnut");

    public static readonly ChartWorkflowCommandDescriptor FormatStockChart = new(
        ChartWorkflowCommandId.FormatStockChart,
        "Format Stock Chart",
        "MainWindowMessage_ChartSelectStockForOptions",
        ChartStockFormatPlanner.TitleResourceKey,
        "MainWindowMessage_ChartStockOptionsUnsupported",
        "ChartLoc_OptionsAvailableStock");

    public static readonly ChartWorkflowCommandDescriptor FormatDataLabels = new(
        ChartWorkflowCommandId.FormatDataLabels,
        "Format Data Labels",
        "MainWindowMessage_ChartSelectForDataLabels",
        "ChartDataLabels_Title");

    public static readonly ChartWorkflowCommandDescriptor FormatTrendline = new(
        ChartWorkflowCommandId.FormatTrendline,
        "Format Trendline",
        "MainWindowMessage_ChartSelectForTrendlines",
        "ChartTrendline_Title",
        "MainWindowMessage_ChartTrendlinesSupportedTypes",
        "ChartLoc_TrendlinesAvailableOn");

    public static readonly ChartWorkflowCommandDescriptor FormatErrorBars = new(
        ChartWorkflowCommandId.FormatErrorBars,
        "Format Error Bars",
        "MainWindowMessage_ChartSelectForErrorBars",
        "ChartErrorBars_Title",
        "MainWindowMessage_ChartTrendlinesSupportedTypes",
        "ChartLoc_ErrorBarsAvailableOn");

    public static readonly ChartWorkflowCommandDescriptor FormatDataSeries = new(
        ChartWorkflowCommandId.FormatDataSeries,
        "Format Data Series",
        "MainWindowMessage_ChartSelectForSeriesFormatting",
        "ChartSeriesFormat_Title",
        "MainWindowMessage_ChartSeriesFormattingNeedsDataSeries",
        "ChartLoc_NoDataSeriesToFormat");

    public static readonly ChartWorkflowCommandDescriptor ComboChart = new(
        ChartWorkflowCommandId.ComboChart,
        "Combo Chart",
        "MainWindowMessage_ChartSelectForComboOptions",
        HostUnsupportedMessageResourceKey: "MainWindowMessage_ChartComboUnsupported",
        UnsupportedStatusResourceKey: "ChartLoc_ComboChartsNeed");

    public static readonly ChartWorkflowCommandDescriptor SecondaryAxis = new(
        ChartWorkflowCommandId.SecondaryAxis,
        "Secondary Axis",
        "MainWindowMessage_ChartSecondaryAxisRequiresChart",
        HostUnsupportedMessageResourceKey: "MainWindowMessage_ChartSecondaryAxisUnsupported",
        UnsupportedStatusResourceKey: "ChartLoc_SecondaryAxisNeeds");

    private static readonly ChartWorkflowCommandDescriptor[] Commands =
    [
        ChangeChartType,
        SelectDataSource,
        MoveChart,
        FormatChartArea,
        ChartTitles,
        FormatBarColumn,
        FormatBubbleChart,
        FormatPieDoughnut,
        FormatStockChart,
        FormatDataLabels,
        FormatTrendline,
        FormatErrorBars,
        FormatDataSeries,
        ComboChart,
        SecondaryAxis,
    ];

    public static IReadOnlyList<ChartWorkflowCommandDescriptor> All => Commands;

    public static ChartWorkflowCommandDescriptor Get(ChartWorkflowCommandId id)
        => WorkflowCommandCatalogPolicy.GetById(Commands, id, command => command.Id);

    public static LocalizedTextDescriptor DescribeCommandResult(
        bool success,
        string commandLabel,
        string? errorMessage = null) =>
        success
            ? LocalizedTextDescriptor.Resource(CommandAppliedStatusResourceKey, commandLabel)
            : errorMessage is null
                ? LocalizedTextDescriptor.Resource(CommandFailedStatusResourceKey, commandLabel)
                : LocalizedTextDescriptor.Literal(errorMessage);

    public static bool CanOpenDialog(ChartModel chart, ChartWorkflowCommandDescriptor command)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(command);

        return command.Id switch
        {
            ChartWorkflowCommandId.FormatBarColumn => ChartBarFormatPlanner.Supports(chart),
            ChartWorkflowCommandId.FormatBubbleChart => ChartBubbleFormatPlanner.Supports(chart),
            ChartWorkflowCommandId.FormatPieDoughnut => ChartPieFormatPlanner.Supports(chart),
            ChartWorkflowCommandId.FormatStockChart => ChartStockFormatPlanner.Supports(chart),
            ChartWorkflowCommandId.FormatTrendline => ChartTrendlinePlanner.SupportsTrendlines(chart.Type),
            ChartWorkflowCommandId.FormatErrorBars => ChartErrorBarsPlanner.SupportsErrorBars(chart.Type),
            ChartWorkflowCommandId.FormatDataSeries => ChartSeriesFormatPlanner.HasDataSeries(chart),
            ChartWorkflowCommandId.ComboChart => ChartComboPlanner.SupportsCombo(chart),
            ChartWorkflowCommandId.SecondaryAxis => ChartAxisPlanner.CanToggleSecondaryAxis(chart),
            _ => true,
        };
    }
}
