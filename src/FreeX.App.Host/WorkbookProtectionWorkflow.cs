using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class WorkbookProtectionWorkflow
{
    public static WorkbookProtectionUiText GetUiText(Workbook workbook)
    {
        var plan = ProtectionWorkflowPlanner.CreateWorkbookChromePlan(workbook.IsStructureProtected);

        return new WorkbookProtectionUiText(
            UiText.Get(plan.ButtonContentResourceKey),
            UiText.Get(plan.TooltipTitleResourceKey),
            UiText.Get(plan.TooltipDescriptionResourceKey));
    }

    public static WorkbookProtectionAction CreateCommand(Workbook workbook, string? password)
    {
        var plan = ProtectionWorkflowPlanner.CreateWorkbookCommandPlan(
            workbook.IsStructureProtected,
            password);

        return new WorkbookProtectionAction(
            CreateCommand(plan),
            UiText.Get(plan.TitleResourceKey),
            UiText.Get(plan.SuccessMessageResourceKey));
    }

    private static IWorkbookCommand CreateCommand(WorkbookProtectionCommandPlan plan) =>
        plan.CommandIntent switch
        {
            ProtectionCommandIntent.ProtectWorkbook => new ProtectWorkbookCommand(plan.Password),
            ProtectionCommandIntent.UnprotectWorkbook => new UnprotectWorkbookCommand(plan.Password),
            _ => throw new ArgumentOutOfRangeException(nameof(plan), plan.CommandIntent, "Unsupported workbook protection intent.")
        };
}

public sealed record WorkbookProtectionAction(
    IWorkbookCommand Command,
    string Title,
    string SuccessMessage);

public sealed record WorkbookProtectionUiText(
    string ButtonContent,
    string TooltipTitle,
    string TooltipDescription);
