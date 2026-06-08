using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class CommandGuards
{
    private const string SheetProtectedMessage = "The sheet is protected.";
    private const string PivotTableNotFoundMessage = "PivotTable was not found.";
    private const string StructuredTableNotFoundMessage = "Table was not found.";
    private const string SourceSheetNotFoundMessage = "Source sheet was not found.";
    private const string TargetSheetNotFoundMessage = "Target sheet was not found.";

    public static CommandOutcome? RejectIfProtected(Sheet sheet)
    {
        return sheet.IsProtected
            ? new CommandOutcome(false, SheetProtectedMessage)
            : null;
    }

    public static CommandOutcome? RejectIfProtectedWithoutPermission(
        Sheet sheet,
        SheetProtectionPermission permission)
    {
        if (!sheet.IsProtected)
            return null;

        return sheet.ProtectionPermissions.Contains(permission)
            ? null
            : new CommandOutcome(false, SheetProtectedMessage);
    }

    public static CommandOutcome? RejectIfWorkbookStructureProtected(Workbook workbook)
    {
        return workbook.IsStructureProtected
            ? new CommandOutcome(false, "The workbook structure is protected.")
            : null;
    }

    public static CommandOutcome RejectPivotTableNotFound() =>
        new(false, PivotTableNotFoundMessage);

    public static CommandOutcome RejectStructuredTableNotFound() =>
        new(false, StructuredTableNotFoundMessage);

    public static CommandOutcome RejectSourceSheetNotFound() =>
        new(false, SourceSheetNotFoundMessage);

    public static CommandOutcome RejectTargetSheetNotFound() =>
        new(false, TargetSheetNotFoundMessage);

    public static string CannotInsertColumnsPastLastColumn(uint count) =>
        $"Cannot insert {count} column(s): data would be pushed past the last column ({CellAddress.MaxCol}).";

    public static string CannotInsertRowsPastLastRow(uint count) =>
        $"Cannot insert {count} row(s): data would be pushed past the last row ({CellAddress.MaxRow}).";

    public static bool CanEditCell(Workbook workbook, Sheet sheet, CellAddress address)
    {
        if (!sheet.IsProtected)
            return true;

        foreach (var range in sheet.AllowEditRanges)
        {
            if (range.Contains(address))
                return true;
        }

        var current = sheet.GetCell(address);
        var styleId = current?.StyleId
            ?? sheet.GetStyleOnly(address.Row, address.Col)
            ?? StyleId.Default;
        var style = workbook.GetStyle(styleId);
        return !style.Locked;
    }

    public static CommandOutcome? RejectInvalidFilterRange(
        SheetId sheetId,
        GridRange range,
        uint filterColOffset)
    {
        if (range.Start.Sheet != sheetId || range.End.Sheet != sheetId)
            return new CommandOutcome(false, "Filter range must be on the target sheet.");

        if (!WorksheetBounds.IsValidAddress(range.Start) || !WorksheetBounds.IsValidAddress(range.End))
            return new CommandOutcome(false, "Filter range is outside the worksheet bounds.");

        if (filterColOffset >= range.ColCount)
            return new CommandOutcome(false, "Filter column offset is outside the filter range.");

        return null;
    }
}
