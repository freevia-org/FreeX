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
    int? BarOverlapPercent = null);
