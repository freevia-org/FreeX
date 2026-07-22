using System.Globalization;

using FreeX.Core.Model;

namespace FreeX.App.Presentation.Consolidate;

public static class ConsolidateApplyPlanner
{
    /// <summary>
    /// Safety cap on the number of cells a single Consolidate source range may dense-allocate, mirroring
    /// <c>FreeX.Core.Formula.FormulaSafetyLimits.MaxMaterializedRangeCells</c> (not directly referenceable
    /// here -- that constant is internal to the Core.Formula assembly). Without this cap, a whole-sheet or
    /// full-column/full-row source reference (e.g. A1:XFD1048576, ~17 billion cells) would attempt to
    /// allocate a 2-D array of that size and crash.
    /// </summary>
    public const long MaxSourceRangeCells = 1_000_000L;

    public static ConsolidateCellValue[,] ReadSource(Sheet sheet, GridRange range)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var effectiveRange = ClampToSafeSourceRange(sheet, range);
        var rowCount = (int)effectiveRange.RowCount;
        var colCount = (int)effectiveRange.ColCount;
        var grid = new ConsolidateCellValue[rowCount, colCount];

        for (var row = 0; row < rowCount; row++)
        {
            for (var col = 0; col < colCount; col++)
            {
                var value = sheet.GetValue(effectiveRange.Start.Row + (uint)row, effectiveRange.Start.Col + (uint)col);
                grid[row, col] = ToCellValue(value);
            }
        }

        return grid;
    }

    /// <summary>
    /// Clamps an oversized Consolidate source range so <see cref="ReadSource"/> never dense-allocates
    /// past <see cref="MaxSourceRangeCells"/>. A whole-sheet/full-column/full-row source only carries
    /// meaningful data in the sheet's populated (used) area, mirroring Excel, so the range is first
    /// clamped to its intersection with <see cref="Sheet.GetUsedRange"/>; if that intersection is still
    /// too large (an unusually large but genuinely populated sheet) or there is no populated area at all,
    /// the row extent is truncated as a final safety net so the total cell count never exceeds the cap.
    /// </summary>
    private static GridRange ClampToSafeSourceRange(Sheet sheet, GridRange range)
    {
        if (range.CellCount <= MaxSourceRangeCells)
            return range;

        var usedRange = sheet.GetUsedRange();
        var effectiveRange = usedRange.HasValue && GridRange.TryIntersect(range, usedRange.Value, out var intersection)
            ? intersection
            : new GridRange(range.Start, range.Start);

        if (effectiveRange.CellCount <= MaxSourceRangeCells)
            return effectiveRange;

        var maxRows = Math.Max(1u, (uint)(MaxSourceRangeCells / effectiveRange.ColCount));
        var clampedEndRow = effectiveRange.Start.Row + Math.Min(maxRows, effectiveRange.RowCount) - 1;
        return new GridRange(
            effectiveRange.Start,
            new CellAddress(effectiveRange.Start.Sheet, clampedEndRow, effectiveRange.End.Col));
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
