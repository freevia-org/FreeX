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

/// <summary>
/// Resolves the single contiguous column-outline group nearest a selection, so ribbon
/// Hide/Show-Detail commands can scope themselves to "the group at the cursor" (matching Excel)
/// instead of acting on the whole sheet. Mirrors <see cref="RowOutlineGroupScope"/> for columns
/// (R40-commands-group-outline-3-2).
/// </summary>
internal static class ColumnOutlineGroupScope
{
    /// <summary>
    /// Given a selection [selectionStart, selectionEnd], finds the innermost contiguous run of
    /// columns sharing an outline level that the selection sits inside (or immediately borders, for
    /// the case where the selection is on the group's summary/toggle column). Returns null when the
    /// selection isn't associated with any group.
    /// </summary>
    public static (uint Start, uint End, int Level)? Resolve(
        IReadOnlyDictionary<uint, int> levels, uint selectionStart, uint selectionEnd)
    {
        uint anchor = 0;
        var found = false;
        for (var c = selectionStart; c <= selectionEnd; c++)
        {
            if (levels.TryGetValue(c, out var lvl) && lvl > 0)
            {
                anchor = c;
                found = true;
                break;
            }
        }

        if (!found)
        {
            if (selectionStart > 0 && levels.TryGetValue(selectionStart - 1, out var before) && before > 0)
            {
                anchor = selectionStart - 1;
                found = true;
            }
            else if (levels.TryGetValue(selectionEnd + 1, out var after) && after > 0)
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
/// Computes the visible outline "anchor" (summary) column for a contiguous run of collapsed detail
/// columns, matching Excel's own placement per <c>outlinePr/@summaryRight</c>: the column just past
/// the run in the summary direction (to the right by default; to the left when
/// <c>Sheet.OutlineSummaryRight</c> is explicitly false). Shared by
/// <see cref="CollapseColGroupCommand"/> and <see cref="ExpandColGroupCommand"/> so the two stay in
/// agreement about which column a given run's anchor is (R35-deferred-collapse-anchor-1).
/// </summary>
internal static class ColumnGroupAnchorHelper
{
    public static List<(uint Start, uint End)> GetContiguousRuns(IEnumerable<uint> cols)
    {
        var sorted = new List<uint>(cols);
        sorted.Sort();
        var runs = new List<(uint Start, uint End)>();
        var haveRun = false;
        uint runStart = 0, runEnd = 0;
        foreach (var col in sorted)
        {
            if (!haveRun)
            {
                runStart = runEnd = col;
                haveRun = true;
            }
            else if (col == runEnd)
            {
                // Duplicate (defensive; callers pass a HashSet-derived sequence in practice).
            }
            else if (col == runEnd + 1)
            {
                runEnd = col;
            }
            else
            {
                runs.Add((runStart, runEnd));
                runStart = runEnd = col;
            }
        }
        if (haveRun)
            runs.Add((runStart, runEnd));
        return runs;
    }

    /// <summary>Returns the anchor column for a run, or null when summaryRight is false and the run starts at column 1 (no column to its left to anchor to).</summary>
    public static uint? ComputeAnchor(bool summaryRight, uint runStart, uint runEnd) =>
        summaryRight ? runEnd + 1 : (runStart > 1 ? runStart - 1 : null);
}

/// <summary>
/// Collapses (hides) columns whose outline level is >= the given level. When a selection is
/// supplied, only the specific contiguous group at that selection is affected (matching Excel);
/// omitting the selection preserves the legacy sheet-wide behavior for existing callers.
/// </summary>
public sealed class CollapseColGroupCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _level;
    private readonly uint? _selectionStart;
    private readonly uint? _selectionEnd;
    private HashSet<uint>? _newly;
    private HashSet<uint>? _newlyAnchored;

    public string Label => "Collapse Column Group";

    public CollapseColGroupCommand(SheetId sheetId, int level, uint? selectionStart = null, uint? selectionEnd = null)
    {
        _sheetId = sheetId;
        _level   = level;
        _selectionStart = selectionStart;
        _selectionEnd   = selectionEnd ?? selectionStart;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatColumns) is { } protectedOutcome)
            return protectedOutcome;

        _newly = [];
        _newlyAnchored = [];
        var summaryRight = sheet.OutlineSummaryRight ?? true;

        if (_selectionStart is { } selStart)
        {
            if (ColumnOutlineGroupScope.Resolve(sheet.ColOutlineLevels, selStart, _selectionEnd ?? selStart) is not { } group)
                return new CommandOutcome(true);

            foreach (var (col, lvl) in sheet.ColOutlineLevels)
            {
                if (col < group.Start || col > group.End || lvl < group.Level)
                    continue;
                if (sheet.GroupHiddenCols.Contains(col))
                    continue;
                sheet.GroupHiddenCols.Add(col);
                _newly.Add(col);
            }

            if (ColumnGroupAnchorHelper.ComputeAnchor(summaryRight, group.Start, group.End) is { } anchor &&
                sheet.CollapsedAnchorCols.Add(anchor))
            {
                _newlyAnchored.Add(anchor);
            }
            return new CommandOutcome(true);
        }

        foreach (var (col, lvl) in sheet.ColOutlineLevels)
        {
            if (lvl >= _level && !sheet.GroupHiddenCols.Contains(col))
            {
                sheet.GroupHiddenCols.Add(col);
                _newly.Add(col);
            }
        }

        // Anchor placement is based on every column currently qualifying at this level (not just
        // the ones newly hidden by this call), so a repeated/partial collapse still resolves the
        // same physical anchor position for each contiguous detail run.
        var qualifyingCols = new List<uint>();
        foreach (var (col, lvl) in sheet.ColOutlineLevels)
        {
            if (lvl >= _level)
                qualifyingCols.Add(col);
        }
        foreach (var run in ColumnGroupAnchorHelper.GetContiguousRuns(qualifyingCols))
        {
            if (ColumnGroupAnchorHelper.ComputeAnchor(summaryRight, run.Start, run.End) is { } anchor &&
                sheet.CollapsedAnchorCols.Add(anchor))
            {
                _newlyAnchored.Add(anchor);
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
        if (_newlyAnchored is not null)
            foreach (var col in _newlyAnchored)
                sheet.CollapsedAnchorCols.Remove(col);
    }
}

/// <summary>
/// Expands (shows) columns whose outline level is >= the given level. When a selection is
/// supplied, only the specific contiguous group at that selection is affected (matching Excel);
/// omitting the selection preserves the legacy sheet-wide behavior for existing callers.
/// </summary>
public sealed class ExpandColGroupCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _level;
    private readonly uint? _selectionStart;
    private readonly uint? _selectionEnd;
    private HashSet<uint>? _removed;
    private HashSet<uint>? _removedAnchors;

    public string Label => "Expand Column Group";

    public ExpandColGroupCommand(SheetId sheetId, int level, uint? selectionStart = null, uint? selectionEnd = null)
    {
        _sheetId = sheetId;
        _level   = level;
        _selectionStart = selectionStart;
        _selectionEnd   = selectionEnd ?? selectionStart;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatColumns) is { } protectedOutcome)
            return protectedOutcome;

        _removed = [];
        _removedAnchors = [];
        var summaryRight = sheet.OutlineSummaryRight ?? true;

        if (_selectionStart is { } selStart)
        {
            if (ColumnOutlineGroupScope.Resolve(sheet.ColOutlineLevels, selStart, _selectionEnd ?? selStart) is not { } group)
                return new CommandOutcome(true);

            foreach (var col in sheet.GroupHiddenCols.ToList())
            {
                if (col < group.Start || col > group.End)
                    continue;
                if (!sheet.ColOutlineLevels.TryGetValue(col, out var lvl) || lvl < group.Level)
                    continue;
                sheet.GroupHiddenCols.Remove(col);
                _removed.Add(col);
            }

            // The group's detail columns are visible again, so its anchor no longer summarizes a
            // collapsed run -- clear the stale collapsed marker so a later save doesn't re-stamp
            // collapsed="1" on a column that has nothing left to summarize.
            if (ColumnGroupAnchorHelper.ComputeAnchor(summaryRight, group.Start, group.End) is { } anchor &&
                sheet.CollapsedAnchorCols.Remove(anchor))
            {
                _removedAnchors.Add(anchor);
            }
            return new CommandOutcome(true);
        }

        foreach (var col in sheet.GroupHiddenCols.ToList())
        {
            if (sheet.ColOutlineLevels.TryGetValue(col, out var lvl) && lvl >= _level)
            {
                sheet.GroupHiddenCols.Remove(col);
                _removed.Add(col);
            }
        }

        // This call just un-hid every column at this level sheet-wide, so -- mirroring
        // CollapseColGroupCommand's placement over the same qualifying-column set -- none of those
        // runs have any hidden detail left to summarize; clear each run's anchor marker.
        var qualifyingCols = new List<uint>();
        foreach (var (col, lvl) in sheet.ColOutlineLevels)
        {
            if (lvl >= _level)
                qualifyingCols.Add(col);
        }
        foreach (var run in ColumnGroupAnchorHelper.GetContiguousRuns(qualifyingCols))
        {
            if (ColumnGroupAnchorHelper.ComputeAnchor(summaryRight, run.Start, run.End) is { } anchor &&
                sheet.CollapsedAnchorCols.Remove(anchor))
            {
                _removedAnchors.Add(anchor);
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
        if (_removedAnchors is not null)
            foreach (var col in _removedAnchors)
                sheet.CollapsedAnchorCols.Add(col);
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
