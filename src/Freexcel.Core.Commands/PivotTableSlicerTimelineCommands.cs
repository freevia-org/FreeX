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

        var startDate = ParseTimelineDate(_selectedStartDate, DateOnly.MinValue);
        var endDate = ParseTimelineDate(_selectedEndDate, DateOnly.MaxValue);
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
        var selectedItems = ReadTimelineSelectedItems(sourceSheet, pivotTable, sourceFieldIndex, startDate, endDate);
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

    private static DateOnly ParseTimelineDate(string? value, DateOnly fallback) =>
        DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var parsed)
            ? parsed
            : fallback;

    private static IReadOnlyList<string> ReadTimelineSelectedItems(
        Sheet sheet,
        PivotTableModel pivotTable,
        int sourceFieldIndex,
        DateOnly startDate,
        DateOnly endDate)
    {
        var selectedItems = new List<string>();
        var sourceColumn = pivotTable.SourceRange.Start.Col + (uint)sourceFieldIndex;
        var field = pivotTable.RowFields
            .Concat(pivotTable.ColumnFields)
            .Concat(pivotTable.PageFields)
            .FirstOrDefault(item => item.SourceFieldIndex == sourceFieldIndex)
            ?? new PivotFieldModel(sourceFieldIndex);

        for (var row = pivotTable.SourceRange.Start.Row + 1; row <= pivotTable.SourceRange.End.Row; row++)
        {
            if (sheet.GetValue(row, sourceColumn) is not DateTimeValue dateValue)
                continue;

            var date = DateOnly.FromDateTime(dateValue.ToDateTime());
            if (date < startDate || date > endDate)
                continue;

            var key = TimelineKeyText(dateValue, field);
            if (!selectedItems.Contains(key, StringComparer.CurrentCultureIgnoreCase))
                selectedItems.Add(key);
        }

        return selectedItems;
    }

    private static string TimelineKeyText(DateTimeValue dateValue, PivotFieldModel field)
    {
        var date = dateValue.ToDateTime();
        return field.Grouping switch
        {
            PivotFieldGrouping.Year => date.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PivotFieldGrouping.Quarter => $"{date.Year}-Q{((date.Month - 1) / 3) + 1}",
            PivotFieldGrouping.Month => date.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),
            PivotFieldGrouping.Day => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            _ => date.ToShortDateString()
        };
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

        var dateBounds = ReadDateBounds(sourceSheet, target.Value.PivotTable, sourceFieldIndex);
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

    private static (string? Start, string? End) ReadDateBounds(Sheet sheet, PivotTableModel pivotTable, int sourceFieldIndex)
    {
        DateOnly? start = null;
        DateOnly? end = null;
        var sourceColumn = pivotTable.SourceRange.Start.Col + (uint)sourceFieldIndex;
        for (var row = pivotTable.SourceRange.Start.Row + 1; row <= pivotTable.SourceRange.End.Row; row++)
        {
            if (!TryGetDateOnly(sheet.GetValue(row, sourceColumn), out var date))
                continue;
            start = start is null || date < start.Value ? date : start;
            end = end is null || date > end.Value ? date : end;
        }

        return (start?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            end?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static bool TryGetDateOnly(ScalarValue value, out DateOnly date)
    {
        date = default;
        switch (value)
        {
            case DateTimeValue dateTime:
                date = DateOnly.FromDateTime(dateTime.ToDateTime());
                return true;
            case NumberValue number when number.Value > 0 && double.IsFinite(number.Value):
                date = DateOnly.FromDateTime(DateTime.FromOADate(number.Value));
                return true;
            case TextValue text:
                return DateOnly.TryParse(text.Value, System.Globalization.CultureInfo.InvariantCulture, out date);
            default:
                return false;
        }
    }

}

