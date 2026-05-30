using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class ProtectionDialogPlanner
{
    private static readonly (string Label, SheetProtectionPermission Permission)[] SheetPermissionChoices =
    [
        (UiText.Get("Protection_PermissionSelectLockedCells"), SheetProtectionPermission.SelectLockedCells),
        (UiText.Get("Protection_PermissionSelectUnlockedCells"), SheetProtectionPermission.SelectUnlockedCells),
        (UiText.Get("Protection_PermissionFormatCells"), SheetProtectionPermission.FormatCells),
        (UiText.Get("Protection_PermissionFormatColumns"), SheetProtectionPermission.FormatColumns),
        (UiText.Get("Protection_PermissionFormatRows"), SheetProtectionPermission.FormatRows),
        (UiText.Get("Protection_PermissionInsertColumns"), SheetProtectionPermission.InsertColumns),
        (UiText.Get("Protection_PermissionInsertRows"), SheetProtectionPermission.InsertRows),
        (UiText.Get("Protection_PermissionInsertHyperlinks"), SheetProtectionPermission.InsertHyperlinks),
        (UiText.Get("Protection_PermissionDeleteColumns"), SheetProtectionPermission.DeleteColumns),
        (UiText.Get("Protection_PermissionDeleteRows"), SheetProtectionPermission.DeleteRows),
        (UiText.Get("Protection_PermissionSort"), SheetProtectionPermission.Sort),
        (UiText.Get("Protection_PermissionUseAutoFilter"), SheetProtectionPermission.UseAutoFilter),
        (UiText.Get("Protection_PermissionUsePivotTableReports"), SheetProtectionPermission.UsePivotTableReports),
        (UiText.Get("Protection_PermissionEditObjects"), SheetProtectionPermission.EditObjects),
        (UiText.Get("Protection_PermissionEditScenarios"), SheetProtectionPermission.EditScenarios)
    ];

    private static readonly SheetProtectionPermission[] DefaultSelectedSheetPermissions =
    [
        SheetProtectionPermission.SelectLockedCells,
        SheetProtectionPermission.SelectUnlockedCells
    ];

    public static ProtectionDialogResult CreateSheetResult(Sheet sheet, string? password) =>
        CreateSheetResult(sheet, password, GetDefaultSelectedSheetPermissions());

    public static ProtectionDialogResult CreateSheetResult(
        Sheet sheet,
        string? password,
        IReadOnlyList<string> selectedSheetPermissions) =>
        sheet.IsProtected
            ? new ProtectionDialogResult(ProtectionDialogMode.Unprotect, null, [])
            : new ProtectionDialogResult(ProtectionDialogMode.Protect, password, selectedSheetPermissions);

    public static ProtectionDialogResult CreateSheetResult(Sheet sheet, string? password, string? confirmation) =>
        sheet.IsProtected || PasswordsMatch(password, confirmation)
            ? CreateSheetResult(sheet, password)
            : new ProtectionDialogResult(ProtectionDialogMode.Protect, null, GetDefaultSelectedSheetPermissions());

    public static ProtectionDialogResult CreateWorkbookResult(Workbook workbook, string? password) =>
        workbook.IsStructureProtected
            ? new ProtectionDialogResult(ProtectionDialogMode.Unprotect, null, [])
            : new ProtectionDialogResult(ProtectionDialogMode.Protect, password, []);

    public static IReadOnlyList<string> GetDefaultSheetPermissions() =>
        SheetPermissionChoices.Select(choice => choice.Label).ToList();

    public static IReadOnlyList<string> GetDefaultSelectedSheetPermissions() =>
        DefaultSelectedSheetPermissions.Select(FormatSheetPermission).ToList();

    public static IReadOnlyList<SheetProtectionPermission> ParseSheetPermissions(IEnumerable<string> labels) =>
        labels.Select(ParseSheetPermission)
            .Where(permission => permission is not null)
            .Select(permission => permission!.Value)
            .Distinct()
            .ToList();

    public static string FormatSheetPermission(SheetProtectionPermission permission) =>
        SheetPermissionChoices.FirstOrDefault(choice => choice.Permission == permission).Label
        ?? permission.ToString();

    public static bool PasswordsMatch(string? password, string? confirmation) =>
        string.Equals(password ?? "", confirmation ?? "", StringComparison.Ordinal);

    public static bool TryParseAllowEditRange(string text, SheetId sheetId, out GridRange range) =>
        ProtectionInputParser.TryParseAllowEditRange(text, sheetId, out range);

    private static SheetProtectionPermission? ParseSheetPermission(string label)
    {
        foreach (var choice in SheetPermissionChoices)
            if (string.Equals(choice.Label, label, StringComparison.Ordinal))
                return choice.Permission;

        return null;
    }
}
