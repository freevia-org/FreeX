using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

internal static partial class RowColumnShiftHelpers
{
    public static IReadOnlyList<GridRange> InsertColumnsIntoMergedRegions(
        IEnumerable<GridRange> mergedRegions,
        uint beforeCol,
        uint count) =>
        mergedRegions
            .Select(region => InsertColumnsIntoMergedRegion(region, beforeCol, count))
            .ToList();

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

    private static GridRange InsertColumnsIntoMergedRegion(GridRange region, uint beforeCol, uint count)
    {
        if (region.Start.Col >= beforeCol)
        {
            return new GridRange(
                new CellAddress(region.Start.Sheet, region.Start.Row, region.Start.Col + count),
                new CellAddress(region.End.Sheet, region.End.Row, region.End.Col + count));
        }

        if (region.End.Col >= beforeCol)
        {
            return new GridRange(
                region.Start,
                new CellAddress(region.End.Sheet, region.End.Row, region.End.Col + count));
        }

        return region;
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

        if (newEnd > 0 && newEnd >= newStart)
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
