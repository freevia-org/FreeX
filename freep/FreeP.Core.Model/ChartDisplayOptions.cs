namespace FreeP.Core.Model;

/// <summary>
/// User-facing chart display options edited together by the PowerPoint-style chart
/// options dialog. The command layer keeps this value independent of either host UI.
/// </summary>
public sealed record ChartDisplayOptions(
    string? Title,
    LegendPosition? Legend,
    bool ShowValueLabels,
    DataLabelPosition LabelPosition,
    bool CategoryGridlines,
    bool ValueGridlines,
    bool ShowPercentLabels = false,
    bool ShowCategoryLabels = false,
    bool ShowSeriesLabels = false,
    bool ShowLegendKeys = false,
    string? LabelNumberFormat = null,
    string? LabelSeparator = null,
    int? BarGapWidthPercent = null,
    int? BarOverlapPercent = null,
    ChartDisplayBlanksAs? DisplayBlanksAs = null,
    bool? ShowDataLabelsOverMaximum = null,
    bool? VaryColors = null,
    bool? LegendOverlay = null,
    bool? HighLowLines = null,
    ChartTextStyle? LabelTextStyle = null,
    bool ShowBubbleSize = false,
    int? StyleId = null,
    bool? ShowLeaderLines = null,
    bool? TitleOverlay = null,
    bool? PlotVisibleOnly = null,
    bool? RoundedCorners = null,
    bool? ShowWaterfallConnectorLines = null,
    bool? ShowDropLines = null,
    bool? ShowUpDownBars = null);
