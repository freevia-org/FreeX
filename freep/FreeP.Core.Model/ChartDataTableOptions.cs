namespace FreeP.Core.Model;

/// <summary>Editable visibility and border options for a chart data table.</summary>
public sealed record ChartDataTableOptions(
    bool ShowDataTable,
    bool ShowHorizontalBorder,
    bool ShowVerticalBorder,
    bool ShowOutlineBorder,
    bool ShowLegendKeys);
