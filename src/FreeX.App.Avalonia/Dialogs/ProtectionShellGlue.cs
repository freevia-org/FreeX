using FreeX.App.Presentation.Protection;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Dialogs;

/// <summary>
/// Portable (no Avalonia UI) glue backing the Avalonia Protect Sheet and Protect Workbook dialogs. It
/// projects the current Core protection state into the portable dialog models (via
/// <see cref="ProtectionStateProjector"/>) and maps a validated <see cref="ProtectSheetOptions"/> /
/// <see cref="ProtectWorkbookOptions"/> — or an unprotect request — onto the Core protect/unprotect commands
/// the shell then runs through its shared session command path. Kept UI-free so it is unit-testable without a
/// window.
/// </summary>
internal static class ProtectionShellGlue
{
    /// <summary>Projects the sheet's current protection state into the Protect Sheet dialog model.</summary>
    public static SheetProtectionState ProjectSheet(Sheet sheet) => ProtectionStateProjector.ForSheet(sheet);

    /// <summary>Projects the workbook's current protection state into the Protect Workbook dialog model.</summary>
    public static WorkbookProtectionState ProjectWorkbook(Workbook workbook) =>
        ProtectionStateProjector.ForWorkbook(workbook);

    /// <summary>
    /// Maps the validated Protect Sheet dialog options onto a <see cref="ProtectSheetCommand"/>, carrying the
    /// allowed-action permission set (in canonical dialog order) and the optional password. Callers validate
    /// the password/confirm pair (see <see cref="ProtectSheetOptions.ValidatePassword"/>) before calling this.
    /// </summary>
    public static ProtectSheetCommand BuildProtectSheetCommand(SheetId sheetId, ProtectSheetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new ProtectSheetCommand(sheetId, options.ToCorePassword(), options.ToCorePermissions());
    }

    /// <summary>
    /// Maps an unprotect-sheet request onto an <see cref="UnprotectSheetCommand"/>, carrying the password the
    /// user supplied so the command can verify it against the stored secret.
    /// </summary>
    public static UnprotectSheetCommand BuildUnprotectSheetCommand(SheetId sheetId, string? password) =>
        new(sheetId, string.IsNullOrEmpty(password) ? null : password);

    /// <summary>
    /// Maps the validated Protect Workbook dialog options onto a <see cref="ProtectWorkbookCommand"/>, carrying
    /// the optional password. Window protection is not modelled by Core, so only the structure flag and
    /// password survive the projection. Callers validate the password/confirm pair before calling this.
    /// </summary>
    public static ProtectWorkbookCommand BuildProtectWorkbookCommand(ProtectWorkbookOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new ProtectWorkbookCommand(options.ToCorePassword(), options.ToCoreStructureProtected());
    }

    /// <summary>
    /// Maps an unprotect-workbook request onto an <see cref="UnprotectWorkbookCommand"/>, carrying the password
    /// the user supplied so the command can verify it against the stored secret.
    /// </summary>
    public static UnprotectWorkbookCommand BuildUnprotectWorkbookCommand(string? password) =>
        new(string.IsNullOrEmpty(password) ? null : password);

    /// <summary>The display label for a Protect Sheet allowed-action toggle.</summary>
    public static string DescribePermission(SheetProtectionPermission permission) => permission switch
    {
        SheetProtectionPermission.SelectLockedCells => "Select locked cells",
        SheetProtectionPermission.SelectUnlockedCells => "Select unlocked cells",
        SheetProtectionPermission.FormatCells => "Format cells",
        SheetProtectionPermission.FormatColumns => "Format columns",
        SheetProtectionPermission.FormatRows => "Format rows",
        SheetProtectionPermission.InsertColumns => "Insert columns",
        SheetProtectionPermission.InsertRows => "Insert rows",
        SheetProtectionPermission.InsertHyperlinks => "Insert hyperlinks",
        SheetProtectionPermission.DeleteColumns => "Delete columns",
        SheetProtectionPermission.DeleteRows => "Delete rows",
        SheetProtectionPermission.Sort => "Sort",
        SheetProtectionPermission.UseAutoFilter => "Use AutoFilter",
        SheetProtectionPermission.UsePivotTableReports => "Use PivotTable and PivotChart reports",
        SheetProtectionPermission.EditObjects => "Edit objects",
        SheetProtectionPermission.EditScenarios => "Edit scenarios",
        _ => permission.ToString(),
    };
}
