using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

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

        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is null)
            return new CommandOutcome(false, "PivotTable was not found.");

        _snapshot = PivotViewSnapshot.Capture(pivotTable);
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.TargetRange);

        Replace(pivotTable.LabelFilters, _labelFilters);
        Replace(pivotTable.ValueFilters, _valueFilters);
        Replace(pivotTable.Sorts, _sorts);
        PivotTableRefreshService.Refresh(ctx.Workbook, sheet, pivotTable);
        var outputRange = PivotTableRefreshService.GetMaterializedOutputRange(sheet, pivotTable);
        foreach (var chart in sheet.Charts.Where(chart =>
                     chart.IsPivotChart &&
                     string.Equals(chart.PivotTableName, pivotTable.Name, StringComparison.OrdinalIgnoreCase)))
        {
            chart.DataRange = outputRange;
            chart.PivotCacheId = pivotTable.CacheId;
        }

        return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is not null && _snapshot is not null)
            _snapshot.Restore(pivotTable);
        AddPivotTableCommand.Restore(sheet, _targetSnapshot);
        _snapshot = null;
        _targetSnapshot = null;
    }

    private static void Replace<T>(List<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        target.AddRange(source);
    }

    private sealed record PivotViewSnapshot(
        IReadOnlyList<PivotLabelFilterModel> LabelFilters,
        IReadOnlyList<PivotValueFilterModel> ValueFilters,
        IReadOnlyList<PivotSortModel> Sorts)
    {
        public static PivotViewSnapshot Capture(PivotTableModel pivotTable) =>
            new(
                pivotTable.LabelFilters.ToList(),
                pivotTable.ValueFilters.ToList(),
                pivotTable.Sorts.ToList());

        public void Restore(PivotTableModel pivotTable)
        {
            Replace(pivotTable.LabelFilters, LabelFilters);
            Replace(pivotTable.ValueFilters, ValueFilters);
            Replace(pivotTable.Sorts, Sorts);
        }
    }
}
