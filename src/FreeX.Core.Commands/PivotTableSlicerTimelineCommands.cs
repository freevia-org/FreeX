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
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;

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

        var target = PivotTableSlicerTimelineCommandHelpers.FindConnectedPivotTable(ctx.Workbook, timeline.SourcePivotTableName);
        if (target is null)
            return PivotTableSlicerTimelineCommandGuards.ConnectedPivotTableNotFound();

        var (sheet, pivotTable) = target.Value;
        // Check protection of BOTH the pivot table's own sheet AND the sheet the timeline widget
        // itself is anchored on (timeline.SourceSheetName) — they can differ when the timeline is
        // placed on a dashboard sheet that filters a pivot living elsewhere.
        if (PivotTableSlicerTimelineCommandGuards.RejectIfEitherSheetProtected(ctx.Workbook, sheet, timeline.SourceSheetName) is { } protectedOutcome)
            return protectedOutcome;

        var sourceSheet = ctx.Workbook.GetSheet(pivotTable.SourceRange.Start.Sheet) ?? sheet;
        var headers = PivotTableSlicerTimelineCommandHelpers.ReadPivotHeaders(sourceSheet, pivotTable);
        var sourceFieldIndex = PivotTableTimelineCommandLookups.FindSourceFieldIndex(
            headers,
            timeline.SourceFieldName,
            StringComparison.OrdinalIgnoreCase);
        if (sourceFieldIndex < 0)
            return PivotTableSlicerTimelineCommandGuards.ConnectedPivotTableFieldNotFound();

        _snapshot = TimelineRangeSnapshot.Capture(timeline, pivotTable);
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.LastRenderedRange ?? pivotTable.TargetRange);

        timeline.SelectedStartDate = NormalizeSelectedDate(_selectedStartDate);
        timeline.SelectedEndDate = NormalizeSelectedDate(_selectedEndDate);
        var selectedItems = PivotTimelineSelectionPlanner.ReadSelectedItems(sourceSheet, pivotTable, sourceFieldIndex, startDate, endDate);
        // H10: identical fix as SetSlicerSelectionCommand — a timeline can be connected to a date field
        // that was never dragged into Row/Column/PageFields. Without ensuring it's in one of those
        // lists, ReplaceSelectedItems below is a no-op and the range selection never filters anything.
        PivotTableSlicerTimelineCommandHelpers.EnsureFieldInLayout(pivotTable.RowFields, pivotTable.ColumnFields, pivotTable.PageFields, sourceFieldIndex);
        PivotTableSlicerTimelineCommandHelpers.ReplaceSelectedItems(pivotTable.RowFields, sourceFieldIndex, selectedItems);
        PivotTableSlicerTimelineCommandHelpers.ReplaceSelectedItems(pivotTable.ColumnFields, sourceFieldIndex, selectedItems);
        PivotTableSlicerTimelineCommandHelpers.ReplaceSelectedItems(pivotTable.PageFields, sourceFieldIndex, selectedItems);

        PivotTableRefreshService.Refresh(ctx.Workbook, sheet, pivotTable);
        return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        var timeline = PivotTableTimelineCommandLookups.FindTimeline(ctx.Workbook, _timelineName);
        var target = timeline?.SourcePivotTableName is null ? null : PivotTableSlicerTimelineCommandHelpers.FindConnectedPivotTable(ctx.Workbook, timeline.SourcePivotTableName);
        if (timeline is not null && target is { } connected && _snapshot is not null)
        {
            PivotTableRefreshService.ClearRenderedRange(connected.Sheet, connected.PivotTable.LastRenderedRange);
            _snapshot.Restore(timeline, connected.PivotTable);
            AddPivotTableCommand.Restore(connected.Sheet, _targetSnapshot);
        }

        _snapshot = null;
        _targetSnapshot = null;
    }

    private static string? NormalizeSelectedDate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record TimelineRangeSnapshot(
        string? SelectedStartDate,
        string? SelectedEndDate,
        IReadOnlyList<PivotFieldModel> RowFields,
        IReadOnlyList<PivotFieldModel> ColumnFields,
        IReadOnlyList<PivotFieldModel> PageFields,
        GridRange? LastRenderedRange)
    {
        public static TimelineRangeSnapshot Capture(TimelineModel timeline, PivotTableModel pivotTable) =>
            new(
                timeline.SelectedStartDate,
                timeline.SelectedEndDate,
                pivotTable.RowFields.ToList(),
                pivotTable.ColumnFields.ToList(),
                pivotTable.PageFields.ToList(),
                pivotTable.LastRenderedRange);

        public void Restore(TimelineModel timeline, PivotTableModel pivotTable)
        {
            timeline.SelectedStartDate = SelectedStartDate;
            timeline.SelectedEndDate = SelectedEndDate;
            PivotTableCommandCollections.Replace(pivotTable.RowFields, RowFields);
            PivotTableCommandCollections.Replace(pivotTable.ColumnFields, ColumnFields);
            PivotTableCommandCollections.Replace(pivotTable.PageFields, PageFields);
            pivotTable.LastRenderedRange = LastRenderedRange;
        }
    }
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

