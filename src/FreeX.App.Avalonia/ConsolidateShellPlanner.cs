using System.Globalization;

using FreeX.App.Presentation.Consolidate;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Portable, UI-free glue between the Avalonia Consolidate dialog and the cell-write command path.
/// It reads a source <see cref="GridRange"/> into the portable <see cref="ConsolidateCellValue"/> grid the
/// <see cref="ConsolidatePlanner"/> consumes (numbers vs labels vs blanks classified from the sheet's
/// <see cref="ScalarValue"/>s), maps a planned <see cref="ConsolidateResult"/> over a destination anchor into
/// concrete cell edits, and reports which non-empty destination cells an apply would overwrite. No Avalonia
/// types, so it is unit-testable without a running window.
/// </summary>
internal static class ConsolidateShellPlanner
{
    /// <summary>The aggregation functions the dialog offers, in dropdown order, paired with their labels.</summary>
    public static IReadOnlyList<(ConsolidateFunction Function, string Label)> FunctionChoices { get; } =
    [
        (ConsolidateFunction.Sum, "Sum"),
        (ConsolidateFunction.Count, "Count"),
        (ConsolidateFunction.Average, "Average"),
        (ConsolidateFunction.Max, "Max"),
        (ConsolidateFunction.Min, "Min"),
        (ConsolidateFunction.Product, "Product"),
        (ConsolidateFunction.CountNumbers, "Count Numbers"),
        (ConsolidateFunction.StdDev, "StdDev"),
        (ConsolidateFunction.StdDevp, "StdDevp"),
        (ConsolidateFunction.Var, "Var"),
        (ConsolidateFunction.Varp, "Varp"),
    ];

    /// <summary>
    /// Reads the cells of <paramref name="range"/> on <paramref name="sheet"/> into the portable grid the
    /// planner consumes. Numeric cells carry their displayed text (so a number used as a label still matches
    /// by its rendering); text/bool/date/error cells become labels; blank cells become
    /// <see cref="ConsolidateCellValue.Blank"/>. The grid is addressed <c>[row, column]</c> relative to the
    /// range's top-left corner.
    /// </summary>
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

    /// <summary>Classifies a sheet <see cref="ScalarValue"/> into a portable consolidate cell value.</summary>
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

    /// <summary>
    /// Maps a planned <paramref name="result"/> onto the worksheet, anchoring its top-left output cell at
    /// <paramref name="destination"/>. Each cell's <see cref="ConsolidateOutputCell.Row"/>/<see cref="ConsolidateOutputCell.Column"/>
    /// offset is applied to the destination; number cells write a <see cref="NumberValue"/>, label cells a
    /// <see cref="TextValue"/>, and the (rare) blank corner cell a <see cref="BlankValue"/>. Cells whose
    /// address would fall outside the worksheet bounds are skipped.
    /// </summary>
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

    /// <summary>Converts a planned output cell into the scalar value to write into the destination.</summary>
    public static ScalarValue ToScalar(ConsolidateOutputCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);

        if (cell.IsNumber)
            return new NumberValue(cell.Number!.Value);

        return cell.IsLabel ? new TextValue(cell.Text!) : new BlankValue();
    }

    /// <summary>
    /// The non-empty cells an apply would overwrite. These are the edits whose target cell currently holds a
    /// value; the dialog warns about them before writing. Label/number cells the consolidation itself fills
    /// in are still counted (any pre-existing content is overwritten).
    /// </summary>
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
