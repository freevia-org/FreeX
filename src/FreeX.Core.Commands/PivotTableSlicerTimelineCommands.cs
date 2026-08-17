using FreeX.Core.Model;

namespace FreeX.Core.Commands;

file static class PivotTableTimelineCommandLookups
{
    public static TimelineModel? FindTimeline(Workbook workbook, string? timelineName)
    {
        foreach (var timeline in workbook.Timelines)
        {
            if (string.Equals(timeline.Name, timelineName, StringComparison.OrdinalIgnoreCase))
                return timeline;
        }

        return null;
    }

    public static int FindSourceFieldIndex(IReadOnlyList<string> headers, string? sourceFieldName, StringComparison comparison)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            if (string.Equals(headers[index], sourceFieldName, comparison))
                return index;
        }

        return -1;
    }
}

public sealed class SetTimelineRangeCommand : IWorkbookCommand
{
    private readonly string _timelineName;
    private readonly string? _selectedStartDate;
    private readonly string? _selectedEndDate;
    private TimelineRangeSnapshot? _snapshot;
    private List<(Sheet Sheet, PivotTableModel PivotTable, List<(CellAddress Address, Cell? Cell)> Snapshot)>? _targetSnapshots;

    public SetTimelineRangeCommand(string timelineName, string? selectedStartDate, string? selectedEndDate)
    {
        _timelineName = timelineName;
        _selectedStartDate = selectedStartDate;
        _selectedEndDate = selectedEndDate;
    }

    public string Label => "Set Timeline Range";

    /// <summary>The start date string this command will apply (yyyy-MM-dd, or null for open-ended).</summary>
    public string? SelectedStartDate => _selectedStartDate;

    /// <summary>The end date string this command will apply (yyyy-MM-dd, or null for open-ended).</summary>
    public string? SelectedEndDate => _selectedEndDate;

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var timeline = PivotTableTimelineCommandLookups.FindTimeline(ctx.Workbook, _timelineName);
        if (timeline is null)
            return new CommandOutcome(false, "Timeline was not found.");
        if (string.IsNullOrWhiteSpace(timeline.SourcePivotTableName) ||
            string.IsNullOrWhiteSpace(timeline.SourceFieldName))
        {
            return new CommandOutcome(false, "Timeline is not connected to a PivotTable field.");
        }

        if (!PivotTimelineSelectionPlanner.TryParseTimelineDate(_selectedStartDate, DateOnly.MinValue, out var startDate) ||
            !PivotTimelineSelectionPlanner.TryParseTimelineDate(_selectedEndDate, DateOnly.MaxValue, out var endDate))
        {
            return new CommandOutcome(false, "Timeline dates must use yyyy-MM-dd.");
        }

        if (startDate > endDate)
            return new CommandOutcome(false, "Timeline start date must be on or before the end date.");

        // R133x-commands-slicer-timeline-multipivot-runtime: a timeline can drive SEVERAL pivot tables at
        // once (Excel's "Report Connections") -- resolve every connection (ConnectedPivotTableNames,
        // primary first), not just the single primary SourcePivotTableName, or every connection past the
        // first silently stops being filtered by this control even though the R133 persistence fix keeps
        // the file recording all of them.
        var connectedNames = PivotTableSlicerTimelineCommandHelpers.ResolveConnectedPivotTableNames(
            timeline.SourcePivotTableName, timeline.ConnectedPivotTableNames);

        var targets = new List<(Sheet Sheet, PivotTableModel PivotTable)>();
        foreach (var name in connectedNames)
        {
            var resolved = PivotTableSlicerTimelineCommandHelpers.FindConnectedPivotTable(ctx.Workbook, name);
            if (resolved is null)
            {
                if (string.Equals(name, timeline.SourcePivotTableName, StringComparison.OrdinalIgnoreCase))
                    return PivotTableSlicerTimelineCommandGuards.ConnectedPivotTableNotFound();
                continue;
            }

            targets.Add(resolved.Value);
        }

        if (targets.Count == 0)
            return PivotTableSlicerTimelineCommandGuards.ConnectedPivotTableNotFound();

        // Check protection of BOTH each connected pivot table's own sheet AND the sheet the timeline
        // widget itself is anchored on (timeline.SourceSheetName) — they can differ when the timeline is
        // placed on a dashboard sheet that filters pivots living elsewhere. Validate every target BEFORE
        // mutating anything so a protected connection blocks the whole range change, not just part of it.
        foreach (var (targetSheet, _) in targets)
        {
            if (PivotTableSlicerTimelineCommandGuards.RejectIfEitherSheetProtected(ctx.Workbook, targetSheet, timeline.SourceSheetName) is { } protectedOutcome)
                return protectedOutcome;
        }

        var resolvedTargets = new List<(Sheet Sheet, PivotTableModel PivotTable, Sheet SourceSheet, int SourceFieldIndex)>();
        foreach (var (targetSheet, pivotTable) in targets)
        {
            var sourceSheet = ctx.Workbook.GetSheet(pivotTable.SourceRange.Start.Sheet) ?? targetSheet;
            var headers = PivotTableSlicerTimelineCommandHelpers.ReadPivotHeaders(sourceSheet, pivotTable);
            var sourceFieldIndex = PivotTableTimelineCommandLookups.FindSourceFieldIndex(
                headers,
                timeline.SourceFieldName,
                StringComparison.OrdinalIgnoreCase);
            if (sourceFieldIndex < 0)
            {
                if (string.Equals(pivotTable.Name, timeline.SourcePivotTableName, StringComparison.OrdinalIgnoreCase))
                    return PivotTableSlicerTimelineCommandGuards.ConnectedPivotTableFieldNotFound();
                continue;
            }

            resolvedTargets.Add((targetSheet, pivotTable, sourceSheet, sourceFieldIndex));
        }

        if (resolvedTargets.Count == 0)
            return PivotTableSlicerTimelineCommandGuards.ConnectedPivotTableFieldNotFound();

        _snapshot = TimelineRangeSnapshot.Capture(timeline, resolvedTargets.Select(t => (t.Sheet, t.PivotTable)).ToList());
        _targetSnapshots = resolvedTargets
            .Select(t => (t.Sheet, t.PivotTable, AddPivotTableCommand.Snapshot(t.Sheet, t.PivotTable.LastRenderedRange ?? t.PivotTable.TargetRange)))
            .ToList();

        timeline.SelectedStartDate = NormalizeSelectedDate(_selectedStartDate);
        timeline.SelectedEndDate = NormalizeSelectedDate(_selectedEndDate);

        foreach (var (targetSheet, pivotTable, sourceSheet, sourceFieldIndex) in resolvedTargets)
        {
            // P9: a null/null range (both bounds cleared, e.g. clicking the timeline's clear icon) means
            // "remove the filter", not "select every date currently in the source range". The previous
            // code always called ReadSelectedItems(MinValue, MaxValue), which enumerates only the DateTimeValue
            // rows that exist RIGHT NOW and installs that as an explicit SelectedItems list — rows with
            // blank/text values in the field (which Excel keys to "(blank)"/text and which a real clear
            // must restore) stay excluded, and rows added by a later refresh with dates outside today's
            // snapshot also stay filtered out, even though HasActiveTimelineFilter/SelectedStartDate/
            // SelectedEndDate all read back as "no filter" — an invisible, un-clearable stale filter.
            // Passing an empty list makes ReplaceSelectedItems null out SelectedItem/SelectedItems (the
            // same "genuinely cleared" path SetSlicerSelectionCommand's clear button already uses), so
            // MatchesFieldSelections stops filtering the field entirely, matching Excel.
            // R133x: computed PER connected pivot table -- each one resolves its own source rows, so the
            // same date range can select a different concrete row set in a pivot with different source data.
            var selectedItems = timeline.SelectedStartDate is null && timeline.SelectedEndDate is null
                ? []
                : PivotTimelineSelectionPlanner.ReadSelectedItems(sourceSheet, pivotTable, sourceFieldIndex, startDate, endDate);
            // H10: identical fix as SetSlicerSelectionCommand — a timeline can be connected to a date field
            // that was never dragged into Row/Column/PageFields. Without ensuring it's in one of those
            // lists, ReplaceSelectedItems below is a no-op and the range selection never filters anything.
            PivotTableSlicerTimelineCommandHelpers.EnsureFieldInLayout(pivotTable.RowFields, pivotTable.ColumnFields, pivotTable.PageFields, sourceFieldIndex);
            PivotTableSlicerTimelineCommandHelpers.ReplaceSelectedItems(pivotTable.RowFields, sourceFieldIndex, selectedItems);
            PivotTableSlicerTimelineCommandHelpers.ReplaceSelectedItems(pivotTable.ColumnFields, sourceFieldIndex, selectedItems);
            PivotTableSlicerTimelineCommandHelpers.ReplaceSelectedItems(pivotTable.PageFields, sourceFieldIndex, selectedItems);

            // R140-remediation-pivot-refresh-growth-guard-completeness: identical fix as
            // SetSlicerSelectionCommand -- a timeline range change can bring previously-filtered-out
            // row/column items back into view, which can grow ANY connected pivot's footprint past its
            // previous render. A timeline can drive several pivot tables at once (R133x), so a conflict
            // on ANY one of them must fail the whole command atomically -- see RestoreAllTimelineTargets
            // below and PivotTableRefreshService.GrowthGuard.cs.
            var baseline = PivotTableRefreshService.CaptureGrowthGuardBaseline(targetSheet, pivotTable);
            if (PivotTableRefreshService.RefreshGuarded(ctx.Workbook, targetSheet, pivotTable, baseline, RestoreAllTimelineTargets) is { } failure)
            {
                _snapshot = null;
                _targetSnapshots = null;
                return failure;
            }
            // R134-commands-pivotchart-stale-datarange: identical fix as SetSlicerSelectionCommand -- a
            // timeline range change re-filters the pivot's rows (Refresh above), which moves/shrinks/
            // grows its materialized output range; without this, a PivotChart bound to this pivot table
            // keeps rendering the cells the pivot occupied BEFORE the range change.
            PivotTableRefreshService.UpdateBoundPivotCharts(ctx.Workbook, targetSheet, pivotTable);
        }

        return new CommandOutcome(true, AffectedCells: resolvedTargets.Select(t => t.PivotTable.TargetRange.Start).ToArray());

        void RestoreAllTimelineTargets()
        {
            if (_snapshot is not { } snap)
                return;

            foreach (var pts in snap.PivotTables)
                PivotTableRefreshService.ClearRenderedRange(pts.Sheet, pts.PivotTable.LastRenderedRange);
            snap.Restore(timeline);
            if (_targetSnapshots is not null)
                foreach (var (targetCellSheet, _, cellSnapshot) in _targetSnapshots)
                    AddPivotTableCommand.Restore(targetCellSheet, cellSnapshot);
        }
    }

    public void Revert(ICommandContext ctx)
    {
        var timeline = PivotTableTimelineCommandLookups.FindTimeline(ctx.Workbook, _timelineName);
        if (timeline is not null && _snapshot is not null)
        {
            // Clear each affected pivot's CURRENT rendered range (post-Apply) before the snapshot
            // restores RowFields/ColumnFields/PageFields/LastRenderedRange back to their pre-Apply
            // values, mirroring the single-pivot original ordering for every connected pivot table.
            // Uses the DIRECT sheet/pivot-table object references captured at Apply time (not a
            // by-name re-lookup) so a rename applied after this command (e.g. RenamePivotTableCommand
            // sitting above this one on the undo stack) can never make the restore silently miss its
            // target -- the same live objects that were mutated are the ones restored.
            foreach (var snapshot in _snapshot.PivotTables)
                PivotTableRefreshService.ClearRenderedRange(snapshot.Sheet, snapshot.PivotTable.LastRenderedRange);

            _snapshot.Restore(timeline);

            if (_targetSnapshots is not null)
            {
                foreach (var (sheet, _, cellSnapshot) in _targetSnapshots)
                    AddPivotTableCommand.Restore(sheet, cellSnapshot);
            }

            // R134-commands-pivotchart-stale-datarange: point every affected pivot's bound PivotChart(s)
            // back at the just-restored (pre-Apply) output range, mirroring the Apply-side sync above.
            foreach (var snapshot in _snapshot.PivotTables)
                PivotTableRefreshService.UpdateBoundPivotCharts(ctx.Workbook, snapshot.Sheet, snapshot.PivotTable);
        }

        _snapshot = null;
        _targetSnapshots = null;
    }

    private static string? NormalizeSelectedDate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// R133x-commands-slicer-timeline-multipivot-runtime: the timeline-level selection (SelectedStartDate/
    /// SelectedEndDate) is captured ONCE, but the pivot-table-level layout (RowFields/ColumnFields/
    /// PageFields/LastRenderedRange) is captured per CONNECTED pivot table -- a single timeline range
    /// change can mutate several pivot tables at once, and undo must restore every one of them, not just
    /// the primary connection. Each entry holds a DIRECT reference to the mutated <see cref="PivotTableModel"/>
    /// (not its name), so restoring never depends on a by-name re-lookup that a rename applied between
    /// Apply and Revert (by another command sitting above this one on the undo stack) could invalidate.
    /// </summary>
    private sealed record TimelineRangeSnapshot(
        string? SelectedStartDate,
        string? SelectedEndDate,
        IReadOnlyList<PivotTableFieldsSnapshot> PivotTables)
    {
        public static TimelineRangeSnapshot Capture(TimelineModel timeline, IReadOnlyList<(Sheet Sheet, PivotTableModel PivotTable)> targets) =>
            new(
                timeline.SelectedStartDate,
                timeline.SelectedEndDate,
                targets.Select(t => new PivotTableFieldsSnapshot(
                    t.Sheet,
                    t.PivotTable,
                    t.PivotTable.RowFields.ToList(),
                    t.PivotTable.ColumnFields.ToList(),
                    t.PivotTable.PageFields.ToList(),
                    t.PivotTable.LastRenderedRange)).ToList());

        public void Restore(TimelineModel timeline)
        {
            timeline.SelectedStartDate = SelectedStartDate;
            timeline.SelectedEndDate = SelectedEndDate;

            foreach (var snapshot in PivotTables)
            {
                var pivotTable = snapshot.PivotTable;
                PivotTableCommandCollections.Replace(pivotTable.RowFields, snapshot.RowFields);
                PivotTableCommandCollections.Replace(pivotTable.ColumnFields, snapshot.ColumnFields);
                PivotTableCommandCollections.Replace(pivotTable.PageFields, snapshot.PageFields);
                pivotTable.LastRenderedRange = snapshot.LastRenderedRange;
            }
        }
    }

    private sealed record PivotTableFieldsSnapshot(
        Sheet Sheet,
        PivotTableModel PivotTable,
        IReadOnlyList<PivotFieldModel> RowFields,
        IReadOnlyList<PivotFieldModel> ColumnFields,
        IReadOnlyList<PivotFieldModel> PageFields,
        GridRange? LastRenderedRange);
}

/// <summary>
/// Cycles the timeline's display granularity level (Years → Quarters → Months → Days → Years) by
/// updating the OOXML <c>level</c> attribute on the <see cref="TimelineModel"/>. The pivot table
/// filter is NOT changed — only the display bucket changes (matching Excel's behaviour when you
/// click the granularity dropdown and pick a level). Undoable via <see cref="Revert"/>.
/// </summary>
public sealed class SetTimelineGranularityCommand : IWorkbookCommand
{
    // OOXML level: 0=Years 1=Quarters 2=Months 3=Days (matches TimelineLayoutBuilder.LevelToGranularity).
    private const int MaxLevel = 3;
    private readonly string _timelineName;
    private readonly int _newLevel;
    // H59: must be nullable and capture timeline.Level VERBATIM (including null/absent), not the
    // ?? 2 fallback used for computing the new level — otherwise undo would turn an absent Level
    // (no OOXML level attribute written) into an explicit Level=2, a spurious round-trip regression.
    private int? _previousLevel;

    public SetTimelineGranularityCommand(string timelineName, int newLevel)
    {
        _timelineName = timelineName;
        _newLevel = Math.Clamp(newLevel, 0, MaxLevel);
    }

    public string Label => "Set Timeline Granularity";

    /// <summary>Cycles a current OOXML level (0–3, or null→2 for Month) to the next level in the ring.</summary>
    public static int CycleLevel(int? currentLevel)
    {
        var current = Math.Clamp(currentLevel ?? 2, 0, MaxLevel);
        return (current + 1) % (MaxLevel + 1);
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var timeline = PivotTableTimelineCommandLookups.FindTimeline(ctx.Workbook, _timelineName);
        if (timeline is null)
            return new CommandOutcome(false, "Timeline was not found.");

        // Same guard as every sibling slicer/timeline command: granularity is persisted workbook
        // state (round-tripped as the OOXML `level` attribute), not ephemeral UI state, so it must
        // be blocked on a protected sheet without UsePivotTableReports permission just like
        // SetTimelineRangeCommand/AddTimelineCommand/AddSlicerCommand/SetSlicerSelectionCommand.
        // Checks BOTH the connected pivot table's sheet AND the sheet the timeline widget itself
        // is anchored on (timeline.SourceSheetName), since they can differ.
        if (!string.IsNullOrWhiteSpace(timeline.SourcePivotTableName))
        {
            var target = PivotTableSlicerTimelineCommandHelpers.FindConnectedPivotTable(ctx.Workbook, timeline.SourcePivotTableName);
            if (target is { } connected &&
                PivotTableSlicerTimelineCommandGuards.RejectIfEitherSheetProtected(ctx.Workbook, connected.Sheet, timeline.SourceSheetName) is { } protectedOutcome)
            {
                return protectedOutcome;
            }
        }

        _previousLevel = timeline.Level;
        timeline.Level = _newLevel;

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var timeline = PivotTableTimelineCommandLookups.FindTimeline(ctx.Workbook, _timelineName);
        if (timeline is null)
            return;

        timeline.Level = _previousLevel;
    }
}

public sealed class AddTimelineCommand : IWorkbookCommand
{
    private readonly string _timelineName;
    private readonly string _pivotTableName;
    private readonly string _sourceFieldName;
    private TimelineModel? _addedTimeline;

    public AddTimelineCommand(string timelineName, string pivotTableName, string sourceFieldName)
    {
        _timelineName = timelineName;
        _pivotTableName = pivotTableName;
        _sourceFieldName = sourceFieldName;
    }

    public string Label => "Insert Timeline";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (string.IsNullOrWhiteSpace(_timelineName) ||
            string.IsNullOrWhiteSpace(_pivotTableName) ||
            string.IsNullOrWhiteSpace(_sourceFieldName))
        {
            return new CommandOutcome(false, "Timeline name, PivotTable, and field are required.");
        }

        if (PivotTableTimelineCommandLookups.FindTimeline(ctx.Workbook, _timelineName) is not null)
            return new CommandOutcome(false, "A timeline with that name already exists.");

        var target = PivotTableSlicerTimelineCommandHelpers.FindConnectedPivotTable(ctx.Workbook, _pivotTableName);
        if (target is null)
            return PivotTableSlicerTimelineCommandGuards.ConnectedPivotTableNotFound();
        if (CommandGuards.RejectIfProtectedWithoutPermission(target.Value.Sheet, SheetProtectionPermission.UsePivotTableReports) is { } protectedOutcome)
            return protectedOutcome;
        if (PivotTableSlicerTimelineCommandGuards.RejectIfEditObjectsBlocked(target.Value.Sheet) is { } objectProtectedOutcome)
            return objectProtectedOutcome;

        var sourceSheet = ctx.Workbook.GetSheet(target.Value.PivotTable.SourceRange.Start.Sheet) ?? target.Value.Sheet;
        var headers = PivotTableSlicerTimelineCommandHelpers.ReadPivotHeaders(sourceSheet, target.Value.PivotTable);
        var sourceFieldIndex = PivotTableTimelineCommandLookups.FindSourceFieldIndex(
            headers,
            _sourceFieldName,
            StringComparison.CurrentCultureIgnoreCase);
        if (sourceFieldIndex < 0)
            return PivotTableSlicerTimelineCommandGuards.ConnectedPivotTableFieldNotFound();

        var dateBounds = PivotTimelineSelectionPlanner.ReadDateBounds(sourceSheet, target.Value.PivotTable, sourceFieldIndex);
        if (dateBounds.Start is null && dateBounds.End is null)
            return new CommandOutcome(false, "Timeline source field must contain dates.");

        var timeline = new TimelineModel
        {
            Name = _timelineName.Trim(),
            CacheName = $"Timeline_{PivotTableSlicerTimelineCommandHelpers.SanitizeCacheName(_timelineName, "Timeline")}",
            SourcePivotTableName = target.Value.PivotTable.Name,
            SourceFieldName = headers[sourceFieldIndex],
            StartDate = dateBounds.Start,
            EndDate = dateBounds.End,
            DrawingAnchor = PivotTableFloatingControlAnchor.CreateDefault(target.Value.PivotTable)
        };
        ctx.Workbook.Timelines.Add(timeline);
        _addedTimeline = timeline;
        return new CommandOutcome(true, AffectedCells: [target.Value.PivotTable.TargetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_addedTimeline is not null)
            ctx.Workbook.Timelines.Remove(_addedTimeline);
        _addedTimeline = null;
    }

}

