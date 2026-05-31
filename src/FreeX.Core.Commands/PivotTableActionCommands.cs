using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class RenamePivotTableCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private readonly string _newName;
    private readonly List<(SheetId SheetId, Guid ChartId)> _updatedCharts = [];
    private readonly List<SlicerModel> _updatedSlicers = [];
    private readonly List<TimelineModel> _updatedTimelines = [];
    private string? _oldName;

    public RenamePivotTableCommand(SheetId sheetId, string pivotTableName, string newName)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
        _newName = newName.Trim();
    }

    public string Label => "Rename PivotTable";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (string.IsNullOrWhiteSpace(_newName))
            return new CommandOutcome(false, "PivotTable name is required.");

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UsePivotTableReports) is { } protectedOutcome)
            return protectedOutcome;

        var pivotTable = FindPivotTable(sheet, _pivotTableName);
        if (pivotTable is null)
            return new CommandOutcome(false, "PivotTable was not found.");
        if (PivotTableNameExists(ctx.Workbook, pivotTable, _newName))
            return new CommandOutcome(false, "PivotTable name is already in use.");

        _oldName = pivotTable.Name;
        if (string.Equals(_oldName, _newName, StringComparison.Ordinal))
            return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start]);

        _updatedCharts.Clear();
        _updatedSlicers.Clear();
        _updatedTimelines.Clear();
        foreach (var workbookSheet in ctx.Workbook.Sheets)
        {
            foreach (var chart in workbookSheet.Charts.Where(chart =>
                         chart.IsPivotChart &&
                         string.Equals(chart.PivotTableName, _oldName, StringComparison.OrdinalIgnoreCase)))
            {
                _updatedCharts.Add((workbookSheet.Id, chart.Id));
            }
        }

        _updatedSlicers.AddRange(ctx.Workbook.Slicers.Where(slicer =>
            string.Equals(slicer.SourcePivotTableName, _oldName, StringComparison.OrdinalIgnoreCase)));
        _updatedTimelines.AddRange(ctx.Workbook.Timelines.Where(timeline =>
            string.Equals(timeline.SourcePivotTableName, _oldName, StringComparison.OrdinalIgnoreCase)));

        pivotTable.Name = _newName;
        foreach (var (chartSheetId, chartId) in _updatedCharts)
        {
            var chart = ctx.GetSheet(chartSheetId).Charts.FirstOrDefault(item => item.Id == chartId);
            if (chart is not null)
                chart.PivotTableName = _newName;
        }

        foreach (var slicer in _updatedSlicers)
            slicer.SourcePivotTableName = _newName;
        foreach (var timeline in _updatedTimelines)
            timeline.SourcePivotTableName = _newName;

        return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_oldName is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        var pivotTable = FindPivotTable(sheet, _newName);
        if (pivotTable is not null)
            pivotTable.Name = _oldName;

        foreach (var (chartSheetId, chartId) in _updatedCharts)
        {
            var chart = ctx.GetSheet(chartSheetId).Charts.FirstOrDefault(item => item.Id == chartId);
            if (chart is not null)
                chart.PivotTableName = _oldName;
        }

        foreach (var slicer in _updatedSlicers)
            slicer.SourcePivotTableName = _oldName;
        foreach (var timeline in _updatedTimelines)
            timeline.SourcePivotTableName = _oldName;

        _updatedCharts.Clear();
        _updatedSlicers.Clear();
        _updatedTimelines.Clear();
        _oldName = null;
    }

    private static PivotTableModel? FindPivotTable(Sheet sheet, string name) =>
        sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, name, StringComparison.OrdinalIgnoreCase));

    private static bool PivotTableNameExists(Workbook workbook, PivotTableModel target, string name) =>
        workbook.Sheets
            .SelectMany(sheet => sheet.PivotTables)
            .Any(pivot => !ReferenceEquals(pivot, target) &&
                          string.Equals(pivot.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed class ClearPivotTableViewCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private PivotViewClearSnapshot? _snapshot;
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;

    public ClearPivotTableViewCommand(SheetId sheetId, string pivotTableName)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
    }

    public string Label => "Clear PivotTable";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UsePivotTableReports) is { } protectedOutcome)
            return protectedOutcome;

        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is null)
            return new CommandOutcome(false, "PivotTable was not found.");

        _snapshot = PivotViewClearSnapshot.Capture(pivotTable);
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.TargetRange);

        PivotTableCommandCollections.Replace(pivotTable.RowFields, ClearSelections(pivotTable.RowFields));
        PivotTableCommandCollections.Replace(pivotTable.ColumnFields, ClearSelections(pivotTable.ColumnFields));
        PivotTableCommandCollections.Replace(pivotTable.PageFields, ClearSelections(pivotTable.PageFields));
        pivotTable.LabelFilters.Clear();
        pivotTable.ValueFilters.Clear();
        pivotTable.Sorts.Clear();

        RefreshPivotTableAndCharts(ctx.Workbook, sheet, pivotTable);
        return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is not null && _snapshot is not null)
        {
            _snapshot.Restore(pivotTable);
        }
        AddPivotTableCommand.Restore(sheet, _targetSnapshot);
        if (pivotTable is not null)
            UpdateBoundPivotChartRanges(ctx.Workbook, sheet, pivotTable);
        _snapshot = null;
        _targetSnapshot = null;
    }

    private static IReadOnlyList<PivotFieldModel> ClearSelections(IEnumerable<PivotFieldModel> fields) =>
        fields
            .Select(field => field with { SelectedItem = null, SelectedItems = null })
            .ToList();

    private sealed record PivotViewClearSnapshot(
        IReadOnlyList<PivotFieldModel> RowFields,
        IReadOnlyList<PivotFieldModel> ColumnFields,
        IReadOnlyList<PivotFieldModel> PageFields,
        IReadOnlyList<PivotLabelFilterModel> LabelFilters,
        IReadOnlyList<PivotValueFilterModel> ValueFilters,
        IReadOnlyList<PivotSortModel> Sorts)
    {
        public static PivotViewClearSnapshot Capture(PivotTableModel pivotTable) =>
            new(
                pivotTable.RowFields.ToList(),
                pivotTable.ColumnFields.ToList(),
                pivotTable.PageFields.ToList(),
                pivotTable.LabelFilters.ToList(),
                pivotTable.ValueFilters.ToList(),
                pivotTable.Sorts.ToList());

        public void Restore(PivotTableModel pivotTable)
        {
            PivotTableCommandCollections.Replace(pivotTable.RowFields, RowFields);
            PivotTableCommandCollections.Replace(pivotTable.ColumnFields, ColumnFields);
            PivotTableCommandCollections.Replace(pivotTable.PageFields, PageFields);
            PivotTableCommandCollections.Replace(pivotTable.LabelFilters, LabelFilters);
            PivotTableCommandCollections.Replace(pivotTable.ValueFilters, ValueFilters);
            PivotTableCommandCollections.Replace(pivotTable.Sorts, Sorts);
        }
    }

    private static void RefreshPivotTableAndCharts(Workbook workbook, Sheet sheet, PivotTableModel pivotTable)
    {
        PivotTableRefreshService.Refresh(workbook, sheet, pivotTable);
        UpdateBoundPivotChartRanges(workbook, sheet, pivotTable);
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
}

public sealed class MovePivotTableCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private readonly CellAddress _targetStart;
    private GridRange? _oldTargetRange;
    private GridRange? _newTargetRange;
    private List<(CellAddress Address, Cell? Cell)>? _rangeSnapshot;

    public MovePivotTableCommand(SheetId sheetId, string pivotTableName, CellAddress targetStart)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
        _targetStart = targetStart;
    }

    public string Label => "Move PivotTable";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_targetStart.Sheet != _sheetId)
            return new CommandOutcome(false, "PivotTable target range must be on the target sheet.");

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UsePivotTableReports) is { } protectedOutcome)
            return protectedOutcome;

        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is null)
            return new CommandOutcome(false, "PivotTable was not found.");
        if (!TryCreateMovedTargetRange(pivotTable.TargetRange, _targetStart, out var movedRange))
            return new CommandOutcome(false, "PivotTable target range is outside the worksheet bounds.");

        _oldTargetRange = pivotTable.TargetRange;
        _newTargetRange = movedRange;
        _rangeSnapshot = SnapshotRanges(sheet, _oldTargetRange.Value, _newTargetRange.Value);

        if (_oldTargetRange.Value != _newTargetRange.Value)
        {
            ClearRange(sheet, _oldTargetRange.Value);
            pivotTable.TargetRange = _newTargetRange.Value;
            RefreshPivotTableAndCharts(ctx.Workbook, sheet, pivotTable);
        }

        return new CommandOutcome(true, AffectedCells: [_newTargetRange.Value.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_oldTargetRange is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is not null)
            pivotTable.TargetRange = _oldTargetRange.Value;

        AddPivotTableCommand.Restore(sheet, _rangeSnapshot);
        if (pivotTable is not null)
            UpdateBoundPivotChartRanges(ctx.Workbook, sheet, pivotTable);
        _oldTargetRange = null;
        _newTargetRange = null;
        _rangeSnapshot = null;
    }

    private static bool TryCreateMovedTargetRange(GridRange currentRange, CellAddress targetStart, out GridRange targetRange)
    {
        var rowCount = currentRange.RowCount;
        var colCount = currentRange.ColCount;
        if (targetStart.Row > CellAddress.MaxRow - rowCount + 1 ||
            targetStart.Col > CellAddress.MaxCol - colCount + 1)
        {
            targetRange = default;
            return false;
        }

        targetRange = new GridRange(
            targetStart,
            new CellAddress(
                targetStart.Sheet,
                targetStart.Row + rowCount - 1,
                targetStart.Col + colCount - 1));
        return true;
    }

    private static List<(CellAddress Address, Cell? Cell)> SnapshotRanges(Sheet sheet, params GridRange[] ranges)
    {
        var snapshot = new List<(CellAddress Address, Cell? Cell)>();
        var seen = new HashSet<CellAddress>();
        foreach (var range in ranges)
        {
            for (var row = range.Start.Row; row <= range.End.Row; row++)
            for (var col = range.Start.Col; col <= range.End.Col; col++)
            {
                var address = new CellAddress(sheet.Id, row, col);
                if (seen.Add(address))
                    snapshot.Add((address, sheet.GetCell(address)?.Clone()));
            }
        }

        return snapshot;
    }

    private static void ClearRange(Sheet sheet, GridRange range)
    {
        sheet.ReplaceMergedRegions(sheet.MergedRegions.Where(region => !region.Overlaps(range)));
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        for (var col = range.Start.Col; col <= range.End.Col; col++)
            sheet.ClearCell(row, col);
    }

    private static void RefreshPivotTableAndCharts(Workbook workbook, Sheet sheet, PivotTableModel pivotTable)
    {
        PivotTableRefreshService.Refresh(workbook, sheet, pivotTable);
        UpdateBoundPivotChartRanges(workbook, sheet, pivotTable);
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
}
