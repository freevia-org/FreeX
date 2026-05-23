using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class ProtectionDialogPlanner
{
    private static readonly (string Label, SheetProtectionPermission Permission)[] SheetPermissionChoices =
    [
        ("Select locked cells", SheetProtectionPermission.SelectLockedCells),
        ("Select unlocked cells", SheetProtectionPermission.SelectUnlockedCells),
        ("Format cells", SheetProtectionPermission.FormatCells),
        ("Format columns", SheetProtectionPermission.FormatColumns),
        ("Format rows", SheetProtectionPermission.FormatRows),
        ("Insert columns", SheetProtectionPermission.InsertColumns),
        ("Insert rows", SheetProtectionPermission.InsertRows),
        ("Insert hyperlinks", SheetProtectionPermission.InsertHyperlinks),
        ("Delete columns", SheetProtectionPermission.DeleteColumns),
        ("Delete rows", SheetProtectionPermission.DeleteRows),
        ("Sort", SheetProtectionPermission.Sort),
        ("Use AutoFilter", SheetProtectionPermission.UseAutoFilter),
        ("Use PivotTable reports", SheetProtectionPermission.UsePivotTableReports),
        ("Edit objects", SheetProtectionPermission.EditObjects),
        ("Edit scenarios", SheetProtectionPermission.EditScenarios)
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
