using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>Sets the outline level on a column range. Level 1–8; pass 0 to clear.</summary>
public sealed class GroupColumnsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _startCol, _endCol;
    private readonly int _level;
    private Dictionary<uint, int>? _previousLevels;
    private HashSet<uint>? _previouslyHiddenByGroup;

    public string Label => _level > 0 ? "Group Columns" : "Ungroup Columns";

    public GroupColumnsCommand(SheetId sheetId, uint startCol, uint endCol, int level)
    {
        if (level is < 0 or > 8)
            throw new ArgumentOutOfRangeException(nameof(level), "Outline level must be 0–8.");
        _sheetId  = sheetId;
        _startCol = startCol;
        _endCol   = endCol;
        _level    = level;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } p) return p;

        _previousLevels = [];
        _previouslyHiddenByGroup = [];
        for (uint c = _startCol; c <= _endCol; c++)
        {
            sheet.ColOutlineLevels.TryGetValue(c, out var prev);
            _previousLevels[c] = prev;
            if (_level == 0)
            {
                sheet.ColOutlineLevels.Remove(c);
                if (sheet.GroupHiddenCols.Remove(c))
                    _previouslyHiddenByGroup.Add(c);
            }
            else
                sheet.ColOutlineLevels[c] = _level;
        }
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousLevels is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (col, prev) in _previousLevels)
        {
            if (prev == 0)
                sheet.ColOutlineLevels.Remove(col);
            else
                sheet.ColOutlineLevels[col] = prev;
        }
        if (_previouslyHiddenByGroup is not null)
            foreach (var c in _previouslyHiddenByGroup)
                sheet.GroupHiddenCols.Add(c);
    }
}

/// <summary>Collapses (hides) all columns whose outline level is >= the given level.</summary>
public sealed class CollapseColGroupCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _level;
    private HashSet<uint>? _newly;

    public string Label => "Collapse Column Group";

    public CollapseColGroupCommand(SheetId sheetId, int level)
    {
        _sheetId = sheetId;
        _level   = level;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatColumns) is { } protectedOutcome)
            return protectedOutcome;

        _newly = [];
        foreach (var (col, lvl) in sheet.ColOutlineLevels)
        {
            if (lvl >= _level && !sheet.GroupHiddenCols.Contains(col))
            {
                sheet.GroupHiddenCols.Add(col);
                _newly.Add(col);
            }
        }
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_newly is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        foreach (var col in _newly)
            sheet.GroupHiddenCols.Remove(col);
    }
}

/// <summary>Expands (shows) all columns whose outline level is >= the given level.</summary>
public sealed class ExpandColGroupCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _level;
    private HashSet<uint>? _removed;

    public string Label => "Expand Column Group";

    public ExpandColGroupCommand(SheetId sheetId, int level)
    {
        _sheetId = sheetId;
        _level   = level;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatColumns) is { } protectedOutcome)
            return protectedOutcome;

        _removed = [];
        foreach (var col in sheet.GroupHiddenCols.ToList())
        {
            if (sheet.ColOutlineLevels.TryGetValue(col, out var lvl) && lvl >= _level)
            {
                sheet.GroupHiddenCols.Remove(col);
                _removed.Add(col);
            }
        }
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_removed is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        foreach (var col in _removed)
            sheet.GroupHiddenCols.Add(col);
    }
}
