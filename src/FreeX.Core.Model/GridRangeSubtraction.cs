namespace FreeX.Core.Model;

public static class GridRangeSubtraction
{
    public static IEnumerable<GridRange> Subtract(GridRange source, GridRange remove)
    {
        if (!source.Overlaps(remove))
        {
            yield return source;
            yield break;
        }

        var top = Math.Max(source.Start.Row, remove.Start.Row);
        var bottom = Math.Min(source.End.Row, remove.End.Row);
        var left = Math.Max(source.Start.Col, remove.Start.Col);
        var right = Math.Min(source.End.Col, remove.End.Col);
        var sheet = source.Start.Sheet;

        if (source.Start.Row < top)
            yield return Create(sheet, source.Start.Row, source.Start.Col, top - 1, source.End.Col);

        if (bottom < source.End.Row)
            yield return Create(sheet, bottom + 1, source.Start.Col, source.End.Row, source.End.Col);

        if (source.Start.Col < left)
            yield return Create(sheet, top, source.Start.Col, bottom, left - 1);

        if (right < source.End.Col)
            yield return Create(sheet, top, right + 1, bottom, source.End.Col);
    }

    private static GridRange Create(SheetId sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet, startRow, startCol), new CellAddress(sheet, endRow, endCol));
}
