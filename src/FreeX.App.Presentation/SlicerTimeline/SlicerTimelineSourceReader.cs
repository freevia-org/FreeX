using System.Globalization;

using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.SlicerTimeline;

/// <summary>
/// Reads distinct field items from a resolved PivotTable source range.
/// </summary>
public static class SlicerTimelineSourceReader
{
    public static IReadOnlyList<string> ReadFieldItems(
        Sheet sourceSheet,
        PivotTableModel pivotTable,
        string? sourceFieldName)
    {
        ArgumentNullException.ThrowIfNull(sourceSheet);
        ArgumentNullException.ThrowIfNull(pivotTable);

        if (!HasReadableSourceRange(pivotTable))
            return [];

        var fieldIndex = FindSourceFieldIndex(sourceSheet, pivotTable, sourceFieldName);
        if (fieldIndex < 0)
            return [];

        var sourceColumn = pivotTable.SourceRange.Start.Col + (uint)fieldIndex;
        if (sourceColumn > pivotTable.SourceRange.End.Col)
            return [];

        return PivotFieldItemsReader.ReadItems(sourceSheet, pivotTable, fieldIndex, FormatValue);
    }

    public static int FindSourceFieldIndex(
        Sheet sourceSheet,
        PivotTableModel pivotTable,
        string? sourceFieldName)
    {
        ArgumentNullException.ThrowIfNull(sourceSheet);
        ArgumentNullException.ThrowIfNull(pivotTable);
        if (string.IsNullOrWhiteSpace(sourceFieldName) || !HasReadableSourceRange(pivotTable))
            return -1;

        var index = 0;
        for (var col = pivotTable.SourceRange.Start.Col; col <= pivotTable.SourceRange.End.Col; col++, index++)
        {
            var header = FormatValue(sourceSheet.GetValue(pivotTable.SourceRange.Start.Row, col));
            if (string.Equals(header, sourceFieldName.Trim(), StringComparison.CurrentCultureIgnoreCase))
                return index;
        }

        return -1;
    }

    private static bool HasReadableSourceRange(PivotTableModel pivotTable) =>
        pivotTable.SourceRange.Start.Row > 0 &&
        pivotTable.SourceRange.Start.Col > 0 &&
        pivotTable.SourceRange.End.Row >= pivotTable.SourceRange.Start.Row &&
        pivotTable.SourceRange.End.Col >= pivotTable.SourceRange.Start.Col;

    private static string FormatValue(ScalarValue? value) => value switch
    {
        null or BlankValue => string.Empty,
        TextValue text => text.Value,
        NumberValue number => number.Value.ToString(CultureInfo.CurrentCulture),
        // NumberFormatter, not DateTime.FromOADate: DateTimeValue.Value is an Excel serial (the
        // formula engine, the grid, and TEXT() all read it that way), and .NET's OADate space has
        // no slot for Excel's phantom 1900-02-29 (serial 60) -- converting straight through
        // FromOADate silently prints "1900-02-28" for that serial, one day off from what YEAR/
        // MONTH/DAY, TEXT(), and the grid itself show for the very same cell. Routing through the
        // shared formatter keeps this list agreeing with the cell it was read from.
        DateTimeValue date => NumberFormatter.Format(date, "yyyy-MM-dd"),
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        _ => value.ToString() ?? string.Empty,
    };
}

public sealed record SlicerTimelinePivotSource(
    Sheet PivotSheet,
    Sheet SourceSheet,
    PivotTableModel PivotTable);

/// <summary>
/// Workbook-scoped source resolver and renderer-neutral pane projection for slicers and timelines.
/// </summary>
public sealed class SlicerTimelineSourceSession
{
    private readonly Workbook _workbook;

    public SlicerTimelineSourceSession(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        _workbook = workbook;
    }

    public SlicerTimelinePivotSource? ResolvePivotSource(SlicerModel slicer)
    {
        ArgumentNullException.ThrowIfNull(slicer);
        if (string.IsNullOrWhiteSpace(slicer.SourcePivotTableName))
            return null;

        foreach (var sheet in _workbook.Sheets)
        {
            foreach (var pivotTable in sheet.PivotTables)
            {
                if (!string.Equals(
                        pivotTable.Name,
                        slicer.SourcePivotTableName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var sourceSheet = PivotUiPlanner.ResolvePivotSourceSheet(_workbook, sheet, pivotTable);
                return new SlicerTimelinePivotSource(sheet, sourceSheet, pivotTable);
            }
        }

        return null;
    }

    public IReadOnlyList<string> ReadSlicerSourceItems(SlicerModel slicer)
    {
        ArgumentNullException.ThrowIfNull(slicer);

        var pivotSource = ResolvePivotSource(slicer);
        var resolved = SlicerItemResolver.ResolveAvailableItems(slicer, _workbook, pivotSource?.PivotTable);
        if (resolved.Count > 0)
            return resolved;

        return pivotSource is null
            ? []
            : SlicerTimelineSourceReader.ReadFieldItems(
                pivotSource.SourceSheet,
                pivotSource.PivotTable,
                slicer.SourceFieldName);
    }

    public void PopulateAvailableItems(IEnumerable<SlicerModel> slicers)
    {
        ArgumentNullException.ThrowIfNull(slicers);

        foreach (var slicer in slicers)
            slicer.AvailableItems = ReadSlicerSourceItems(slicer);
    }

    public SlicerPaneItem BuildSlicerPaneItem(SlicerModel slicer)
    {
        ArgumentNullException.ThrowIfNull(slicer);

        return new SlicerPaneItem(
            slicer.Name,
            slicer.SourceFieldName ?? slicer.CacheName,
            SlicerTimelinePanePlanner.BuildSlicerTiles(slicer, ReadSlicerSourceItems(slicer)),
            SlicerTimelinePanePlanner.HasActiveSlicerFilter(slicer));
    }

    public TimelinePaneItem BuildTimelinePaneItem(TimelineModel timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        return SlicerTimelinePanePlanner.BuildTimelineItem(timeline);
    }

    public TimelineGranularity ResolveTimelineGranularity(TimelineModel timeline) =>
        SlicerTimelineGranularity.Resolve(timeline);
}

/// <summary>
/// Resolves timeline display granularity from the timeline's date span.
/// </summary>
public static class SlicerTimelineGranularity
{
    public static TimelineGranularity Resolve(TimelineModel timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        if (!TryParse(timeline.StartDate, out var start) || !TryParse(timeline.EndDate, out var end))
            return TimelineGranularity.Month;

        var days = Math.Abs(end.DayNumber - start.DayNumber);
        return days switch
        {
            <= 62 => TimelineGranularity.Day,
            <= 366 => TimelineGranularity.Month,
            <= 366 * 4 => TimelineGranularity.Quarter,
            _ => TimelineGranularity.Year,
        };
    }

    private static bool TryParse(string? value, out DateOnly date)
    {
        date = default;
        return !string.IsNullOrWhiteSpace(value) &&
            DateOnly.TryParseExact(
                value.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
    }
}
