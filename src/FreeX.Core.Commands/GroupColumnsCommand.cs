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
    private HashSet<uint>? _previousCollapsedAnchors;

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
        _previousCollapsedAnchors = [.. sheet.CollapsedAnchorCols];
        for (uint c = _startCol; c <= _endCol; c++)
        {
            sheet.ColOutlineLevels.TryGetValue(c, out var prev);
            _previousLevels[c] = prev;
            int newLevel;
            if (_level == 0)
            {
                sheet.ColOutlineLevels.Remove(c);
                newLevel = 0;
            }
            else
            {
                newLevel = OutlineGroupingService.GetGroupedOutlineLevel(prev, _level, _preserveExistingHierarchy);
                sheet.ColOutlineLevels[c] = newLevel;
            }

            // A column whose own outline level just decreased (a full Ungroup to 0, or a partial
            // Ungroup of a nested subgroup down to a nonzero level) can no longer rely on whatever
            // nested subgroup previously justified hiding it -- un-hide it here regardless of the
            // target level, not only when ungrouping all the way to 0 (R58-commands-outline-group-6-1).
            if (newLevel < prev && sheet.GroupHiddenCols.Remove(c))
                _previouslyHiddenByGroup.Add(c);
        }

        ColumnGroupAnchorHelper.RemoveInvalidAnchorsAffectedByRange(
            sheet.ColOutlineLevels,
            sheet.GroupHiddenCols,
            sheet.CollapsedAnchorCols,
            sheet.OutlineSummaryRight ?? true,
            _startCol,
            _endCol);

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
        if (_previousCollapsedAnchors is not null)
            ColumnGroupAnchorHelper.RestoreSet(sheet.CollapsedAnchorCols, _previousCollapsedAnchors);
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
        summaryRight
            ? (runEnd < CellAddress.MaxCol ? runEnd + 1 : null)
            : (runStart > 1 ? runStart - 1 : null);

    public static void RemoveInvalidAnchorsAffectedByRange(
        IReadOnlyDictionary<uint, int> levels,
        IReadOnlySet<uint> hiddenCols,
        HashSet<uint> anchors,
        bool summaryRight,
        uint affectedStart,
        uint affectedEnd)
    {
        foreach (var anchor in anchors.ToList())
        {
            uint detailCol;
            if (summaryRight)
            {
                if (anchor <= 1)
                    continue;
                detailCol = anchor - 1;
            }
            else
            {
                if (anchor >= CellAddress.MaxCol)
                    continue;
                detailCol = anchor + 1;
            }

            if (detailCol < affectedStart || detailCol > affectedEnd)
                continue;

            levels.TryGetValue(anchor, out var anchorLevel);
            if (!hiddenCols.Contains(detailCol) ||
                !levels.TryGetValue(detailCol, out var detailLevel) ||
                detailLevel <= anchorLevel)
            {
                anchors.Remove(anchor);
            }
        }
    }

    public static void RestoreSet(HashSet<uint> target, IEnumerable<uint> values)
    {
        target.Clear();
        target.UnionWith(values);
    }

    /// <summary>
    /// Precomputes the full set of columns that are hidden by some still-independently-collapsed
    /// nested subgroup with level in (<paramref name="expandLevel"/>, 7] -- i.e. every column for
    /// which a per-column, per-level run-boundary walk would answer "yes, a deeper subgroup still
    /// covers this column and is collapsed". Expanding an outer group must not un-hide such a
    /// column; Excel leaves the inner, still-collapsed subgroup collapsed
    /// (R75-commands-outline-group-4-2).
    /// <para/>
    /// Doing this once per <c>Apply</c> call -- a single sort plus, for each of the (constant) 7
    /// possible levels, one linear pass building contiguous runs -- is O(N log N) overall. The
    /// previous approach re-walked run boundaries from scratch for every qualifying column
    /// (O(N) columns * up to 7 levels * O(N) walk), which degenerated to O(N^2) on a heavily
    /// nested/grouped sheet (R76-perf-recursion-sweep-2).
    /// </summary>
    public static HashSet<uint> BuildNestedCollapsedHiddenSet(
        IReadOnlyDictionary<uint, int> levels,
        IReadOnlySet<uint> anchors,
        bool summaryRight,
        int expandLevel)
    {
        var hidden = new HashSet<uint>();
        if (anchors.Count == 0 || levels.Count == 0 || expandLevel >= 7)
            return hidden;

        var sortedCols = new List<uint>(levels.Keys);
        sortedCols.Sort();

        for (var lvl = expandLevel + 1; lvl <= 7; lvl++)
        {
            var haveRun = false;
            uint runStart = 0, runEnd = 0;

            void CloseRun()
            {
                if (!haveRun)
                    return;
                if (ComputeAnchor(summaryRight, runStart, runEnd) is { } anchor && anchors.Contains(anchor))
                {
                    for (var c = runStart; c <= runEnd; c++)
                        hidden.Add(c);
                }
                haveRun = false;
            }

            foreach (var col in sortedCols)
            {
                if (levels[col] < lvl)
                {
                    CloseRun();
                    continue;
                }
                if (haveRun && col == runEnd + 1)
                {
                    runEnd = col;
                }
                else
                {
                    CloseRun();
                    runStart = runEnd = col;
                    haveRun = true;
                }
            }
            CloseRun();
        }

        return hidden;
    }
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
    private HashSet<uint>? _previousHiddenCols;
    private HashSet<uint>? _previousCollapsedAnchors;

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

        _previousHiddenCols = [.. sheet.GroupHiddenCols];
        _previousCollapsedAnchors = [.. sheet.CollapsedAnchorCols];
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
            }

            if (ColumnGroupAnchorHelper.ComputeAnchor(summaryRight, group.Start, group.End) is { } anchor)
                sheet.CollapsedAnchorCols.Add(anchor);
            return new CommandOutcome(true);
        }

        foreach (var (col, lvl) in sheet.ColOutlineLevels)
        {
            if (lvl >= _level && !sheet.GroupHiddenCols.Contains(col))
                sheet.GroupHiddenCols.Add(col);
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
            if (ColumnGroupAnchorHelper.ComputeAnchor(summaryRight, run.Start, run.End) is { } anchor)
                sheet.CollapsedAnchorCols.Add(anchor);
        }
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousHiddenCols is null || _previousCollapsedAnchors is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        ColumnGroupAnchorHelper.RestoreSet(sheet.GroupHiddenCols, _previousHiddenCols);
        ColumnGroupAnchorHelper.RestoreSet(sheet.CollapsedAnchorCols, _previousCollapsedAnchors);
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
    private HashSet<uint>? _previousHiddenCols;
    private HashSet<uint>? _previousCollapsedAnchors;

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

        _previousHiddenCols = [.. sheet.GroupHiddenCols];
        _previousCollapsedAnchors = [.. sheet.CollapsedAnchorCols];
        var summaryRight = sheet.OutlineSummaryRight ?? true;

        if (_selectionStart is { } selStart)
        {
            if (ColumnOutlineGroupScope.Resolve(sheet.ColOutlineLevels, selStart, _selectionEnd ?? selStart) is not { } group)
                return new CommandOutcome(true);

            var nestedHidden = ColumnGroupAnchorHelper.BuildNestedCollapsedHiddenSet(
                sheet.ColOutlineLevels, sheet.CollapsedAnchorCols, summaryRight, group.Level);
            foreach (var col in sheet.GroupHiddenCols.ToList())
            {
                if (col < group.Start || col > group.End)
                    continue;
                if (!sheet.ColOutlineLevels.TryGetValue(col, out var lvl) || lvl < group.Level)
                    continue;
                // A column hidden by a deeper, still-independently-collapsed nested subgroup must
                // stay hidden when only the outer group is being expanded (R75-commands-outline-group-4-2).
                if (nestedHidden.Contains(col))
                    continue;
                sheet.GroupHiddenCols.Remove(col);
            }

            // The group's detail columns are visible again, so its anchor no longer summarizes a
            // collapsed run -- clear the stale collapsed marker so a later save doesn't re-stamp
            // collapsed="1" on a column that has nothing left to summarize.
            ColumnGroupAnchorHelper.RemoveInvalidAnchorsAffectedByRange(
                sheet.ColOutlineLevels,
                sheet.GroupHiddenCols,
                sheet.CollapsedAnchorCols,
                summaryRight,
                group.Start,
                group.End);
            return new CommandOutcome(true);
        }

        foreach (var col in sheet.GroupHiddenCols.ToList())
        {
            if (sheet.ColOutlineLevels.TryGetValue(col, out var lvl) && lvl >= _level)
            {
                sheet.GroupHiddenCols.Remove(col);
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
            ColumnGroupAnchorHelper.RemoveInvalidAnchorsAffectedByRange(
                sheet.ColOutlineLevels,
                sheet.GroupHiddenCols,
                sheet.CollapsedAnchorCols,
                summaryRight,
                run.Start,
                run.End);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousHiddenCols is null || _previousCollapsedAnchors is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        ColumnGroupAnchorHelper.RestoreSet(sheet.GroupHiddenCols, _previousHiddenCols);
        ColumnGroupAnchorHelper.RestoreSet(sheet.CollapsedAnchorCols, _previousCollapsedAnchors);
    }
}

public sealed class SetColumnOutlineGroupCollapsedCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _startCol;
    private readonly uint _endCol;
    private readonly int _level;
    private readonly bool _collapsed;
    private HashSet<uint>? _previousHiddenCols;
    private HashSet<uint>? _previousCollapsedAnchors;

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

        _previousHiddenCols = [.. sheet.GroupHiddenCols];
        _previousCollapsedAnchors = [.. sheet.CollapsedAnchorCols];
        var summaryRight = sheet.OutlineSummaryRight ?? true;
        // Only the expand path needs to know which columns are still hidden by an independently
        // collapsed nested subgroup, so build this set once here (not per-column) rather than
        // re-walking run boundaries for every qualifying column (R76-perf-recursion-sweep-2).
        var nestedHidden = _collapsed
            ? null
            : ColumnGroupAnchorHelper.BuildNestedCollapsedHiddenSet(
                sheet.ColOutlineLevels, sheet.CollapsedAnchorCols, summaryRight, _level);
        var qualifyingCols = new List<uint>();
        foreach (var (col, level) in sheet.ColOutlineLevels)
        {
            if (col < _startCol || col > _endCol || level < _level)
                continue;

            qualifyingCols.Add(col);
            if (_collapsed)
                sheet.GroupHiddenCols.Add(col);
            // A column hidden by a deeper, still-independently-collapsed nested subgroup must stay
            // hidden when only this (outer) group is being expanded (R75-commands-outline-group-4-2).
            else if (!nestedHidden!.Contains(col))
                sheet.GroupHiddenCols.Remove(col);
        }
        foreach (var run in ColumnGroupAnchorHelper.GetContiguousRuns(qualifyingCols))
        {
            if (_collapsed)
            {
                if (ColumnGroupAnchorHelper.ComputeAnchor(summaryRight, run.Start, run.End) is { } anchor)
                    sheet.CollapsedAnchorCols.Add(anchor);
            }
            else
            {
                ColumnGroupAnchorHelper.RemoveInvalidAnchorsAffectedByRange(
                    sheet.ColOutlineLevels,
                    sheet.GroupHiddenCols,
                    sheet.CollapsedAnchorCols,
                    summaryRight,
                    run.Start,
                    run.End);
            }
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousHiddenCols is null || _previousCollapsedAnchors is null) return;

        var sheet = ctx.GetSheet(_sheetId);
        ColumnGroupAnchorHelper.RestoreSet(sheet.GroupHiddenCols, _previousHiddenCols);
        ColumnGroupAnchorHelper.RestoreSet(sheet.CollapsedAnchorCols, _previousCollapsedAnchors);
    }
}
