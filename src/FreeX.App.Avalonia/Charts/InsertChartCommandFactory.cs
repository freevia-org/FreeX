using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Charts;

/// <summary>
/// Avalonia compatibility wrapper over the shared chart insertion planner. Kept portable, with no
/// Avalonia types, so ribbon-id mapping, resolved data range, chart type, and default placement stay unit
/// testable without a running shell.
/// </summary>
public static class InsertChartCommandFactory
{
    /// <summary>Default on-sheet placement for a freshly inserted chart, matching Core defaults.</summary>
    public const double DefaultLeft = ChartInsertionPlanner.DefaultLeft;
    public const double DefaultTop = ChartInsertionPlanner.DefaultTop;
    public const double DefaultWidth = ChartInsertionPlanner.DefaultChartWidth;
    public const double DefaultHeight = ChartInsertionPlanner.DefaultChartHeight;

    /// <summary>
    /// The chart type a ribbon Insert chart-type button/menu id maps to, or <c>null</c> when the id is not
    /// a wired chart control.
    /// </summary>
    public static ChartType? ChartTypeForRibbonCommand(string commandId) =>
        ChartCommandWorkflowPlanner.ChartTypeForRibbonCommand(commandId);

    /// <summary>
    /// Builds a default-placement chart insertion command for callers that already resolved the data range.
    /// </summary>
    public static AddChartCommand Build(SheetId sheetId, GridRange selection, ChartType chartType) =>
        ChartCommandWorkflowPlanner.BuildEmbeddedChartCommand(
            sheetId,
            selection,
            chartType,
            title: null,
            ChartInsertionPlanner.DefaultPlacement);

    /// <summary>
    /// Builds a default-placement chart insertion command after resolving single-cell selections through the
    /// shared current-region/table rules.
    /// </summary>
    public static AddChartCommand Build(Sheet sheet, GridRange selection, ChartType chartType) =>
        ChartCommandWorkflowPlanner.BuildEmbeddedChartCommand(sheet, selection, chartType);
}
