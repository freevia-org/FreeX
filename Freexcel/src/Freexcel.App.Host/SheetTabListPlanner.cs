using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record SheetTabListPlan(SheetId CurrentSheetId, IReadOnlyList<SheetTabViewModel> Tabs);

public static class SheetTabListPlanner
{
    public static SheetTabListPlan Build(
        Workbook workbook,
        SheetId currentSheetId,
        HashSet<SheetId> groupedSheetIds)
    {
        if (workbook.Sheets.All(sheet => sheet.IsHidden) && workbook.Sheets.Count > 0)
            workbook.Sheets[0].IsHidden = false;

        if (workbook.GetSheet(currentSheetId)?.IsHidden == true)
            currentSheetId = workbook.Sheets.First(sheet => !sheet.IsHidden).Id;

        var visibleSheets = workbook.Sheets.Where(sheet => !sheet.IsHidden).ToList();
        var visibleIds = visibleSheets.Select(sheet => sheet.Id).ToHashSet();
        groupedSheetIds.RemoveWhere(id => !visibleIds.Contains(id));

        var tabs = new List<SheetTabViewModel>();
        foreach (var sheet in visibleSheets)
        {
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
        var groupedVisibleSheets = workbook.Sheets.Count(sheet => !sheet.IsHidden && groupedSheetIds.Contains(sheet.Id));
        return groupedVisibleSheets > 1 && groupedSheetIds.Contains(currentSheetId);
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
        var visibleSheets = workbook.Sheets.Where(sheet => !sheet.IsHidden).ToList();
        if (visibleSheets.Count == 0)
            return null;

        var index = visibleSheets.FindIndex(sheet => sheet.Id == currentSheetId);
        if (index < 0)
            index = 0;

        var nextIndex = Math.Clamp(index + direction, 0, visibleSheets.Count - 1);
        return visibleSheets[nextIndex].Id;
    }
}
