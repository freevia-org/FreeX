using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class MoveChartCommand : IWorkbookCommand
{
    private readonly SheetId _sourceSheetId;
    private readonly Guid _chartId;
    private readonly SheetId _targetSheetId;
    private ChartModel? _movedChart;
    private bool _applied;

    public string Label => "Move Chart";

    public MoveChartCommand(SheetId sourceSheetId, Guid chartId, SheetId targetSheetId)
    {
        _sourceSheetId = sourceSheetId;
        _chartId = chartId;
        _targetSheetId = targetSheetId;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var source = ctx.Workbook.GetSheet(_sourceSheetId);
        if (source is null)
            return CommandGuards.RejectSourceSheetNotFound();
        if (ChartCommandGuards.RejectIfEditObjectsBlocked(source) is { } sourceProtectedOutcome)
            return sourceProtectedOutcome;

        var target = ctx.Workbook.GetSheet(_targetSheetId);
        if (target is null)
            return CommandGuards.RejectTargetSheetNotFound();
        if (ChartCommandGuards.RejectIfEditObjectsBlocked(target) is { } targetProtectedOutcome)
            return targetProtectedOutcome;

        if (!ChartCommandGuards.TryFindChart(source, _chartId, out var chart))
            return ChartCommandGuards.ChartNotFound();
        if (chart.IsPivotChart)
            return ChartCommandGuards.SelectedChartIsPivotChart();
        if (_sourceSheetId == _targetSheetId)
            return new CommandOutcome(true, AffectedCells: [chart.DataRange.Start]);

        source.Charts.Remove(chart);
        target.Charts.Add(chart);
        _movedChart = chart;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [chart.DataRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied || _movedChart is null)
            return;

        var source = ctx.Workbook.GetSheet(_sourceSheetId);
        var target = ctx.Workbook.GetSheet(_targetSheetId);
        if (source is null || target is null)
            return;

        target.Charts.Remove(_movedChart);
        source.Charts.Add(_movedChart);
        _movedChart = null;
        _applied = false;
    }
}

public sealed class MoveChartToNewSheetCommand : IWorkbookCommand
{
    private readonly SheetId _sourceSheetId;
    private readonly Guid _chartId;
    private readonly string _sheetName;
    private SheetId? _createdSheetId;
    private ChartModel? _movedChart;

    public string Label => "Move Chart";

    public MoveChartToNewSheetCommand(SheetId sourceSheetId, Guid chartId, string sheetName)
    {
        _sourceSheetId = sourceSheetId;
        _chartId = chartId;
        _sheetName = sheetName;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        var validationError = ctx.Workbook.ValidateSheetName(_sheetName);
        if (validationError is not null)
            return new CommandOutcome(false, validationError);

        var source = ctx.Workbook.GetSheet(_sourceSheetId);
        if (source is null)
            return CommandGuards.RejectSourceSheetNotFound();
        if (ChartCommandGuards.RejectIfEditObjectsBlocked(source) is { } sourceProtectedOutcome)
            return sourceProtectedOutcome;

        if (!ChartCommandGuards.TryFindChart(source, _chartId, out var chart))
            return ChartCommandGuards.ChartNotFound();
        if (chart.IsPivotChart)
            return ChartCommandGuards.SelectedChartIsPivotChart();

        Sheet target;
        if (_createdSheetId is { } existingSheetId)
        {
            // R17: redo. Workbook.AddSheet always mints a brand-new SheetId, which would give
            // the re-created chart-holding sheet a DIFFERENT id than the first Apply produced --
            // breaking any later redo-stack command that captured the original id. Re-create with
            // the SAME id captured below instead, via the "reinsert an existing sheet instance"
            // overload (mirrors AddSheetCommand's R16 redo fix).
            target = new Sheet(existingSheetId, _sheetName);
            ctx.Workbook.InsertSheet(ctx.Workbook.Sheets.Count, target);
        }
        else
        {
            target = ctx.Workbook.AddSheet(_sheetName);
            _createdSheetId = target.Id;
        }
        // R76-io-chartsheet-4-1: Excel's "Move Chart > New Sheet" creates a real CHARTSHEET (a
        // full-page chart-only sheet, xl/chartsheets/sheetN.xml on save), not a normal worksheet
        // that merely hosts an embedded chart. Marking the new sheet's Kind accordingly keeps the
        // in-memory model faithful to what the command is documented to do; see this command's
        // XML doc and R76-io-chartsheet-4-1 in the review log for the residual on the SAVE side --
        // XlsxFileAdapter's full (ClosedXML) save path has no writer that can emit a chartsheet part
        // for a freshly-created sheet (only XlsxChartsheetReader + the source-package byte-preservation
        // path round-trip an ALREADY-LOADED chartsheet; there is no chartsheet part generator at all),
        // so a workbook containing this new chartsheet still saves it as a worksheet embedding the
        // chart until that IO-side writer exists.
        target.Kind = SheetKind.Chartsheet;
        target.ResetViewStateToA1();
        source.Charts.Remove(chart);
        target.Charts.Add(chart);
        _movedChart = chart;
        return new CommandOutcome(true, AffectedCells: [chart.DataRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_createdSheetId is null || _movedChart is null)
            return;

        var source = ctx.Workbook.GetSheet(_sourceSheetId);
        var target = ctx.Workbook.GetSheet(_createdSheetId.Value);
        if (source is not null && target is not null)
        {
            target.Charts.Remove(_movedChart);
            source.Charts.Add(_movedChart);
        }

        ctx.Workbook.RemoveSheet(_createdSheetId.Value);
        _movedChart = null;
    }
}
