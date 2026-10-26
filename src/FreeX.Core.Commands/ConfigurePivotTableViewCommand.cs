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

        return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start], IsNoOp: NothingChanged(sheet, pivotTable));
    }

    /// <summary>
    /// r256: re-applying the pivot configuration already in effect writes exactly what is already
    /// there -- the dialogs hand back the pivot's own current state as their default, so
    /// re-confirming one reaches Apply with every argument equal to current state. Without this the
    /// command still pushed an undo entry, and UndoRedoStack.Push clears the redo stack, destroying
    /// a real edit the user could have redone.
    ///
    /// <para>r219 left this family unfixed because deciding "no change" meant also proving the
    /// re-render was unnecessary, and guessing at that is how a guard suppresses a real edit. The
    /// POST-HOC form does not have to prove it: <c>_targetSnapshot</c> IS the block the re-render
    /// overwrote, so comparing it against the sheet afterwards observes what the re-render actually
    /// produced instead of predicting it. Together with the model comparison that is the whole of
    /// what Revert restores.</para>
    /// </summary>
    private bool NothingChanged(Sheet sheet, PivotTableModel pivotTable) =>
        _snapshot is not null
        && _snapshot.Matches(pivotTable)
        && PivotSnapshotComparison.RenderedCellsUnchanged(sheet, _targetSnapshot);

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
