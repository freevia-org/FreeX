using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static partial class RowColumnShiftHelpers
{
    public static IReadOnlyList<GridRange> InsertColumnsIntoMergedRegions(
        IEnumerable<GridRange> mergedRegions,
        uint beforeCol,
        uint count)
    {
        var adjustedMerges = new List<GridRange>();
        foreach (var region in mergedRegions)
        {
            if (TryInsertColumnsIntoMergedRegion(region, beforeCol, count, out var adjusted))
                adjustedMerges.Add(adjusted);
        }

        return adjustedMerges;
    }

    public static IReadOnlyList<GridRange> DeleteColumnsFromMergedRegions(
        IEnumerable<GridRange> mergedRegions,
        uint startCol,
        uint count)
    {
        var endCol = startCol + count - 1;
        var adjustedMerges = new List<GridRange>();
        foreach (var region in mergedRegions)
        {
            if (TryDeleteColumnsFromMergedRegion(region, startCol, endCol, count, out var adjusted))
                adjustedMerges.Add(adjusted);
        }

        return adjustedMerges;
    }

    private static bool TryInsertColumnsIntoMergedRegion(GridRange region, uint beforeCol, uint count, out GridRange adjusted)
    {
        GridRange shifted;
        if (region.Start.Col >= beforeCol)
        {
            shifted = new GridRange(
                new CellAddress(region.Start.Sheet, region.Start.Row, region.Start.Col + count),
                new CellAddress(region.End.Sheet, region.End.Row, region.End.Col + count));
        }
        else if (region.End.Col >= beforeCol)
        {
            shifted = new GridRange(
                region.Start,
                new CellAddress(region.End.Sheet, region.End.Row, region.End.Col + count));
        }
        else
        {
            adjusted = region;
            return true;
        }

        // R16-large-workbook-perf-2: a merged region whose entire shifted position falls past the
        // last column runs off the sheet and is dropped, mirroring Excel; one whose right edge
        // merely overshoots is clamped back to the last column instead of left out-of-bounds.
        if (shifted.Start.Col > CellAddress.MaxCol)
        {
            adjusted = default;
            return false;
        }

        adjusted = shifted.End.Col > CellAddress.MaxCol
            ? new GridRange(shifted.Start, new CellAddress(shifted.End.Sheet, shifted.End.Row, CellAddress.MaxCol))
            : shifted;
        return true;
    }

    private static bool TryDeleteColumnsFromMergedRegion(
        GridRange region,
        uint startCol,
        uint endCol,
        uint count,
        out GridRange adjusted)
    {
        if (region.End.Col < startCol)
        {
            adjusted = region;
            return true;
        }

        if (region.Start.Col > endCol)
        {
            adjusted = new GridRange(
                new CellAddress(region.Start.Sheet, region.Start.Row, region.Start.Col - count),
                new CellAddress(region.End.Sheet, region.End.Row, region.End.Col - count));
            return true;
        }

        var newStart = region.Start.Col < startCol ? region.Start.Col : startCol;
        var newEnd = region.End.Col > endCol
            ? region.End.Col - count
            : startCol > 1 ? startCol - 1 : 0;

        // Drop only when the column range is entirely consumed (no surviving columns) OR
        // when the result is a true single cell (1 column wide AND 1 row tall).
        // A region that becomes 1 column wide but still spans multiple rows is a valid
        // vertical merge in Excel and must be kept.
        bool colRangeGone = newEnd == 0 || newEnd < newStart;
        bool singleCol = newEnd == newStart;
        bool multiRow = region.Start.Row != region.End.Row;

        if (!colRangeGone && (singleCol ? multiRow : true))
        {
            adjusted = new GridRange(
                new CellAddress(region.Start.Sheet, region.Start.Row, newStart),
                new CellAddress(region.End.Sheet, region.End.Row, newEnd));
            return true;
        }

        adjusted = default;
        return false;
    }
}
