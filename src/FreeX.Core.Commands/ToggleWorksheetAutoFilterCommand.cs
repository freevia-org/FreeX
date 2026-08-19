using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class ToggleWorksheetAutoFilterCommand : IWorkbookCommand, IEstimatesMemory
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;

    private WorksheetAutoFilterModel? _previousAutoFilter;
    private uint[]? _previousFilterHiddenRows;
    // G6: sheet.ActiveValueFilterColumns/ValueFilterHiddenRows must roll back alongside AutoFilter/
    // FilterHiddenRows too, otherwise a stale per-column value-filter entry survives turning
    // AutoFilter off and silently resurrects on the next unrelated column's filter (see FilterCommand,
    // finding F8/G7, for why this state exists).
    private Dictionary<uint, IReadOnlyList<string>>? _previousActiveValueFilterColumns;
    private uint[]? _previousValueFilterHiddenRows;
    // R12-sort-filter-1: sheet.ColumnFilterOwnedRows (which rows each condition/average/top-bottom/
    // color column filter currently owns) must roll back alongside the value-filter state above for
    // the same reason (G6) — otherwise a stale per-column ownership entry survives turning AutoFilter
    // off and incorrectly keeps a row hidden the next time an unrelated column's filter re-evaluates it.
    private Dictionary<uint, HashSet<uint>>? _previousColumnFilterOwnedRows;

    public ToggleWorksheetAutoFilterCommand(SheetId sheetId, GridRange range)
    {
        _sheetId = sheetId;
        _range = range;
    }

    public string Label => "Toggle AutoFilter";

    public int EstimatedBytes =>
        256 +
        (_previousFilterHiddenRows?.Length ?? 0) * sizeof(uint) +
        (_previousAutoFilter?.FilterColumns.Count ?? 0) * 256;

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectInvalidFilterRange(_sheetId, _range, filterColOffset: 0) is { } invalidRange)
            return invalidRange;
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UseAutoFilter) is { } protectedOutcome)
            return protectedOutcome;

        _previousAutoFilter = WorksheetAutoFilterCloner.Clone(sheet.AutoFilter);
        _previousFilterHiddenRows = [.. sheet.FilterHiddenRows];
        _previousActiveValueFilterColumns = sheet.ActiveValueFilterColumns.Count == 0
            ? null
            : sheet.ActiveValueFilterColumns.ToDictionary(
                kvp => kvp.Key,
                IReadOnlyList<string> (kvp) => [.. kvp.Value]);
        _previousValueFilterHiddenRows = [.. sheet.ValueFilterHiddenRows];
        _previousColumnFilterOwnedRows = sheet.ColumnFilterOwnedRows.Count == 0
            ? null
            : sheet.ColumnFilterOwnedRows.ToDictionary(
                kvp => kvp.Key,
                HashSet<uint> (kvp) => [.. kvp.Value]);

        if (sheet.AutoFilter is null)
        {
            sheet.AutoFilter = new WorksheetAutoFilterModel(_range.ToString(), null);
            return new CommandOutcome(true);
        }

        // Pass the table-aware first row for the same reason every other member of this family now
        // does. For a worksheet-level AutoFilter the helper returns Start.Row + 1 unchanged -- it only
        // differs when the range is exactly a headerless table's -- so this is a no-op today. It is
        // here so that grepping the filter code for a bare "Start.Row + 1" finds nothing: this bound
        // has now been copied naively into a new sibling three rounds running.
        if (AutoFilterRangeResolver.TryGetWorksheetAutoFilterRange(sheet, out var autoFilterRange))
        {
            FilterHiddenRowUpdater.ClearRange(
                sheet.FilterHiddenRows, autoFilterRange,
                FilterHiddenRowUpdater.GetFilterableFirstRow(sheet, autoFilterRange));
        }
        else
        {
            FilterHiddenRowUpdater.ClearRange(
                sheet.FilterHiddenRows, _range,
                FilterHiddenRowUpdater.GetFilterableFirstRow(sheet, _range));
        }
        sheet.AutoFilter = null;
        sheet.ActiveValueFilterColumns.Clear();
        sheet.ValueFilterHiddenRows.Clear();
        sheet.ColumnFilterOwnedRows.Clear();
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        sheet.AutoFilter = WorksheetAutoFilterCloner.Clone(_previousAutoFilter);
        sheet.FilterHiddenRows.Clear();
        if (_previousFilterHiddenRows is not null)
            sheet.FilterHiddenRows.UnionWith(_previousFilterHiddenRows);

        sheet.ActiveValueFilterColumns.Clear();
        if (_previousActiveValueFilterColumns is not null)
        {
            foreach (var (col, allowedValues) in _previousActiveValueFilterColumns)
                sheet.ActiveValueFilterColumns[col] = allowedValues;
        }

        sheet.ValueFilterHiddenRows.Clear();
        if (_previousValueFilterHiddenRows is not null)
            sheet.ValueFilterHiddenRows.UnionWith(_previousValueFilterHiddenRows);

        sheet.ColumnFilterOwnedRows.Clear();
        if (_previousColumnFilterOwnedRows is not null)
        {
            foreach (var (col, ownedRows) in _previousColumnFilterOwnedRows)
                sheet.ColumnFilterOwnedRows[col] = [.. ownedRows];
        }
    }

}
