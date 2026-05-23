using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class TopBottomFilterCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly uint _filterColOffset;
    private readonly uint _count;
    private readonly bool _top;
    private readonly bool _percent;
    private HashSet<uint>? _previousHiddenRows;
    private HashSet<uint>? _previousFilterHiddenRows;

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
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UseAutoFilter) is { } protectedOutcome)
            return protectedOutcome;

        _previousHiddenRows = [.. sheet.HiddenRows];
        _previousFilterHiddenRows = [.. sheet.FilterHiddenRows];

        var filterCol = _range.Start.Col + _filterColOffset;
        for (uint row = _range.Start.Row + 1; row <= _range.End.Row; row++)
            sheet.FilterHiddenRows.Remove(row);

        if (_count == 0)
            return new CommandOutcome(true);

        var rankedRows = new List<(uint Row, double Value)>();
        for (uint row = _range.Start.Row + 1; row <= _range.End.Row; row++)
        {
            if (sheet.GetValue(row, filterCol) is NumberValue number)
                rankedRows.Add((row, number.Value));
        }

        var keepCount = _percent
            ? (uint)Math.Ceiling(rankedRows.Count * Math.Min(_count, 100u) / 100.0)
            : _count;
        var keptRows = rankedRows
            .OrderBy(item => _top ? -item.Value : item.Value)
            .ThenBy(item => item.Row)
            .Take((int)Math.Min(keepCount, (uint)rankedRows.Count))
            .Select(item => item.Row)
            .ToHashSet();

        for (uint row = _range.Start.Row + 1; row <= _range.End.Row; row++)
        {
            if (!keptRows.Contains(row))
                sheet.FilterHiddenRows.Add(row);
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
}
