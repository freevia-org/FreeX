using FreeX.Core.Model;

namespace FreeX.App.Presentation.Protection;

public enum ProtectionDialogMode
{
    Protect,
    Unprotect
}

public sealed record ProtectionDialogResult(
    ProtectionDialogMode Mode,
    string? Password,
    IReadOnlyList<SheetProtectionPermission> SelectedSheetPermissions);

public static class ProtectionDialogPlanner
{
    public const double ProtectSheetWidth = 430;
    public const double ProtectSheetHeight = 540;
    public const double UnprotectSheetWidth = 380;
    public const double UnprotectSheetHeight = 240;
    public const double ProtectWorkbookCaptureWidth = 380;
    public const double ProtectWorkbookCaptureHeight = 250;

    public static ProtectionDialogResult CreateSheetResult(
        bool isProtected,
        string? password,
        IReadOnlyList<SheetProtectionPermission> selectedSheetPermissions) =>
        isProtected
            ? new ProtectionDialogResult(ProtectionDialogMode.Unprotect, password, [])
            : new ProtectionDialogResult(ProtectionDialogMode.Protect, password, selectedSheetPermissions);

    public static ProtectionDialogResult CreateSheetResult(
        bool isProtected,
        string? password,
        string? confirmation,
        IReadOnlyList<SheetProtectionPermission> defaultSelectedSheetPermissions) =>
        isProtected || PasswordsMatch(password, confirmation)
            ? CreateSheetResult(isProtected, password, defaultSelectedSheetPermissions)
            : new ProtectionDialogResult(ProtectionDialogMode.Protect, null, defaultSelectedSheetPermissions);

    public static ProtectionDialogResult CreateWorkbookResult(bool isStructureProtected, string? password) =>
        isStructureProtected
            ? new ProtectionDialogResult(ProtectionDialogMode.Unprotect, password, [])
            : new ProtectionDialogResult(ProtectionDialogMode.Protect, password, []);

    public static bool PasswordsMatch(string? password, string? confirmation) =>
        string.Equals(password ?? "", confirmation ?? "", StringComparison.Ordinal);
}
