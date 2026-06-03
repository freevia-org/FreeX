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

        var table = sheet.StructuredTables.FirstOrDefault(candidate => candidate.Id == _tableId);
        if (table is null)
            return new CommandOutcome(false, "Table was not found.");

        var filters = BuildFilters(table);
        if (filters is null)
            return new CommandOutcome(false, "Table filter refers to a missing column.");

        if (FilterHiddenRowsAlreadyMatch(sheet, table.Range, filters))
            return new CommandOutcome(true);

        _previousFilterHiddenRows = [.. sheet.FilterHiddenRows];

        RemoveExistingFilterRows(sheet.FilterHiddenRows, table.Range);

        if (filters.Count == 0)
            return new CommandOutcome(true);

        for (var row = table.Range.Start.Row + 1; row <= table.Range.End.Row; row++)
        {
            if (!RowMatchesAllFilters(sheet, row, filters))
                sheet.FilterHiddenRows.Add(row);
        }

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

    private static List<TableFilterState>? BuildFilters(StructuredTableModel table)
    {
        var filters = new List<TableFilterState>(table.FilterColumns.Count);
        foreach (var filterColumn in table.FilterColumns)
        {
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

    private static void RemoveExistingFilterRows(HashSet<uint> filterHiddenRows, GridRange range)
    {
        var firstDataRow = range.Start.Row + 1;
        var lastDataRow = range.End.Row;
        if (filterHiddenRows.Count < range.RowCount)
        {
            filterHiddenRows.RemoveWhere(row => row >= firstDataRow && row <= lastDataRow);
            return;
        }

        for (var row = firstDataRow; row <= lastDataRow; row++)
            filterHiddenRows.Remove(row);
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

    private static bool FilterHiddenRowsAlreadyMatch(Sheet sheet, GridRange range, IReadOnlyList<TableFilterState> filters)
    {
        for (var row = range.Start.Row + 1; row <= range.End.Row; row++)
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
