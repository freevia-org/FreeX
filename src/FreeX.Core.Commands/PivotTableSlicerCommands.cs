using FreeX.Core.Model;

namespace FreeX.Core.Commands;

file static class PivotTableSlicerCommandLookups
{
    public static SlicerModel? FindSlicer(Workbook workbook, string? slicerName)
    {
        foreach (var slicer in workbook.Slicers)
        {
            if (string.Equals(slicer.Name, slicerName, StringComparison.OrdinalIgnoreCase))
                return slicer;
        }

        return null;
    }

    public static int FindSourceFieldIndex(IReadOnlyList<string> headers, string? sourceFieldName, StringComparison comparison)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            if (string.Equals(headers[index], sourceFieldName, comparison))
                return index;
        }

        return -1;
    }

    /// <summary>Finds the sheet + table for a table-connected slicer's <see cref="SlicerModel.SourceTableId"/>.</summary>
    public static (Sheet Sheet, StructuredTableModel Table)? FindSourceTable(Workbook workbook, int tableId)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (CommandGuards.TryFindStructuredTable(sheet, tableId, out var table))
                return (sheet, table);
        }

        return null;
    }

    /// <summary>Maps a table-slicer's <see cref="SlicerModel.SourceTableColumnId"/> to the table column's 0-based offset.</summary>
    public static int FindTableColumnOffset(StructuredTableModel table, int columnId)
    {
        for (var index = 0; index < table.Columns.Count; index++)
        {
            if (table.Columns[index].Id == columnId)
                return index;
        }

        return -1;
    }
}

public sealed class SetSlicerSelectionCommand : IWorkbookCommand
{
    private readonly string _slicerName;
    private readonly IReadOnlyList<string> _selectedItems;
    private SlicerSelectionSnapshot? _snapshot;
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;
    private TableSlicerSelectionSnapshot? _tableSnapshot;

    public SetSlicerSelectionCommand(string slicerName, IReadOnlyList<string> selectedItems)
    {
        _slicerName = slicerName;
        _selectedItems = selectedItems;
    }

    public string Label => "Set Slicer Selection";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var slicer = PivotTableSlicerCommandLookups.FindSlicer(ctx.Workbook, _slicerName);
        if (slicer is null)
            return new CommandOutcome(false, "Slicer was not found.");

        // H11: a Table slicer (SourceTableId/SourceTableColumnId set, no connected PivotTable) filters
        // its referenced structured table directly instead of a pivot field.
        if (slicer.SourceTableId is { } tableId && slicer.SourceTableColumnId is { } columnId)
            return ApplyTableSlicer(ctx, slicer, tableId, columnId);

        if (string.IsNullOrWhiteSpace(slicer.SourcePivotTableName) ||
            string.IsNullOrWhiteSpace(slicer.SourceFieldName))
        {
            return new CommandOutcome(false, "Slicer is not connected to a PivotTable field.");
        }

        var target = PivotTableSlicerTimelineCommandHelpers.FindConnectedPivotTable(ctx.Workbook, slicer.SourcePivotTableName);
        if (target is null)
            return PivotTableSlicerTimelineCommandGuards.ConnectedPivotTableNotFound();

        var (sheet, pivotTable) = target.Value;
        // Check protection of BOTH the pivot table's own sheet AND the sheet the slicer widget
        // itself is anchored on (slicer.SourceSheetName) — they can differ when the slicer is
        // placed on a dashboard sheet that filters a pivot living elsewhere.
        if (PivotTableSlicerTimelineCommandGuards.RejectIfEitherSheetProtected(ctx.Workbook, sheet, slicer.SourceSheetName) is { } protectedOutcome)
            return protectedOutcome;

        var sourceSheet = ctx.Workbook.GetSheet(pivotTable.SourceRange.Start.Sheet) ?? sheet;
        var headers = PivotTableSlicerTimelineCommandHelpers.ReadPivotHeaders(sourceSheet, pivotTable);
        var sourceFieldIndex = PivotTableSlicerCommandLookups.FindSourceFieldIndex(
            headers,
            slicer.SourceFieldName,
            StringComparison.OrdinalIgnoreCase);
        if (sourceFieldIndex < 0)
            return PivotTableSlicerTimelineCommandGuards.ConnectedPivotTableFieldNotFound();

        _snapshot = SlicerSelectionSnapshot.Capture(slicer, pivotTable);
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.LastRenderedRange ?? pivotTable.TargetRange);

        slicer.SelectedItems.Clear();
        slicer.SelectedItems.AddRange(_selectedItems.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.CurrentCultureIgnoreCase));
        // This command is the ONLY place a user selection change (including a Clear-Filter, which
        // passes an empty list) reaches the model, so mark the selection as explicitly captured — an
        // empty SelectedItems from here on means "user cleared to select-all", not "never touched".
        slicer.SelectionCaptured = true;
        // H10: a slicer can be connected to a field that was never dragged into Row/Column/PageFields.
        // Excel still filters the pivot in that case (the field acts as a page/report filter); without
        // this, ReplaceSelectedItems below would be a no-op against all three lists and the command
        // would report success while leaving the pivot completely unfiltered.
        PivotTableSlicerTimelineCommandHelpers.EnsureFieldInLayout(pivotTable.RowFields, pivotTable.ColumnFields, pivotTable.PageFields, sourceFieldIndex);
        PivotTableSlicerTimelineCommandHelpers.ReplaceSelectedItems(pivotTable.RowFields, sourceFieldIndex, slicer.SelectedItems);
        PivotTableSlicerTimelineCommandHelpers.ReplaceSelectedItems(pivotTable.ColumnFields, sourceFieldIndex, slicer.SelectedItems);
        PivotTableSlicerTimelineCommandHelpers.ReplaceSelectedItems(pivotTable.PageFields, sourceFieldIndex, slicer.SelectedItems);

        PivotTableRefreshService.Refresh(ctx.Workbook, sheet, pivotTable);
        return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start]);
    }

    private CommandOutcome ApplyTableSlicer(ICommandContext ctx, SlicerModel slicer, int tableId, int columnId)
    {
        var source = PivotTableSlicerCommandLookups.FindSourceTable(ctx.Workbook, tableId);
        if (source is null)
            return CommandGuards.RejectStructuredTableNotFound();

        var (sheet, table) = source.Value;
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UseAutoFilter) is { } protectedOutcome)
            return protectedOutcome;

        var columnOffset = PivotTableSlicerCommandLookups.FindTableColumnOffset(table, columnId);
        if (columnOffset < 0)
            return PivotTableSlicerTimelineCommandGuards.ConnectedPivotTableFieldNotFound();

        var normalizedSelection = _selectedItems
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _tableSnapshot = TableSlicerSelectionSnapshot.Capture(slicer, table, columnOffset);

        slicer.SelectedItems.Clear();
        slicer.SelectedItems.AddRange(normalizedSelection);
        slicer.SelectionCaptured = true;

        // Applying a value filter on the referenced table column is the Excel-equivalent of a table
        // slicer selection: it hides every row whose value in that column isn't selected, mirroring
        // FilterCommand/ApplyStructuredTableFiltersCommand's own "hide rows" mechanism instead of
        // inventing a parallel one.
        table.FilterColumns.RemoveAll(filter => filter.ColumnId == columnOffset);
        if (normalizedSelection.Count > 0)
            table.FilterColumns.Add(new StructuredTableFilterColumnModel(columnOffset, normalizedSelection));

        new ApplyStructuredTableFiltersCommand(sheet.Id, tableId).Apply(ctx);

        return new CommandOutcome(true, AffectedCells: [table.Range.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        var slicer = PivotTableSlicerCommandLookups.FindSlicer(ctx.Workbook, _slicerName);

        if (_tableSnapshot is { } tableSnapshot)
        {
            if (slicer is not null)
                tableSnapshot.Restore(ctx, slicer);

            _tableSnapshot = null;
            return;
        }

        var target = slicer?.SourcePivotTableName is null ? null : PivotTableSlicerTimelineCommandHelpers.FindConnectedPivotTable(ctx.Workbook, slicer.SourcePivotTableName);
        if (slicer is not null && target is { } connected && _snapshot is not null)
        {
            PivotTableRefreshService.ClearRenderedRange(connected.Sheet, connected.PivotTable.LastRenderedRange);
            _snapshot.Restore(slicer, connected.PivotTable);
            AddPivotTableCommand.Restore(connected.Sheet, _targetSnapshot);
        }

        _snapshot = null;
        _targetSnapshot = null;
    }

    private sealed record TableSlicerSelectionSnapshot(
        IReadOnlyList<string> SelectedItems,
        bool SelectionCaptured,
        int TableId,
        int ColumnOffset,
        StructuredTableFilterColumnModel? PreviousFilterColumn)
    {
        public static TableSlicerSelectionSnapshot Capture(SlicerModel slicer, StructuredTableModel table, int columnOffset) =>
            new(
                slicer.SelectedItems.ToList(),
                slicer.SelectionCaptured,
                table.Id,
                columnOffset,
                table.FilterColumns.FirstOrDefault(filter => filter.ColumnId == columnOffset));

        public void Restore(ICommandContext ctx, SlicerModel slicer)
        {
            slicer.SelectedItems.Clear();
            slicer.SelectedItems.AddRange(SelectedItems);
            slicer.SelectionCaptured = SelectionCaptured;

            if (PivotTableSlicerCommandLookups.FindSourceTable(ctx.Workbook, TableId) is not { } source)
                return;

            var (sheet, table) = source;
            table.FilterColumns.RemoveAll(filter => filter.ColumnId == ColumnOffset);
            if (PreviousFilterColumn is not null)
                table.FilterColumns.Add(PreviousFilterColumn);

            new ApplyStructuredTableFiltersCommand(sheet.Id, table.Id).Apply(ctx);
        }
    }

    private sealed record SlicerSelectionSnapshot(
        IReadOnlyList<string> SelectedItems,
        bool SelectionCaptured,
        IReadOnlyList<PivotFieldModel> RowFields,
        IReadOnlyList<PivotFieldModel> ColumnFields,
        IReadOnlyList<PivotFieldModel> PageFields,
        GridRange? LastRenderedRange)
    {
        public static SlicerSelectionSnapshot Capture(SlicerModel slicer, PivotTableModel pivotTable) =>
            new(
                slicer.SelectedItems.ToList(),
                slicer.SelectionCaptured,
                pivotTable.RowFields.ToList(),
                pivotTable.ColumnFields.ToList(),
                pivotTable.PageFields.ToList(),
                pivotTable.LastRenderedRange);

        public void Restore(SlicerModel slicer, PivotTableModel pivotTable)
        {
            slicer.SelectedItems.Clear();
            slicer.SelectedItems.AddRange(SelectedItems);
            slicer.SelectionCaptured = SelectionCaptured;
            PivotTableCommandCollections.Replace(pivotTable.RowFields, RowFields);
            PivotTableCommandCollections.Replace(pivotTable.ColumnFields, ColumnFields);
            PivotTableCommandCollections.Replace(pivotTable.PageFields, PageFields);
            pivotTable.LastRenderedRange = LastRenderedRange;
        }
    }
}

public sealed class AddSlicerCommand : IWorkbookCommand
{
    private readonly string _slicerName;
    private readonly string _pivotTableName;
    private readonly string _sourceFieldName;
    private SlicerModel? _addedSlicer;

    public AddSlicerCommand(string slicerName, string pivotTableName, string sourceFieldName)
    {
        _slicerName = slicerName;
        _pivotTableName = pivotTableName;
        _sourceFieldName = sourceFieldName;
    }

    public string Label => "Insert Slicer";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (string.IsNullOrWhiteSpace(_slicerName) ||
            string.IsNullOrWhiteSpace(_pivotTableName) ||
            string.IsNullOrWhiteSpace(_sourceFieldName))
        {
            return new CommandOutcome(false, "Slicer name, PivotTable, and field are required.");
        }

        if (PivotTableSlicerCommandLookups.FindSlicer(ctx.Workbook, _slicerName) is not null)
            return new CommandOutcome(false, "A slicer with that name already exists.");

        var target = PivotTableSlicerTimelineCommandHelpers.FindConnectedPivotTable(ctx.Workbook, _pivotTableName);
        if (target is null)
            return PivotTableSlicerTimelineCommandGuards.ConnectedPivotTableNotFound();
        if (CommandGuards.RejectIfProtectedWithoutPermission(target.Value.Sheet, SheetProtectionPermission.UsePivotTableReports) is { } protectedOutcome)
            return protectedOutcome;
        if (PivotTableSlicerTimelineCommandGuards.RejectIfEditObjectsBlocked(target.Value.Sheet) is { } objectProtectedOutcome)
            return objectProtectedOutcome;

        var sourceSheet = ctx.Workbook.GetSheet(target.Value.PivotTable.SourceRange.Start.Sheet) ?? target.Value.Sheet;
        var headers = PivotTableSlicerTimelineCommandHelpers.ReadPivotHeaders(sourceSheet, target.Value.PivotTable);
        var sourceFieldIndex = PivotTableSlicerCommandLookups.FindSourceFieldIndex(
            headers,
            _sourceFieldName,
            StringComparison.CurrentCultureIgnoreCase);
        if (sourceFieldIndex < 0)
            return PivotTableSlicerTimelineCommandGuards.ConnectedPivotTableFieldNotFound();

        var slicer = new SlicerModel
        {
            Name = _slicerName.Trim(),
            CacheName = $"Slicer_{PivotTableSlicerTimelineCommandHelpers.SanitizeCacheName(_slicerName, "Slicer")}",
            SourcePivotTableName = target.Value.PivotTable.Name,
            SourceFieldName = headers[sourceFieldIndex],
            DrawingAnchor = PivotTableFloatingControlAnchor.CreateDefault(target.Value.PivotTable),
            // R114-commands-pivot-sharedItems: SlicerItemResolver.ResolveAvailableItems only resolves a
            // pivot slicer's items when CacheItems is non-empty (mirroring the native
            // <data><tabular><items> list a loaded workbook's slicer cache carries) -- a freshly
            // inserted slicer with an empty CacheItems can never show any filter button, even once its
            // bound field's SharedItems is populated. Seed one cache item per shared item, all selected
            // (Excel's own "(All items selected)" initial state for a brand-new slicer).
            CacheItems = BuildInitialCacheItems(ctx.Workbook, target.Value.PivotTable, headers[sourceFieldIndex])
        };
        ctx.Workbook.Slicers.Add(slicer);
        _addedSlicer = slicer;
        return new CommandOutcome(true, AffectedCells: [target.Value.PivotTable.TargetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_addedSlicer is not null)
            ctx.Workbook.Slicers.Remove(_addedSlicer);
        _addedSlicer = null;
    }

    /// <summary>
    /// Seeds a freshly inserted pivot slicer's <see cref="SlicerModel.CacheItems"/> -- one entry per
    /// distinct value in the bound cache field's <see cref="PivotCacheFieldModel.SharedItems"/>, all
    /// selected -- so <see cref="SlicerItemResolver.ResolveAvailableItems"/> has something to resolve
    /// immediately, without requiring a save+reload round-trip first. Returns an empty list (matching
    /// the pre-fix behaviour) when the cache or field can't be resolved, or the field carries no shared
    /// items yet (e.g. an OLAP/external cache this codebase doesn't model shared items for).
    /// </summary>
    private static List<SlicerCacheItem> BuildInitialCacheItems(Workbook workbook, PivotTableModel pivotTable, string fieldName)
    {
        var cache = CommandGuards.FindPivotCache(workbook, pivotTable);
        var field = cache?.Fields.FirstOrDefault(candidate => string.Equals(candidate.Name, fieldName, StringComparison.OrdinalIgnoreCase));
        if (field?.SharedItems is not { Count: > 0 } sharedItems)
            return [];

        var items = new List<SlicerCacheItem>(sharedItems.Count);
        for (var index = 0; index < sharedItems.Count; index++)
            items.Add(new SlicerCacheItem(index, IsSelected: true));
        return items;
    }
}

internal static class PivotTableFloatingControlAnchor
{
    private const uint DefaultWidthColumns = 3;
    private const uint DefaultHeightRows = 8;

    public static DrawingAnchorRange CreateDefault(PivotTableModel pivotTable)
    {
        var fromColumn = pivotTable.TargetRange.End.Col;
        var fromRow = pivotTable.TargetRange.Start.Row > 0 ? pivotTable.TargetRange.Start.Row - 1 : 0;

        return new DrawingAnchorRange(
            new DrawingAnchorPoint(fromColumn, 0, fromRow, 0),
            new DrawingAnchorPoint(fromColumn + DefaultWidthColumns, 0, fromRow + DefaultHeightRows, 0));
    }
}
