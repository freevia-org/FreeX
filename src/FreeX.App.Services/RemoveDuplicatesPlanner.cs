using System.Globalization;
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

    public static IReadOnlyList<RemoveDuplicateColumnChoice> SelectAll(int columnCount) =>
        BuildColumnChoices(columnCount, isSelected: true);

    public static IReadOnlyList<RemoveDuplicateColumnChoice> SelectAll(IEnumerable<RemoveDuplicateColumnChoice> columns) =>
        columns.Select(static column => column with { IsSelected = true }).ToArray();

    public static IReadOnlyList<RemoveDuplicateColumnChoice> ClearAll(IEnumerable<RemoveDuplicateColumnChoice> columns) =>
        columns.Select(static column => column with { IsSelected = false }).ToArray();

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

    private static string FormatCellValue(ScalarValue? value) => value switch
    {
        null or BlankValue => "",
        NumberValue n => n.Value.ToString(CultureInfo.InvariantCulture),
        TextValue t => t.Value,
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        DateTimeValue dt => FormatDateTimeCellValue(dt),
        ErrorValue err => err.Code,
        _ => ""
    };

    private static string FormatDateTimeCellValue(DateTimeValue value)
    {
        try
        {
            return value.ToDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch
        {
            return value.Value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
