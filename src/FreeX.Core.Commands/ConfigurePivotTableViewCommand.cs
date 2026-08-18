using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class ConfigurePivotTableViewCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private readonly IReadOnlyList<PivotLabelFilterModel> _labelFilters;
    private readonly IReadOnlyList<PivotValueFilterModel> _valueFilters;
    private readonly IReadOnlyList<PivotSortModel> _sorts;
    private PivotViewSnapshot? _snapshot;
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

        _snapshot = PivotViewSnapshot.Capture(pivotTable);
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.LastRenderedRange ?? pivotTable.TargetRange);

        PivotTableCommandCollections.Replace(pivotTable.LabelFilters, _labelFilters);
        PivotTableCommandCollections.Replace(pivotTable.ValueFilters, _valueFilters);
        PivotTableCommandCollections.Replace(pivotTable.Sorts, _sorts);

        // R140-remediation-pivot-refresh-growth-guard-completeness: see PivotTableRefreshService.GrowthGuard.cs.
        var snapshot = _snapshot;
        var baseline = PivotTableRefreshService.CaptureGrowthGuardBaseline(sheet, pivotTable);
        if (PivotTableRefreshService.RefreshGuarded(ctx.Workbook, sheet, pivotTable, baseline, () => snapshot!.Restore(pivotTable)) is { } failure)
        {
            _snapshot = null;
            _targetSnapshot = null;
            return failure;
        }
        UpdateBoundPivotChartRanges(ctx.Workbook, sheet, pivotTable);

        return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.TryFindPivotTable(sheet, _pivotTableName, out var pivotTable) && _snapshot is not null)
        {
            PivotTableRefreshService.ClearRenderedRange(sheet, pivotTable.LastRenderedRange);
            _snapshot.Restore(pivotTable);
        }
        AddPivotTableCommand.Restore(sheet, _targetSnapshot);
        if (pivotTable is not null)
            UpdateBoundPivotChartRanges(ctx.Workbook, sheet, pivotTable);
        _snapshot = null;
        _targetSnapshot = null;
    }

    private static void UpdateBoundPivotChartRanges(Workbook workbook, Sheet sheet, PivotTableModel pivotTable)
    {
        var outputRange = PivotTableRefreshService.GetMaterializedOutputRange(sheet, pivotTable);
        foreach (var chartSheet in workbook.Sheets)
        foreach (var chart in chartSheet.Charts.Where(chart =>
                     chart.IsPivotChart &&
                     string.Equals(chart.PivotTableName, pivotTable.Name, StringComparison.OrdinalIgnoreCase)))
        {
            chart.DataRange = outputRange;
            chart.PivotCacheId = pivotTable.CacheId;
        }
    }

    private sealed record PivotViewSnapshot(
        IReadOnlyList<PivotLabelFilterModel> LabelFilters,
        IReadOnlyList<PivotValueFilterModel> ValueFilters,
        IReadOnlyList<PivotSortModel> Sorts,
        GridRange? LastRenderedRange)
    {
        public static PivotViewSnapshot Capture(PivotTableModel pivotTable) =>
            new(
                pivotTable.LabelFilters.ToList(),
                pivotTable.ValueFilters.ToList(),
                pivotTable.Sorts.ToList(),
                pivotTable.LastRenderedRange);

        public void Restore(PivotTableModel pivotTable)
        {
            PivotTableCommandCollections.Replace(pivotTable.LabelFilters, LabelFilters);
            PivotTableCommandCollections.Replace(pivotTable.ValueFilters, ValueFilters);
            PivotTableCommandCollections.Replace(pivotTable.Sorts, Sorts);
            pivotTable.LastRenderedRange = LastRenderedRange;
        }
    }
}
