using FreeX.Core.Model;

namespace FreeX.Core.Commands;

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
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UsePivotTableReports) is { } protectedOutcome)
            return protectedOutcome;

        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is null)
            return new CommandOutcome(false, "PivotTable was not found.");
        if (_dataFields.Count == 0)
            return new CommandOutcome(false, "PivotTable requires at least one data field.");

        _snapshot = PivotLayoutSnapshot.Capture(pivotTable);
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.TargetRange);
        var viewState = PruneViewStateForLayout(pivotTable, _rowFields, _columnFields, _dataFields);

        PivotTableCommandCollections.Replace(pivotTable.RowFields, _rowFields);
        PivotTableCommandCollections.Replace(pivotTable.ColumnFields, _columnFields);
        PivotTableCommandCollections.Replace(pivotTable.PageFields, _pageFields);
        PivotTableCommandCollections.Replace(pivotTable.DataFields, _dataFields);
        PivotTableCommandCollections.Replace(pivotTable.LabelFilters, viewState.LabelFilters);
        PivotTableCommandCollections.Replace(pivotTable.ValueFilters, viewState.ValueFilters);
        PivotTableCommandCollections.Replace(pivotTable.Sorts, viewState.Sorts);
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

    private sealed record PivotLayoutSnapshot(
        IReadOnlyList<PivotFieldModel> RowFields,
        IReadOnlyList<PivotFieldModel> ColumnFields,
        IReadOnlyList<PivotFieldModel> PageFields,
        IReadOnlyList<PivotDataFieldModel> DataFields,
        IReadOnlyList<PivotLabelFilterModel> LabelFilters,
        IReadOnlyList<PivotValueFilterModel> ValueFilters,
        IReadOnlyList<PivotSortModel> Sorts)
    {
        public static PivotLayoutSnapshot Capture(PivotTableModel pivotTable) =>
            new(
                pivotTable.RowFields.ToList(),
                pivotTable.ColumnFields.ToList(),
                pivotTable.PageFields.ToList(),
                pivotTable.DataFields.ToList(),
                pivotTable.LabelFilters.ToList(),
                pivotTable.ValueFilters.ToList(),
                pivotTable.Sorts.ToList());

        public void Restore(PivotTableModel pivotTable)
        {
            PivotTableCommandCollections.Replace(pivotTable.RowFields, RowFields);
            PivotTableCommandCollections.Replace(pivotTable.ColumnFields, ColumnFields);
            PivotTableCommandCollections.Replace(pivotTable.PageFields, PageFields);
            PivotTableCommandCollections.Replace(pivotTable.DataFields, DataFields);
            PivotTableCommandCollections.Replace(pivotTable.LabelFilters, LabelFilters);
            PivotTableCommandCollections.Replace(pivotTable.ValueFilters, ValueFilters);
            PivotTableCommandCollections.Replace(pivotTable.Sorts, Sorts);
        }
    }

    private sealed record PivotViewState(
        IReadOnlyList<PivotLabelFilterModel> LabelFilters,
        IReadOnlyList<PivotValueFilterModel> ValueFilters,
        IReadOnlyList<PivotSortModel> Sorts);

    private static PivotViewState PruneViewStateForLayout(
        PivotTableModel pivotTable,
        IReadOnlyList<PivotFieldModel> rowFields,
        IReadOnlyList<PivotFieldModel> columnFields,
        IReadOnlyList<PivotDataFieldModel> dataFields)
    {
        var visibleFieldIndexes = rowFields
            .Concat(columnFields)
            .Select(field => field.SourceFieldIndex)
            .ToHashSet();
        var dataFieldIndexMap = BuildDataFieldIndexMap(pivotTable.DataFields, dataFields);

        var labelFilters = pivotTable.LabelFilters
            .Where(filter => visibleFieldIndexes.Contains(filter.SourceFieldIndex))
            .ToList();
        var valueFilters = pivotTable.ValueFilters
            .Select(filter => RemapValueFilter(filter, dataFieldIndexMap, visibleFieldIndexes))
            .OfType<PivotValueFilterModel>()
            .ToList();
        var sorts = pivotTable.Sorts
            .Select(sort => RemapSort(sort, dataFieldIndexMap, visibleFieldIndexes))
            .OfType<PivotSortModel>()
            .ToList();

        return new PivotViewState(labelFilters, valueFilters, sorts);
    }

    private static Dictionary<int, int> BuildDataFieldIndexMap(
        IReadOnlyList<PivotDataFieldModel> oldFields,
        IReadOnlyList<PivotDataFieldModel> newFields)
    {
        var result = new Dictionary<int, int>();
        var matchedNewIndexes = new HashSet<int>();
        for (var oldIndex = 0; oldIndex < oldFields.Count; oldIndex++)
        {
            var newIndex = FindMatchingDataFieldIndex(oldFields[oldIndex], newFields, matchedNewIndexes);
            if (newIndex < 0)
                continue;

            result[oldIndex] = newIndex;
            matchedNewIndexes.Add(newIndex);
        }

        return result;
    }

    private static int FindMatchingDataFieldIndex(
        PivotDataFieldModel oldField,
        IReadOnlyList<PivotDataFieldModel> newFields,
        IReadOnlySet<int> matchedNewIndexes)
    {
        var exact = FindDataFieldIndex(newFields, matchedNewIndexes, field => field == oldField);
        if (exact >= 0)
            return exact;

        var sameName = FindDataFieldIndex(newFields, matchedNewIndexes, field =>
            IsSameDataSource(field, oldField) &&
            string.Equals(field.Name, oldField.Name, StringComparison.OrdinalIgnoreCase));
        if (sameName >= 0)
            return sameName;

        var sameSummary = FindDataFieldIndex(newFields, matchedNewIndexes, field =>
            IsSameDataSource(field, oldField) &&
            string.Equals(field.SummaryFunction, oldField.SummaryFunction, StringComparison.OrdinalIgnoreCase));
        if (sameSummary >= 0)
            return sameSummary;

        return FindDataFieldIndex(newFields, matchedNewIndexes, field => IsSameDataSource(field, oldField));
    }

    private static int FindDataFieldIndex(
        IReadOnlyList<PivotDataFieldModel> fields,
        IReadOnlySet<int> matchedIndexes,
        Func<PivotDataFieldModel, bool> predicate)
    {
        for (var index = 0; index < fields.Count; index++)
        {
            if (!matchedIndexes.Contains(index) && predicate(fields[index]))
                return index;
        }

        return -1;
    }

    private static bool IsSameDataSource(PivotDataFieldModel field, PivotDataFieldModel other) =>
        field.SourceFieldIndex == other.SourceFieldIndex &&
        string.Equals(field.CalculatedFieldName, other.CalculatedFieldName, StringComparison.OrdinalIgnoreCase);

    private static PivotValueFilterModel? RemapValueFilter(
        PivotValueFilterModel filter,
        IReadOnlyDictionary<int, int> dataFieldIndexMap,
        IReadOnlySet<int> visibleFieldIndexes)
    {
        if (!dataFieldIndexMap.TryGetValue(filter.DataFieldIndex, out var newDataFieldIndex))
            return null;
        if (filter.SourceFieldIndex is { } sourceFieldIndex && !visibleFieldIndexes.Contains(sourceFieldIndex))
            return null;

        return filter with { DataFieldIndex = newDataFieldIndex };
    }

    private static PivotSortModel? RemapSort(
        PivotSortModel sort,
        IReadOnlyDictionary<int, int> dataFieldIndexMap,
        IReadOnlySet<int> visibleFieldIndexes)
    {
        if (!visibleFieldIndexes.Contains(sort.FieldIndex))
            return null;

        if (sort.Target != PivotSortTarget.Value)
            return sort;

        return dataFieldIndexMap.TryGetValue(sort.DataFieldIndex, out var newDataFieldIndex)
            ? sort with { DataFieldIndex = newDataFieldIndex }
            : null;
    }
}
