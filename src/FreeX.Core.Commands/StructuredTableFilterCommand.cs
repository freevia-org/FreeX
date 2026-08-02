using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class ApplyStructuredTableFiltersCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _tableId;
    private HashSet<uint>? _previousFilterHiddenRows;

    public string Label => "Apply Table Filter";

    public ApplyStructuredTableFiltersCommand(SheetId sheetId, int tableId)
    {
        _sheetId = sheetId;
        _tableId = tableId;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UseAutoFilter) is { } protectedOutcome)
            return protectedOutcome;

        if (!CommandGuards.TryFindStructuredTable(sheet, _tableId, out var table))
            return CommandGuards.RejectStructuredTableNotFound();

        var filters = BuildFilters(table);
        if (filters is null)
            return new CommandOutcome(false, "Table filter refers to a missing column.");

        if (FilterHiddenRowsAlreadyMatch(sheet, table.Range, table.TotalsRowShown, filters))
            return new CommandOutcome(true);

        _previousFilterHiddenRows = [.. sheet.FilterHiddenRows];

        ApplyFilters(sheet, table, filters);

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousFilterHiddenRows is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        sheet.FilterHiddenRows.Clear();
        sheet.FilterHiddenRows.UnionWith(_previousFilterHiddenRows);
    }

    /// <summary>
    /// R115: shared entry point used by <see cref="ResizeStructuredTableCommand"/> so it can recompute
    /// a table's own FilterHiddenRows contribution in-place after reconciling FilterColumns down to a
    /// narrower column span, without going through this command's own Apply/Revert cycle (the caller
    /// already owns snapshotting/undo for sheet.FilterHiddenRows around its whole resize operation).
    /// Mirrors this command's own Apply body exactly -- a column that fell out of the table's range no
    /// longer contributes a filter criterion (its FilterColumns entry is already gone by the time this
    /// runs), so any row that was hidden solely because of it reappears here, matching Excel scoping
    /// AutoFilter state to a table's CURRENT column set. Returns false only when
    /// <paramref name="table"/>'s own FilterColumns still reference a column outside its current
    /// range (a caller bug -- reconcile FilterColumns before calling this).
    /// </summary>
    internal static bool RecomputeHiddenRows(Sheet sheet, StructuredTableModel table)
    {
        var filters = BuildFilters(table);
        if (filters is null)
            return false;

        if (FilterHiddenRowsAlreadyMatch(sheet, table.Range, table.TotalsRowShown, filters))
            return true;

        ApplyFilters(sheet, table, filters);
        return true;
    }

    private static void ApplyFilters(Sheet sheet, GridRange range, bool totalsRowShown, IReadOnlyList<TableFilterState> filters)
    {
        RemoveExistingFilterRows(sheet, range, totalsRowShown);

        if (filters.Count == 0)
            return;

        var lastDataRow = LastDataRow(range, totalsRowShown);
        for (var row = range.Start.Row + 1; row <= lastDataRow; row++)
        {
            if (!RowMatchesAllFilters(sheet, row, filters))
                sheet.FilterHiddenRows.Add(row);
        }
    }

    private static void ApplyFilters(Sheet sheet, StructuredTableModel table, IReadOnlyList<TableFilterState> filters) =>
        ApplyFilters(sheet, table.Range, table.TotalsRowShown, filters);

    private static List<TableFilterState>? BuildFilters(StructuredTableModel table)
    {
        var filters = new List<TableFilterState>(table.FilterColumns.Count);
        foreach (var filterColumn in table.FilterColumns)
        {
            // R106-commands-autofilter-table-sync-1: TopBottomFilterCommand/FilterConditionCommand
            // now also write a FilterColumns entry for their own criterion kinds (Top10/DynamicFilter
            // as raw NativeFilterXmls passthrough, custom comparisons as CustomFilters) -- neither is
            // representable as a plain value-list AllowedValues match, and both are already enforced
            // live via sheet.ColumnFilterOwnedRows (which RemoveExistingFilterRows above already
            // consults to avoid un-hiding their rows). Reconstructing them here as an
            // AllowedValues-from-Values filter would wrongly treat "no Values recorded" as "hide
            // every row in this column", corrupting rows this table's OWN value-list filters have no
            // opinion on. Skip them; only genuine value-list entries participate in this rebuild.
            if (filterColumn.CustomFilters.Count > 0 || filterColumn.NativeFilterXmls.Count > 0)
                continue;

            var tableColumnIndex = filterColumn.ColumnId;
            if (tableColumnIndex < 0 || tableColumnIndex >= table.Columns.Count)
                return null;

            filters.Add(new TableFilterState(
                table.Range.Start.Col + (uint)tableColumnIndex,
                FilterAllowedValueMatcher.Create(filterColumn.Values),
                filterColumn.Values.Count,
                filterColumn.IncludeBlank));
        }

        if (filters.Count > 1)
            filters.Sort(static (left, right) => left.EstimatedSelectivity.CompareTo(right.EstimatedSelectivity));

        return filters;
    }

    private static uint LastDataRow(GridRange range, bool totalsRowShown) =>
        totalsRowShown && range.End.Row > range.Start.Row ? range.End.Row - 1 : range.End.Row;

    /// <summary>
    /// Un-hides the table's data rows so they can be recomputed against the current value-list
    /// filters — but only rows this table's value filters are actually responsible for. A row also
    /// hidden by a Top-10/Above-Average/condition/color filter on some column (tracked in
    /// <see cref="Sheet.ColumnFilterOwnedRows"/>) must stay hidden; those mechanisms don't
    /// participate in <see cref="BuildFilters"/> at all, so blindly clearing every row in range would
    /// silently discard their filtering the moment this table's own filters are re-applied. Mirrors
    /// <see cref="FilterCommand.RecomputeHiddenRows"/>'s ownership guard for the same reason
    /// (finding R21-autofilter-sort-state-2).
    /// </summary>
    private static void RemoveExistingFilterRows(Sheet sheet, GridRange range, bool totalsRowShown)
    {
        var filterHiddenRows = sheet.FilterHiddenRows;
        var firstDataRow = range.Start.Row + 1;
        var lastDataRow = LastDataRow(range, totalsRowShown);
        if (filterHiddenRows.Count < range.RowCount)
        {
            filterHiddenRows.RemoveWhere(row => row >= firstDataRow && row <= lastDataRow &&
                !FilterHiddenRowUpdater.IsHiddenByAnyColumnOwnedFilter(sheet, row));
            return;
        }

        for (var row = firstDataRow; row <= lastDataRow; row++)
        {
            if (!FilterHiddenRowUpdater.IsHiddenByAnyColumnOwnedFilter(sheet, row))
                filterHiddenRows.Remove(row);
        }
    }

    private static bool RowMatchesAllFilters(Sheet sheet, uint row, IReadOnlyList<TableFilterState> filters)
    {
        for (var index = 0; index < filters.Count; index++)
        {
            var filter = filters[index];
            var text = FilterValueFormatter.ToText(sheet.GetValue(row, filter.Column));
            if (text.Length == 0 && filter.IncludeBlank)
                continue;

            if (!filter.AllowedValues.Contains(text))
                return false;
        }

        return true;
    }

    private static bool FilterHiddenRowsAlreadyMatch(Sheet sheet, GridRange range, bool totalsRowShown, IReadOnlyList<TableFilterState> filters)
    {
        var lastDataRow = LastDataRow(range, totalsRowShown);
        for (var row = range.Start.Row + 1; row <= lastDataRow; row++)
        {
            var shouldBeHidden = filters.Count > 0 && !RowMatchesAllFilters(sheet, row, filters);
            if (sheet.FilterHiddenRows.Contains(row) != shouldBeHidden)
                return false;
        }

        return true;
    }

    private sealed record TableFilterState(
        uint Column,
        FilterAllowedValueMatcher AllowedValues,
        int AllowedValueCount,
        bool IncludeBlank)
    {
        public int EstimatedSelectivity => AllowedValueCount + (IncludeBlank ? 1 : 0);
    }
}
