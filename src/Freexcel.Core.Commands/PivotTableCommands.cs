using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class AddPivotTableCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly GridRange _targetRange;
    private readonly string _name;
    private readonly IReadOnlyList<int> _rowFieldIndexes;
    private readonly IReadOnlyList<int> _dataFieldIndexes;
    private PivotCacheModel? _addedCache;
    private PivotTableModel? _addedPivotTable;
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;

    public string Label => "Insert PivotTable";

    public AddPivotTableCommand(
        SheetId sheetId,
        GridRange sourceRange,
        GridRange targetRange,
        string name,
        IReadOnlyList<int> rowFieldIndexes,
        IReadOnlyList<int> dataFieldIndexes)
    {
        _sheetId = sheetId;
        _sourceRange = sourceRange;
        _targetRange = targetRange;
        _name = name;
        _rowFieldIndexes = rowFieldIndexes;
        _dataFieldIndexes = dataFieldIndexes;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_sourceRange.Start.Sheet != _sheetId || _sourceRange.End.Sheet != _sheetId)
            return new CommandOutcome(false, "PivotTable source range must be on the target sheet.");
        if (_targetRange.Start.Sheet != _sheetId || _targetRange.End.Sheet != _sheetId)
            return new CommandOutcome(false, "PivotTable target range must be on the target sheet.");
        if (_sourceRange.ColCount == 0 || _sourceRange.RowCount < 2)
            return new CommandOutcome(false, "PivotTable source range must include headers and data.");
        if (string.IsNullOrWhiteSpace(_name))
            return new CommandOutcome(false, "PivotTable name is required.");

        var fieldCount = checked((int)_sourceRange.ColCount);
        if (!_rowFieldIndexes.Concat(_dataFieldIndexes).All(index => index >= 0 && index < fieldCount))
            return new CommandOutcome(false, "PivotTable field index is outside the source range.");
        if (_dataFieldIndexes.Count == 0)
            return new CommandOutcome(false, "PivotTable requires at least one data field.");

        var sheet = ctx.GetSheet(_sheetId);
        _targetSnapshot = Snapshot(sheet, _targetRange);
        var headers = ReadHeaders(sheet, fieldCount);
        var cacheId = NextCacheId(ctx.Workbook);
        var cache = new PivotCacheModel
        {
            CacheId = cacheId,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = _sourceRange.ToString()
        };
        foreach (var header in headers)
            cache.Fields.Add(new PivotCacheFieldModel(header));

        var pivotTable = new PivotTableModel
        {
            Name = _name,
            CacheId = cacheId,
            SourceRange = _sourceRange,
            TargetRange = _targetRange
        };
        pivotTable.RowFields.AddRange(_rowFieldIndexes.Select(index => new PivotFieldModel(index)));
        pivotTable.DataFields.AddRange(_dataFieldIndexes.Select(index =>
            new PivotDataFieldModel(index, $"Sum of {headers[index]}", "sum")));

        ctx.Workbook.PivotCaches.Add(cache);
        sheet.PivotTables.Add(pivotTable);
        PivotTableRefreshService.Refresh(ctx.Workbook, sheet, pivotTable);
        _addedCache = cache;
        _addedPivotTable = pivotTable;
        return new CommandOutcome(true, AffectedCells: [_targetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (_addedPivotTable is not null)
            sheet.PivotTables.Remove(_addedPivotTable);
        if (_addedCache is not null)
            ctx.Workbook.PivotCaches.Remove(_addedCache);
        Restore(sheet, _targetSnapshot);
        _addedPivotTable = null;
        _addedCache = null;
        _targetSnapshot = null;
    }

    private List<string> ReadHeaders(Sheet sheet, int fieldCount)
    {
        var headers = new List<string>(fieldCount);
        for (var index = 0; index < fieldCount; index++)
        {
            var value = sheet.GetValue(_sourceRange.Start.Row, _sourceRange.Start.Col + (uint)index);
            headers.Add(value is TextValue text && !string.IsNullOrWhiteSpace(text.Value)
                ? text.Value
                : $"Field{index + 1}");
        }

        return headers;
    }

    private static int NextCacheId(Workbook workbook) =>
        workbook.PivotCaches.Count == 0
            ? 1
            : workbook.PivotCaches.Max(cache => cache.CacheId) + 1;

    internal static List<(CellAddress Address, Cell? Cell)> Snapshot(Sheet sheet, GridRange range)
    {
        var snapshot = new List<(CellAddress Address, Cell? Cell)>();
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        for (var col = range.Start.Col; col <= range.End.Col; col++)
        {
            var address = new CellAddress(sheet.Id, row, col);
            snapshot.Add((address, sheet.GetCell(address)?.Clone()));
        }

        return snapshot;
    }

    internal static void Restore(Sheet sheet, IReadOnlyList<(CellAddress Address, Cell? Cell)>? snapshot)
    {
        if (snapshot is null)
            return;

        foreach (var (address, cell) in snapshot)
        {
            if (cell is null)
                sheet.ClearCell(address);
            else
                sheet.SetCell(address, cell.Clone());
        }
    }
}

public sealed class RefreshPivotTableCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;

    public RefreshPivotTableCommand(SheetId sheetId, string pivotTableName)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
    }

    public string Label => "Refresh PivotTable";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is null)
            return new CommandOutcome(false, "PivotTable was not found.");

        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.TargetRange);
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
        AddPivotTableCommand.Restore(ctx.GetSheet(_sheetId), _targetSnapshot);
        _targetSnapshot = null;
    }
}

public sealed class ConfigurePivotTableLayoutCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private readonly IReadOnlyList<PivotFieldModel> _rowFields;
    private readonly IReadOnlyList<PivotFieldModel> _columnFields;
    private readonly IReadOnlyList<PivotFieldModel> _pageFields;
    private readonly IReadOnlyList<PivotDataFieldModel> _dataFields;
    private PivotLayoutSnapshot? _snapshot;
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;

    public ConfigurePivotTableLayoutCommand(
        SheetId sheetId,
        string pivotTableName,
        IReadOnlyList<PivotFieldModel> rowFields,
        IReadOnlyList<PivotFieldModel> columnFields,
        IReadOnlyList<PivotFieldModel> pageFields,
        IReadOnlyList<PivotDataFieldModel> dataFields)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
        _rowFields = rowFields;
        _columnFields = columnFields;
        _pageFields = pageFields;
        _dataFields = dataFields;
    }

    public string Label => "Configure PivotTable Layout";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is null)
            return new CommandOutcome(false, "PivotTable was not found.");
        if (_rowFields.Count == 0)
            return new CommandOutcome(false, "PivotTable requires at least one row field.");
        if (_dataFields.Count == 0)
            return new CommandOutcome(false, "PivotTable requires at least one data field.");

        _snapshot = PivotLayoutSnapshot.Capture(pivotTable);
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.TargetRange);

        Replace(pivotTable.RowFields, _rowFields);
        Replace(pivotTable.ColumnFields, _columnFields);
        Replace(pivotTable.PageFields, _pageFields);
        Replace(pivotTable.DataFields, _dataFields);
        PivotTableRefreshService.Refresh(ctx.Workbook, sheet, pivotTable);
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

    private sealed record PivotLayoutSnapshot(
        IReadOnlyList<PivotFieldModel> RowFields,
        IReadOnlyList<PivotFieldModel> ColumnFields,
        IReadOnlyList<PivotFieldModel> PageFields,
        IReadOnlyList<PivotDataFieldModel> DataFields)
    {
        public static PivotLayoutSnapshot Capture(PivotTableModel pivotTable) =>
            new(
                pivotTable.RowFields.ToList(),
                pivotTable.ColumnFields.ToList(),
                pivotTable.PageFields.ToList(),
                pivotTable.DataFields.ToList());

        public void Restore(PivotTableModel pivotTable)
        {
            Replace(pivotTable.RowFields, RowFields);
            Replace(pivotTable.ColumnFields, ColumnFields);
            Replace(pivotTable.PageFields, PageFields);
            Replace(pivotTable.DataFields, DataFields);
        }
    }
}

public sealed class DrillDownPivotTableCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private readonly CellAddress _pivotCell;
    private SheetId? _detailSheetId;

    public DrillDownPivotTableCommand(SheetId sheetId, string pivotTableName, CellAddress pivotCell)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
        _pivotCell = pivotCell;
    }

    public string Label => "Show PivotTable Details";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is null)
            return new CommandOutcome(false, "PivotTable was not found.");

        var details = PivotTableRefreshService.ExtractDetailRows(ctx.Workbook, sheet, pivotTable, _pivotCell);
        if (details.Headers.Count == 0 || details.Rows.Count == 0)
            return new CommandOutcome(false, "No detail rows were found for this PivotTable cell.");

        var detailSheet = ctx.Workbook.AddSheet(GenerateDetailSheetName(ctx.Workbook));
        _detailSheetId = detailSheet.Id;
        for (var col = 0; col < details.Headers.Count; col++)
            detailSheet.SetCell(new CellAddress(detailSheet.Id, 1, (uint)col + 1), new TextValue(details.Headers[col]));
        for (var row = 0; row < details.Rows.Count; row++)
        for (var col = 0; col < details.Headers.Count; col++)
            detailSheet.SetCell(new CellAddress(detailSheet.Id, (uint)row + 2, (uint)col + 1), Cell.FromValue(details.Rows[row][col]));

        return new CommandOutcome(true, AffectedCells: [new CellAddress(detailSheet.Id, 1, 1)]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_detailSheetId is { } detailSheetId)
            ctx.Workbook.RemoveSheet(detailSheetId);
        _detailSheetId = null;
    }

    private static string GenerateDetailSheetName(Workbook workbook)
    {
        for (var index = 1; index <= 10000; index++)
        {
            var name = index == 1 ? "Detail" : $"Detail{index}";
            if (workbook.ValidateSheetName(name) is null)
                return name;
        }

        return $"Detail{Guid.NewGuid():N}"[..31];
    }
}
