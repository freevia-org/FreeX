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
    private const string CannotChangePartOfDataTableMessage = "You cannot change part of a Data Table.";

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

    /// <summary>
    /// R107-round2: shared "a structured table's name is about to be freed" guard for every command
    /// that removes a <see cref="StructuredTableModel"/> from a sheet's <c>StructuredTables</c>
    /// collection — Convert to Range, Delete Sheet, and a row/column delete that fully consumes the
    /// table's range are the three known ways this happens (see PivotTableRefreshService.Refresh's
    /// id-or-name fallback doc comment for the full hazard). A table-backed pivot cache that has never
    /// been refreshed since the file was loaded (the normal starting state — WorkbookOpenService never
    /// calls PivotTableRefreshService.Refresh at open time) still identifies its source purely by
    /// <see cref="PivotCacheModel.SourceTableName"/>, with <see cref="PivotCacheModel.SourceTableId"/>
    /// left null. Left alone, that dangling name is exactly what a later rename (or a brand-new table)
    /// reusing the freed name would collide with — the next refresh's null-id fallback would then
    /// resolve the dangling name against that unrelated table and silently rebind the pivot to its
    /// data. Pinning every such cache's SourceTableId to the removed table's own (now-orphaned) id at
    /// the moment the name is freed closes that: the id-based lookup in
    /// PivotTableRefreshService.Refresh then leaves the pivot's last-known extent untouched instead of
    /// ever falling back to a name match against a decoy. A cache that has already established a
    /// different SourceTableId (the post-first-refresh state) is left untouched — this never touches
    /// pivot caches that are already correctly bound. Returns the caches that were pinned so the
    /// caller can null <see cref="PivotCacheModel.SourceTableId"/> back out via
    /// <see cref="UnpinOrphanedPivotCacheSourceTableIds"/> on Undo.
    ///
    /// Also ratchets <see cref="Workbook.NextStructuredTableIdWatermark"/> up to at least
    /// <paramref name="removedTable"/>'s id, unconditionally (even when no pivot cache matches it by
    /// name). This is the ONLY point every table-removal path funnels through, so it is also where a
    /// table that predates any in-session <c>CreateStructuredTableCommand</c> call (e.g. one loaded
    /// straight from a file, whose id the watermark has never seen) gets its id remembered before it's
    /// gone — otherwise a table removed as the very first structured-table action in a session (nothing
    /// yet having called NextTableId while it was still live to raise the live-scanned max) would let a
    /// same-session <c>CreateStructuredTableCommand</c> immediately hand its id right back out. This
    /// also protects a table-connected <see cref="SlicerModel.SourceTableId"/> (which is pure id-based,
    /// with no name fallback of its own to guard) from silently re-attaching to an unrelated new table
    /// that reused its old, dead table's id.
    /// </summary>
    public static List<PivotCacheModel> PinOrphanedPivotCacheSourceTableIds(Workbook workbook, StructuredTableModel removedTable)
    {
        workbook.NextStructuredTableIdWatermark = Math.Max(workbook.NextStructuredTableIdWatermark, removedTable.Id);

        List<PivotCacheModel>? orphaned = null;
        foreach (var cache in workbook.PivotCaches)
        {
            if (cache.SourceType != PivotCacheSourceType.Table || cache.SourceTableId is not null)
                continue;
            if (!string.Equals(cache.SourceTableName, removedTable.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            (orphaned ??= []).Add(cache);
            cache.SourceTableId = removedTable.Id;
        }

        return orphaned ?? [];
    }

    /// <summary>Undoes <see cref="PinOrphanedPivotCacheSourceTableIds"/>: puts SourceTableId back to null.</summary>
    public static void UnpinOrphanedPivotCacheSourceTableIds(IReadOnlyList<PivotCacheModel> orphanedCaches)
    {
        foreach (var cache in orphanedCaches)
            cache.SourceTableId = null;
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
        if (!sheet.HasArrayOrSpillMembers && !sheet.HasDataTableRanges)
            return null;

        HashSet<CellAddress>? addressSet = null;

        foreach (var address in addresses)
        {
            if (sheet.HasArrayOrSpillMembers && sheet.TryGetArrayExtent(address, out var anchor, out var rows, out var cols))
            {
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

            // R90-app-goalseek-whatif-5-3: a What-If Analysis Data Table's result body is a single
            // logical array (Excel's {=TABLE(,...)}) even though FreeX stores it as plain per-cell
            // formulas — block editing/deleting just one interior cell, matching Excel's "You cannot
            // change part of a Data Table.", while still allowing the whole body to be replaced at once.
            if (sheet.HasDataTableRanges && sheet.TryGetDataTableRange(address, out var dataTableRange))
            {
                addressSet ??= new HashSet<CellAddress>(addresses);

                foreach (var member in dataTableRange.AllCells())
                {
                    if (!addressSet.Contains(member))
                        return new CommandOutcome(false, CannotChangePartOfDataTableMessage);
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
