using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class ConfigurePivotTableViewCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private readonly IReadOnlyList<PivotLabelFilterModel> _labelFilters;
    private readonly IReadOnlyList<PivotValueFilterModel> _valueFilters;
    private readonly IReadOnlyList<PivotSortModel> _sorts;
    private PivotViewStateSnapshot? _snapshot;
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;

    public ConfigurePivotTableViewCommand(
        SheetId sheetId,
        string pivotTableName,
        IReadOnlyList<PivotLabelFilterModel> labelFilters,
        IReadOnlyList<PivotValueFilterModel> valueFilters,
        IReadOnlyList<PivotSortModel> sorts)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
        _labelFilters = labelFilters;
        _valueFilters = valueFilters;
        _sorts = sorts;
    }

    public string Label => "Configure PivotTable View";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UsePivotTableReports) is { } protectedOutcome)
            return protectedOutcome;

        if (!CommandGuards.TryFindPivotTable(sheet, _pivotTableName, out var pivotTable))
            return CommandGuards.RejectPivotTableNotFound();

        _snapshot = PivotViewStateSnapshot.Capture(pivotTable);
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.LastRenderedRange ?? pivotTable.TargetRange);

        PivotTableCommandCollections.Replace(pivotTable.LabelFilters, _labelFilters);
        PivotTableCommandCollections.Replace(pivotTable.ValueFilters, _valueFilters);
        PivotTableCommandCollections.Replace(pivotTable.Sorts, _sorts);

        // R140-remediation-pivot-refresh-growth-guard-completeness: see PivotTableRefreshService.GrowthGuard.cs.
        if (PivotTableCommandRefreshTransaction.RefreshGuarded(
                ctx.Workbook, sheet, pivotTable, _snapshot) is { } failure)
        {
            _snapshot = null;
            _targetSnapshot = null;
            return failure;
        }

        return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        CommandGuards.TryFindPivotTable(sheet, _pivotTableName, out var pivotTable);
        PivotTableCommandRefreshTransaction.Revert(
            ctx.Workbook,
            sheet,
            pivotTable,
            _targetSnapshot,
            _snapshot);
        _snapshot = null;
        _targetSnapshot = null;
    }

}
