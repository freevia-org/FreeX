using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static class SheetGroupSelectionService
{
    public static IReadOnlyList<SheetId> SelectSingle(SheetId clicked) => [clicked];

    public static IReadOnlyList<SheetId> Toggle(SheetId clicked, IReadOnlyCollection<SheetId> current)
    {
        var selected = current.ToList();
        if (selected.Contains(clicked))
        {
            if (selected.Count > 1)
                selected.Remove(clicked);
        }
        else
        {
            selected.Add(clicked);
        }

        return selected;
    }

    public static IReadOnlyList<SheetId> SelectRange(IReadOnlyList<SheetId> visibleSheets, SheetId anchor, SheetId target)
    {
        var anchorIndex = FindIndex(visibleSheets, anchor);
        var targetIndex = FindIndex(visibleSheets, target);
        if (anchorIndex < 0 || targetIndex < 0)
            return [target];

        var start = Math.Min(anchorIndex, targetIndex);
        var end = Math.Max(anchorIndex, targetIndex);
        return visibleSheets.Skip(start).Take(end - start + 1).ToList();
    }

    public static IReadOnlyList<SheetId> SelectAll(IReadOnlyList<SheetId> visibleSheets) => visibleSheets.ToList();

    private static int FindIndex(IReadOnlyList<SheetId> sheets, SheetId id)
    {
        for (var i = 0; i < sheets.Count; i++)
            if (sheets[i] == id)
                return i;
        return -1;
    }
}
