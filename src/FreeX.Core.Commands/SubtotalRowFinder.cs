using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Finds the rows within a range that <see cref="SubtotalCommand"/> itself created (each group's
/// own subtotal row plus the grand-total row).
/// </summary>
/// <remarks>
/// This intersects <see cref="Sheet.SubtotalRows"/> -- real state the command sets when it inserts
/// a row -- with the requested range. It intentionally does NOT scan cell formula text for a
/// "SUBTOTAL(" prefix: a hand-authored formula that happens to start with SUBTOTAL( (e.g. a user's
/// own running total the user typed into an ordinary data row) is not evidence that Data &gt;
/// Subtotal created that row, and treating it as such made "Remove Subtotals"/"Replace current
/// subtotals" whole-row-delete the user's own unrelated data. See the review finding
/// subtotal-formula-prefix-false-positive-deletion.
/// </remarks>
internal static class SubtotalRowFinder
{
    public static List<uint> Find(Sheet sheet, SheetId sheetId, GridRange range)
    {
        _ = sheetId; // sheet.SubtotalRows is already scoped to this sheet.
        if (sheet.SubtotalRows.Count == 0)
            return [];

        var rows = new List<uint>();
        foreach (var row in sheet.SubtotalRows)
        {
            if (row >= range.Start.Row && row <= range.End.Row)
                rows.Add(row);
        }

        rows.Sort();
        return rows;
    }
}
