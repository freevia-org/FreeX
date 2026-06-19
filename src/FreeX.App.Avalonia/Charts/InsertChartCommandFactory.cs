using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Charts;

/// <summary>
/// UI-free factory that turns a sheet selection plus a chosen <see cref="ChartType"/> into the Core
/// <see cref="AddChartCommand"/> the shell executes. Kept portable (no Avalonia types) so the mapping —
/// the resolved data range, chart type, and default on-sheet placement — is unit testable without a
/// running shell. The shell hands the built command to <c>WorkbookSession.ExecuteReviewCommand</c>; the
/// chart renderer paints it from <c>Sheet.Charts</c> on the next refresh.
/// </summary>
public static class InsertChartCommandFactory
{
    /// <summary>Default on-sheet placement (points) for a freshly inserted chart, matching Core defaults.</summary>
    public const double DefaultLeft = 20;
    public const double DefaultTop = 20;
    public const double DefaultWidth = 400;
    public const double DefaultHeight = 300;

    /// <summary>
    /// The chart type a ribbon Insert chart-type button/menu id maps to, or <c>null</c> when the id is not
    /// a wired chart control. Recognizes both the Avalonia ribbon's control ids (e.g. <c>insert.column</c>,
    /// <c>insert.line</c>) and its chart-type menu ids (e.g. <c>insert.colStacked</c>), plus the
    /// descriptive "Recommended Charts"/"Column Chart" labels the desktop host uses, so the same mapping
    /// is reusable across ribbon shells.
    /// </summary>
    public static ChartType? ChartTypeForRibbonCommand(string commandId) => commandId switch
    {
        // Avalonia ribbon control ids.
        "insert.column" => ChartType.Column,
        "insert.colClustered" => ChartType.Column,
        "insert.colStacked" => ChartType.StackedColumn,
        "insert.col100" => ChartType.PercentStackedColumn,
        "insert.bar" => ChartType.Bar,
        "insert.line" => ChartType.Line,
        "insert.area" => ChartType.Area,
        "insert.pie" => ChartType.Pie,
        "insert.doughnut" => ChartType.Doughnut,
        "insert.scatter" => ChartType.Scatter,
        "insert.recommended" => ChartType.Column,

        // Descriptive labels shared with the desktop host.
        "Recommended Charts" => ChartType.Column,
        "Column Chart" => ChartType.Column,
        "Stacked Column Chart" => ChartType.StackedColumn,
        "100% Stacked Column Chart" => ChartType.PercentStackedColumn,
        "Bar Chart" => ChartType.Bar,
        "Stacked Bar Chart" => ChartType.StackedBar,
        "100% Stacked Bar Chart" => ChartType.PercentStackedBar,
        "Line Chart" => ChartType.Line,
        "Area Chart" => ChartType.Area,
        "Pie Chart" => ChartType.Pie,
        "Doughnut Chart" => ChartType.Doughnut,
        "Scatter Chart" => ChartType.Scatter,
        "Stock Chart" => ChartType.Stock,
        "Bubble Chart" => ChartType.Bubble,
        "Radar Chart" => ChartType.Radar,
        _ => null,
    };

    /// <summary>
    /// Builds an <see cref="AddChartCommand"/> that charts <paramref name="selection"/> on
    /// <paramref name="sheetId"/> with <paramref name="chartType"/>. The selection is used verbatim as the
    /// chart's data range; the chart is placed at the shared default position/size. A title is not derived
    /// here — Core leaves it unset so the renderer falls back to its own default — keeping this mapping a
    /// pure projection of the selection and type.
    /// </summary>
    public static AddChartCommand Build(SheetId sheetId, GridRange selection, ChartType chartType) =>
        new(
            sheetId,
            selection,
            chartType,
            title: null,
            left: DefaultLeft,
            top: DefaultTop,
            width: DefaultWidth,
            height: DefaultHeight);
}
