namespace FreeP.Core.Model;

/// <summary>Editable visibility, border, fill, and text options for a chart data table.</summary>
public sealed record ChartDataTableOptions(
    bool ShowDataTable,
    bool ShowHorizontalBorder,
    bool ShowVerticalBorder,
    bool ShowOutlineBorder,
    bool ShowLegendKeys,
    string? BackgroundColor = null,
    string? BorderColor = null,
    double? BorderWidthPt = null,
    string? TextColor = null,
    double? FontSizePt = null,
    string? FontFamily = null,
    bool? Bold = null,
    bool? Italic = null);
