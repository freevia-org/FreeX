using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class WorkbookProtectionWorkflow
{
    public static WorkbookProtectionUiText GetUiText(Workbook workbook)
    {
        if (workbook.IsStructureProtected)
        {
            return new WorkbookProtectionUiText(
                UiText.Get("Protection_UnprotectWorkbookButton"),
                UiText.Get("Protection_UnprotectWorkbookTitle"),
                UiText.Get("Protection_UnprotectWorkbookDescription"));
        }

        return new WorkbookProtectionUiText(
            UiText.Get("MainWindow_Content_ProtectWorkbook"),
            UiText.Get("MainWindow_TooltipTitle_ProtectWorkbook"),
            UiText.Get("MainWindow_TooltipDescription_PreventStructuralChangesToTheWorkbookSuchAsAddingDeletingOrRenamingSheet_47267D4F"));
    }

    public static WorkbookProtectionAction CreateCommand(Workbook workbook, string? password)
    {
        if (workbook.IsStructureProtected)
        {
            return new WorkbookProtectionAction(
                new UnprotectWorkbookCommand(),
                UiText.Get("Protection_UnprotectWorkbookTitle"),
                UiText.Get("Protection_WorkbookUnprotectedMessage"));
        }

        return new WorkbookProtectionAction(
            new ProtectWorkbookCommand(password),
            UiText.Get("MainWindowMessage_ProtectWorkbookTitle"),
            UiText.Get("Protection_WorkbookProtectedMessage"));
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
