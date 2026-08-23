using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class PastePlacementPolicy
{
    public static IEnumerable<CellAddress> EnumerateTileAnchors(
        GridRange sourceRange,
        CellAddress destination,
        GridRange? destinationRange,
        bool transpose)
    {
        if (destinationRange is not { } targetRange)
        {
            yield return destination;
            yield break;
        }

        var pasteRows = transpose ? sourceRange.ColCount : sourceRange.RowCount;
        var pasteCols = transpose ? sourceRange.RowCount : sourceRange.ColCount;
        var targetRows = targetRange.RowCount;
        var targetCols = targetRange.ColCount;

        if (targetRows <= pasteRows && targetCols <= pasteCols)
        {
            yield return targetRange.Start;
            yield break;
        }

        for (var rowOffset = 0U; rowOffset + pasteRows <= targetRows; rowOffset += pasteRows)
        {
            for (var colOffset = 0U; colOffset + pasteCols <= targetCols; colOffset += pasteCols)
            {
                yield return new CellAddress(
                    targetRange.Start.Sheet,
                    targetRange.Start.Row + rowOffset,
                    targetRange.Start.Col + colOffset);
            }
        }
    }

    public static IEnumerable<(T Source, CellAddress Destination)> EnumerateMappedItems<T>(
        IEnumerable<T> sources,
        Func<T, CellAddress> sourceAddress,
        GridRange sourceRange,
        CellAddress destination,
        GridRange? destinationRange,
        bool transpose)
    {
        foreach (var tileAnchor in EnumerateTileAnchors(sourceRange, destination, destinationRange, transpose))
        {
            foreach (var source in sources)
                yield return (source, MapAddress(sourceAddress(source), sourceRange, tileAnchor, transpose));
        }
    }

    public static CellAddress MapAddress(
        CellAddress source,
        GridRange sourceRange,
        CellAddress destination,
        bool transpose)
    {
        var rowOffset = source.Row - sourceRange.Start.Row;
        var colOffset = source.Col - sourceRange.Start.Col;
        return transpose
            ? new CellAddress(destination.Sheet, destination.Row + colOffset, destination.Col + rowOffset)
            : new CellAddress(destination.Sheet, destination.Row + rowOffset, destination.Col + colOffset);
    }

    public static GridRange MapRange(
        GridRange range,
        GridRange sourceRange,
        CellAddress destination,
        bool transpose) =>
        new(
            MapAddress(range.Start, sourceRange, destination, transpose),
            MapAddress(range.End, sourceRange, destination, transpose));

    public static GridRange GetDestinationRange(
        GridRange sourceRange,
        CellAddress destination,
        bool transpose)
    {
        var rowCount = transpose ? sourceRange.ColCount : sourceRange.RowCount;
        var colCount = transpose ? sourceRange.RowCount : sourceRange.ColCount;
        return new GridRange(
            destination,
            new CellAddress(destination.Sheet, destination.Row + rowCount - 1, destination.Col + colCount - 1));
    }
}
