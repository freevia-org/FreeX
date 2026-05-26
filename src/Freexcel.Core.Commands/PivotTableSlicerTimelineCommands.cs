using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

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

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var timeline = ctx.Workbook.Timelines.FirstOrDefault(item =>
            string.Equals(item.Name, _timelineName, StringComparison.OrdinalIgnoreCase));
        if (timeline is null)
            return new CommandOutcome(false, "Timeline was not found.");
        if (string.IsNullOrWhiteSpace(timeline.SourcePivotTableName) ||
            string.IsNullOrWhiteSpace(timeline.SourceFieldName))
        {
            return new CommandOutcome(false, "Timeline is not connected to a PivotTable field.");
        }

        var startDate = PivotTimelineSelectionPlanner.ParseTimelineDate(_selectedStartDate, DateOnly.MinValue);
        var endDate = PivotTimelineSelectionPlanner.ParseTimelineDate(_selectedEndDate, DateOnly.MaxValue);
        if (startDate > endDate)
            return new CommandOutcome(false, "Timeline start date must be on or before the end date.");

        var target = PivotTableSlicerTimelineCommandHelpers.FindConnectedPivotTable(ctx.Workbook, timeline.SourcePivotTableName);
        if (target is null)
            return new CommandOutcome(false, "Connected PivotTable was not found.");

        var (sheet, pivotTable) = target.Value;
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UsePivotTableReports) is { } protectedOutcome)
            return protectedOutcome;

        var sourceSheet = ctx.Workbook.GetSheet(pivotTable.SourceRange.Start.Sheet) ?? sheet;
        var headers = PivotTableSlicerTimelineCommandHelpers.ReadPivotHeaders(sourceSheet, pivotTable);
        var sourceFieldIndex = headers.FindIndex(header =>
            string.Equals(header, timeline.SourceFieldName, StringComparison.OrdinalIgnoreCase));
        if (sourceFieldIndex < 0)
            return new CommandOutcome(false, "Connected PivotTable field was not found.");

        _snapshot = TimelineRangeSnapshot.Capture(timeline, pivotTable);
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.TargetRange);

        timeline.SelectedStartDate = _selectedStartDate;
        timeline.SelectedEndDate = _selectedEndDate;
        var selectedItems = PivotTimelineSelectionPlanner.ReadSelectedItems(sourceSheet, pivotTable, sourceFieldIndex, startDate, endDate);
        PivotTableSlicerTimelineCommandHelpers.ReplaceSelectedItems(pivotTable.RowFields, sourceFieldIndex, selectedItems);
        PivotTableSlicerTimelineCommandHelpers.ReplaceSelectedItems(pivotTable.ColumnFields, sourceFieldIndex, selectedItems);
        PivotTableSlicerTimelineCommandHelpers.ReplaceSelectedItems(pivotTable.PageFields, sourceFieldIndex, selectedItems);

        PivotTableRefreshService.Refresh(ctx.Workbook, sheet, pivotTable);
        return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        var timeline = ctx.Workbook.Timelines.FirstOrDefault(item =>
            string.Equals(item.Name, _timelineName, StringComparison.OrdinalIgnoreCase));
        var target = timeline?.SourcePivotTableName is null ? null : PivotTableSlicerTimelineCommandHelpers.FindConnectedPivotTable(ctx.Workbook, timeline.SourcePivotTableName);
        if (timeline is not null && target is { } connected && _snapshot is not null)
        {
            _snapshot.Restore(timeline, connected.PivotTable);
            AddPivotTableCommand.Restore(connected.Sheet, _targetSnapshot);
        }

        _snapshot = null;
        _targetSnapshot = null;
    }

    private sealed record TimelineRangeSnapshot(
        string? SelectedStartDate,
        string? SelectedEndDate,
        IReadOnlyList<PivotFieldModel> RowFields,
        IReadOnlyList<PivotFieldModel> ColumnFields,
        IReadOnlyList<PivotFieldModel> PageFields)
    {
        public static TimelineRangeSnapshot Capture(TimelineModel timeline, PivotTableModel pivotTable) =>
            new(
                timeline.SelectedStartDate,
                timeline.SelectedEndDate,
                pivotTable.RowFields.ToList(),
                pivotTable.ColumnFields.ToList(),
                pivotTable.PageFields.ToList());

        public void Restore(TimelineModel timeline, PivotTableModel pivotTable)
        {
            timeline.SelectedStartDate = SelectedStartDate;
            timeline.SelectedEndDate = SelectedEndDate;
            Replace(pivotTable.RowFields, RowFields);
            Replace(pivotTable.ColumnFields, ColumnFields);
            Replace(pivotTable.PageFields, PageFields);
        }

        private static void Replace<T>(List<T> target, IReadOnlyList<T> source)
        {
            target.Clear();
            target.AddRange(source);
        }
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

        if (ctx.Workbook.Timelines.Any(timeline => string.Equals(timeline.Name, _timelineName, StringComparison.OrdinalIgnoreCase)))
            return new CommandOutcome(false, "A timeline with that name already exists.");

        var target = PivotTableSlicerTimelineCommandHelpers.FindConnectedPivotTable(ctx.Workbook, _pivotTableName);
        if (target is null)
            return new CommandOutcome(false, "Connected PivotTable was not found.");
        if (CommandGuards.RejectIfProtectedWithoutPermission(target.Value.Sheet, SheetProtectionPermission.UsePivotTableReports) is { } protectedOutcome)
            return protectedOutcome;
        if (CommandGuards.RejectIfProtectedWithoutPermission(target.Value.Sheet, SheetProtectionPermission.EditObjects) is { } objectProtectedOutcome)
            return objectProtectedOutcome;

        var sourceSheet = ctx.Workbook.GetSheet(target.Value.PivotTable.SourceRange.Start.Sheet) ?? target.Value.Sheet;
        var headers = PivotTableSlicerTimelineCommandHelpers.ReadPivotHeaders(sourceSheet, target.Value.PivotTable);
        var sourceFieldIndex = headers.FindIndex(header => string.Equals(header, _sourceFieldName, StringComparison.CurrentCultureIgnoreCase));
        if (sourceFieldIndex < 0)
            return new CommandOutcome(false, "Connected PivotTable field was not found.");

        var dateBounds = PivotTimelineSelectionPlanner.ReadDateBounds(sourceSheet, target.Value.PivotTable, sourceFieldIndex);
        var timeline = new TimelineModel
        {
            Name = _timelineName.Trim(),
            CacheName = $"Timeline_{PivotTableSlicerTimelineCommandHelpers.SanitizeCacheName(_timelineName, "Timeline")}",
            SourcePivotTableName = target.Value.PivotTable.Name,
            SourceFieldName = headers[sourceFieldIndex],
            StartDate = dateBounds.Start,
            EndDate = dateBounds.End
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

