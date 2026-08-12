using Free.Shared.AppServices;

namespace FreeX.App.Presentation.Charts.Editing;

public enum ChartAxisWorkflowCommandId
{
    XAxisFormat,
    YAxisFormat,
    XAxisTickMarks,
    YAxisTickMarks,
    XAxisLabels,
    YAxisLabels,
    XAxisLabelFont,
    YAxisLabelFont,
    XAxisLabelAngle,
    YAxisLabelAngle,
    XAxisLine,
    YAxisLine,
    XAxisGridlines,
    YAxisGridlines,
    XAxisGridlineStyle,
    YAxisGridlineStyle,
    XAxisNumberFormat,
    YAxisNumberFormat,
    XAxisLogScale,
    YAxisLogScale,
    XAxisBounds,
    YAxisBounds,
}

public sealed record ChartAxisWorkflowCommandDescriptor(
    ChartAxisWorkflowCommandId Id,
    bool UseXAxis,
    string Label,
    string HostMissingSelectionMessageResourceKey,
    ChartAxisQuickCommand? QuickCommand = null)
{
    public string TitleResourceKey => Id switch
    {
        ChartAxisWorkflowCommandId.XAxisFormat => "ChartAxisFormat_XAxisTitle",
        ChartAxisWorkflowCommandId.YAxisFormat => "ChartAxisFormat_YAxisTitle",
        ChartAxisWorkflowCommandId.XAxisTickMarks => "MainWindow_TooltipTitle_XAxisTicks",
        ChartAxisWorkflowCommandId.YAxisTickMarks => "MainWindow_TooltipTitle_YAxisTicks",
        ChartAxisWorkflowCommandId.XAxisLabels => "MainWindow_TooltipTitle_XAxisLabels",
        ChartAxisWorkflowCommandId.YAxisLabels => "MainWindow_TooltipTitle_YAxisLabels",
        ChartAxisWorkflowCommandId.XAxisLabelFont => "MainWindow_TooltipTitle_XAxisLabelFont",
        ChartAxisWorkflowCommandId.YAxisLabelFont => "MainWindow_TooltipTitle_YAxisLabelFont",
        ChartAxisWorkflowCommandId.XAxisLabelAngle => "MainWindow_TooltipTitle_XAxisLabelAngle",
        ChartAxisWorkflowCommandId.YAxisLabelAngle => "MainWindow_TooltipTitle_YAxisLabelAngle",
        ChartAxisWorkflowCommandId.XAxisLine => "MainWindow_TooltipTitle_XAxisLine",
        ChartAxisWorkflowCommandId.YAxisLine => "MainWindow_TooltipTitle_YAxisLine",
        ChartAxisWorkflowCommandId.XAxisGridlines => "MainWindow_TooltipTitle_XAxisGridlines",
        ChartAxisWorkflowCommandId.YAxisGridlines => "MainWindow_TooltipTitle_YAxisGridlines",
        ChartAxisWorkflowCommandId.XAxisGridlineStyle => "MainWindow_TooltipTitle_XGridlineStyle",
        ChartAxisWorkflowCommandId.YAxisGridlineStyle => "MainWindow_TooltipTitle_YGridlineStyle",
        ChartAxisWorkflowCommandId.XAxisNumberFormat => "MainWindow_TooltipTitle_XAxisNumberFormat",
        ChartAxisWorkflowCommandId.YAxisNumberFormat => "MainWindow_TooltipTitle_YAxisNumberFormat",
        ChartAxisWorkflowCommandId.XAxisLogScale => "MainWindow_TooltipTitle_XLogScale",
        ChartAxisWorkflowCommandId.YAxisLogScale => "MainWindow_TooltipTitle_YLogScale",
        ChartAxisWorkflowCommandId.XAxisBounds => "MainWindow_TooltipTitle_XAxisBounds",
        ChartAxisWorkflowCommandId.YAxisBounds => "MainWindow_TooltipTitle_YAxisBounds",
        _ => throw new ArgumentOutOfRangeException(nameof(Id), Id, null),
    };
}

/// <summary>
/// Shared descriptors for chart axis contextual commands. The axis planner owns the layout deltas; this
/// catalog owns command labels and missing-chart message keys so renderer dispatch tables stay thin.
/// </summary>
public static class ChartAxisWorkflowCommandCatalog
{
    public static readonly ChartAxisWorkflowCommandDescriptor XAxisFormat = new(
        ChartAxisWorkflowCommandId.XAxisFormat,
        UseXAxis: true,
        "Format X Axis",
        "MainWindowMessage_ChartAxisOptionsRequiresChart");

    public static readonly ChartAxisWorkflowCommandDescriptor YAxisFormat = new(
        ChartAxisWorkflowCommandId.YAxisFormat,
        UseXAxis: false,
        "Format Y Axis",
        "MainWindowMessage_ChartAxisOptionsRequiresChart");

    public static readonly ChartAxisWorkflowCommandDescriptor XAxisTickMarks = new(
        ChartAxisWorkflowCommandId.XAxisTickMarks,
        UseXAxis: true,
        "X Axis Ticks",
        "MainWindowMessage_ChartAxisTicksRequiresChart",
        ChartAxisQuickCommand.TickMarks);

    public static readonly ChartAxisWorkflowCommandDescriptor YAxisTickMarks = new(
        ChartAxisWorkflowCommandId.YAxisTickMarks,
        UseXAxis: false,
        "Y Axis Ticks",
        "MainWindowMessage_ChartAxisTicksRequiresChart",
        ChartAxisQuickCommand.TickMarks);

    public static readonly ChartAxisWorkflowCommandDescriptor XAxisLabels = new(
        ChartAxisWorkflowCommandId.XAxisLabels,
        UseXAxis: true,
        "X Axis Labels",
        "MainWindowMessage_ChartAxisLabelsRequiresChart",
        ChartAxisQuickCommand.Labels);

    public static readonly ChartAxisWorkflowCommandDescriptor YAxisLabels = new(
        ChartAxisWorkflowCommandId.YAxisLabels,
        UseXAxis: false,
        "Y Axis Labels",
        "MainWindowMessage_ChartAxisLabelsRequiresChart",
        ChartAxisQuickCommand.Labels);

    public static readonly ChartAxisWorkflowCommandDescriptor XAxisLabelFont = new(
        ChartAxisWorkflowCommandId.XAxisLabelFont,
        UseXAxis: true,
        "X Axis Label Font",
        "MainWindowMessage_ChartAxisLabelFormattingRequiresChart",
        ChartAxisQuickCommand.LabelFont);

    public static readonly ChartAxisWorkflowCommandDescriptor YAxisLabelFont = new(
        ChartAxisWorkflowCommandId.YAxisLabelFont,
        UseXAxis: false,
        "Y Axis Label Font",
        "MainWindowMessage_ChartAxisLabelFormattingRequiresChart",
        ChartAxisQuickCommand.LabelFont);

    public static readonly ChartAxisWorkflowCommandDescriptor XAxisLabelAngle = new(
        ChartAxisWorkflowCommandId.XAxisLabelAngle,
        UseXAxis: true,
        "X Axis Label Angle",
        "MainWindowMessage_ChartAxisLabelRotationRequiresChart",
        ChartAxisQuickCommand.LabelAngle);

    public static readonly ChartAxisWorkflowCommandDescriptor YAxisLabelAngle = new(
        ChartAxisWorkflowCommandId.YAxisLabelAngle,
        UseXAxis: false,
        "Y Axis Label Angle",
        "MainWindowMessage_ChartAxisLabelRotationRequiresChart",
        ChartAxisQuickCommand.LabelAngle);

    public static readonly ChartAxisWorkflowCommandDescriptor XAxisLine = new(
        ChartAxisWorkflowCommandId.XAxisLine,
        UseXAxis: true,
        "X Axis Line",
        "MainWindowMessage_ChartAxisLineFormattingRequiresChart",
        ChartAxisQuickCommand.AxisLine);

    public static readonly ChartAxisWorkflowCommandDescriptor YAxisLine = new(
        ChartAxisWorkflowCommandId.YAxisLine,
        UseXAxis: false,
        "Y Axis Line",
        "MainWindowMessage_ChartAxisLineFormattingRequiresChart",
        ChartAxisQuickCommand.AxisLine);

    public static readonly ChartAxisWorkflowCommandDescriptor XAxisGridlines = new(
        ChartAxisWorkflowCommandId.XAxisGridlines,
        UseXAxis: true,
        "X Axis Gridlines",
        "MainWindowMessage_ChartAxisGridlinesRequiresChart",
        ChartAxisQuickCommand.Gridlines);

    public static readonly ChartAxisWorkflowCommandDescriptor YAxisGridlines = new(
        ChartAxisWorkflowCommandId.YAxisGridlines,
        UseXAxis: false,
        "Y Axis Gridlines",
        "MainWindowMessage_ChartAxisGridlinesRequiresChart",
        ChartAxisQuickCommand.Gridlines);

    public static readonly ChartAxisWorkflowCommandDescriptor XAxisGridlineStyle = new(
        ChartAxisWorkflowCommandId.XAxisGridlineStyle,
        UseXAxis: true,
        "X Gridline Style",
        "MainWindowMessage_ChartGridlineFormattingRequiresChart",
        ChartAxisQuickCommand.GridlineStyle);

    public static readonly ChartAxisWorkflowCommandDescriptor YAxisGridlineStyle = new(
        ChartAxisWorkflowCommandId.YAxisGridlineStyle,
        UseXAxis: false,
        "Y Gridline Style",
        "MainWindowMessage_ChartGridlineFormattingRequiresChart",
        ChartAxisQuickCommand.GridlineStyle);

    public static readonly ChartAxisWorkflowCommandDescriptor XAxisNumberFormat = new(
        ChartAxisWorkflowCommandId.XAxisNumberFormat,
        UseXAxis: true,
        "X Axis Number Format",
        "MainWindowMessage_ChartAxisNumberFormatRequiresChart",
        ChartAxisQuickCommand.NumberFormat);

    public static readonly ChartAxisWorkflowCommandDescriptor YAxisNumberFormat = new(
        ChartAxisWorkflowCommandId.YAxisNumberFormat,
        UseXAxis: false,
        "Y Axis Number Format",
        "MainWindowMessage_ChartAxisNumberFormatRequiresChart",
        ChartAxisQuickCommand.NumberFormat);

    public static readonly ChartAxisWorkflowCommandDescriptor XAxisLogScale = new(
        ChartAxisWorkflowCommandId.XAxisLogScale,
        UseXAxis: true,
        "X Log Scale",
        "MainWindowMessage_ChartAxisScaleRequiresChart");

    public static readonly ChartAxisWorkflowCommandDescriptor YAxisLogScale = new(
        ChartAxisWorkflowCommandId.YAxisLogScale,
        UseXAxis: false,
        "Y Log Scale",
        "MainWindowMessage_ChartAxisScaleRequiresChart");

    public static readonly ChartAxisWorkflowCommandDescriptor XAxisBounds = new(
        ChartAxisWorkflowCommandId.XAxisBounds,
        UseXAxis: true,
        "X Axis Bounds",
        "MainWindowMessage_ChartAxisBoundsRequiresChart");

    public static readonly ChartAxisWorkflowCommandDescriptor YAxisBounds = new(
        ChartAxisWorkflowCommandId.YAxisBounds,
        UseXAxis: false,
        "Y Axis Bounds",
        "MainWindowMessage_ChartAxisBoundsRequiresChart");

    private static readonly ChartAxisWorkflowCommandDescriptor[] Commands =
    [
        XAxisFormat,
        YAxisFormat,
        XAxisTickMarks,
        YAxisTickMarks,
        XAxisLabels,
        YAxisLabels,
        XAxisLabelFont,
        YAxisLabelFont,
        XAxisLabelAngle,
        YAxisLabelAngle,
        XAxisLine,
        YAxisLine,
        XAxisGridlines,
        YAxisGridlines,
        XAxisGridlineStyle,
        YAxisGridlineStyle,
        XAxisNumberFormat,
        YAxisNumberFormat,
        XAxisLogScale,
        YAxisLogScale,
        XAxisBounds,
        YAxisBounds,
    ];

    public static IReadOnlyList<ChartAxisWorkflowCommandDescriptor> All => Commands;

    public static ChartAxisWorkflowCommandDescriptor FormatAxis(bool useXAxis) =>
        useXAxis ? XAxisFormat : YAxisFormat;

    public static ChartAxisWorkflowCommandDescriptor TickMarks(bool useXAxis) =>
        useXAxis ? XAxisTickMarks : YAxisTickMarks;

    public static ChartAxisWorkflowCommandDescriptor Labels(bool useXAxis) =>
        useXAxis ? XAxisLabels : YAxisLabels;

    public static ChartAxisWorkflowCommandDescriptor LabelFont(bool useXAxis) =>
        useXAxis ? XAxisLabelFont : YAxisLabelFont;

    public static ChartAxisWorkflowCommandDescriptor LabelAngle(bool useXAxis) =>
        useXAxis ? XAxisLabelAngle : YAxisLabelAngle;

    public static ChartAxisWorkflowCommandDescriptor AxisLine(bool useXAxis) =>
        useXAxis ? XAxisLine : YAxisLine;

    public static ChartAxisWorkflowCommandDescriptor Gridlines(bool useXAxis) =>
        useXAxis ? XAxisGridlines : YAxisGridlines;

    public static ChartAxisWorkflowCommandDescriptor GridlineStyle(bool useXAxis) =>
        useXAxis ? XAxisGridlineStyle : YAxisGridlineStyle;

    public static ChartAxisWorkflowCommandDescriptor NumberFormat(bool useXAxis) =>
        useXAxis ? XAxisNumberFormat : YAxisNumberFormat;

    public static ChartAxisWorkflowCommandDescriptor LogScale(bool useXAxis) =>
        useXAxis ? XAxisLogScale : YAxisLogScale;

    public static ChartAxisWorkflowCommandDescriptor Bounds(bool useXAxis) =>
        useXAxis ? XAxisBounds : YAxisBounds;

    public static ChartAxisWorkflowCommandDescriptor Get(ChartAxisWorkflowCommandId id)
        => WorkflowCommandCatalogPolicy.GetById(Commands, id, command => command.Id);
}
