using FreeX.Core.Model;
using System.Buffers;

namespace FreeX.Core.Commands;

public sealed class TopBottomFilterCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly uint _filterColOffset;
    private readonly uint _count;
    private readonly bool _top;
    private readonly bool _percent;
    private uint[]? _previousHiddenRows;
    private uint[]? _previousFilterHiddenRows;

    public string Label => (_top, _percent) switch
    {
        (true, true) => "Top Percent Filter",
        (false, true) => "Bottom Percent Filter",
        (true, false) => "Top Items Filter",
        _ => "Bottom Items Filter"
    };

    public TopBottomFilterCommand(SheetId sheetId, GridRange range, uint filterColOffset, uint count, bool top)
        : this(sheetId, range, filterColOffset, count, top, percent: false)
    {
    }

    private TopBottomFilterCommand(SheetId sheetId, GridRange range, uint filterColOffset, uint count, bool top, bool percent)
    {
        _sheetId = sheetId;
        _range = range;
        _filterColOffset = filterColOffset;
        _count = count;
        _top = top;
        _percent = percent;
    }

    public static TopBottomFilterCommand Percent(
        SheetId sheetId,
        GridRange range,
        uint filterColOffset,
        uint percent,
        bool top) =>
        new(sheetId, range, filterColOffset, percent, top, percent: true);

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectInvalidFilterRange(_sheetId, _range, _filterColOffset) is { } invalidRange)
            return invalidRange;
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UseAutoFilter) is { } protectedOutcome)
            return protectedOutcome;

        _previousHiddenRows = null;
        _previousFilterHiddenRows = null;

        if (_count == 0)
        {
            if (!FilterHiddenRowUpdater.ContainsAnyInRange(sheet.FilterHiddenRows, _range))
                return new CommandOutcome(true);

            SnapshotHiddenRows(sheet);
            FilterHiddenRowUpdater.ClearRange(sheet.FilterHiddenRows, _range);
            return new CommandOutcome(true);
        }

        var filterCol = _range.Start.Col + _filterColOffset;
        var firstDataRow = _range.Start.Row + 1;
        var lastDataRow = _range.End.Row;
        if (firstDataRow > lastDataRow)
            return new CommandOutcome(true);

        var dataRowCount = (int)Math.Min(lastDataRow - firstDataRow + 1, (uint)int.MaxValue);
        var keepCount = _percent
            ? GetPercentKeepCount(sheet, filterCol, firstDataRow, lastDataRow)
            : (int)Math.Min(_count, (uint)dataRowCount);

        if (keepCount >= dataRowCount)
        {
            ApplyNumericVisibility(sheet, filterCol, firstDataRow, lastDataRow);
            return new CommandOutcome(true);
        }

        var keptRows = ArrayPool<bool>.Shared.Rent(dataRowCount);
        Array.Clear(keptRows, 0, dataRowCount);

        try
        {
            if (keepCount > 0)
                SelectBestRows(sheet, filterCol, firstDataRow, lastDataRow, keepCount, _top, keptRows);

            ApplyKeptRowVisibility(sheet, firstDataRow, lastDataRow, keptRows);
        }
        finally
        {
            ArrayPool<bool>.Shared.Return(keptRows);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousHiddenRows is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        sheet.HiddenRows.Clear();
        sheet.HiddenRows.UnionWith(_previousHiddenRows);
        sheet.FilterHiddenRows.Clear();
        if (_previousFilterHiddenRows is not null)
            sheet.FilterHiddenRows.UnionWith(_previousFilterHiddenRows);
    }

    private int GetPercentKeepCount(Sheet sheet, uint filterCol, uint firstDataRow, uint lastDataRow)
    {
        var numericCount = 0;
        for (var row = firstDataRow; row <= lastDataRow; row++)
        {
            if (sheet.GetValue(row, filterCol) is NumberValue)
                numericCount++;
        }

        return (int)Math.Ceiling(numericCount * (Math.Min(_count, 100u) / 100.0));
    }

    private static void SelectBestRows(
        Sheet sheet,
        uint filterCol,
        uint firstDataRow,
        uint lastDataRow,
        int keepCount,
        bool top,
        bool[] keptRows)
    {
        var heap = ArrayPool<RankedFilterRow>.Shared.Rent(keepCount);
        var heapCount = 0;

        try
        {
            for (var row = firstDataRow; row <= lastDataRow; row++)
            {
                if (sheet.GetValue(row, filterCol) is not NumberValue number)
                    continue;

                var candidate = new RankedFilterRow(row, number.Value);
                if (heapCount < keepCount)
                {
                    heap[heapCount] = candidate;
                    SiftUpWorstFirst(heap, heapCount, top);
                    heapCount++;
                }
                else if (IsBetter(candidate, heap[0], top))
                {
                    heap[0] = candidate;
                    SiftDownWorstFirst(heap, heapCount, top);
                }
            }

            for (var i = 0; i < heapCount; i++)
                keptRows[(int)(heap[i].Row - firstDataRow)] = true;
        }
        finally
        {
            ArrayPool<RankedFilterRow>.Shared.Return(heap);
        }
    }

    private void ApplyNumericVisibility(
        Sheet sheet,
        uint filterCol,
        uint firstDataRow,
        uint lastDataRow)
    {
        for (var row = firstDataRow; row <= lastDataRow; row++)
        {
            var visible = sheet.GetValue(row, filterCol) is NumberValue;
            if (sheet.FilterHiddenRows.Contains(row) == !visible)
                continue;

            SnapshotHiddenRows(sheet);
            FilterHiddenRowUpdater.SetHidden(sheet.FilterHiddenRows, row, !visible);
        }
    }

    private void ApplyKeptRowVisibility(
        Sheet sheet,
        uint firstDataRow,
        uint lastDataRow,
        bool[] keptRows)
    {
        for (var row = firstDataRow; row <= lastDataRow; row++)
        {
            var visible = keptRows[(int)(row - firstDataRow)];
            if (sheet.FilterHiddenRows.Contains(row) == !visible)
                continue;

            SnapshotHiddenRows(sheet);
            FilterHiddenRowUpdater.SetHidden(sheet.FilterHiddenRows, row, !visible);
        }
    }

    private readonly record struct RankedFilterRow(uint Row, double Value);

    private static void SiftUpWorstFirst(RankedFilterRow[] heap, int index, bool top)
    {
        while (index > 0)
        {
            var parent = (index - 1) / 2;
            if (!IsWorse(heap[index], heap[parent], top))
                return;

            (heap[parent], heap[index]) = (heap[index], heap[parent]);
            index = parent;
        }
    }

    private static void SiftDownWorstFirst(RankedFilterRow[] heap, int count, bool top)
    {
        var index = 0;
        while (true)
        {
            var left = index * 2 + 1;
            if (left >= count)
                return;

            var worstChild = left;
            var right = left + 1;
            if (right < count && IsWorse(heap[right], heap[left], top))
                worstChild = right;

            if (!IsWorse(heap[worstChild], heap[index], top))
                return;

            (heap[index], heap[worstChild]) = (heap[worstChild], heap[index]);
            index = worstChild;
        }
    }

    private static bool IsBetter(RankedFilterRow candidate, RankedFilterRow currentWorst, bool top)
    {
        if (candidate.Value != currentWorst.Value)
            return top ? candidate.Value > currentWorst.Value : candidate.Value < currentWorst.Value;

        return candidate.Row < currentWorst.Row;
    }

    private static bool IsWorse(RankedFilterRow candidate, RankedFilterRow other, bool top)
    {
        if (candidate.Value != other.Value)
            return top ? candidate.Value < other.Value : candidate.Value > other.Value;

        return candidate.Row > other.Row;
    }

    private void SnapshotHiddenRows(Sheet sheet)
    {
        if (_previousHiddenRows is not null)
            return;

        _previousHiddenRows = [.. sheet.HiddenRows];
        _previousFilterHiddenRows = [.. sheet.FilterHiddenRows];
    }
}
