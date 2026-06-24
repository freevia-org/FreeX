using System.Globalization;

using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Reads the distinct available items for a slicer's connected PivotTable field straight off the
/// source range, and resolves a timeline's display granularity from its date bounds. Pure model
/// reads (no Avalonia types), mirroring the Windows host's <c>ReadPivotFieldItems</c> ordering so
/// the slicer preview tiles and timeline labels match the desktop. The shell feeds the result into
/// the portable <see cref="SlicerLayoutBuilder"/> / <see cref="TimelineLayoutBuilder"/>.
/// </summary>
public static class SlicerTimelineSourceReader
{
    /// <summary>
    /// Returns the distinct, ordered field items for <paramref name="sourceFieldName"/> within
    /// <paramref name="pivotTable"/>'s source range (header row excluded), or an empty list when the
    /// field is not found. Matches the source ordering (current-culture, case-insensitive).
    /// </summary>
    public static IReadOnlyList<string> ReadFieldItems(Sheet sheet, PivotTableModel pivotTable, string? sourceFieldName)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(pivotTable);

        var fieldIndex = FindSourceFieldIndex(sheet, pivotTable, sourceFieldName);
        if (fieldIndex < 0)
            return [];

        var sourceColumn = pivotTable.SourceRange.Start.Col + (uint)fieldIndex;
        if (sourceColumn > pivotTable.SourceRange.End.Col)
            return [];

        return PivotFieldItemsReader.ReadItems(sheet, pivotTable, fieldIndex, FormatValue);
    }

    /// <summary>
    /// Resolves the zero-based source-field index whose header matches <paramref name="sourceFieldName"/>,
    /// or -1 when no header matches.
    /// </summary>
    public static int FindSourceFieldIndex(Sheet sheet, PivotTableModel pivotTable, string? sourceFieldName)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(pivotTable);
        if (string.IsNullOrWhiteSpace(sourceFieldName))
            return -1;

        var index = 0;
        for (var col = pivotTable.SourceRange.Start.Col; col <= pivotTable.SourceRange.End.Col; col++, index++)
        {
            var header = FormatValue(sheet.GetValue(pivotTable.SourceRange.Start.Row, col));
            if (string.Equals(header, sourceFieldName.Trim(), StringComparison.CurrentCultureIgnoreCase))
                return index;
        }

        return -1;
    }

    private static string FormatValue(ScalarValue? value) => value switch
    {
        null or BlankValue => string.Empty,
        TextValue text => text.Value,
        NumberValue number => number.Value.ToString(CultureInfo.CurrentCulture),
        DateTimeValue date => DateTime.FromOADate(date.Value).ToString("yyyy-MM-dd", CultureInfo.CurrentCulture),
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        _ => value.ToString() ?? string.Empty,
    };
}

/// <summary>
/// Resolves the display <see cref="TimelineGranularity"/> for a timeline from the span of its known
/// date bounds: short spans show days, longer spans roll up to month / quarter / year. Mirrors the
/// desktop's bucket-by-span heuristic so the date label reads sensibly at any range size.
/// </summary>
public static class SlicerTimelineGranularity
{
    /// <summary>Resolves the granularity from the timeline's start/end bounds; defaults to month.</summary>
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
            DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
