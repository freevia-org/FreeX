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
    private HashSet<uint>? _previousCollapsedAnchors;

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
        _previousCollapsedAnchors = [.. sheet.CollapsedAnchorRows];
        for (uint r = _startRow; r <= _endRow; r++)
        {
            sheet.RowOutlineLevels.TryGetValue(r, out var prev);
            _previousLevels[r] = prev;
            int newLevel;
            if (_level == 0)
            {
                sheet.RowOutlineLevels.Remove(r);
                newLevel = 0;
            }
            else
            {
                newLevel = OutlineGroupingService.GetGroupedOutlineLevel(prev, _level, _preserveExistingHierarchy);
                sheet.RowOutlineLevels[r] = newLevel;
            }

            // A row whose own outline level just decreased (a full Ungroup to 0, or a partial
            // Ungroup of a nested subgroup down to a nonzero level) can no longer rely on whatever
            // nested subgroup previously justified hiding it -- un-hide it here regardless of the
            // target level, not only when ungrouping all the way to 0 (R58-commands-outline-group-6-1).
            if (newLevel < prev && sheet.GroupHiddenRows.Remove(r))
                _previouslyHiddenByGroup.Add(r);
        }

        RowGroupAnchorHelper.RemoveInvalidAnchorsAffectedByRange(
            sheet.RowOutlineLevels,
            sheet.GroupHiddenRows,
            sheet.CollapsedAnchorRows,
            sheet.OutlineSummaryBelow ?? true,
            _startRow,
            _endRow);

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
        if (_previousCollapsedAnchors is not null)
            RowGroupAnchorHelper.RestoreSet(sheet.CollapsedAnchorRows, _previousCollapsedAnchors);
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
/// Computes the visible outline "anchor" (subtotal/summary) row for a contiguous run of collapsed
/// detail rows, matching Excel's own placement per <c>outlinePr/@summaryBelow</c>: the row just
/// past the run in the summary direction (below the run by default; above it when
/// <c>Sheet.OutlineSummaryBelow</c> is explicitly false). Shared by
/// <see cref="CollapseRowGroupCommand"/> and <see cref="ExpandRowGroupCommand"/> so the two stay
/// in agreement about which row a given run's anchor is (R35-deferred-collapse-anchor-1).
/// </summary>
internal static class RowGroupAnchorHelper
{
    public static List<(uint Start, uint End)> GetContiguousRuns(IEnumerable<uint> rows)
    {
        var sorted = new List<uint>(rows);
        sorted.Sort();
        var runs = new List<(uint Start, uint End)>();
        var haveRun = false;
        uint runStart = 0, runEnd = 0;
        foreach (var row in sorted)
        {
            if (!haveRun)
            {
                runStart = runEnd = row;
                haveRun = true;
            }
            else if (row == runEnd)
            {
                // Duplicate (defensive; callers pass a HashSet-derived sequence in practice).
            }
            else if (row == runEnd + 1)
            {
                runEnd = row;
            }
            else
            {
                runs.Add((runStart, runEnd));
                runStart = runEnd = row;
            }
        }
        if (haveRun)
            runs.Add((runStart, runEnd));
        return runs;
    }

    /// <summary>Returns the anchor row for a run, or null when summaryBelow is false and the run starts at row 1 (no row above to anchor to).</summary>
    public static uint? ComputeAnchor(bool summaryBelow, uint runStart, uint runEnd) =>
        summaryBelow
            ? (runEnd < CellAddress.MaxRow ? runEnd + 1 : null)
            : (runStart > 1 ? runStart - 1 : null);

    public static void RemoveInvalidAnchorsAffectedByRange(
        IReadOnlyDictionary<uint, int> levels,
        IReadOnlySet<uint> hiddenRows,
        HashSet<uint> anchors,
        bool summaryBelow,
        uint affectedStart,
        uint affectedEnd)
    {
        foreach (var anchor in anchors.ToList())
        {
            uint detailRow;
            if (summaryBelow)
            {
                if (anchor <= 1)
                    continue;
                detailRow = anchor - 1;
            }
            else
            {
                if (anchor >= CellAddress.MaxRow)
                    continue;
                detailRow = anchor + 1;
            }

            if (detailRow < affectedStart || detailRow > affectedEnd)
                continue;

            levels.TryGetValue(anchor, out var anchorLevel);
            if (!hiddenRows.Contains(detailRow) ||
                !levels.TryGetValue(detailRow, out var detailLevel) ||
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
    /// True when <paramref name="row"/> qualifies for an expand at <paramref name="expandLevel"/>
    /// (its own level is deeper) but is still hidden by a nested subgroup that is independently
    /// collapsed -- i.e. some level strictly between <paramref name="expandLevel"/> and the row's
    /// own level has a contiguous run (containing this row) whose anchor is still in
    /// <paramref name="anchors"/>. Expanding an outer group must not un-hide such a row; Excel
    /// leaves the inner, still-collapsed subgroup collapsed (R75-commands-outline-group-4-2).
    /// </summary>
    public static bool IsHiddenByNestedCollapsedGroup(
        IReadOnlyDictionary<uint, int> levels,
        IReadOnlySet<uint> anchors,
        bool summaryBelow,
        uint row,
        int expandLevel)
    {
        if (!levels.TryGetValue(row, out var rowLevel) || rowLevel <= expandLevel)
            return false;

        for (var lvl = expandLevel + 1; lvl <= rowLevel; lvl++)
        {
            var runStart = row;
            while (runStart > 0 && levels.TryGetValue(runStart - 1, out var above) && above >= lvl)
                runStart--;
            var runEnd = row;
            while (levels.TryGetValue(runEnd + 1, out var below) && below >= lvl)
                runEnd++;

            if (ComputeAnchor(summaryBelow, runStart, runEnd) is { } anchor && anchors.Contains(anchor))
                return true;
        }
        return false;
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
    private HashSet<uint>? _previousHiddenRows;
    private HashSet<uint>? _previousCollapsedAnchors;

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

        _previousHiddenRows = [.. sheet.GroupHiddenRows];
        _previousCollapsedAnchors = [.. sheet.CollapsedAnchorRows];
        var summaryBelow = sheet.OutlineSummaryBelow ?? true;

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
            }

            if (RowGroupAnchorHelper.ComputeAnchor(summaryBelow, group.Start, group.End) is { } anchor)
                sheet.CollapsedAnchorRows.Add(anchor);
            return new CommandOutcome(true);
        }

        foreach (var (row, lvl) in sheet.RowOutlineLevels)
        {
            if (lvl >= _level && !sheet.GroupHiddenRows.Contains(row))
                sheet.GroupHiddenRows.Add(row);
        }

        // Anchor placement is based on every row currently qualifying at this level (not just the
        // ones newly hidden by this call), so a repeated/partial collapse still resolves the same
        // physical anchor position for each contiguous detail run.
        var qualifyingRows = new List<uint>();
        foreach (var (row, lvl) in sheet.RowOutlineLevels)
        {
            if (lvl >= _level)
                qualifyingRows.Add(row);
        }
        foreach (var run in RowGroupAnchorHelper.GetContiguousRuns(qualifyingRows))
        {
            if (RowGroupAnchorHelper.ComputeAnchor(summaryBelow, run.Start, run.End) is { } anchor)
                sheet.CollapsedAnchorRows.Add(anchor);
        }
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousHiddenRows is null || _previousCollapsedAnchors is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        RowGroupAnchorHelper.RestoreSet(sheet.GroupHiddenRows, _previousHiddenRows);
        RowGroupAnchorHelper.RestoreSet(sheet.CollapsedAnchorRows, _previousCollapsedAnchors);
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
    private HashSet<uint>? _previousHiddenRows;
    private HashSet<uint>? _previousCollapsedAnchors;

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

        _previousHiddenRows = [.. sheet.GroupHiddenRows];
        _previousCollapsedAnchors = [.. sheet.CollapsedAnchorRows];
        var summaryBelow = sheet.OutlineSummaryBelow ?? true;

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
                // A row hidden by a deeper, still-independently-collapsed nested subgroup must
                // stay hidden when only the outer group is being expanded (R75-commands-outline-group-4-2).
                if (RowGroupAnchorHelper.IsHiddenByNestedCollapsedGroup(
                        sheet.RowOutlineLevels, sheet.CollapsedAnchorRows, summaryBelow, row, group.Level))
                    continue;
                sheet.GroupHiddenRows.Remove(row);
            }

            // The group's detail rows are visible again, so its anchor no longer summarizes a
            // collapsed run -- clear the stale collapsed marker so a later save doesn't re-stamp
            // collapsed="1" on a row that has nothing left to summarize.
            RowGroupAnchorHelper.RemoveInvalidAnchorsAffectedByRange(
                sheet.RowOutlineLevels,
                sheet.GroupHiddenRows,
                sheet.CollapsedAnchorRows,
                summaryBelow,
                group.Start,
                group.End);
            return new CommandOutcome(true);
        }

        foreach (var row in sheet.GroupHiddenRows.ToList())
        {
            if (sheet.RowOutlineLevels.TryGetValue(row, out var lvl) && lvl >= _level)
            {
                sheet.GroupHiddenRows.Remove(row);
            }
        }

        // This call just un-hid every row at this level sheet-wide, so -- mirroring
        // CollapseRowGroupCommand's placement over the same qualifying-row set -- none of those
        // runs have any hidden detail left to summarize; clear each run's anchor marker.
        var qualifyingRows = new List<uint>();
        foreach (var (row, lvl) in sheet.RowOutlineLevels)
        {
            if (lvl >= _level)
                qualifyingRows.Add(row);
        }
        foreach (var run in RowGroupAnchorHelper.GetContiguousRuns(qualifyingRows))
            RowGroupAnchorHelper.RemoveInvalidAnchorsAffectedByRange(
                sheet.RowOutlineLevels,
                sheet.GroupHiddenRows,
                sheet.CollapsedAnchorRows,
                summaryBelow,
                run.Start,
                run.End);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousHiddenRows is null || _previousCollapsedAnchors is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        RowGroupAnchorHelper.RestoreSet(sheet.GroupHiddenRows, _previousHiddenRows);
        RowGroupAnchorHelper.RestoreSet(sheet.CollapsedAnchorRows, _previousCollapsedAnchors);
    }
}

public sealed class SetRowOutlineGroupCollapsedCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _startRow;
    private readonly uint _endRow;
    private readonly int _level;
    private readonly bool _collapsed;
    private HashSet<uint>? _previousHiddenRows;
    private HashSet<uint>? _previousCollapsedAnchors;

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

        _previousHiddenRows = [.. sheet.GroupHiddenRows];
        _previousCollapsedAnchors = [.. sheet.CollapsedAnchorRows];
        var summaryBelow = sheet.OutlineSummaryBelow ?? true;
        var qualifyingRows = new List<uint>();
        foreach (var (row, level) in sheet.RowOutlineLevels)
        {
            if (row < _startRow || row > _endRow || level < _level)
                continue;

            qualifyingRows.Add(row);
            if (_collapsed)
                sheet.GroupHiddenRows.Add(row);
            // A row hidden by a deeper, still-independently-collapsed nested subgroup must stay
            // hidden when only this (outer) group is being expanded (R75-commands-outline-group-4-2).
            else if (!RowGroupAnchorHelper.IsHiddenByNestedCollapsedGroup(
                         sheet.RowOutlineLevels, sheet.CollapsedAnchorRows, summaryBelow, row, _level))
                sheet.GroupHiddenRows.Remove(row);
        }
        foreach (var run in RowGroupAnchorHelper.GetContiguousRuns(qualifyingRows))
        {
            if (_collapsed)
            {
                if (RowGroupAnchorHelper.ComputeAnchor(summaryBelow, run.Start, run.End) is { } anchor)
                    sheet.CollapsedAnchorRows.Add(anchor);
            }
            else
            {
                RowGroupAnchorHelper.RemoveInvalidAnchorsAffectedByRange(
                    sheet.RowOutlineLevels,
                    sheet.GroupHiddenRows,
                    sheet.CollapsedAnchorRows,
                    summaryBelow,
                    run.Start,
                    run.End);
            }
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousHiddenRows is null || _previousCollapsedAnchors is null) return;

        var sheet = ctx.GetSheet(_sheetId);
        RowGroupAnchorHelper.RestoreSet(sheet.GroupHiddenRows, _previousHiddenRows);
        RowGroupAnchorHelper.RestoreSet(sheet.CollapsedAnchorRows, _previousCollapsedAnchors);
    }
}
