using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class ConfigurePivotTableCalculatedItemsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private readonly IReadOnlyList<PivotFieldModel> _rowFields;
    private readonly IReadOnlyList<PivotFieldModel> _columnFields;
    private readonly IReadOnlyList<PivotFieldModel> _pageFields;
    private readonly IReadOnlyList<PivotCalculatedFieldModel> _calculatedFields;
    private readonly IReadOnlyList<PivotCalculatedItemModel> _calculatedItems;
    private PivotCalculatedItemsSnapshot? _snapshot;
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;

    public ConfigurePivotTableCalculatedItemsCommand(
        SheetId sheetId,
        string pivotTableName,
        IReadOnlyList<PivotFieldModel> rowFields,
        IReadOnlyList<PivotFieldModel> columnFields,
        IReadOnlyList<PivotFieldModel> pageFields,
        IReadOnlyList<PivotCalculatedFieldModel> calculatedFields,
        IReadOnlyList<PivotCalculatedItemModel> calculatedItems)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
        _rowFields = rowFields;
        _columnFields = columnFields;
        _pageFields = pageFields;
        _calculatedFields = calculatedFields;
        _calculatedItems = calculatedItems;
    }

    public string Label => "Configure PivotTable Calculations";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UsePivotTableReports) is { } protectedOutcome)
            return protectedOutcome;

        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is null)
            return new CommandOutcome(false, "PivotTable was not found.");

        var fieldCount = checked((int)pivotTable.SourceRange.ColCount);
        if (_rowFields.Concat(_columnFields).Concat(_pageFields)
                .Any(field => field.SourceFieldIndex < 0 || field.SourceFieldIndex >= fieldCount) ||
            _calculatedItems.Any(item => item.SourceFieldIndex < 0 || item.SourceFieldIndex >= fieldCount))
        {
            return new CommandOutcome(false, "PivotTable field index is outside the source range.");
        }

        if (_calculatedFields.Any(field => string.IsNullOrWhiteSpace(field.Name) || string.IsNullOrWhiteSpace(field.Formula)) ||
            _calculatedItems.Any(item => string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Formula)))
        {
            return new CommandOutcome(false, "Calculated field and item names and formulas are required.");
        }

        _snapshot = PivotCalculatedItemsSnapshot.Capture(pivotTable);
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.TargetRange);

        Replace(pivotTable.RowFields, _rowFields);
        Replace(pivotTable.ColumnFields, _columnFields);
        Replace(pivotTable.PageFields, _pageFields);
        Replace(pivotTable.CalculatedFields, _calculatedFields);
        Replace(pivotTable.CalculatedItems, _calculatedItems);

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

    private sealed record PivotCalculatedItemsSnapshot(
        IReadOnlyList<PivotFieldModel> RowFields,
        IReadOnlyList<PivotFieldModel> ColumnFields,
        IReadOnlyList<PivotFieldModel> PageFields,
        IReadOnlyList<PivotCalculatedFieldModel> CalculatedFields,
        IReadOnlyList<PivotCalculatedItemModel> CalculatedItems)
    {
        public static PivotCalculatedItemsSnapshot Capture(PivotTableModel pivotTable) =>
            new(
                pivotTable.RowFields.ToList(),
                pivotTable.ColumnFields.ToList(),
                pivotTable.PageFields.ToList(),
                pivotTable.CalculatedFields.ToList(),
                pivotTable.CalculatedItems.ToList());

        public void Restore(PivotTableModel pivotTable)
        {
            Replace(pivotTable.RowFields, RowFields);
            Replace(pivotTable.ColumnFields, ColumnFields);
            Replace(pivotTable.PageFields, PageFields);
            Replace(pivotTable.CalculatedFields, CalculatedFields);
            Replace(pivotTable.CalculatedItems, CalculatedItems);
        }
    }
}

public sealed class ChangePivotTableSourceCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private readonly GridRange _sourceRange;
    private PivotSourceSnapshot? _snapshot;
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;

    public ChangePivotTableSourceCommand(SheetId sheetId, string pivotTableName, GridRange sourceRange)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
        _sourceRange = sourceRange;
    }

    public string Label => "Change PivotTable Data Source";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_sourceRange.ColCount == 0 || _sourceRange.RowCount < 2)
            return new CommandOutcome(false, "PivotTable source range must include headers and data.");

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UsePivotTableReports) is { } protectedOutcome)
            return protectedOutcome;

        var sourceSheet = ctx.GetSheet(_sourceRange.Start.Sheet);
        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is null)
            return new CommandOutcome(false, "PivotTable was not found.");

        var fieldCount = checked((int)_sourceRange.ColCount);
        if (pivotTable.RowFields.Concat(pivotTable.ColumnFields).Concat(pivotTable.PageFields)
                .Any(field => field.SourceFieldIndex < 0 || field.SourceFieldIndex >= fieldCount) ||
            pivotTable.DataFields.Any(field => field.SourceFieldIndex < 0 || field.SourceFieldIndex >= fieldCount))
        {
            return new CommandOutcome(false, "Existing PivotTable fields are outside the new source range.");
        }

        var cache = ctx.Workbook.PivotCaches.FirstOrDefault(item => item.CacheId == pivotTable.CacheId);
        _snapshot = PivotSourceSnapshot.Capture(pivotTable, cache);
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.TargetRange);

        pivotTable.SourceRange = _sourceRange;
        if (cache is not null)
        {
            cache.SourceSheetName = sourceSheet.Name;
            cache.SourceReference = _sourceRange.ToString();
            cache.Fields.Clear();
            foreach (var header in ReadHeaders(sourceSheet, _sourceRange))
                cache.Fields.Add(new PivotCacheFieldModel(header));
        }

        PivotTableRefreshService.Refresh(ctx.Workbook, sheet, pivotTable);
        return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        var cache = pivotTable is null
            ? null
            : ctx.Workbook.PivotCaches.FirstOrDefault(item => item.CacheId == pivotTable.CacheId);
        if (pivotTable is not null && _snapshot is not null)
            _snapshot.Restore(pivotTable, cache);
        AddPivotTableCommand.Restore(sheet, _targetSnapshot);
        _snapshot = null;
        _targetSnapshot = null;
    }

    private static List<string> ReadHeaders(Sheet sheet, GridRange sourceRange)
    {
        var headers = new List<string>();
        for (var col = sourceRange.Start.Col; col <= sourceRange.End.Col; col++)
        {
            var value = sheet.GetValue(sourceRange.Start.Row, col);
            headers.Add(value is TextValue text && !string.IsNullOrWhiteSpace(text.Value)
                ? text.Value
                : $"Field{headers.Count + 1}");
        }

        return headers;
    }

    private sealed record PivotSourceSnapshot(
        GridRange SourceRange,
        string? CacheSourceSheetName,
        string? CacheSourceReference,
        string? CacheSourceTableName,
        IReadOnlyList<PivotCacheFieldModel> CacheFields)
    {
        public static PivotSourceSnapshot Capture(PivotTableModel pivotTable, PivotCacheModel? cache) =>
            new(
                pivotTable.SourceRange,
                cache?.SourceSheetName,
                cache?.SourceReference,
                cache?.SourceTableName,
                cache?.Fields.ToList() ?? []);

        public void Restore(PivotTableModel pivotTable, PivotCacheModel? cache)
        {
            pivotTable.SourceRange = SourceRange;
            if (cache is null)
                return;

            cache.SourceSheetName = CacheSourceSheetName;
            cache.SourceReference = CacheSourceReference;
            cache.SourceTableName = CacheSourceTableName;
            cache.Fields.Clear();
            cache.Fields.AddRange(CacheFields);
        }
    }
}

