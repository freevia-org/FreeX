using FreeX.Core.Model;

namespace FreeX.App.Presentation.Protection;

/// <summary>
/// What the Protect Sheet dialog should display for the current sheet state. When the sheet is
/// already protected the dialog acts as an unprotect prompt (password only); otherwise it offers the
/// full allowed-action checklist seeded from the current toggles.
/// </summary>
/// <param name="IsProtected">True when the sheet is currently protected (dialog enters unprotect mode).</param>
/// <param name="HasPassword">True when a protection password is currently stored.</param>
/// <param name="Options">
/// The pre-filled dialog options: for an unprotected sheet these are the defaults; for a protected
/// sheet they reflect the currently enabled actions so the state can be inspected.
/// </param>
public sealed record SheetProtectionState(
    bool IsProtected,
    bool HasPassword,
    ProtectSheetOptions Options);

/// <summary>
/// What the Protect Workbook dialog should display for the current workbook state.
/// </summary>
/// <param name="IsStructureProtected">
/// True when the dialog should act as an unprotect prompt: either structure protection is
/// currently applied, or a protection password is stored at all (e.g. a "Windows only" lock,
/// which Core has no dedicated flag for but still preserves the password on read/write). Real
/// Excel treats any active protection -- structure or windows-only -- as requiring the existing
/// password before it can be changed, rather than only gating on the structure flag.
/// </param>
/// <param name="HasPassword">True when a structure-protection password is currently stored.</param>
/// <param name="Options">The pre-filled dialog options reflecting the current state.</param>
public sealed record WorkbookProtectionState(
    bool IsStructureProtected,
    bool HasPassword,
    ProtectWorkbookOptions Options);

/// <summary>
/// Projects the current Core protection state of a sheet or workbook into the model a protect dialog
/// binds to. Read-only: it inspects state and never mutates it.
/// </summary>
public static class ProtectionStateProjector
{
    /// <summary>Describes the Protect Sheet dialog for the given sheet's current state.</summary>
    public static SheetProtectionState ForSheet(Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var hasPassword = ProtectionPassword.IsSet(sheet.ProtectionPassword);
        var options = sheet.IsProtected
            ? ProtectSheetOptions.FromCorePermissions(sheet.ProtectionPermissions)
            : ProtectSheetOptions.Default;

        return new SheetProtectionState(sheet.IsProtected, hasPassword, options);
    }

    /// <summary>Describes the Protect Workbook dialog for the given workbook's current state.</summary>
    public static WorkbookProtectionState ForWorkbook(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var hasPassword = ProtectionPassword.IsSet(workbook.StructureProtectionPassword);

        // A "Windows only" protected workbook (lockWindows + password, no lockStructure) preserves
        // its password on read (XlsxWorkbookMetadataReader) even though workbook.IsStructureProtected
        // is false. Gating purely on IsStructureProtected would open the dialog in protect-mode for
        // such a workbook and let ProtectWorkbookCommand silently overwrite that never-invalid,
        // never-expired password with whatever the user types (or nothing) -- with no verification
        // prompt, unlike real Excel. Treat any stored password as "protected" too so the dialog opens
        // in unprotect-mode and requires the existing password before anything can change.
        var isProtected = workbook.IsStructureProtected || hasPassword;
        var options = isProtected
            ? ProtectWorkbookOptions.FromCore(structureProtected: workbook.IsStructureProtected)
            : ProtectWorkbookOptions.Default;

        return new WorkbookProtectionState(isProtected, hasPassword, options);
    }
}
