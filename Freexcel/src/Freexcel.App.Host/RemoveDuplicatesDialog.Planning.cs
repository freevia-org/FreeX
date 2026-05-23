using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed partial class RemoveDuplicatesDialog
{
    public static IReadOnlyList<RemoveDuplicateColumnChoice> SelectAll(int columnCount) =>
        BuildColumnChoices(columnCount, isSelected: true);

    public static IReadOnlyList<RemoveDuplicateColumnChoice> SelectAll(IEnumerable<RemoveDuplicateColumnChoice> columns) =>
        columns.Select(column => column with { IsSelected = true }).ToList();

    public static IReadOnlyList<RemoveDuplicateColumnChoice> ClearAll(IEnumerable<RemoveDuplicateColumnChoice> columns) =>
        columns.Select(column => column with { IsSelected = false }).ToList();

    public static RemoveDuplicatesDialogResult CreateResult(IEnumerable<RemoveDuplicateColumnChoice> columns)
    {
        var offsets = columns
            .Where(column => column.IsSelected)
            .Select(column => column.Offset)
            .ToList();
        return new RemoveDuplicatesDialogResult(offsets);
    }

    public static GridRange ExcludeHeaderRow(GridRange range, bool hasHeaders)
    {
        if (!hasHeaders || range.Start.Row >= range.End.Row)
            return range;

        return new GridRange(
            new CellAddress(range.Start.Sheet, range.Start.Row + 1, range.Start.Col),
            range.End);
    }

    private static IReadOnlyList<RemoveDuplicateColumnChoice> BuildColumnChoices(int columnCount, bool isSelected)
    {
        if (columnCount < 0)
            throw new ArgumentOutOfRangeException(nameof(columnCount), columnCount, "Column count cannot be negative.");

        return Enumerable
            .Range(0, columnCount)
            .Select(index => new RemoveDuplicateColumnChoice((uint)index, $"Column {index + 1}", isSelected))
            .ToList();
    }

    public static IReadOnlyList<RemoveDuplicateColumnChoice> BuildColumnChoices(GridRange range) =>
        Enumerable
            .Range(0, (int)range.ColCount)
            .Select(index => new RemoveDuplicateColumnChoice((uint)index, $"Column {index + 1}", true))
            .ToList();

    public static IReadOnlyList<RemoveDuplicateColumnChoice> BuildColumnChoices(Sheet sheet, GridRange range) =>
        BuildColumnChoices(sheet, range, hasHeaders: true);

    public static IReadOnlyList<RemoveDuplicateColumnChoice> BuildColumnChoices(Sheet sheet, GridRange range, bool hasHeaders) =>
        Enumerable
            .Range(0, (int)range.ColCount)
            .Select(index =>
            {
                var absoluteColumn = range.Start.Col + (uint)index;
                var header = hasHeaders
                    ? SpreadsheetDisplayFormatter.FormatCellValue(sheet.GetCell(range.Start.Row, absoluteColumn)?.Value)
                    : "";
                if (string.IsNullOrWhiteSpace(header))
                    header = $"Column {CellAddress.NumberToColumnName(absoluteColumn)}";

                return new RemoveDuplicateColumnChoice((uint)index, header, true);
            })
            .ToList();

    public static bool GuessHasHeaders(Sheet sheet, GridRange range)
    {
        if (range.Start.Row >= range.End.Row)
            return false;

        var textHeaders = 0;
        var typedBodyValues = 0;
        for (var column = range.Start.Col; column <= range.End.Col; column++)
        {
            var firstValue = sheet.GetCell(range.Start.Row, column)?.Value;
            var secondValue = sheet.GetCell(range.Start.Row + 1, column)?.Value;
            if (IsNonBlankText(firstValue))
                textHeaders++;
            if (secondValue is NumberValue or DateTimeValue or BoolValue)
                typedBodyValues++;
        }

        return textHeaders > 0 && typedBodyValues > 0;
    }

    private static bool IsNonBlankText(ScalarValue? value) =>
        value is TextValue text && !string.IsNullOrWhiteSpace(text.Value);
}
