using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class WorkbookProtectionWorkflow
{
    public static WorkbookProtectionUiText GetUiText(Workbook workbook)
    {
        if (workbook.IsStructureProtected)
        {
            return new WorkbookProtectionUiText(
                "Unprotect Workbook",
                "Unprotect Workbook",
                "Allow structural changes to the workbook such as adding, deleting, or renaming sheets.");
        }

        return new WorkbookProtectionUiText(
            "Protect Workbook",
            "Protect Workbook",
            "Prevent structural changes to the workbook such as adding, deleting, or renaming sheets.");
    }

    public static WorkbookProtectionAction CreateCommand(Workbook workbook, string? password)
    {
        if (workbook.IsStructureProtected)
        {
            return new WorkbookProtectionAction(
                new UnprotectWorkbookCommand(),
                "Unprotect Workbook",
                "Workbook structure is now unprotected.");
        }

        return new WorkbookProtectionAction(
            new ProtectWorkbookCommand(password),
            "Protect Workbook",
            "Workbook structure is now protected.");
    }
}

public sealed record WorkbookProtectionAction(
    IWorkbookCommand Command,
    string Title,
    string SuccessMessage);

public sealed record WorkbookProtectionUiText(
    string ButtonContent,
    string TooltipTitle,
    string TooltipDescription);
