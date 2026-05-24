using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>Sets the outline level on a row range. Level 1–8; pass 0 to clear.</summary>
public sealed class GroupRowsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _startRow, _endRow;
    private readonly int _level;
    private Dictionary<uint, int>? _previousLevels;
    private HashSet<uint>? _previouslyHiddenByGroup;

    public string Label => _level > 0 ? "Group Rows" : "Ungroup Rows";

    public GroupRowsCommand(SheetId sheetId, uint startRow, uint endRow, int level)
    {
        if (level is < 0 or > 8)
            throw new ArgumentOutOfRangeException(nameof(level), "Outline level must be 0–8.");
        _sheetId  = sheetId;
        _startRow = startRow;
        _endRow   = endRow;
        _level    = level;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } p) return p;

        _previousLevels = [];
        _previouslyHiddenByGroup = [];
        for (uint r = _startRow; r <= _endRow; r++)
        {
            sheet.RowOutlineLevels.TryGetValue(r, out var prev);
            _previousLevels[r] = prev;
            if (_level == 0)
            {
                sheet.RowOutlineLevels.Remove(r);
                if (sheet.GroupHiddenRows.Remove(r))
                    _previouslyHiddenByGroup.Add(r);
            }
            else
                sheet.RowOutlineLevels[r] = _level;
        }
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousLevels is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (row, prev) in _previousLevels)
        {
            if (prev == 0)
                sheet.RowOutlineLevels.Remove(row);
            else
                sheet.RowOutlineLevels[row] = prev;
        }
        if (_previouslyHiddenByGroup is not null)
            foreach (var r in _previouslyHiddenByGroup)
                sheet.GroupHiddenRows.Add(r);
    }
}

/// <summary>Collapses (hides) all rows whose outline level is >= the given level.</summary>
public sealed class CollapseRowGroupCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _level;
    private HashSet<uint>? _newly;

    public string Label => "Collapse Group";

    public CollapseRowGroupCommand(SheetId sheetId, int level)
    {
        _sheetId = sheetId;
        _level   = level;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatRows) is { } protectedOutcome)
            return protectedOutcome;

        _newly = [];
        foreach (var (row, lvl) in sheet.RowOutlineLevels)
        {
            if (lvl >= _level && !sheet.GroupHiddenRows.Contains(row))
            {
                sheet.GroupHiddenRows.Add(row);
                _newly.Add(row);
            }
        }
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_newly is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        foreach (var row in _newly)
            sheet.GroupHiddenRows.Remove(row);
    }
}

/// <summary>Expands (shows) all rows whose outline level is >= the given level.</summary>
public sealed class ExpandRowGroupCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _level;
    private HashSet<uint>? _removed;

    public string Label => "Expand Group";

    public ExpandRowGroupCommand(SheetId sheetId, int level)
    {
        _sheetId = sheetId;
        _level   = level;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatRows) is { } protectedOutcome)
            return protectedOutcome;

        _removed = [];
        foreach (var row in sheet.GroupHiddenRows.ToList())
        {
            if (sheet.RowOutlineLevels.TryGetValue(row, out var lvl) && lvl >= _level)
            {
                sheet.GroupHiddenRows.Remove(row);
                _removed.Add(row);
            }
        }
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_removed is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        foreach (var row in _removed)
            sheet.GroupHiddenRows.Add(row);
    }
}
