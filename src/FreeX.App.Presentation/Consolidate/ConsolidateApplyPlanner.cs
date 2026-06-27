using System.Globalization;

using FreeX.Core.Model;

namespace FreeX.App.Presentation.Consolidate;

public static class ConsolidateApplyPlanner
{
    public static ConsolidateCellValue[,] ReadSource(Sheet sheet, GridRange range)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var rowCount = (int)range.RowCount;
        var colCount = (int)range.ColCount;
        var grid = new ConsolidateCellValue[rowCount, colCount];

        for (var row = 0; row < rowCount; row++)
        {
            for (var col = 0; col < colCount; col++)
            {
                var value = sheet.GetValue(range.Start.Row + (uint)row, range.Start.Col + (uint)col);
                grid[row, col] = ToCellValue(value);
            }
        }

        return grid;
    }

    public static ConsolidateCellValue ToCellValue(ScalarValue? value) => value switch
    {
        NumberValue number => ConsolidateCellValue.FromNumber(
            number.Value,
            number.Value.ToString(CultureInfo.InvariantCulture)),
        DateTimeValue dateTime => ConsolidateCellValue.FromNumber(
            dateTime.Value,
            dateTime.Value.ToString(CultureInfo.InvariantCulture)),
        BoolValue boolean => ConsolidateCellValue.FromLabel(boolean.Value ? "TRUE" : "FALSE"),
        TextValue text => ConsolidateCellValue.FromLabel(text.Value),
        ErrorValue error => ConsolidateCellValue.FromLabel(error.Code),
        _ => ConsolidateCellValue.Blank,
    };

    public static IReadOnlyList<(CellAddress Address, Cell NewCell)> MapToEdits(
        SheetId sheetId,
        ConsolidateResult result,
        CellAddress destination)
    {
        ArgumentNullException.ThrowIfNull(result);

        var edits = new List<(CellAddress, Cell)>(result.Cells.Count);
        foreach (var cell in result.Cells)
        {
            var row = destination.Row + (uint)cell.Row;
            var col = destination.Col + (uint)cell.Column;
            if (row > CellAddress.MaxRow || col > CellAddress.MaxCol)
                continue;

            var address = new CellAddress(sheetId, row, col);
            edits.Add((address, Cell.FromValue(ToScalar(cell))));
        }

        return edits;
    }

    public static ScalarValue ToScalar(ConsolidateOutputCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);

        if (cell.IsNumber)
            return new NumberValue(cell.Number!.Value);

        return cell.IsLabel ? new TextValue(cell.Text!) : new BlankValue();
    }

    public static IReadOnlyList<CellAddress> FindOverwriteTargets(
        Sheet sheet,
        IReadOnlyList<(CellAddress Address, Cell NewCell)> edits)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(edits);

        var targets = new List<CellAddress>();
        foreach (var (address, _) in edits)
        {
            if (sheet.GetValue(address.Row, address.Col) is not (null or BlankValue))
                targets.Add(address);
        }

        return targets;
    }
}
