using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class AverageFilterCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly uint _filterColOffset;
    private readonly bool _above;
    private HashSet<uint>? _previousHiddenRows;
    private HashSet<uint>? _previousFilterHiddenRows;

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
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UseAutoFilter) is { } protectedOutcome)
            return protectedOutcome;

        _previousHiddenRows = [.. sheet.HiddenRows];
        _previousFilterHiddenRows = [.. sheet.FilterHiddenRows];

        var filterCol = _range.Start.Col + _filterColOffset;
        for (uint row = _range.Start.Row + 1; row <= _range.End.Row; row++)
            sheet.FilterHiddenRows.Remove(row);

        var numericRows = new List<(uint Row, double Value)>();
        for (uint row = _range.Start.Row + 1; row <= _range.End.Row; row++)
        {
            if (sheet.GetValue(row, filterCol) is NumberValue number)
                numericRows.Add((row, number.Value));
        }

        if (numericRows.Count == 0)
            return new CommandOutcome(true);

        var average = numericRows.Average(item => item.Value);
        var keptRows = numericRows
            .Where(item => _above ? item.Value > average : item.Value < average)
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
