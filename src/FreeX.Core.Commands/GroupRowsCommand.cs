using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Sets the outline level on a row range. Level 1–8; pass 0 to clear.</summary>
public sealed class GroupRowsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _startRow, _endRow;
    private readonly int _level;
    private readonly bool _preserveExistingHierarchy;
    private Dictionary<uint, int>? _previousLevels;
    private HashSet<uint>? _previouslyHiddenByGroup;

    public string Label => _level > 0 ? "Group Rows" : "Ungroup Rows";

    public GroupRowsCommand(SheetId sheetId, uint startRow, uint endRow, int level, bool preserveExistingHierarchy = false)
    {
        OutlineGroupingService.ValidateOutlineLevel(level);
        _sheetId  = sheetId;
        _startRow = startRow;
        _endRow   = endRow;
        _level    = level;
        _preserveExistingHierarchy = preserveExistingHierarchy;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatRows) is { } protectedOutcome)
            return protectedOutcome;

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
                sheet.RowOutlineLevels[r] = OutlineGroupingService.GetGroupedOutlineLevel(prev, _level, _preserveExistingHierarchy);
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

/// <summary>
/// Resolves the single contiguous row-outline group nearest a selection, so ribbon
/// Hide/Show-Detail commands can scope themselves to "the group at the cursor" (matching
/// Excel) instead of acting on the whole sheet.
/// </summary>
internal static class RowOutlineGroupScope
{
    /// <summary>
    /// Given a selection [selectionStart, selectionEnd], finds the innermost contiguous run of
    /// rows sharing an outline level that the selection sits inside (or immediately borders, for
    /// the case where the selection is on the group's summary/toggle row). Returns null when the
    /// selection isn't associated with any group.
    /// </summary>
    public static (uint Start, uint End, int Level)? Resolve(
        IReadOnlyDictionary<uint, int> levels, uint selectionStart, uint selectionEnd)
    {
        uint anchor = 0;
        var found = false;
        for (var r = selectionStart; r <= selectionEnd; r++)
        {
            if (levels.TryGetValue(r, out var lvl) && lvl > 0)
            {
                anchor = r;
                found = true;
                break;
            }
        }

        if (!found)
        {
            if (selectionStart > 0 && levels.TryGetValue(selectionStart - 1, out var above) && above > 0)
            {
                anchor = selectionStart - 1;
                found = true;
            }
            else if (levels.TryGetValue(selectionEnd + 1, out var below) && below > 0)
            {
                anchor = selectionEnd + 1;
                found = true;
            }
        }

        if (!found)
            return null;

        var level = levels[anchor];

        var start = anchor;
        while (start > 0 && levels.TryGetValue(start - 1, out var prevLevel) && prevLevel >= level)
            start--;

        var end = anchor;
        while (levels.TryGetValue(end + 1, out var nextLevel) && nextLevel >= level)
            end++;

        return (start, end, level);
    }
}

/// <summary>
/// Collapses (hides) rows whose outline level is >= the given level. When a selection is
/// supplied, only the specific contiguous group at that selection is affected (matching Excel);
/// omitting the selection preserves the legacy sheet-wide behavior for existing callers.
/// </summary>
public sealed class CollapseRowGroupCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _level;
    private readonly uint? _selectionStart;
    private readonly uint? _selectionEnd;
    private HashSet<uint>? _newly;

    public string Label => "Collapse Group";

    public CollapseRowGroupCommand(SheetId sheetId, int level, uint? selectionStart = null, uint? selectionEnd = null)
    {
        _sheetId = sheetId;
        _level   = level;
        _selectionStart = selectionStart;
        _selectionEnd   = selectionEnd ?? selectionStart;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatRows) is { } protectedOutcome)
            return protectedOutcome;

        _newly = [];

        if (_selectionStart is { } selStart)
        {
            if (RowOutlineGroupScope.Resolve(sheet.RowOutlineLevels, selStart, _selectionEnd ?? selStart) is not { } group)
                return new CommandOutcome(true);

            foreach (var (row, lvl) in sheet.RowOutlineLevels)
            {
                if (row < group.Start || row > group.End || lvl < group.Level)
                    continue;
                if (sheet.GroupHiddenRows.Contains(row))
                    continue;
                sheet.GroupHiddenRows.Add(row);
                _newly.Add(row);
            }
            return new CommandOutcome(true);
        }

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

/// <summary>
/// Expands (shows) rows whose outline level is >= the given level. When a selection is
/// supplied, only the specific contiguous group at that selection is affected (matching Excel);
/// omitting the selection preserves the legacy sheet-wide behavior for existing callers.
/// </summary>
public sealed class ExpandRowGroupCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _level;
    private readonly uint? _selectionStart;
    private readonly uint? _selectionEnd;
    private HashSet<uint>? _removed;

    public string Label => "Expand Group";

    public ExpandRowGroupCommand(SheetId sheetId, int level, uint? selectionStart = null, uint? selectionEnd = null)
    {
        _sheetId = sheetId;
        _level   = level;
        _selectionStart = selectionStart;
        _selectionEnd   = selectionEnd ?? selectionStart;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatRows) is { } protectedOutcome)
            return protectedOutcome;

        _removed = [];

        if (_selectionStart is { } selStart)
        {
            if (RowOutlineGroupScope.Resolve(sheet.RowOutlineLevels, selStart, _selectionEnd ?? selStart) is not { } group)
                return new CommandOutcome(true);

            foreach (var row in sheet.GroupHiddenRows.ToList())
            {
                if (row < group.Start || row > group.End)
                    continue;
                if (!sheet.RowOutlineLevels.TryGetValue(row, out var lvl) || lvl < group.Level)
                    continue;
                sheet.GroupHiddenRows.Remove(row);
                _removed.Add(row);
            }
            return new CommandOutcome(true);
        }

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

public sealed class SetRowOutlineGroupCollapsedCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _startRow;
    private readonly uint _endRow;
    private readonly int _level;
    private readonly bool _collapsed;
    private Dictionary<uint, bool>? _previousHiddenStates;

    public string Label => _collapsed ? "Collapse Row Group" : "Expand Row Group";

    public SetRowOutlineGroupCollapsedCommand(SheetId sheetId, uint startRow, uint endRow, int level, bool collapsed)
    {
        OutlineGroupingService.ValidateOutlineLevel(level);
        if (level == 0)
            throw new ArgumentOutOfRangeException(nameof(level), "Outline level must be 1-8.");

        _sheetId = sheetId;
        _startRow = startRow;
        _endRow = endRow;
        _level = level;
        _collapsed = collapsed;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatRows) is { } protectedOutcome)
            return protectedOutcome;

        _previousHiddenStates = [];
        foreach (var (row, level) in sheet.RowOutlineLevels)
        {
            if (row < _startRow || row > _endRow || level < _level)
                continue;

            _previousHiddenStates[row] = sheet.GroupHiddenRows.Contains(row);
            if (_collapsed)
                sheet.GroupHiddenRows.Add(row);
            else
                sheet.GroupHiddenRows.Remove(row);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousHiddenStates is null) return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (row, wasHidden) in _previousHiddenStates)
        {
            if (wasHidden)
                sheet.GroupHiddenRows.Add(row);
            else
                sheet.GroupHiddenRows.Remove(row);
        }
    }
}
