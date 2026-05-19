using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class SelectionCornerNavigator
{
    public static CellAddress GetNextCorner(GridRange range, CellAddress current)
    {
        var corners = GetUniqueCorners(range);
        var index = corners.FindIndex(corner => corner == current);
        return index < 0 ? range.Start : corners[(index + 1) % corners.Count];
    }

    private static List<CellAddress> GetUniqueCorners(GridRange range)
    {
        var ordered = new[]
        {
            range.Start,
            new CellAddress(range.Start.Sheet, range.Start.Row, range.End.Col),
            range.End,
            new CellAddress(range.Start.Sheet, range.End.Row, range.Start.Col)
        };

        var corners = new List<CellAddress>(4);
        foreach (var corner in ordered)
        {
            if (!corners.Contains(corner))
                corners.Add(corner);
        }

        return corners;
    }
}
