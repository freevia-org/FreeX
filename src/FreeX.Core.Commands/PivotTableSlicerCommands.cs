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
    private List<(Sheet Sheet, PivotTableModel PivotTable, List<(CellAddress Address, Cell? Cell)> Snapshot)>? _targetSnapshots;
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

        // R133x-commands-slicer-timeline-multipivot-runtime: a slicer can drive SEVERAL pivot tables at
        // once (Excel's "Report Connections") -- resolve every connection (ConnectedPivotTableNames,
        // primary first), not just the single primary SourcePivotTableName, or every connection past the
        // first silently stops being filtered by this control even though the R133 persistence fix keeps
        // the file recording all of them.
        var connectedNames = PivotTableSlicerTimelineCommandHelpers.ResolveConnectedPivotTableNames(
            slicer.SourcePivotTableName, slicer.ConnectedPivotTableNames);

        var targets = new List<(Sheet Sheet, PivotTableModel PivotTable)>();
        foreach (var name in connectedNames)
        {
            var resolved = PivotTableSlicerTimelineCommandHelpers.FindConnectedPivotTable(ctx.Workbook, name);
            if (resolved is null)
            {
                // The primary connection missing is a hard failure (pre-existing behavior); a stale
                // secondary connection (e.g. a pivot table deleted outside this slicer's knowledge) is
                // skipped so the still-valid connections keep working.
                if (string.Equals(name, slicer.SourcePivotTableName, StringComparison.OrdinalIgnoreCase))
                    return PivotTableSlicerTimelineCommandGuards.ConnectedPivotTableNotFound();
                continue;
            }

            targets.Add(resolved.Value);
        }

        if (targets.Count == 0)
            return PivotTableSlicerTimelineCommandGuards.ConnectedPivotTableNotFound();

        // Check protection of BOTH each connected pivot table's own sheet AND the sheet the slicer
        // widget itself is anchored on (slicer.SourceSheetName) — they can differ when the slicer is
        // placed on a dashboard sheet that filters pivots living elsewhere. Validate every target BEFORE
        // mutating anything so a protected connection blocks the whole selection change, not just part
        // of it.
        foreach (var (targetSheet, _) in targets)
        {
            if (PivotTableSlicerTimelineCommandGuards.RejectIfEitherSheetProtected(ctx.Workbook, targetSheet, slicer.SourceSheetName) is { } protectedOutcome)
                return protectedOutcome;
        }

        var resolvedTargets = new List<(Sheet Sheet, PivotTableModel PivotTable, int SourceFieldIndex)>();
        foreach (var (targetSheet, pivotTable) in targets)
        {
            var sourceSheet = ctx.Workbook.GetSheet(pivotTable.SourceRange.Start.Sheet) ?? targetSheet;
            var headers = PivotTableSlicerTimelineCommandHelpers.ReadPivotHeaders(sourceSheet, pivotTable);
            var sourceFieldIndex = PivotTableSlicerCommandLookups.FindSourceFieldIndex(
                headers,
                slicer.SourceFieldName,
                StringComparison.OrdinalIgnoreCase);
            if (sourceFieldIndex < 0)
            {
                if (string.Equals(pivotTable.Name, slicer.SourcePivotTableName, StringComparison.OrdinalIgnoreCase))
                    return PivotTableSlicerTimelineCommandGuards.ConnectedPivotTableFieldNotFound();
                continue;
            }

            resolvedTargets.Add((targetSheet, pivotTable, sourceFieldIndex));
        }

        if (resolvedTargets.Count == 0)
            return PivotTableSlicerTimelineCommandGuards.ConnectedPivotTableFieldNotFound();

        _snapshot = SlicerSelectionSnapshot.Capture(slicer, resolvedTargets);
        _targetSnapshots = resolvedTargets
            .Select(t => (t.Sheet, t.PivotTable, AddPivotTableCommand.Snapshot(t.Sheet, t.PivotTable.LastRenderedRange ?? t.PivotTable.TargetRange)))
            .ToList();

        slicer.SelectedItems.Clear();
        slicer.SelectedItems.AddRange(_selectedItems.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.CurrentCultureIgnoreCase));
        // This command is the ONLY place a user selection change (including a Clear-Filter, which
        // passes an empty list) reaches the model, so mark the selection as explicitly captured — an
        // empty SelectedItems from here on means "user cleared to select-all", not "never touched".
        slicer.SelectionCaptured = true;

        foreach (var (targetSheet, pivotTable, sourceFieldIndex) in resolvedTargets)
        {
            // H10: a slicer can be connected to a field that was never dragged into Row/Column/PageFields.
            // Excel still filters the pivot in that case (the field acts as a page/report filter); without
            // this, ReplaceSelectedItems below would be a no-op against all three lists and the command
            // would report success while leaving the pivot completely unfiltered.
            PivotTableSlicerTimelineCommandHelpers.EnsureFieldInLayout(pivotTable.RowFields, pivotTable.ColumnFields, pivotTable.PageFields, sourceFieldIndex);
            PivotTableSlicerTimelineCommandHelpers.ReplaceSelectedItems(pivotTable.RowFields, sourceFieldIndex, slicer.SelectedItems);
            PivotTableSlicerTimelineCommandHelpers.ReplaceSelectedItems(pivotTable.ColumnFields, sourceFieldIndex, slicer.SelectedItems);
            PivotTableSlicerTimelineCommandHelpers.ReplaceSelectedItems(pivotTable.PageFields, sourceFieldIndex, slicer.SelectedItems);

            PivotTableRefreshService.Refresh(ctx.Workbook, targetSheet, pivotTable);
        }

        return new CommandOutcome(true, AffectedCells: resolvedTargets.Select(t => t.PivotTable.TargetRange.Start).ToArray());
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

        if (slicer is not null && _snapshot is not null)
        {
            // Clear each affected pivot's CURRENT rendered range (post-Apply) before the snapshot
            // restores RowFields/ColumnFields/PageFields/LastRenderedRange back to their pre-Apply
            // values, mirroring the single-pivot original ordering for every connected pivot table.
            // Uses the DIRECT sheet/pivot-table object references captured at Apply time (not a
            // by-name re-lookup) so a rename applied after this command (e.g. RenamePivotTableCommand
            // sitting above this one on the undo stack) can never make the restore silently miss its
            // target -- the same live objects that were mutated are the ones restored.
            foreach (var snapshot in _snapshot.PivotTables)
                PivotTableRefreshService.ClearRenderedRange(snapshot.Sheet, snapshot.PivotTable.LastRenderedRange);

            _snapshot.Restore(slicer);

            if (_targetSnapshots is not null)
            {
                foreach (var (sheet, _, cellSnapshot) in _targetSnapshots)
                    AddPivotTableCommand.Restore(sheet, cellSnapshot);
            }
        }

        _snapshot = null;
        _targetSnapshots = null;
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

    /// <summary>
    /// R133x-commands-slicer-timeline-multipivot-runtime: the slicer-level selection (SelectedItems/
    /// SelectionCaptured) is captured ONCE, but the pivot-table-level layout (RowFields/ColumnFields/
    /// PageFields/LastRenderedRange) is captured per CONNECTED pivot table -- a single slicer selection
    /// can mutate several pivot tables at once, and undo must restore every one of them, not just the
    /// primary connection. Each entry holds a DIRECT reference to the mutated <see cref="PivotTableModel"/>
    /// (not its name), so restoring never depends on a by-name re-lookup that a rename applied between
    /// Apply and Revert (by another command sitting above this one on the undo stack) could invalidate.
    /// </summary>
    private sealed record SlicerSelectionSnapshot(
        IReadOnlyList<string> SelectedItems,
        bool SelectionCaptured,
        IReadOnlyList<PivotTableFieldsSnapshot> PivotTables)
    {
        public static SlicerSelectionSnapshot Capture(
            SlicerModel slicer, IReadOnlyList<(Sheet Sheet, PivotTableModel PivotTable, int SourceFieldIndex)> targets) =>
            new(
                slicer.SelectedItems.ToList(),
                slicer.SelectionCaptured,
                targets.Select(t => new PivotTableFieldsSnapshot(
                    t.Sheet,
                    t.PivotTable,
                    t.PivotTable.RowFields.ToList(),
                    t.PivotTable.ColumnFields.ToList(),
                    t.PivotTable.PageFields.ToList(),
                    t.PivotTable.LastRenderedRange)).ToList());

        public void Restore(SlicerModel slicer)
        {
            slicer.SelectedItems.Clear();
            slicer.SelectedItems.AddRange(SelectedItems);
            slicer.SelectionCaptured = SelectionCaptured;

            foreach (var snapshot in PivotTables)
            {
                var pivotTable = snapshot.PivotTable;
                PivotTableCommandCollections.Replace(pivotTable.RowFields, snapshot.RowFields);
                PivotTableCommandCollections.Replace(pivotTable.ColumnFields, snapshot.ColumnFields);
                PivotTableCommandCollections.Replace(pivotTable.PageFields, snapshot.PageFields);
                pivotTable.LastRenderedRange = snapshot.LastRenderedRange;
            }
        }
    }

    private sealed record PivotTableFieldsSnapshot(
        Sheet Sheet,
        PivotTableModel PivotTable,
        IReadOnlyList<PivotFieldModel> RowFields,
        IReadOnlyList<PivotFieldModel> ColumnFields,
        IReadOnlyList<PivotFieldModel> PageFields,
        GridRange? LastRenderedRange);
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
