using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

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
            return new CommandOutcome(false, "Source sheet was not found.");
        if (CommandGuards.RejectIfProtectedWithoutPermission(source, SheetProtectionPermission.EditObjects) is { } sourceProtectedOutcome)
            return sourceProtectedOutcome;

        var target = ctx.Workbook.GetSheet(_targetSheetId);
        if (target is null)
            return new CommandOutcome(false, "Target sheet was not found.");
        if (CommandGuards.RejectIfProtectedWithoutPermission(target, SheetProtectionPermission.EditObjects) is { } targetProtectedOutcome)
            return targetProtectedOutcome;

        var chart = source.Charts.FirstOrDefault(item => item.Id == _chartId);
        if (chart is null)
            return new CommandOutcome(false, "Chart was not found.");
        if (chart.IsPivotChart)
            return new CommandOutcome(false, "Selected chart is a PivotChart.");
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
            return new CommandOutcome(false, "Source sheet was not found.");
        if (CommandGuards.RejectIfProtectedWithoutPermission(source, SheetProtectionPermission.EditObjects) is { } sourceProtectedOutcome)
            return sourceProtectedOutcome;

        var chart = source.Charts.FirstOrDefault(item => item.Id == _chartId);
        if (chart is null)
            return new CommandOutcome(false, "Chart was not found.");
        if (chart.IsPivotChart)
            return new CommandOutcome(false, "Selected chart is a PivotChart.");

        var target = ctx.Workbook.AddSheet(_sheetName);
        source.Charts.Remove(chart);
        target.Charts.Add(chart);
        _createdSheetId = target.Id;
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
        _createdSheetId = null;
        _movedChart = null;
    }
}
