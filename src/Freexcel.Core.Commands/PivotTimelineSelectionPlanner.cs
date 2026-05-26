using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

internal static class PivotTimelineSelectionPlanner
{
    public static DateOnly ParseTimelineDate(string? value, DateOnly fallback) =>
        DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var parsed)
            ? parsed
            : fallback;

    public static IReadOnlyList<string> ReadSelectedItems(
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

    public static (string? Start, string? End) ReadDateBounds(Sheet sheet, PivotTableModel pivotTable, int sourceFieldIndex)
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
