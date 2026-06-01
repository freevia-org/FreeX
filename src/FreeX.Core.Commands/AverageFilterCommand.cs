using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class AverageFilterCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly uint _filterColOffset;
    private readonly bool _above;
    private uint[]? _previousHiddenRows;
    private uint[]? _previousFilterHiddenRows;

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

        _previousHiddenRows = [.. sheet.HiddenRows];
        _previousFilterHiddenRows = [.. sheet.FilterHiddenRows];

        var filterCol = _range.Start.Col + _filterColOffset;
        var numericCount = 0;
        var sum = 0d;
        for (uint row = _range.Start.Row + 1; row <= _range.End.Row; row++)
        {
            if (sheet.GetValue(row, filterCol) is NumberValue number)
            {
                numericCount++;
                sum += number.Value;
            }
        }

        if (numericCount == 0)
        {
            FilterHiddenRowUpdater.ClearRange(sheet.FilterHiddenRows, _range);
            return new CommandOutcome(true);
        }

        var average = sum / numericCount;

        for (uint row = _range.Start.Row + 1; row <= _range.End.Row; row++)
        {
            var visible = sheet.GetValue(row, filterCol) is NumberValue number &&
                (_above ? number.Value > average : number.Value < average);
            FilterHiddenRowUpdater.SetVisible(sheet.FilterHiddenRows, row, visible);
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
