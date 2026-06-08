using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class CommandGuards
{
    private const string SheetProtectedMessage = "The sheet is protected.";
    private const string PivotTableNotFoundMessage = "PivotTable was not found.";
    private const string StructuredTableNotFoundMessage = "Table was not found.";
    private const string StructuredTableHasNoColumnsMessage = "Table has no columns.";
    private const string SourceSheetNotFoundMessage = "Source sheet was not found.";
    private const string TargetSheetNotFoundMessage = "Target sheet was not found.";
    private const string PivotTableNameRequiredMessage = "PivotTable name is required.";
    private const string PivotTableTargetRangeOnTargetSheetMessage = "PivotTable target range must be on the target sheet.";
    private const string PivotTableSourceRangeRequiresHeadersMessage = "PivotTable source range must include headers and data.";
    private const string PivotTableFieldIndexOutsideSourceRangeMessage = "PivotTable field index is outside the source range.";
    private const string PivotTableRequiresDataFieldMessage = "PivotTable requires at least one data field.";
    private const string RowRangeOutsideWorksheetBoundsMessage = "Row range is outside the worksheet bounds.";
    private const string ColumnRangeOutsideWorksheetBoundsMessage = "Column range is outside the worksheet bounds.";
    private const string AllowedEditRangeOnTargetSheetMessage = "Allowed edit range must be on the target sheet.";
    private const string CouldNotInsertSubtotalRowMessage = "Could not insert subtotal row.";

    public static CommandOutcome? RejectIfProtected(Sheet sheet)
    {
        return sheet.IsProtected
            ? new CommandOutcome(false, SheetProtectedMessage)
            : null;
    }

    public static CommandOutcome RejectSheetProtected() =>
        new(false, SheetProtectedMessage);

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

    public static CommandOutcome RejectPivotTableNameRequired() =>
        new(false, PivotTableNameRequiredMessage);

    public static CommandOutcome RejectPivotTableTargetRangeOnTargetSheet() =>
        new(false, PivotTableTargetRangeOnTargetSheetMessage);

    public static CommandOutcome RejectPivotTableSourceRangeRequiresHeaders() =>
        new(false, PivotTableSourceRangeRequiresHeadersMessage);

    public static CommandOutcome RejectPivotTableFieldIndexOutsideSourceRange() =>
        new(false, PivotTableFieldIndexOutsideSourceRangeMessage);

    public static CommandOutcome RejectPivotTableRequiresDataField() =>
        new(false, PivotTableRequiresDataFieldMessage);

    public static CommandOutcome RejectRowRangeOutsideWorksheetBounds() =>
        new(false, RowRangeOutsideWorksheetBoundsMessage);

    public static CommandOutcome RejectColumnRangeOutsideWorksheetBounds() =>
        new(false, ColumnRangeOutsideWorksheetBoundsMessage);

    public static CommandOutcome RejectAllowedEditRangeOnTargetSheet() =>
        new(false, AllowedEditRangeOnTargetSheetMessage);

    public static CommandOutcome RejectCouldNotInsertSubtotalRow() =>
        new(false, CouldNotInsertSubtotalRowMessage);

    public static CommandOutcome RejectStructuredTableNotFound() =>
        new(false, StructuredTableNotFoundMessage);

    public static CommandOutcome RejectStructuredTableHasNoColumns() =>
        new(false, StructuredTableHasNoColumnsMessage);

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
