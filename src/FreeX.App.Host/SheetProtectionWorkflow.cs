using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class SheetProtectionWorkflow
{
    public static SheetProtectionUiText GetUiText(Sheet sheet)
    {
        if (sheet.IsProtected)
        {
            return new SheetProtectionUiText(
                UiText.Get("Protection_UnprotectSheetButton"),
                UiText.Get("Protection_UnprotectSheetTitle"),
                UiText.Get("Protection_UnprotectSheetDescription"));
        }

        return new SheetProtectionUiText(
            UiText.Get("MainWindow_Content_ProtectSheet"),
            UiText.Get("MainWindow_TooltipTitle_ProtectSheet"),
            UiText.Get("MainWindow_TooltipDescription_SetSheetProtectionForLockedCellsWithAnOptionalPassword"));
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
                UiText.Get("Protection_UnprotectSheetTitle"),
                UiText.Get("Protection_SheetUnprotectedMessage"),
                []);
        }

        return new SheetProtectionAction(
            new ProtectSheetCommand(
                sheet.Id,
                result.Password,
                ProtectionDialogPlanner.ParseSheetPermissions(result.SelectedSheetPermissions)),
            UiText.Get("MainWindowMessage_ProtectSheetTitle"),
            UiText.Get("Protection_SheetProtectedMessage"),
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
