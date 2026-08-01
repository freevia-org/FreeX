using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Applies the Select Data Source dialog's "Hidden and Empty Cell Settings" sub-dialog to a chart:
/// how blank cells inside the plotted range render (gaps / zero / connect data points with a line)
/// and whether hidden worksheet rows/columns still plot. Unlike
/// <see cref="ConfigurePivotChartOptionsCommand"/> (which sets the same two
/// <see cref="ChartModel.BlankDisplayMode"/>/<see cref="ChartModel.ShowDataInHiddenRowsAndColumns"/>
/// fields but only for a PivotChart), this command works for ANY chart -- ChartRenderer/
/// ChartLayoutEngine already branch on both fields for ordinary (non-pivot) charts too
/// (R92-app-chart-data-edit-5-3: previously there was no UI path at all to move a normal chart's
/// blank-display mode off the <see cref="ChartBlankDisplayMode.Gap"/> default or to show hidden
/// rows/columns, since the only command that touched these fields rejected non-PivotCharts).
/// </summary>
public sealed class ConfigureChartHiddenEmptyCellsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _chartId;
    private readonly ChartBlankDisplayMode _blankDisplayMode;
    private readonly bool _showDataInHiddenRowsAndColumns;
    private ChartBlankDisplayMode? _previousBlankDisplayMode;
    private bool? _previousShowDataInHiddenRowsAndColumns;

    public string Label => "Hidden and Empty Cell Settings";

    public ConfigureChartHiddenEmptyCellsCommand(
        SheetId sheetId,
        Guid chartId,
        ChartBlankDisplayMode blankDisplayMode,
        bool showDataInHiddenRowsAndColumns)
    {
        _sheetId = sheetId;
        _chartId = chartId;
        _blankDisplayMode = blankDisplayMode;
        _showDataInHiddenRowsAndColumns = showDataInHiddenRowsAndColumns;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (!ChartCommandGuards.TryFindChart(sheet, _chartId, out var chart))
            return ChartCommandGuards.ChartNotFound();

        // R112-model-drawing-object-lock-1-1 sibling fix: layer in the per-chart Locked override so
        // an author-unlocked chart's hidden/empty-cell settings stay editable even while the sheet
        // blocks "Edit objects".
        if (ChartCommandGuards.RejectIfEditObjectsBlocked(sheet, chart) is { } protectedOutcome)
            return protectedOutcome;

        _previousBlankDisplayMode = chart.BlankDisplayMode;
        _previousShowDataInHiddenRowsAndColumns = chart.ShowDataInHiddenRowsAndColumns;

        chart.BlankDisplayMode = _blankDisplayMode;
        chart.ShowDataInHiddenRowsAndColumns = _showDataInHiddenRowsAndColumns;

        return new CommandOutcome(true, AffectedCells: [chart.DataRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousBlankDisplayMode is null || _previousShowDataInHiddenRowsAndColumns is null)
            return;

        if (!ChartCommandGuards.TryFindChart(ctx.GetSheet(_sheetId), _chartId, out var chart))
            return;

        chart.BlankDisplayMode = _previousBlankDisplayMode.Value;
        chart.ShowDataInHiddenRowsAndColumns = _previousShowDataInHiddenRowsAndColumns.Value;
        _previousBlankDisplayMode = null;
        _previousShowDataInHiddenRowsAndColumns = null;
    }
}
