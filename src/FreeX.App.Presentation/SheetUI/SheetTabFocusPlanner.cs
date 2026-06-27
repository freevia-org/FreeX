using FreeX.Core.Model;

namespace FreeX.App.Presentation.SheetUI;

/// <summary>
/// Renderer-neutral keyboard focus policy for a visible sheet-tab list. Hosts provide their own tab
/// model plus an ID selector so every renderer and test shares the same edge and missing-current rules.
/// </summary>
public static class SheetTabFocusPlanner
{
    public static SheetId? AdjacentTab(
        IReadOnlyList<SheetId> visibleTabIds,
        SheetId currentSheetId,
        int direction) =>
        AdjacentTab(visibleTabIds, currentSheetId, direction, static id => id);

    public static SheetId? AdjacentTab<TTab>(
        IReadOnlyList<TTab> visibleTabs,
        SheetId currentSheetId,
        int direction,
        Func<TTab, SheetId> getSheetId)
    {
        ArgumentNullException.ThrowIfNull(visibleTabs);
        ArgumentNullException.ThrowIfNull(getSheetId);

        if (visibleTabs.Count == 0)
            return null;

        var index = IndexOf(visibleTabs, currentSheetId, getSheetId);
        if (index < 0)
            index = GetMissingCurrentAnchorIndex(visibleTabs.Count, direction);

        var step = Math.Sign(direction);
        var nextIndex = Math.Clamp(index + step, 0, visibleTabs.Count - 1);
        return getSheetId(visibleTabs[nextIndex]);
    }

    public static SheetId? EdgeTab(IReadOnlyList<SheetId> visibleTabIds, bool first) =>
        EdgeTab(visibleTabIds, first, static id => id);

    public static SheetId? EdgeTab<TTab>(
        IReadOnlyList<TTab> visibleTabs,
        bool first,
        Func<TTab, SheetId> getSheetId)
    {
        ArgumentNullException.ThrowIfNull(visibleTabs);
        ArgumentNullException.ThrowIfNull(getSheetId);

        if (visibleTabs.Count == 0)
            return null;

        return getSheetId(visibleTabs[first ? 0 : visibleTabs.Count - 1]);
    }

    private static int IndexOf<TTab>(
        IReadOnlyList<TTab> visibleTabs,
        SheetId sheetId,
        Func<TTab, SheetId> getSheetId)
    {
        for (var index = 0; index < visibleTabs.Count; index++)
        {
            if (getSheetId(visibleTabs[index]) == sheetId)
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
