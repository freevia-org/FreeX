using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record WorkbookSheetTab(
    SheetId Id,
    string Name,
    bool IsActive,
    CellColor? TabColor = null);

public sealed record WorkbookHiddenSheet(
    SheetId Id,
    string Name)
{
    public override string ToString() => Name;
}

public sealed record WorkbookSheetSelection(
    Sheet Sheet,
    int Index,
    IReadOnlyList<WorkbookSheetTab> Tabs);

public sealed class WorkbookSheetSelectionService
{
    public WorkbookSheetSelection EnsureActiveSheet(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        if (workbook.Sheets.Count == 0)
            workbook.AddSheet("Sheet1");

        var index = ResolveActiveIndex(workbook);
        workbook.ActiveSheetIndex = index;
        return CreateSelection(workbook, index);
    }

    public WorkbookSheetSelection SelectSheet(Workbook workbook, SheetId sheetId)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        if (workbook.Sheets.Count == 0)
            workbook.AddSheet("Sheet1");

        var selectableIndexes = GetSelectableIndexes(workbook);
        var index = selectableIndexes.FirstOrDefault(candidate => workbook.Sheets[candidate].Id == sheetId, -1);
        if (index < 0)
            return EnsureActiveSheet(workbook);

        workbook.ActiveSheetIndex = index;
        return CreateSelection(workbook, index);
    }

    private static WorkbookSheetSelection CreateSelection(Workbook workbook, int activeIndex)
    {
        var activeSheet = workbook.Sheets[activeIndex];
        var tabs = GetSelectableIndexes(workbook)
            .Select(index =>
            {
                var sheet = workbook.Sheets[index];
                return new WorkbookSheetTab(sheet.Id, sheet.Name, sheet.Id == activeSheet.Id, sheet.TabColor);
            })
            .ToList();

        return new WorkbookSheetSelection(activeSheet, activeIndex, tabs);
    }

    private static int ResolveActiveIndex(Workbook workbook)
    {
        var selectableIndexes = GetSelectableIndexes(workbook);
        if (workbook.ActiveSheetIndex is { } requested &&
            selectableIndexes.Contains(requested))
        {
            return requested;
        }

        return selectableIndexes[0];
    }

    private static List<int> GetSelectableIndexes(Workbook workbook)
    {
        var visible = workbook.Sheets
            .Select((sheet, index) => (sheet, index))
            .Where(candidate => !candidate.sheet.IsHidden && !candidate.sheet.IsVeryHidden)
            .Select(candidate => candidate.index)
            .ToList();

        return visible.Count > 0
            ? visible
            : Enumerable.Range(0, workbook.Sheets.Count).ToList();
    }
}
