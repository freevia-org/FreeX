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
/// <param name="IsStructureProtected">True when structure protection is currently applied.</param>
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
        var options = workbook.IsStructureProtected
            ? ProtectWorkbookOptions.FromCore(structureProtected: true)
            : ProtectWorkbookOptions.Default;

        return new WorkbookProtectionState(workbook.IsStructureProtected, hasPassword, options);
    }
}
