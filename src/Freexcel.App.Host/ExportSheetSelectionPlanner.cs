using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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

        var groupedSet = groupedSheetIds.ToHashSet();
        groupedSet.Add(currentSheetId);
        var sheetIds = VisibleSheets(workbook)
            .Where(sheet => groupedSet.Contains(sheet.Id))
            .Select(sheet => sheet.Id)
            .ToList();

        return sheetIds.Count == 0
            ? ResolveSingleSheet(workbook, currentSheetId)
            : sheetIds;
    }

    private static IReadOnlyList<SheetId> ResolveSingleSheet(Workbook workbook, SheetId sheetId) =>
        workbook.GetSheet(sheetId) is { IsHidden: false, IsVeryHidden: false } sheet
            ? [sheet.Id]
            : [];

    private static IEnumerable<Sheet> VisibleSheets(Workbook workbook) =>
        workbook.Sheets.Where(sheet => !sheet.IsHidden && !sheet.IsVeryHidden);
}
