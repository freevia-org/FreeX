using System.Globalization;
using FreeX.App.Presentation;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum RemoveDuplicatesPlanStatus
{
    Ready,
    NoColumnsSelected
}

public sealed record RemoveDuplicateColumnChoice(uint Offset, string Label, bool IsSelected);

public sealed record RemoveDuplicatesPlannerText(string ColumnLabelFormat)
{
    public static RemoveDuplicatesPlannerText Default { get; } = new("Column {0}");

    public string FormatColumnLabel(object value) =>
        string.Format(CultureInfo.CurrentCulture, ColumnLabelFormat, value);
}

public sealed record RemoveDuplicatesPlan(
    GridRange SourceRange,
    GridRange ActiveRange,
    bool HasHeaders,
    IReadOnlyList<RemoveDuplicateColumnChoice> Columns)
{
    public IReadOnlyList<uint> SelectedColumnOffsets { get; } = Columns
        .Where(static column => column.IsSelected)
        .Select(static column => column.Offset)
        .ToArray();

    public RemoveDuplicateRowsCommand CreateCommand(SheetId sheetId, GridRange activeRange) =>
        new(sheetId, activeRange, SelectedColumnOffsets);

    public RemoveDuplicateRowsCommand CreateCommand(SheetId sheetId) =>
        CreateCommand(sheetId, ActiveRangeForSheet(sheetId));

    public GridRange ActiveRangeForSheet(SheetId sheetId) =>
        new(
            new CellAddress(sheetId, ActiveRange.Start.Row, ActiveRange.Start.Col),
            new CellAddress(sheetId, ActiveRange.End.Row, ActiveRange.End.Col));
}

public sealed record RemoveDuplicatesPlanResult(
    RemoveDuplicatesPlan? Plan,
    RemoveDuplicatesPlanStatus Status,
    string StatusText)
{
    public bool IsReady => Status == RemoveDuplicatesPlanStatus.Ready;

    public static RemoveDuplicatesPlanResult Ready(RemoveDuplicatesPlan plan) =>
        new(plan, RemoveDuplicatesPlanStatus.Ready, "Ready to remove duplicate rows.");

    public static RemoveDuplicatesPlanResult NoColumnsSelected() =>
        new(null, RemoveDuplicatesPlanStatus.NoColumnsSelected, "Select at least one column.");
}

public sealed record WorkbookRemoveDuplicatesResult(
    bool Success,
    string? ErrorMessage,
    int RemovedRowCount,
    WorkbookCellEditResult EditResult);

public static class RemoveDuplicatesPlanner
{
    public static RemoveDuplicatesPlanResult CreatePlan(
        GridRange range,
        bool hasHeaders,
        IEnumerable<RemoveDuplicateColumnChoice> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        var selectedColumns = columns.ToArray();
        if (!selectedColumns.Any(static column => column.IsSelected))
            return RemoveDuplicatesPlanResult.NoColumnsSelected();

        return RemoveDuplicatesPlanResult.Ready(new RemoveDuplicatesPlan(
            range,
            ExcludeHeaderRow(range, hasHeaders),
            hasHeaders,
            selectedColumns));
    }

    public static RemoveDuplicatesPlanResult CreatePlan(
        GridRange range,
        bool hasHeaders,
        IEnumerable<uint> selectedColumnOffsets)
    {
        ArgumentNullException.ThrowIfNull(selectedColumnOffsets);

        return CreatePlan(
            range,
            hasHeaders,
            selectedColumnOffsets.Select(static offset => new RemoveDuplicateColumnChoice(
                offset,
                string.Empty,
                true)));
    }

    public static IReadOnlyList<RemoveDuplicateColumnChoice> SelectAll(int columnCount) =>
        BuildColumnChoices(columnCount, isSelected: true);

    public static IReadOnlyList<RemoveDuplicateColumnChoice> SelectAll(IEnumerable<RemoveDuplicateColumnChoice> columns) =>
        columns.Select(static column => column with { IsSelected = true }).ToArray();

    public static IReadOnlyList<RemoveDuplicateColumnChoice> ClearAll(IEnumerable<RemoveDuplicateColumnChoice> columns) =>
        columns.Select(static column => column with { IsSelected = false }).ToArray();

    public static IReadOnlyList<uint> GetSelectedColumnOffsets(IEnumerable<RemoveDuplicateColumnChoice> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        return columns
            .Where(static column => column.IsSelected)
            .Select(static column => column.Offset)
            .ToArray();
    }

    public static IReadOnlyList<RemoveDuplicateColumnChoice> BuildColumnChoices(
        int columnCount,
        bool isSelected = true,
        RemoveDuplicatesPlannerText? text = null)
    {
        if (columnCount < 0)
            throw new ArgumentOutOfRangeException(nameof(columnCount), columnCount, "Column count cannot be negative.");

        var labels = text ?? RemoveDuplicatesPlannerText.Default;
        return Enumerable
            .Range(0, columnCount)
            .Select(index => new RemoveDuplicateColumnChoice(
                (uint)index,
                labels.FormatColumnLabel(index + 1),
                isSelected))
            .ToArray();
    }

    public static IReadOnlyList<RemoveDuplicateColumnChoice> BuildColumnChoices(
        GridRange range,
        RemoveDuplicatesPlannerText? text = null) =>
        BuildColumnChoices((int)range.ColCount, isSelected: true, text);

    public static IReadOnlyList<RemoveDuplicateColumnChoice> BuildColumnChoices(
        Sheet sheet,
        GridRange range,
        bool hasHeaders = true,
        RemoveDuplicatesPlannerText? text = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var labels = text ?? RemoveDuplicatesPlannerText.Default;
        return Enumerable
            .Range(0, (int)range.ColCount)
            .Select(index =>
            {
                var absoluteColumn = range.Start.Col + (uint)index;
                var header = hasHeaders
                    ? FormatCellValue(sheet.GetCell(range.Start.Row, absoluteColumn)?.Value)
                    : "";
                if (string.IsNullOrWhiteSpace(header))
                    header = labels.FormatColumnLabel(CellAddress.NumberToColumnName(absoluteColumn));

                return new RemoveDuplicateColumnChoice((uint)index, header, true);
            })
            .ToArray();
    }

    public static bool GuessHasHeaders(Sheet sheet, GridRange range)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        if (range.Start.Row >= range.End.Row)
            return false;

        var columnCount = 0;
        var textHeaders = 0;
        var typedBodyValues = 0;
        var labelLikeTextHeaders = 0;
        for (var column = range.Start.Col; column <= range.End.Col; column++)
        {
            columnCount++;
            var firstValue = sheet.GetCell(range.Start.Row, column)?.Value;
            var secondValue = sheet.GetCell(range.Start.Row + 1, column)?.Value;
            if (IsNonBlankText(firstValue))
            {
                textHeaders++;

                // R90-removedup-5-2: when a column is text-typed all the way down (e.g. a
                // "Name"/"City" contact table), the type-mismatch signal below never fires because
                // both the header and the body are TextValue. Fall back to Excel's other tell for
                // an all-text header: the header word itself does not recur as an ordinary data
                // value anywhere else in that column, unlike a genuine data value (which may repeat
                // as a duplicate row, as in the failure scenario's "Alice"/"Paris" repeat).
                if (firstValue is TextValue headerText &&
                    !ColumnBodyContainsText(sheet, range, column, headerText.Value))
                {
                    labelLikeTextHeaders++;
                }
            }

            if (secondValue is NumberValue or DateTimeValue or BoolValue)
                typedBodyValues++;
        }

        if (textHeaders == 0)
            return false;

        if (typedBodyValues > 0)
            return true;

        // Every column is text-typed (no NumberValue/DateTimeValue/BoolValue anywhere in the row
        // beneath the header): require every column's first-row text to look like a label -- i.e.
        // not simply another occurrence of that column's own data -- and require at least two
        // columns, since a single all-text column's first value being merely "not repeated later"
        // is too weak a signal on its own (an ordinary unlabeled list of unique text values would
        // otherwise be misdetected as having a header).
        return columnCount > 1 && labelLikeTextHeaders == columnCount;
    }

    private static bool ColumnBodyContainsText(Sheet sheet, GridRange range, uint column, string headerText)
    {
        for (var row = range.Start.Row + 1; row <= range.End.Row; row++)
        {
            if (sheet.GetCell(row, column)?.Value is TextValue bodyText &&
                string.Equals(bodyText.Value, headerText, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static GridRange ExcludeHeaderRow(GridRange range, bool hasHeaders)
    {
        if (!hasHeaders || range.Start.Row >= range.End.Row)
            return range;

        return new GridRange(
            new CellAddress(range.Start.Sheet, range.Start.Row + 1, range.Start.Col),
            range.End);
    }

    private static bool IsNonBlankText(ScalarValue? value) =>
        value is TextValue text && !string.IsNullOrWhiteSpace(text.Value);

    private static string FormatCellValue(ScalarValue? value) =>
        SpreadsheetDisplayFormatter.FormatCellValue(value);
}
