using FreeX.Core.Model;
using System.Buffers;

namespace FreeX.Core.Commands;

public sealed class AverageFilterCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly uint _filterColOffset;
    private readonly bool _above;
    private FilterUndoSnapshot _undoSnapshot;
    // R33-commands-autofilter-slicer-1: keep the worksheet AutoFilter's <dynamicFilter> filterColumn
    // model in sync with the interactively-applied Above/Below Average criterion, so it round-trips
    // through XlsxWorksheetAutoFilterXmlMapper instead of being silently dropped on save.
    private List<WorksheetAutoFilterColumnModel>? _previousAutoFilterColumns;

    public string Label => _above ? "Above Average Filter" : "Below Average Filter";

    public AverageFilterCommand(SheetId sheetId, GridRange range, uint filterColOffset, bool above)
    {
        _sheetId = sheetId;
        _range = range;
        _filterColOffset = filterColOffset;
        _above = above;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectInvalidFilterRange(_sheetId, _range, _filterColOffset) is { } invalidRange)
            return invalidRange;
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UseAutoFilter) is { } protectedOutcome)
            return protectedOutcome;

        _undoSnapshot.Reset();

        _previousAutoFilterColumns = WorksheetAutoFilterColumnSync.Apply(
            sheet,
            _range,
            (int)_filterColOffset,
            new WorksheetAutoFilterColumnModel(
                ColumnId: (int)_filterColOffset,
                Values: [],
                IncludeBlank: false,
                CustomFilters: [],
                CustomFiltersAnd: false,
                CustomFiltersAndRaw: null,
                NativeCustomFiltersAttributes: null,
                Top10: null,
                DynamicFilter: new WorksheetAutoFilterDynamicFilterModel(Type: _above ? "aboveAverage" : "belowAverage"),
                ColorFilter: null,
                IconFilter: null,
                DateGroups: [],
                NativeFiltersAttributes: null,
                NativeFilterXmls: []));

        var filterCol = _range.Start.Col + _filterColOffset;
        var firstDataRow = _range.Start.Row + 1;
        var lastDataRow = _range.End.Row;
        if (firstDataRow > lastDataRow)
            return new CommandOutcome(true);

        var dataRowCount = (int)Math.Min(lastDataRow - firstDataRow + 1, (uint)int.MaxValue);
        var values = ArrayPool<double>.Shared.Rent(dataRowCount);
        var numericCount = 0;
        var sum = 0d;

        try
        {
            for (var offset = 0; offset < dataRowCount; offset++)
            {
                var row = firstDataRow + (uint)offset;
                if (sheet.GetValue(row, filterCol) is NumberValue number)
                {
                    values[offset] = number.Value;
                    numericCount++;
                    sum += number.Value;
                }
                else
                {
                    values[offset] = double.NaN;
                }
            }

            if (numericCount == 0)
            {
                if (!sheet.ColumnFilterOwnedRows.TryGetValue(filterCol, out var ownedRows) || ownedRows.Count == 0)
                    return new CommandOutcome(true);

                _undoSnapshot.CaptureIfNeeded(sheet);
                FilterHiddenRowUpdater.ClearColumnOwnedRange(sheet, filterCol, _range);
                return new CommandOutcome(true);
            }

            var average = sum / numericCount;

            for (var offset = 0; offset < dataRowCount; offset++)
            {
                var value = values[offset];
                var visible = !double.IsNaN(value) && (_above ? value > average : value < average);
                var row = firstDataRow + (uint)offset;
                if (FilterHiddenRowUpdater.IsColumnOwnedVisibilityAlreadyCorrect(sheet, filterCol, row, visible))
                    continue;

                _undoSnapshot.CaptureIfNeeded(sheet);
                FilterHiddenRowUpdater.ApplyColumnOwnedVisibility(sheet, filterCol, row, visible);
            }
        }
        finally
        {
            ArrayPool<double>.Shared.Return(values);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        WorksheetAutoFilterColumnSync.Restore(sheet, _range, _previousAutoFilterColumns);

        if (!_undoSnapshot.HasSnapshot)
            return;

        _undoSnapshot.Restore(sheet);
    }
}
