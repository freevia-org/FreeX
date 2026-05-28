using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record SheetTabListPlan(SheetId CurrentSheetId, IReadOnlyList<SheetTabViewModel> Tabs);

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

        if (firstVisibleIndex >= 0 && workbook.GetSheet(currentSheetId)?.IsHidden == true)
            currentSheetId = sheets[firstVisibleIndex].Id;

        groupedSheetIds.RemoveWhere(id => workbook.GetSheet(id)?.IsHidden != false);

        var tabs = new List<SheetTabViewModel>(sheets.Count);
        for (var index = 0; index < sheets.Count; index++)
        {
            var sheet = sheets[index];
            if (sheet.IsHidden)
                continue;

            if (groupedSheetIds.Count == 0 && sheet.Id == currentSheetId)
                groupedSheetIds.Add(sheet.Id);

            tabs.Add(new SheetTabViewModel(sheet.Id, sheet.Name, sheet.TabColor)
            {
                IsActive = sheet.Id == currentSheetId,
                IsGrouped = groupedSheetIds.Contains(sheet.Id)
            });
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

        return direction > 0
            ? secondVisible ?? firstVisible
            : firstVisible;
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
        if (currentIndex < 0)
            currentIndex = 0;

        var nextIndex = Math.Clamp(currentIndex + direction, 0, visibleSheetIds.Count - 1);
        var nextSheetId = visibleSheetIds[nextIndex];
        var anchor = anchorSheetId is { } id && visibleSheetIds.Contains(id)
            ? id
            : visibleSheetIds[currentIndex];
        var selected = SheetGroupSelectionService.SelectRange(visibleSheetIds, anchor, nextSheetId);

        return new SheetKeyboardGroupSelectionPlan(nextSheetId, anchor, selected);
    }
}
