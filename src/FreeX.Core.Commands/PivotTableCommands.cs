using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

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
        if (_targetRange.Start.Sheet != _sheetId || _targetRange.End.Sheet != _sheetId)
            return CommandGuards.RejectPivotTableTargetRangeOnTargetSheet();
        if (_sourceRange.ColCount == 0 || _sourceRange.RowCount < 2)
            return CommandGuards.RejectPivotTableSourceRangeRequiresHeaders();
        if (string.IsNullOrWhiteSpace(_name))
            return CommandGuards.RejectPivotTableNameRequired();

        var fieldCount = checked((int)_sourceRange.ColCount);
        if (!_rowFieldIndexes.Concat(_dataFieldIndexes).All(index => index >= 0 && index < fieldCount))
            return CommandGuards.RejectPivotTableFieldIndexOutsideSourceRange();
        if (_dataFieldIndexes.Count == 0)
            return CommandGuards.RejectPivotTableRequiresDataField();

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UsePivotTableReports) is { } protectedOutcome)
            return protectedOutcome;

        var sourceSheet = ctx.GetSheet(_sourceRange.Start.Sheet);
        _targetSnapshot = Snapshot(sheet, _targetRange);
        var headers = ReadHeaders(sourceSheet, fieldCount);
        var cacheId = NextCacheId(ctx.Workbook);
        var cache = new PivotCacheModel
        {
            CacheId = cacheId,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sourceSheet.Name,
            SourceReference = _sourceRange.ToString()
        };
        for (var index = 0; index < headers.Count; index++)
            cache.Fields.Add(BuildPivotCacheFieldFromSourceData(headers[index], sourceSheet, _sourceRange, index));

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
        {
            PivotTableRefreshService.ClearRenderedRange(sheet, _addedPivotTable.LastRenderedRange);
            sheet.PivotTables.Remove(_addedPivotTable);
        }
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

    // Builds a cache field whose sharedItems type/range metadata (ContainsNumber/ContainsDate/
    // ContainsString + MinValue/MaxValue/MinDate/MaxDate) reflects the actual source-column data at
    // creation time. Fields created via `new PivotCacheFieldModel(header)` alone leave every optional
    // flag at its all-false/null default, which serializes to a bare <sharedItems/> that Excel's schema
    // defaults interpret as containsString=true/containsNumber=false/containsDate=false -- always wrong
    // for a numeric or date column and breaking the field's filter dropdown / Group dialog bounds.
    private static PivotCacheFieldModel BuildPivotCacheFieldFromSourceData(
        string header,
        Sheet sourceSheet,
        GridRange sourceRange,
        int columnIndex)
    {
        var col = sourceRange.Start.Col + (uint)columnIndex;
        var containsString = false;
        var containsNumber = false;
        var containsDate = false;
        var containsBlank = false;
        double? minValue = null;
        double? maxValue = null;
        string? minDate = null;
        string? maxDate = null;

        for (var row = sourceRange.Start.Row + 1; row <= sourceRange.End.Row; row++)
        {
            switch (sourceSheet.GetValue(row, col))
            {
                case NumberValue number:
                    containsNumber = true;
                    if (minValue is null || number.Value < minValue.Value)
                        minValue = number.Value;
                    if (maxValue is null || number.Value > maxValue.Value)
                        maxValue = number.Value;
                    break;
                case DateTimeValue date:
                    containsDate = true;
                    var iso = date.ToDateTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
                    if (minDate is null || string.CompareOrdinal(iso, minDate) < 0)
                        minDate = iso;
                    if (maxDate is null || string.CompareOrdinal(iso, maxDate) > 0)
                        maxDate = iso;
                    break;
                case TextValue text when !string.IsNullOrEmpty(text.Value):
                    containsString = true;
                    break;
                case BoolValue:
                    containsString = true;
                    break;
                case ErrorValue:
                    containsString = true;
                    break;
                default:
                    containsBlank = true;
                    break;
            }
        }

        var typeCount = (containsString ? 1 : 0) + (containsNumber ? 1 : 0) + (containsDate ? 1 : 0);
        return new PivotCacheFieldModel(
            header,
            ContainsBlank: containsBlank,
            ContainsString: containsString,
            ContainsNumber: containsNumber,
            ContainsDate: containsDate,
            ContainsMixedTypes: typeCount > 1,
            MinValue: minValue,
            MaxValue: maxValue,
            MinDate: minDate,
            MaxDate: maxDate);
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

public sealed class AddPivotTableToNewWorksheetCommand : IWorkbookCommand
{
    public const uint InitialTargetRow = 3;
    public const uint InitialTargetColumn = 1;

    private readonly GridRange _sourceRange;
    private readonly string _name;
    private readonly IReadOnlyList<int> _rowFieldIndexes;
    private readonly IReadOnlyList<int> _dataFieldIndexes;
    private SheetId? _createdSheetId;
    private AddPivotTableCommand? _innerCommand;

    public string Label => "Insert PivotTable";
    public SheetId? CreatedSheetId => _createdSheetId;

    public AddPivotTableToNewWorksheetCommand(
        GridRange sourceRange,
        string name,
        IReadOnlyList<int> rowFieldIndexes,
        IReadOnlyList<int> dataFieldIndexes)
    {
        _sourceRange = sourceRange;
        _name = name;
        _rowFieldIndexes = rowFieldIndexes;
        _dataFieldIndexes = dataFieldIndexes;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        var sheet = ctx.Workbook.AddSheet(GetUniquePivotSheetName(ctx.Workbook));
        sheet.ResetViewStateToA1();
        _createdSheetId = sheet.Id;
        var targetRange = CreateInitialTargetRange(sheet.Id, _sourceRange, _rowFieldIndexes.Count, _dataFieldIndexes.Count);
        _innerCommand = new AddPivotTableCommand(
            sheet.Id,
            _sourceRange,
            targetRange,
            _name,
            _rowFieldIndexes,
            _dataFieldIndexes);

        var outcome = _innerCommand.Apply(ctx);
        if (outcome.Success)
            return outcome;

        ctx.Workbook.RemoveSheet(sheet.Id);
        _createdSheetId = null;
        _innerCommand = null;
        return outcome;
    }

    public void Revert(ICommandContext ctx)
    {
        if (_createdSheetId is null)
            return;

        _innerCommand?.Revert(ctx);
        ctx.Workbook.RemoveSheet(_createdSheetId.Value);
        _createdSheetId = null;
        _innerCommand = null;
    }

    private static GridRange CreateInitialTargetRange(SheetId sheetId, GridRange sourceRange, int rowFieldCount, int dataFieldCount)
    {
        var start = new CellAddress(sheetId, InitialTargetRow, InitialTargetColumn);
        var outputColumns = Math.Max(1, rowFieldCount) + Math.Max(1, dataFieldCount);
        var outputRows = Math.Max(3u, sourceRange.RowCount + 2);
        var endRow = Math.Min(CellAddress.MaxRow, (uint)Math.Min(uint.MaxValue, (ulong)start.Row + outputRows - 1));
        var endCol = Math.Min(CellAddress.MaxCol, (uint)Math.Min(uint.MaxValue, (ulong)start.Col + (uint)outputColumns - 1));
        var end = new CellAddress(
            sheetId,
            endRow,
            endCol);
        return new GridRange(start, end);
    }

    private static string GetUniquePivotSheetName(Workbook workbook)
    {
        const string baseName = "PivotTable";
        if (workbook.ValidateSheetName(baseName) is null)
            return baseName;

        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName} {i}";
            if (workbook.ValidateSheetName(candidate) is null)
                return candidate;
        }
    }
}

public sealed class RefreshPivotTableCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;
    private GridRange? _lastRenderedRangeSnapshot;

    public RefreshPivotTableCommand(SheetId sheetId, string pivotTableName)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
    }

    public string Label => "Refresh PivotTable";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UsePivotTableReports) is { } protectedOutcome)
            return protectedOutcome;

        if (!CommandGuards.TryFindPivotTable(sheet, _pivotTableName, out var pivotTable))
            return CommandGuards.RejectPivotTableNotFound();

        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.LastRenderedRange ?? pivotTable.TargetRange);
        _lastRenderedRangeSnapshot = pivotTable.LastRenderedRange;
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
        if (_targetSnapshot is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.TryFindPivotTable(sheet, _pivotTableName, out var pivotTable))
        {
            PivotTableRefreshService.ClearRenderedRange(sheet, pivotTable.LastRenderedRange);
            pivotTable.LastRenderedRange = _lastRenderedRangeSnapshot;
        }
        AddPivotTableCommand.Restore(sheet, _targetSnapshot);
        _targetSnapshot = null;
        _lastRenderedRangeSnapshot = null;
    }
}

