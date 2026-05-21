using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class SheetProtectionWorkflow
{
    public static SheetProtectionUiText GetUiText(Sheet sheet)
    {
        if (sheet.IsProtected)
        {
            return new SheetProtectionUiText(
                "Unprotect Sheet",
                "Unprotect Sheet",
                "Remove sheet protection so locked cells can be edited again.");
        }

        return new SheetProtectionUiText(
            "Protect Sheet",
            "Protect Sheet",
            "Set sheet protection for locked cells with an optional password.");
    }

    public static SheetProtectionAction CreateCommand(Sheet sheet, string? password)
    {
        var result = ProtectionDialogPlanner.CreateSheetResult(sheet, password);
        return CreateCommand(sheet, result);
    }

    public static SheetProtectionAction CreateCommand(Sheet sheet, ProtectionDialogResult result)
    {
        if (sheet.IsProtected)
        {
            return new SheetProtectionAction(
                new UnprotectSheetCommand(sheet.Id),
                "Unprotect Sheet",
                "Sheet is now unprotected.",
                []);
        }

        return new SheetProtectionAction(
            new ProtectSheetCommand(sheet.Id, result.Password),
            "Protect Sheet",
            "Sheet is now protected.",
            result.SelectedSheetPermissions);
    }
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
