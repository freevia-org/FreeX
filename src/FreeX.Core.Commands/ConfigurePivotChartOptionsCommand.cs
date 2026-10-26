using FreeX.Core.Model;

namespace FreeX.Core.Commands;

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
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UsePivotTableReports) is { } pivotProtectedOutcome)
            return pivotProtectedOutcome;

        if (!ChartCommandGuards.TryFindChart(sheet, _chartId, out var chart))
            return ChartCommandGuards.PivotChartNotFound();

        // R112-model-drawing-object-lock-1-1 sibling fix: layer in the per-chart Locked override so
        // an author-unlocked PivotChart's options stay editable even while the sheet blocks "Edit
        // objects" -- matches ChangePivotChartTypeCommand.
        if (ChartCommandGuards.RejectIfEditObjectsBlocked(sheet, chart) is { } protectedOutcome)
            return protectedOutcome;
        if (!chart.IsPivotChart || string.IsNullOrWhiteSpace(chart.PivotTableName))
            return ChartCommandGuards.SelectedChartIsNotPivotChart();

        _previousChartStyleId = chart.ChartStyleId;
        _previousShowFieldButtons = chart.ShowPivotChartFieldButtons;
        _previousShowReportFilterButtons = chart.ShowPivotChartReportFilterButtons;
        _previousShowAxisFieldButtons = chart.ShowPivotChartAxisFieldButtons;
        _previousShowValueFieldButtons = chart.ShowPivotChartValueFieldButtons;
        _previousDataTable = chart.DataTable?.Clone();
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
            if (showDataTable)
            {
                chart.DataTable ??= new ChartDataTableModel
                {
                    ShowHorizontalBorder = true,
                    ShowVerticalBorder = true,
                    ShowOutline = true,
                    ShowLegendKeys = _showDataTableLegendKeys ?? false
                };

                if (_showDataTableLegendKeys is { } showLegendKeys)
                    chart.DataTable.ShowLegendKeys = showLegendKeys;
            }
            else
            {
                chart.DataTable = null;
            }
        }
        else if (_showDataTableLegendKeys is { } showLegendKeys && chart.DataTable is not null)
        {
            chart.DataTable.ShowLegendKeys = showLegendKeys;
        }
        chart.RoundedCorners = _roundedCorners ?? chart.RoundedCorners;
        chart.ShowDataInHiddenRowsAndColumns = _showHiddenData ?? chart.ShowDataInHiddenRowsAndColumns;
        chart.BlankDisplayMode = _blankDisplayMode ?? chart.BlankDisplayMode;

        return new CommandOutcome(true, AffectedCells: [chart.DataRange.Start], IsNoOp: NothingChanged(chart));
    }

    /// <summary>
    /// r259: re-confirming the PivotChart Options dialog without changing a setting writes every
    /// property back as it was -- and every one of these settings is a checkbox or a dropdown the
    /// dialog pre-fills from the chart's current state, so OK-without-changes is the ordinary case.
    /// Without this the command still pushed an undo entry, and UndoRedoStack.Push clears the redo
    /// stack, destroying a real edit the user could have redone.
    ///
    /// <para>The decision is POST-HOC over the command's whole undo record: Revert restores exactly
    /// these ten fields, and each is compared against what the chart holds now. The data-table half
    /// goes through <see cref="ChartDataTableModel.SameAs"/> rather than <c>==</c>, because that type
    /// is a CLASS captured by Clone -- reference equality there would never fire.</para>
    /// </summary>
    private bool NothingChanged(ChartModel chart) =>
        _previousChartStyleId == chart.ChartStyleId
        && _previousShowFieldButtons == chart.ShowPivotChartFieldButtons
        && _previousShowReportFilterButtons == chart.ShowPivotChartReportFilterButtons
        && _previousShowAxisFieldButtons == chart.ShowPivotChartAxisFieldButtons
        && _previousShowValueFieldButtons == chart.ShowPivotChartValueFieldButtons
        && (!_previousDataTableCaptured || SameDataTable(_previousDataTable, chart.DataTable))
        && _previousRoundedCorners == chart.RoundedCorners
        && _previousShowHiddenData == chart.ShowDataInHiddenRowsAndColumns
        && _previousBlankDisplayMode == chart.BlankDisplayMode;

    private static bool SameDataTable(ChartDataTableModel? captured, ChartDataTableModel? current) =>
        captured is null ? current is null : captured.SameAs(current);

    public void Revert(ICommandContext ctx)
    {
        if (_previousShowFieldButtons is null)
            return;

        if (!ChartCommandGuards.TryFindChart(ctx.GetSheet(_sheetId), _chartId, out var chart))
            return;

        chart.ChartStyleId = _previousChartStyleId;
        chart.ShowPivotChartFieldButtons = _previousShowFieldButtons.Value;
        chart.ShowPivotChartReportFilterButtons = _previousShowReportFilterButtons ?? true;
        chart.ShowPivotChartAxisFieldButtons = _previousShowAxisFieldButtons ?? true;
        chart.ShowPivotChartValueFieldButtons = _previousShowValueFieldButtons ?? true;
        if (_previousDataTableCaptured)
            chart.DataTable = _previousDataTable?.Clone();
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

    private static int? NormalizeStyleId(int? chartStyleId)
    {
        if (chartStyleId is null)
            return null;

        return Math.Clamp(chartStyleId.Value, 1, 48);
    }
}
