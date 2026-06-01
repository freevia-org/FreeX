using FreeX.Core.Model;

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

        _previousHiddenRows = [.. sheet.HiddenRows];
        _previousFilterHiddenRows = [.. sheet.FilterHiddenRows];

        if (_count == 0)
        {
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

        var keptRows = keepCount == 0
            ? new HashSet<uint>()
            : SelectBestRows(sheet, filterCol, firstDataRow, lastDataRow, keepCount, _top);

        ApplyKeptRowVisibility(sheet.FilterHiddenRows, firstDataRow, lastDataRow, keptRows);

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

    private static HashSet<uint> SelectBestRows(
        Sheet sheet,
        uint filterCol,
        uint firstDataRow,
        uint lastDataRow,
        int keepCount,
        bool top)
    {
        var comparer = new RankedFilterRowWorstFirstComparer(top);
        var queue = new PriorityQueue<RankedFilterRow, RankedFilterRow>(keepCount, comparer);

        for (var row = firstDataRow; row <= lastDataRow; row++)
        {
            if (sheet.GetValue(row, filterCol) is not NumberValue number)
                continue;

            var candidate = new RankedFilterRow(row, number.Value);
            if (queue.Count < keepCount)
            {
                queue.Enqueue(candidate, candidate);
            }
            else if (queue.TryPeek(out _, out var worst) && comparer.Compare(candidate, worst) > 0)
            {
                queue.Dequeue();
                queue.Enqueue(candidate, candidate);
            }
        }

        var keptRows = new HashSet<uint>(queue.Count);
        while (queue.TryDequeue(out var row, out _))
            keptRows.Add(row.Row);

        return keptRows;
    }

    private static void ApplyNumericVisibility(
        Sheet sheet,
        uint filterCol,
        uint firstDataRow,
        uint lastDataRow)
    {
        for (var row = firstDataRow; row <= lastDataRow; row++)
            FilterHiddenRowUpdater.SetVisible(sheet.FilterHiddenRows, row, sheet.GetValue(row, filterCol) is NumberValue);
    }

    private static void ApplyKeptRowVisibility(
        HashSet<uint> filterHiddenRows,
        uint firstDataRow,
        uint lastDataRow,
        HashSet<uint> keptRows)
    {
        for (var row = firstDataRow; row <= lastDataRow; row++)
            FilterHiddenRowUpdater.SetVisible(filterHiddenRows, row, keptRows.Contains(row));
    }

    private readonly record struct RankedFilterRow(uint Row, double Value);

    private sealed class RankedFilterRowWorstFirstComparer(bool top) : IComparer<RankedFilterRow>
    {
        public int Compare(RankedFilterRow x, RankedFilterRow y) =>
            -CompareBestFirst(x, y, top);

        private static int CompareBestFirst(RankedFilterRow x, RankedFilterRow y, bool top)
        {
            var valueComparison = top
                ? Comparer<double>.Default.Compare(-x.Value, -y.Value)
                : Comparer<double>.Default.Compare(x.Value, y.Value);
            return valueComparison != 0
                ? valueComparison
                : x.Row.CompareTo(y.Row);
        }
    }
}
