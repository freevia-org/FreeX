using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// r164 remediation, dense whole-sheet enumeration. Ctrl+A selects the entire grid
/// (1..<see cref="CellAddress.MaxRow"/> x 1..<see cref="CellAddress.MaxCol"/> = 17,179,869,184
/// cells), and <see cref="GridRange.AllCells"/> walks every one of those addresses. A command that
/// only ever touches cells holding something -- Clear Contents, Clear Comments, Delete Cells, Sort,
/// Remove Duplicates -- therefore hangs the synchronous UI thread on a select-all, even though the
/// work it actually has to do is proportional to the populated cells, not to the selection.
///
/// Clamping the selection to the part that can possibly hold anything fixes that without a cell
/// limit, which matters because a cap would newly REJECT gestures that succeed today (a whole-column
/// Clear Contents completes in ~100 ms). Behaviour inside the sheet's populated area is unchanged:
/// the caller keeps its existing dense loop, just over a smaller box.
/// </summary>
public static class SheetRangeScope
{
    /// <summary>
    /// Narrows <paramref name="range"/> to the smallest box that still covers every address in it
    /// that could hold a value, a spill, a style-only override, a hyperlink, rich text, a phonetic
    /// guide, or a comment. Returns null when the range holds none of those, i.e. when the caller
    /// has nothing to do at all.
    /// </summary>
    /// <remarks>
    /// The sheet's used range already accounts for values, spills and style-only cells; the
    /// companion dictionaries are keyed sparsely, so scanning them costs the size of the document,
    /// never the size of the selection. Whole-row/whole-column DEFAULT styles
    /// (<see cref="Sheet.RowStyles"/>/<see cref="Sheet.ColumnStyles"/>) are deliberately not
    /// included: they resolve through <see cref="Sheet.GetStyleOnly"/> for every address in the row
    /// or column, so honouring them here would re-introduce the unbounded scan -- and the only thing
    /// the dense loops did with such a cell was materialise an empty cell carrying a style the row
    /// or column default already supplies.
    /// </remarks>
    public static GridRange? ClampToPopulated(Sheet sheet, GridRange range)
    {
        var minRow = uint.MaxValue;
        var minCol = uint.MaxValue;
        uint maxRow = 0;
        uint maxCol = 0;
        var found = false;

        void Include(uint row, uint col)
        {
            found = true;
            if (row < minRow) minRow = row;
            if (row > maxRow) maxRow = row;
            if (col < minCol) minCol = col;
            if (col > maxCol) maxCol = col;
        }

        void IncludeKeys(IEnumerable<CellAddress> addresses)
        {
            foreach (var address in addresses)
            {
                if (range.Contains(address))
                    Include(address.Row, address.Col);
            }
        }

        if (sheet.GetUsedRange() is { } used && GridRange.TryIntersect(used, range, out var usedPart))
        {
            Include(usedPart.Start.Row, usedPart.Start.Col);
            Include(usedPart.End.Row, usedPart.End.Col);
        }

        // A merged region is structure the caller can still have to act on even where every cell it
        // covers is empty -- Remove Duplicates, for instance, accepts a merge FULLY inside the
        // operated range and rejects one that only partially overlaps it, so trimming the range
        // back past a merge's far edge would flip that decision.
        foreach (var merge in sheet.MergedRegions)
        {
            if (GridRange.TryIntersect(merge, range, out var mergePart))
            {
                Include(mergePart.Start.Row, mergePart.Start.Col);
                Include(mergePart.End.Row, mergePart.End.Col);
            }
        }

        IncludeKeys(sheet.Hyperlinks.Keys);
        IncludeKeys(sheet.HyperlinkMetadata.Keys);
        IncludeKeys(sheet.RichTextRuns.Keys);
        IncludeKeys(sheet.CellPhoneticGuides.Keys);
        IncludeKeys(sheet.Comments.Keys);
        IncludeKeys(sheet.ThreadedComments.Keys);
        IncludeKeys(sheet.ShownComments);
        IncludeKeys(sheet.CommentAuthors.Keys);

        if (!found)
            return null;

        return new GridRange(
            new CellAddress(sheet.Id, minRow, minCol),
            new CellAddress(sheet.Id, maxRow, maxCol));
    }

    /// <summary>
    /// Clamps only the dimension(s) in which <paramref name="range"/> runs to the edge of the grid --
    /// a whole-column selection keeps its columns and clamps its rows, a whole-row selection keeps
    /// its rows and clamps its columns, and a select-all clamps both. A range the caller bounded
    /// itself is returned untouched.
    /// </summary>
    /// <remarks>
    /// For operations where trimming a deliberately-chosen bounded range would change the result --
    /// copying A1:Z100 has to carry the blank cells too, because pasting them is what clears the
    /// destination -- but where an UNBOUNDED selection cannot have meant "and also the 17 billion
    /// empty cells". Mirrors <see cref="ApplyStyleCommand.StyleOnlyCreateZone"/>'s clamp-only-the-
    /// unbounded-dimension rule.
    /// </remarks>
    public static GridRange ClampUnboundedToPopulated(Sheet sheet, GridRange range)
    {
        var unboundedRows = range.End.Row >= CellAddress.MaxRow;
        var unboundedCols = range.End.Col >= CellAddress.MaxCol;
        if (!unboundedRows && !unboundedCols)
            return range;

        if (ClampToPopulated(sheet, range) is not { } populated)
        {
            // Nothing at all in the selection: keep a single cell so callers that assume a
            // non-empty range (and produce an empty payload from it) keep working.
            return new GridRange(range.Start, range.Start);
        }

        return new GridRange(
            new CellAddress(
                sheet.Id,
                unboundedRows ? Math.Max(populated.Start.Row, range.Start.Row) : range.Start.Row,
                unboundedCols ? Math.Max(populated.Start.Col, range.Start.Col) : range.Start.Col),
            new CellAddress(
                sheet.Id,
                unboundedRows ? Math.Min(populated.End.Row, range.End.Row) : range.End.Row,
                unboundedCols ? Math.Min(populated.End.Col, range.End.Col) : range.End.Col));
    }

    /// <summary>
    /// Shrinks only the END of <paramref name="range"/> down to the last populated row/column inside
    /// it, leaving the start where the caller put it. Returns null when the range holds nothing.
    /// </summary>
    /// <remarks>
    /// For commands whose semantics are anchored to <c>range.Start</c> -- Sort reads its key column
    /// as an OFFSET from the start and treats the first row as the header, so moving the start would
    /// silently re-point the sort key at a different column -- only the trailing end can be trimmed.
    /// Dropping trailing empty rows/columns from those operations is a no-op, and it is enough to
    /// turn a select-all into a scan of the real data.
    /// </remarks>
    public static GridRange? ClampEndToPopulated(Sheet sheet, GridRange range)
    {
        if (ClampToPopulated(sheet, range) is not { } populated)
            return null;

        return new GridRange(
            range.Start,
            new CellAddress(
                sheet.Id,
                Math.Max(populated.End.Row, range.Start.Row),
                Math.Max(populated.End.Col, range.Start.Col)));
    }
}
