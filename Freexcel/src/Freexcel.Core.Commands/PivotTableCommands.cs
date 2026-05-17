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
        _addedPivotTable = null;
        _addedCache = null;
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
}
