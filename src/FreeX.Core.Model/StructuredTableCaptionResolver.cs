using System.Globalization;

namespace FreeX.Core.Model;

/// <summary>
/// Projects a structured-table column's data-body values into slicer captions.
/// </summary>
public static class StructuredTableCaptionResolver
{
    /// <summary>
    /// Resolves captions in first-occurrence order. Returns <see langword="false"/> when the
    /// referenced table or column does not exist; an existing column with no captionable data
    /// returns <see langword="true"/> with an empty list.
    /// </summary>
    public static bool TryResolveColumnCaptions(
        Workbook workbook,
        int tableId,
        int columnId,
        out IReadOnlyList<string> captions)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        captions = [];
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var table in sheet.StructuredTables)
            {
                if (table.Id != tableId)
                    continue;

                var columnOffset = FindColumnOffset(table, columnId);
                if (columnOffset < 0)
                    return false;

                captions = ResolveColumnCaptions(sheet, table, columnOffset);
                return true;
            }
        }

        return false;
    }

    private static int FindColumnOffset(StructuredTableModel table, int columnId)
    {
        for (var index = 0; index < table.Columns.Count; index++)
        {
            if (table.Columns[index].Id == columnId)
                return index;
        }

        return -1;
    }

    private static IReadOnlyList<string> ResolveColumnCaptions(
        Sheet sheet,
        StructuredTableModel table,
        int columnOffset)
    {
        var range = table.Range;
        var column = range.Start.Col + checked((uint)columnOffset);
        if (column > range.End.Col)
            return [];

        var rowCount = checked((int)range.RowCount);
        var headerRows = Math.Clamp(table.HeaderRowCount ?? 1, 0, rowCount);
        var remainingRows = rowCount - headerRows;
        var totalsRows = table.TotalsRowShown
            ? Math.Clamp(table.TotalsRowCount ?? 1, 0, remainingRows)
            : 0;
        var dataRows = remainingRows - totalsRows;
        if (dataRows == 0)
            return [];

        var firstDataRow = range.Start.Row + checked((uint)headerRows);
        var lastDataRow = firstDataRow + checked((uint)dataRows) - 1;
        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var results = new List<string>();
        for (var row = firstDataRow; ; row++)
        {
            var caption = ToCaption(sheet.GetCell(row, column)?.Value ?? BlankValue.Instance);
            if (!string.IsNullOrEmpty(caption) && seen.Add(caption))
                results.Add(caption);
            if (row == lastDataRow)
                break;
        }

        return results;
    }

    private static string ToCaption(ScalarValue value) => value switch
    {
        TextValue text => text.Value,
        NumberValue number => number.Value.ToString(CultureInfo.CurrentCulture),
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        DateTimeValue date => date.ToDateTime().ToString(CultureInfo.CurrentCulture),
        BlankValue => string.Empty,
        ErrorValue => string.Empty,
        _ => value.ToString() ?? string.Empty,
    };
}
