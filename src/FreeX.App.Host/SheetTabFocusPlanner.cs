using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class SheetTabFocusPlanner
{
    public static SheetId? AdjacentTab(
        IReadOnlyList<SheetTabViewModel> visibleTabs,
        SheetId currentSheetId,
        int direction)
    {
        if (visibleTabs.Count == 0)
            return null;

        var index = IndexOf(visibleTabs, currentSheetId);
        if (index < 0)
            index = GetMissingCurrentAnchorIndex(visibleTabs.Count, direction);

        var step = Math.Sign(direction);
        var nextIndex = Math.Clamp(index + step, 0, visibleTabs.Count - 1);
        return visibleTabs[nextIndex].Id;
    }

    public static SheetId? EdgeTab(IReadOnlyList<SheetTabViewModel> visibleTabs, bool first)
    {
        if (visibleTabs.Count == 0)
            return null;

        return first ? visibleTabs[0].Id : visibleTabs[^1].Id;
    }

    private static int IndexOf(IReadOnlyList<SheetTabViewModel> visibleTabs, SheetId sheetId)
    {
        for (var index = 0; index < visibleTabs.Count; index++)
        {
            if (visibleTabs[index].Id == sheetId)
                return index;
        }

        return -1;
    }

    private static int GetMissingCurrentAnchorIndex(
        int visibleTabCount,
        int direction) =>
        direction switch
        {
            < 0 => visibleTabCount,
            0 => 0,
            _ => -1
        };
}
