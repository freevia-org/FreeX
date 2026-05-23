using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class ConfigurePivotChartOptionsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _chartId;
    private readonly int? _chartStyleId;
    private readonly bool _showFieldButtons;
    private readonly bool? _showReportFilterButtons;
    private readonly bool? _showAxisFieldButtons;
    private readonly bool? _showValueFieldButtons;
    private readonly bool? _showDataTable;
    private readonly bool? _showDataTableLegendKeys;
    private readonly bool? _roundedCorners;
    private readonly bool? _showHiddenData;
    private readonly ChartBlankDisplayMode? _blankDisplayMode;
    private int? _previousChartStyleId;
    private bool? _previousShowFieldButtons;
    private bool? _previousShowReportFilterButtons;
    private bool? _previousShowAxisFieldButtons;
    private bool? _previousShowValueFieldButtons;
    private ChartDataTableModel? _previousDataTable;
    private bool _previousDataTableCaptured;
    private bool? _previousRoundedCorners;
    private bool? _previousShowHiddenData;
    private ChartBlankDisplayMode? _previousBlankDisplayMode;

    public string Label => "PivotChart Options";

    public ConfigurePivotChartOptionsCommand(
        SheetId sheetId,
        Guid chartId,
        int? chartStyleId,
        bool showFieldButtons,
        bool? showReportFilterButtons = null,
        bool? showAxisFieldButtons = null,
        bool? showValueFieldButtons = null,
        bool? showDataTable = null,
        bool? showDataTableLegendKeys = null,
        bool? roundedCorners = null,
        bool? showHiddenData = null,
        ChartBlankDisplayMode? blankDisplayMode = null)
    {
        _sheetId = sheetId;
        _chartId = chartId;
        _chartStyleId = NormalizeStyleId(chartStyleId);
        _showFieldButtons = showFieldButtons;
        _showReportFilterButtons = showReportFilterButtons;
        _showAxisFieldButtons = showAxisFieldButtons;
        _showValueFieldButtons = showValueFieldButtons;
        _showDataTable = showDataTable;
        _showDataTableLegendKeys = showDataTableLegendKeys;
        _roundedCorners = roundedCorners;
        _showHiddenData = showHiddenData;
        _blankDisplayMode = blankDisplayMode;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var chart = ctx.GetSheet(_sheetId).Charts.FirstOrDefault(item => item.Id == _chartId);
        if (chart is null)
            return new CommandOutcome(false, "PivotChart was not found.");
        if (!chart.IsPivotChart || string.IsNullOrWhiteSpace(chart.PivotTableName))
            return new CommandOutcome(false, "Selected chart is not a PivotChart.");

        _previousChartStyleId = chart.ChartStyleId;
        _previousShowFieldButtons = chart.ShowPivotChartFieldButtons;
        _previousShowReportFilterButtons = chart.ShowPivotChartReportFilterButtons;
        _previousShowAxisFieldButtons = chart.ShowPivotChartAxisFieldButtons;
        _previousShowValueFieldButtons = chart.ShowPivotChartValueFieldButtons;
        _previousDataTable = CloneDataTable(chart.DataTable);
        _previousDataTableCaptured = true;
        _previousRoundedCorners = chart.RoundedCorners;
        _previousShowHiddenData = chart.ShowDataInHiddenRowsAndColumns;
        _previousBlankDisplayMode = chart.BlankDisplayMode;
        chart.ChartStyleId = _chartStyleId;
        chart.ShowPivotChartFieldButtons = _showFieldButtons;
        chart.ShowPivotChartReportFilterButtons = _showReportFilterButtons ?? chart.ShowPivotChartReportFilterButtons;
        chart.ShowPivotChartAxisFieldButtons = _showAxisFieldButtons ?? chart.ShowPivotChartAxisFieldButtons;
        chart.ShowPivotChartValueFieldButtons = _showValueFieldButtons ?? chart.ShowPivotChartValueFieldButtons;
        if (_showDataTable is { } showDataTable)
        {
            chart.DataTable = showDataTable
                ? new ChartDataTableModel
                {
                    ShowHorizontalBorder = true,
                    ShowVerticalBorder = true,
                    ShowOutline = true,
                    ShowLegendKeys = _showDataTableLegendKeys ?? chart.DataTable?.ShowLegendKeys ?? false
                }
                : null;
        }
        else if (_showDataTableLegendKeys is { } showLegendKeys && chart.DataTable is not null)
        {
            chart.DataTable.ShowLegendKeys = showLegendKeys;
        }
        chart.RoundedCorners = _roundedCorners ?? chart.RoundedCorners;
        chart.ShowDataInHiddenRowsAndColumns = _showHiddenData ?? chart.ShowDataInHiddenRowsAndColumns;
        chart.BlankDisplayMode = _blankDisplayMode ?? chart.BlankDisplayMode;

        return new CommandOutcome(true, AffectedCells: [chart.DataRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousShowFieldButtons is null)
            return;

        var chart = ctx.GetSheet(_sheetId).Charts.FirstOrDefault(item => item.Id == _chartId);
        if (chart is null)
            return;

        chart.ChartStyleId = _previousChartStyleId;
        chart.ShowPivotChartFieldButtons = _previousShowFieldButtons.Value;
        chart.ShowPivotChartReportFilterButtons = _previousShowReportFilterButtons ?? true;
        chart.ShowPivotChartAxisFieldButtons = _previousShowAxisFieldButtons ?? true;
        chart.ShowPivotChartValueFieldButtons = _previousShowValueFieldButtons ?? true;
        if (_previousDataTableCaptured)
            chart.DataTable = CloneDataTable(_previousDataTable);
        chart.RoundedCorners = _previousRoundedCorners ?? false;
        chart.ShowDataInHiddenRowsAndColumns = _previousShowHiddenData ?? false;
        chart.BlankDisplayMode = _previousBlankDisplayMode ?? ChartBlankDisplayMode.Gap;
        _previousChartStyleId = null;
        _previousShowFieldButtons = null;
        _previousShowReportFilterButtons = null;
        _previousShowAxisFieldButtons = null;
        _previousShowValueFieldButtons = null;
        _previousDataTable = null;
        _previousDataTableCaptured = false;
        _previousRoundedCorners = null;
        _previousShowHiddenData = null;
        _previousBlankDisplayMode = null;
    }

    private static ChartDataTableModel? CloneDataTable(ChartDataTableModel? dataTable) =>
        dataTable is null
            ? null
            : new ChartDataTableModel
            {
                ShowHorizontalBorder = dataTable.ShowHorizontalBorder,
                ShowVerticalBorder = dataTable.ShowVerticalBorder,
                ShowOutline = dataTable.ShowOutline,
                ShowLegendKeys = dataTable.ShowLegendKeys
            };

    private static int? NormalizeStyleId(int? chartStyleId)
    {
        if (chartStyleId is null)
            return null;

        return Math.Clamp(chartStyleId.Value, 1, 48);
    }
}
