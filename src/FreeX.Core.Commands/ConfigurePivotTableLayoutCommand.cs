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
    // meta-F2 (round154): merged regions (e.g. from MergeAndCenterLabels) that overlapped the pivot's
    // OLD footprint before this command's RefreshGuarded call re-renders it, stripping them via
    // PivotTableRefreshService.ClearTargetRange's unconditional sheet.ReplaceMergedRegions(...Where(
    // !Overlaps)). _targetSnapshot only carries (CellAddress, Cell?) pairs -- AddPivotTableCommand.
    // Restore never touches MergedRegions -- so without this, Revert put the old footprint's cell
    // VALUES back but left it permanently un-merged. Mirrors MovePivotTableCommand's sweep92-F1 fix.
    private List<GridRange>? _oldMergedRegions;

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

        if (!CommandGuards.TryFindPivotTable(sheet, _pivotTableName, out var pivotTable))
            return CommandGuards.RejectPivotTableNotFound();
        if (_dataFields.Count == 0)
            return CommandGuards.RejectPivotTableRequiresDataField();

        _snapshot = PivotLayoutSnapshot.Capture(pivotTable);
        var oldFootprint = pivotTable.LastRenderedRange ?? pivotTable.TargetRange;
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, oldFootprint);
        // meta-F2 (round154): capture before RefreshGuarded (below) re-renders the pivot and strips any
        // merges overlapping the old footprint. Scoped to exactly what that re-render can clear -- not
        // the whole sheet -- so an unrelated merge elsewhere is never touched.
        _oldMergedRegions = sheet.MergedRegions.Where(region => region.Overlaps(oldFootprint)).ToList();
        var viewState = PruneViewStateForLayout(pivotTable, _rowFields, _columnFields, _dataFields);

        PivotTableCommandCollections.Replace(pivotTable.RowFields, _rowFields);
        PivotTableCommandCollections.Replace(pivotTable.ColumnFields, _columnFields);
        PivotTableCommandCollections.Replace(pivotTable.PageFields, _pageFields);
        PivotTableCommandCollections.Replace(pivotTable.DataFields, _dataFields);
        PivotTableCommandCollections.Replace(pivotTable.LabelFilters, viewState.LabelFilters);
        PivotTableCommandCollections.Replace(pivotTable.ValueFilters, viewState.ValueFilters);
        PivotTableCommandCollections.Replace(pivotTable.Sorts, viewState.Sorts);

        // R140-remediation-pivot-refresh-growth-guard-completeness: dragging a field into Rows/Columns
        // from the PivotTable Field List -- the single most common real-world way a pivot grows -- goes
        // through this command. Without this guard a layout change that needs more rows/columns than
        // the pivot's previous render occupied could silently overwrite adjacent user content with no
        // way to recover it via Undo (the snapshot above is bounded to the OLD footprint).
        var snapshot = _snapshot;
        var baseline = PivotTableRefreshService.CaptureGrowthGuardBaseline(sheet, pivotTable);
        if (PivotTableRefreshService.RefreshGuarded(ctx.Workbook, sheet, pivotTable, baseline, () => snapshot!.Restore(pivotTable)) is { } failure)
        {
            _snapshot = null;
            _targetSnapshot = null;
            _oldMergedRegions = null;
            return failure;
        }

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
        if (CommandGuards.TryFindPivotTable(sheet, _pivotTableName, out var pivotTable) && _snapshot is not null)
        {
            PivotTableRefreshService.ClearRenderedRange(sheet, pivotTable.LastRenderedRange);
            _snapshot.Restore(pivotTable);
        }
        AddPivotTableCommand.Restore(sheet, _targetSnapshot);
        // meta-F2 (round154): put back the old footprint's merged regions the re-render in Apply
        // stripped -- AddPivotTableCommand.Restore above only replays cell values, never MergedRegions.
        // ClearRenderedRange above only touched the CURRENT (post-Apply) rendered range, so the old
        // footprint is untouched by this Revert by the time we get here -- nothing to overlap or clobber.
        if (_oldMergedRegions is { Count: > 0 })
        {
            foreach (var region in _oldMergedRegions)
                sheet.AddMergedRegion(region);
        }
        if (pivotTable is not null)
            UpdateBoundPivotChartRanges(ctx.Workbook, sheet, pivotTable);
        _snapshot = null;
        _targetSnapshot = null;
        _oldMergedRegions = null;
    }

    private sealed record PivotLayoutSnapshot(
        IReadOnlyList<PivotFieldModel> RowFields,
        IReadOnlyList<PivotFieldModel> ColumnFields,
        IReadOnlyList<PivotFieldModel> PageFields,
        IReadOnlyList<PivotDataFieldModel> DataFields,
        IReadOnlyList<PivotLabelFilterModel> LabelFilters,
        IReadOnlyList<PivotValueFilterModel> ValueFilters,
        IReadOnlyList<PivotSortModel> Sorts,
        GridRange? LastRenderedRange)
    {
        public static PivotLayoutSnapshot Capture(PivotTableModel pivotTable) =>
            new(
                pivotTable.RowFields.ToList(),
                pivotTable.ColumnFields.ToList(),
                pivotTable.PageFields.ToList(),
                pivotTable.DataFields.ToList(),
                pivotTable.LabelFilters.ToList(),
                pivotTable.ValueFilters.ToList(),
                pivotTable.Sorts.ToList(),
                pivotTable.LastRenderedRange);

        public void Restore(PivotTableModel pivotTable)
        {
            PivotTableCommandCollections.Replace(pivotTable.RowFields, RowFields);
            PivotTableCommandCollections.Replace(pivotTable.ColumnFields, ColumnFields);
            PivotTableCommandCollections.Replace(pivotTable.PageFields, PageFields);
            PivotTableCommandCollections.Replace(pivotTable.DataFields, DataFields);
            PivotTableCommandCollections.Replace(pivotTable.LabelFilters, LabelFilters);
            PivotTableCommandCollections.Replace(pivotTable.ValueFilters, ValueFilters);
            PivotTableCommandCollections.Replace(pivotTable.Sorts, Sorts);
            pivotTable.LastRenderedRange = LastRenderedRange;
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
