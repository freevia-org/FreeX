using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class SheetProtectionWorkflow
{
    public static SheetProtectionUiText GetUiText(Sheet sheet)
    {
        var plan = ProtectionWorkflowPlanner.CreateSheetChromePlan(sheet.IsProtected);

        return new SheetProtectionUiText(
            UiText.Get(plan.ButtonContentResourceKey),
            UiText.Get(plan.TooltipTitleResourceKey),
            UiText.Get(plan.TooltipDescriptionResourceKey));
    }

    public static SheetProtectionAction CreateCommand(Sheet sheet, string? password)
    {
        var result = ProtectionDialogPlanner.CreateSheetResult(
            sheet.IsProtected,
            password,
            SheetProtectionPermissionLabels.GetDefaultSelectedSheetPermissions());
        return CreateCommand(sheet, result);
    }

    public static SheetProtectionAction CreateCommand(Sheet sheet, ProtectionDialogResult result)
    {
        var plan = ProtectionWorkflowPlanner.CreateSheetCommandPlan(
            sheet.Id,
            sheet.IsProtected,
            result.Password,
            SheetProtectionPermissionLabels.ParseSheetPermissions(result.SelectedSheetPermissions));

        return new SheetProtectionAction(
            CreateCommand(plan),
            UiText.Get(plan.TitleResourceKey),
            UiText.Get(plan.SuccessMessageResourceKey),
            result.SelectedSheetPermissions);
    }

    private static IWorkbookCommand CreateCommand(SheetProtectionCommandPlan plan) =>
        plan.CommandIntent switch
        {
            ProtectionCommandIntent.ProtectSheet => new ProtectSheetCommand(
                plan.SheetId,
                plan.Password,
                plan.Permissions),
            ProtectionCommandIntent.UnprotectSheet => new UnprotectSheetCommand(plan.SheetId, plan.Password),
            _ => throw new ArgumentOutOfRangeException(nameof(plan), plan.CommandIntent, "Unsupported sheet protection intent.")
        };
}

public sealed record SheetProtectionAction(
    IWorkbookCommand Command,
    string Title,
    string SuccessMessage,
    IReadOnlyList<string> SelectedSheetPermissions);

public sealed record SheetProtectionUiText(
    string ButtonContent,
    string TooltipTitle,
    string TooltipDescription);
