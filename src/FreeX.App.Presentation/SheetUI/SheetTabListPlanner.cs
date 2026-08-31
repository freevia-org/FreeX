using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.SheetUI;

public sealed record SheetTabListPlan(SheetId CurrentSheetId, IReadOnlyList<SheetTabListEntry> Tabs);

public sealed record SheetTabListEntry(
    SheetId Id,
    string Name,
    CellColor? TabColor,
    bool IsProtected,
    bool IsActive,
    bool IsGrouped,
    bool IsLeftSideCoveredByActive,
    bool IsRightSideCoveredByActive);

public sealed record SheetKeyboardGroupSelectionPlan(
    SheetId CurrentSheetId,
    SheetId AnchorSheetId,
    IReadOnlyList<SheetId> GroupedSheetIds);

public static class SheetTabListPlanner
{
    public static SheetTabListPlan Build(
        Workbook workbook,
        SheetId currentSheetId,
        HashSet<SheetId> groupedSheetIds)
    {
        var theme = workbook.Theme;
        var sheets = workbook.Sheets;
        var firstVisibleIndex = -1;
        for (var index = 0; index < sheets.Count; index++)
        {
            if (!sheets[index].IsHidden)
            {
                firstVisibleIndex = index;
                break;
            }
        }

        if (firstVisibleIndex < 0 && sheets.Count > 0)
        {
            sheets[0].IsHidden = false;
            firstVisibleIndex = 0;
        }

        if (firstVisibleIndex >= 0 && workbook.GetSheet(currentSheetId)?.IsHidden != false)
            currentSheetId = sheets[firstVisibleIndex].Id;

        groupedSheetIds.RemoveWhere(id => workbook.GetSheet(id)?.IsHidden != false);

        var visibleSheetCount = 0;
        var activeVisibleIndex = -1;
        for (var index = 0; index < sheets.Count; index++)
        {
            var sheet = sheets[index];
            if (sheet.IsHidden)
                continue;

            if (sheet.Id == currentSheetId)
                activeVisibleIndex = visibleSheetCount;

            visibleSheetCount++;
        }

        var tabs = new List<SheetTabListEntry>(visibleSheetCount);
        var visibleIndex = 0;
        for (var index = 0; index < sheets.Count; index++)
        {
            var sheet = sheets[index];
            if (sheet.IsHidden)
                continue;

            if (groupedSheetIds.Count == 0 && sheet.Id == currentSheetId)
                groupedSheetIds.Add(sheet.Id);

            tabs.Add(new SheetTabListEntry(
                sheet.Id,
                sheet.Name,
                // Re-resolve the theme link (when the tab colour came from a <tabColor theme="n"/>)
                // against the workbook's CURRENT theme rather than reading the RGB baked at load time,
                // so a theme swap repaints the tab strip in both shells. `theme` is workbook.Theme,
                // hoisted out of this loop above.
                sheet.ResolveTabColor(theme),
                sheet.IsProtected,
                sheet.Id == currentSheetId,
                groupedSheetIds.Contains(sheet.Id),
                activeVisibleIndex >= 0 && visibleIndex == activeVisibleIndex + 1,
                activeVisibleIndex >= 0 && visibleIndex == activeVisibleIndex - 1));

            visibleIndex++;
        }

        return new SheetTabListPlan(currentSheetId, tabs);
    }

    public static bool IsWorkbookGrouped(
        Workbook workbook,
        SheetId currentSheetId,
        IReadOnlySet<SheetId> groupedSheetIds)
    {
        if (!groupedSheetIds.Contains(currentSheetId))
            return false;

        var groupedVisibleSheets = 0;
        var sheets = workbook.Sheets;
        for (var index = 0; index < sheets.Count; index++)
        {
            var sheet = sheets[index];
            if (!sheet.IsHidden && groupedSheetIds.Contains(sheet.Id) && ++groupedVisibleSheets > 1)
                return true;
        }

        return false;
    }

    public static string GenerateUniqueSheetName(Workbook workbook)
    {
        for (var index = workbook.Sheets.Count + 1; index <= 10_000; index++)
        {
            var name = $"Sheet{index}";
            if (workbook.ValidateSheetName(name) is null)
                return name;
        }

        return $"Sheet{Guid.NewGuid():N}"[..31];
    }

    public static SheetId? AdjacentVisibleSheet(Workbook workbook, SheetId currentSheetId, int direction)
    {
        var sheets = workbook.Sheets;
        SheetId? firstVisible = null;
        SheetId? secondVisible = null;
        SheetId? previousVisible = null;
        var foundCurrent = false;

        for (var index = 0; index < sheets.Count; index++)
        {
            var sheet = sheets[index];
            if (sheet.IsHidden)
                continue;

            firstVisible ??= sheet.Id;
            if (firstVisible is not null && secondVisible is null && sheet.Id != firstVisible)
                secondVisible = sheet.Id;

            if (foundCurrent && direction > 0)
                return sheet.Id;

            if (sheet.Id == currentSheetId)
            {
                if (direction < 0)
                    return previousVisible ?? sheet.Id;

                if (direction == 0)
                    return sheet.Id;

                foundCurrent = true;
            }

            previousVisible = sheet.Id;
        }

        if (firstVisible is null)
            return null;

        if (foundCurrent)
            return previousVisible;

        return MissingCurrentFallback(firstVisible.Value, secondVisible, direction);
    }

    public static SheetKeyboardGroupSelectionPlan? SelectAdjacentVisibleSheetGroup(
        Workbook workbook,
        SheetId currentSheetId,
        SheetId? anchorSheetId,
        int direction)
    {
        var sheets = workbook.Sheets;
        var visibleSheetIds = new List<SheetId>(sheets.Count);
        for (var index = 0; index < sheets.Count; index++)
        {
            var sheet = sheets[index];
            if (!sheet.IsHidden)
                visibleSheetIds.Add(sheet.Id);
        }

        if (visibleSheetIds.Count == 0)
            return null;

        var currentIndex = visibleSheetIds.IndexOf(currentSheetId);
        var foundCurrent = currentIndex >= 0;
        if (currentIndex < 0)
            currentIndex = 0;

        var step = Math.Sign(direction);
        var nextIndex = Math.Clamp(currentIndex + step, 0, visibleSheetIds.Count - 1);
        var nextSheetId = visibleSheetIds[nextIndex];
        var anchor = foundCurrent && anchorSheetId is { } id && visibleSheetIds.Contains(id)
            ? id
            : visibleSheetIds[currentIndex];
        var selected = SheetGroupSelectionService.SelectRange(visibleSheetIds, anchor, nextSheetId);

        return new SheetKeyboardGroupSelectionPlan(nextSheetId, anchor, selected);
    }

    private static SheetId MissingCurrentFallback(SheetId firstVisible, SheetId? secondVisible, int direction) =>
        direction > 0
            ? secondVisible ?? firstVisible
            : firstVisible;
}
