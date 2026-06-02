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
                if (!FilterHiddenRowUpdater.ContainsAnyInRange(sheet.FilterHiddenRows, _range))
                    return new CommandOutcome(true);

                _undoSnapshot.CaptureIfNeeded(sheet);
                FilterHiddenRowUpdater.ClearRange(sheet.FilterHiddenRows, _range);
                return new CommandOutcome(true);
            }

            var average = sum / numericCount;

            for (var offset = 0; offset < dataRowCount; offset++)
            {
                var value = values[offset];
                var visible = !double.IsNaN(value) && (_above ? value > average : value < average);
                var row = firstDataRow + (uint)offset;
                if (sheet.FilterHiddenRows.Contains(row) == !visible)
                    continue;

                _undoSnapshot.CaptureIfNeeded(sheet);
                FilterHiddenRowUpdater.SetHidden(sheet.FilterHiddenRows, row, !visible);
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
        if (!_undoSnapshot.HasSnapshot)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        _undoSnapshot.Restore(sheet);
    }
}
