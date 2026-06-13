using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Sets the outline level on a column range. Level 1–8; pass 0 to clear.</summary>
public sealed class GroupColumnsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _startCol, _endCol;
    private readonly int _level;
    private readonly bool _preserveExistingHierarchy;
    private Dictionary<uint, int>? _previousLevels;
    private HashSet<uint>? _previouslyHiddenByGroup;

    public string Label => _level > 0 ? "Group Columns" : "Ungroup Columns";

    public GroupColumnsCommand(SheetId sheetId, uint startCol, uint endCol, int level, bool preserveExistingHierarchy = false)
    {
        OutlineGroupingService.ValidateOutlineLevel(level);
        _sheetId  = sheetId;
        _startCol = startCol;
        _endCol   = endCol;
        _level    = level;
        _preserveExistingHierarchy = preserveExistingHierarchy;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatColumns) is { } protectedOutcome)
            return protectedOutcome;

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
                sheet.ColOutlineLevels[c] = OutlineGroupingService.GetGroupedOutlineLevel(prev, _level, _preserveExistingHierarchy);
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

public sealed class SetColumnOutlineGroupCollapsedCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _startCol;
    private readonly uint _endCol;
    private readonly int _level;
    private readonly bool _collapsed;
    private Dictionary<uint, bool>? _previousHiddenStates;

    public string Label => _collapsed ? "Collapse Column Group" : "Expand Column Group";

    public SetColumnOutlineGroupCollapsedCommand(SheetId sheetId, uint startCol, uint endCol, int level, bool collapsed)
    {
        OutlineGroupingService.ValidateOutlineLevel(level);
        if (level == 0)
            throw new ArgumentOutOfRangeException(nameof(level), "Outline level must be 1-8.");

        _sheetId = sheetId;
        _startCol = startCol;
        _endCol = endCol;
        _level = level;
        _collapsed = collapsed;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatColumns) is { } protectedOutcome)
            return protectedOutcome;

        _previousHiddenStates = [];
        foreach (var (col, level) in sheet.ColOutlineLevels)
        {
            if (col < _startCol || col > _endCol || level < _level)
                continue;

            _previousHiddenStates[col] = sheet.GroupHiddenCols.Contains(col);
            if (_collapsed)
                sheet.GroupHiddenCols.Add(col);
            else
                sheet.GroupHiddenCols.Remove(col);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousHiddenStates is null) return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (col, wasHidden) in _previousHiddenStates)
        {
            if (wasHidden)
                sheet.GroupHiddenCols.Add(col);
            else
                sheet.GroupHiddenCols.Remove(col);
        }
    }
}
