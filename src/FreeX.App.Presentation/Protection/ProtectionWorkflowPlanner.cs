using FreeX.Core.Model;

namespace FreeX.App.Presentation.Protection;

public enum ProtectionCommandIntent
{
    ProtectSheet,
    UnprotectSheet,
    ProtectWorkbook,
    UnprotectWorkbook
}

public sealed record ProtectionChromePlan(
    string ButtonContentResourceKey,
    string TooltipTitleResourceKey,
    string TooltipDescriptionResourceKey);

public sealed record SheetProtectionCommandPlan(
    ProtectionCommandIntent CommandIntent,
    SheetId SheetId,
    string? Password,
    IReadOnlyList<SheetProtectionPermission> Permissions,
    string TitleResourceKey,
    string SuccessMessageResourceKey);

public sealed record WorkbookProtectionCommandPlan(
    ProtectionCommandIntent CommandIntent,
    string? Password,
    string TitleResourceKey,
    string SuccessMessageResourceKey);

/// <summary>
/// Shared protect/unprotect workflow choices. Renderers localize resource keys and execute the
/// resulting command intent, but the app behavior decision lives here.
/// </summary>
public static class ProtectionWorkflowPlanner
{
    public static ProtectionChromePlan CreateSheetChromePlan(bool isProtected) =>
        isProtected
            ? new ProtectionChromePlan(
                "Protection_UnprotectSheetButton",
                "Protection_UnprotectSheetTitle",
                "Protection_UnprotectSheetDescription")
            : new ProtectionChromePlan(
                "MainWindow_Content_ProtectSheet",
                "MainWindow_TooltipTitle_ProtectSheet",
                "MainWindow_TooltipDescription_SetSheetProtectionForLockedCellsWithAnOptionalPassword");

    public static ProtectionChromePlan CreateWorkbookChromePlan(bool isStructureProtected) =>
        isStructureProtected
            ? new ProtectionChromePlan(
                "Protection_UnprotectWorkbookButton",
                "Protection_UnprotectWorkbookTitle",
                "Protection_UnprotectWorkbookDescription")
            : new ProtectionChromePlan(
                "MainWindow_Content_ProtectWorkbook",
                "MainWindow_TooltipTitle_ProtectWorkbook",
                "MainWindow_TooltipDescription_PreventStructuralChangesToTheWorkbookSuchAsAddingDeletingOrRenamingSheet_47267D4F");

    public static SheetProtectionCommandPlan CreateSheetCommandPlan(
        SheetId sheetId,
        bool isProtected,
        string? password,
        IEnumerable<SheetProtectionPermission> selectedPermissions)
    {
        ArgumentNullException.ThrowIfNull(selectedPermissions);

        return isProtected
            ? new SheetProtectionCommandPlan(
                ProtectionCommandIntent.UnprotectSheet,
                sheetId,
                password,
                [],
                "Protection_UnprotectSheetTitle",
                "Protection_SheetUnprotectedMessage")
            : new SheetProtectionCommandPlan(
                ProtectionCommandIntent.ProtectSheet,
                sheetId,
                password,
                ProtectSheetOptions.FromCorePermissions(selectedPermissions).ToCorePermissions(),
                "MainWindowMessage_ProtectSheetTitle",
                "Protection_SheetProtectedMessage");
    }

    public static WorkbookProtectionCommandPlan CreateWorkbookCommandPlan(
        bool isStructureProtected,
        string? password) =>
        isStructureProtected
            ? new WorkbookProtectionCommandPlan(
                ProtectionCommandIntent.UnprotectWorkbook,
                password,
                "Protection_UnprotectWorkbookTitle",
                "Protection_WorkbookUnprotectedMessage")
            : new WorkbookProtectionCommandPlan(
                ProtectionCommandIntent.ProtectWorkbook,
                password,
                "MainWindowMessage_ProtectWorkbookTitle",
                "Protection_WorkbookProtectedMessage");
}
