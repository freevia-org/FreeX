using System.Diagnostics.CodeAnalysis;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static class CommandGuards
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
    private const string CannotChangePartOfArrayMessage = "You cannot change part of an array.";

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

    public static bool TryFindPivotTable(
        Sheet sheet,
        string pivotTableName,
        [NotNullWhen(true)] out PivotTableModel? pivotTable)
    {
        foreach (var candidate in sheet.PivotTables)
        {
            if (string.Equals(candidate.Name, pivotTableName, StringComparison.OrdinalIgnoreCase))
            {
                pivotTable = candidate;
                return true;
            }
        }

        pivotTable = null;
        return false;
    }

    public static PivotCacheModel? FindPivotCache(Workbook workbook, PivotTableModel pivotTable)
    {
        foreach (var cache in workbook.PivotCaches)
            if (cache.CacheId == pivotTable.CacheId)
                return cache;

        return null;
    }

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

    public static bool TryFindStructuredTable(
        Sheet sheet,
        int tableId,
        [NotNullWhen(true)] out StructuredTableModel? table)
    {
        foreach (var candidate in sheet.StructuredTables)
        {
            if (candidate.Id == tableId)
            {
                table = candidate;
                return true;
            }
        }

        table = null;
        return false;
    }

    public static bool TryFindStructuredTableIndex(Sheet sheet, int tableId, out int tableIndex)
    {
        for (var i = 0; i < sheet.StructuredTables.Count; i++)
        {
            if (sheet.StructuredTables[i].Id == tableId)
            {
                tableIndex = i;
                return true;
            }
        }

        tableIndex = -1;
        return false;
    }

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

    // M42: Excel's Allow Users to Edit Ranges feature lets each protected range carry its own
    // password (distinct from the sheet password) and prompts for it before allowing an edit inside
    // that range while the sheet is protected. The password is modeled on
    // Sheet.AllowEditRangePasswords (keyed by the same GridRange stored in AllowEditRanges) and
    // round-tripped by XlsxAllowEditRangeMapper; Sheet.UnlockedAllowEditRanges tracks which
    // password-protected ranges the user has already unlocked this session (an in-memory gate, not
    // persisted). A range with no stored password behaves exactly as before: unconditionally
    // editable. The interactive "prompt for the range password" flow itself lives in the shell
    // (outside this file's scope) and is expected to call TryUnlockAllowEditRange below on a correct
    // password before retrying the edit; this guard only enforces the resulting locked/unlocked
    // state.
    public static bool CanEditCell(Workbook workbook, Sheet sheet, CellAddress address)
    {
        if (!sheet.IsProtected)
            return true;

        foreach (var range in sheet.AllowEditRanges)
        {
            if (!range.Contains(address))
                continue;

            if (!IsPasswordProtected(sheet, range) || sheet.UnlockedAllowEditRanges.Contains(range))
                return true;
        }

        var current = sheet.GetCell(address);
        var styleId = current?.StyleId
            ?? sheet.GetStyleOnly(address.Row, address.Col)
            ?? StyleId.Default;
        var style = workbook.GetStyle(styleId);
        return !style.Locked;
    }

    /// <summary>
    /// Whether <paramref name="address"/> may be SELECTED while the sheet is protected, per the
    /// sheet's <see cref="SheetProtectionPermission.SelectLockedCells"/> /
    /// <see cref="SheetProtectionPermission.SelectUnlockedCells"/> permissions (the "Select
    /// locked cells" / "Select unlocked cells" checkboxes in Excel's Protect Sheet dialog).
    /// Distinct from <see cref="CanEditCell"/>, which governs whether an edit is allowed -- Excel
    /// can (and by default does) allow selecting a locked cell without allowing it to be edited;
    /// when "Select locked cells" is unchecked, locked cells cannot be navigated to at all.
    /// </summary>
    public static bool CanSelectCell(Workbook workbook, Sheet sheet, CellAddress address)
    {
        if (!sheet.IsProtected)
            return true;

        var current = sheet.GetCell(address);
        var styleId = current?.StyleId
            ?? sheet.GetStyleOnly(address.Row, address.Col)
            ?? StyleId.Default;
        var style = workbook.GetStyle(styleId);

        return style.Locked
            ? sheet.ProtectionPermissions.Contains(SheetProtectionPermission.SelectLockedCells)
            : sheet.ProtectionPermissions.Contains(SheetProtectionPermission.SelectUnlockedCells);
    }

    /// <summary>True when <paramref name="range"/> has its own Allow-Edit-Range password set.</summary>
    public static bool IsPasswordProtected(Sheet sheet, GridRange range) =>
        sheet.AllowEditRangePasswords.TryGetValue(range, out var stored) && !string.IsNullOrEmpty(stored);

    /// <summary>
    /// Verifies <paramref name="password"/> against <paramref name="range"/>'s stored Allow-Edit-Range
    /// password and, on success, marks the range unlocked for the remainder of the session (see
    /// <see cref="Sheet.UnlockedAllowEditRanges"/>) so subsequent edits in it are not re-prompted.
    /// Returns false without unlocking anything when the range has no password or the password does
    /// not match.
    /// </summary>
    public static bool TryUnlockAllowEditRange(Sheet sheet, GridRange range, string? password)
    {
        if (!sheet.AllowEditRangePasswords.TryGetValue(range, out var stored) || string.IsNullOrEmpty(stored))
            return false;

        if (!ProtectionPasswordHelper.VerifyStoredPassword(stored, password))
            return false;

        sheet.UnlockedAllowEditRanges.Add(range);
        return true;
    }

    /// <summary>
    /// Rejects an edit/delete of <paramref name="addresses"/> if any of them belongs to a legacy CSE
    /// array or a dynamic-array spill range whose full anchor+extent is not entirely included in
    /// <paramref name="addresses"/>. Mirrors Excel's "You cannot change part of an array" rule: a
    /// single member (or even just the anchor alone) cannot be edited or cleared in isolation, but
    /// selecting and editing/clearing the whole array range at once is allowed.
    /// </summary>
    public static CommandOutcome? RejectIfSplitsArray(Sheet sheet, IEnumerable<CellAddress> addresses)
    {
        if (!sheet.HasArrayOrSpillMembers)
            return null;

        HashSet<CellAddress>? addressSet = null;

        foreach (var address in addresses)
        {
            if (!sheet.TryGetArrayExtent(address, out var anchor, out var rows, out var cols))
                continue;

            addressSet ??= new HashSet<CellAddress>(addresses);

            for (var r = 0u; r < rows; r++)
            {
                for (var c = 0u; c < cols; c++)
                {
                    var member = new CellAddress(anchor.Sheet, anchor.Row + r, anchor.Col + c);
                    if (!addressSet.Contains(member))
                        return new CommandOutcome(false, CannotChangePartOfArrayMessage);
                }
            }
        }

        return null;
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
