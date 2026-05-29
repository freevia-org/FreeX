using FreeX.Core.Model;

namespace FreeX.App.Host;

internal static class ExportSheetSelectionPlanner
{
    public static IReadOnlyList<SheetId> ResolveSheetIds(
        Workbook workbook,
        ExportOptions options,
        SheetId currentSheetId,
        IReadOnlyCollection<SheetId> groupedSheetIds)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        if (options.Scope == ExportContentScope.EntireWorkbook)
            return VisibleSheets(workbook).Select(sheet => sheet.Id).ToList();

        if (options.Scope == ExportContentScope.Selection)
            return ResolveSingleSheet(workbook, currentSheetId);

        var sheetIds = ResolveGroupedSheets(workbook, currentSheetId, groupedSheetIds);

        return sheetIds.Count == 0
            ? ResolveSingleSheet(workbook, currentSheetId)
            : sheetIds;
    }

    private static IReadOnlyList<SheetId> ResolveGroupedSheets(
        Workbook workbook,
        SheetId currentSheetId,
        IReadOnlyCollection<SheetId> groupedSheetIds)
    {
        var groupedSet = GroupedSheetSet(currentSheetId, groupedSheetIds);
        return VisibleSheetIds(workbook)
            .Where(groupedSet.Contains)
            .ToList();
    }

    private static IReadOnlyList<SheetId> ResolveSingleSheet(Workbook workbook, SheetId sheetId) =>
        workbook.GetSheet(sheetId) is { IsHidden: false, IsVeryHidden: false } sheet
            ? [sheet.Id]
            : [];

    private static HashSet<SheetId> GroupedSheetSet(
        SheetId currentSheetId,
        IReadOnlyCollection<SheetId> groupedSheetIds)
    {
        var groupedSet = groupedSheetIds.ToHashSet();
        groupedSet.Add(currentSheetId);
        return groupedSet;
    }

    private static IEnumerable<SheetId> VisibleSheetIds(Workbook workbook) =>
        VisibleSheets(workbook).Select(sheet => sheet.Id);

    private static IEnumerable<Sheet> VisibleSheets(Workbook workbook) =>
        workbook.Sheets.Where(sheet => !sheet.IsHidden && !sheet.IsVeryHidden);
}
