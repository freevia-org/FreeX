namespace FreeX.App.Presentation.Dialogs;

public enum NormalizedOverwriteTargetKind
{
    Pdf,
    Workbook,
}

public sealed record NormalizedOverwritePromptSpec(
    string FileName,
    string WindowTitleResourceKey,
    string FileExistsFormatResourceKey,
    string DetailResourceKey,
    string ReplaceButtonResourceKey,
    string CancelButtonResourceKey,
    string ReplaceHelpTextResourceKey,
    string CancelHelpTextResourceKey,
    string ReplaceButtonAutomationId,
    string CancelButtonAutomationId);

public static class NormalizedOverwritePromptPlanner
{
    public static NormalizedOverwritePromptSpec Build(
        NormalizedOverwriteTargetKind targetKind,
        string normalizedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedPath);

        var fileName = Path.GetFileName(normalizedPath);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = normalizedPath;

        return targetKind switch
        {
            NormalizedOverwriteTargetKind.Pdf => new NormalizedOverwritePromptSpec(
                fileName,
                "NormalizedOverwrite_ReplacePdfTitle",
                "NormalizedOverwrite_FileAlreadyExistsFormat",
                "NormalizedOverwrite_PdfDetail",
                "ShellLoc_ReplaceButton",
                "Common_Cancel",
                "NormalizedOverwrite_ReplacePdfHelpText",
                "NormalizedOverwrite_CancelPdfHelpText",
                "PdfExportOverwriteReplaceButton",
                "PdfExportOverwriteCancelButton"),
            NormalizedOverwriteTargetKind.Workbook => new NormalizedOverwritePromptSpec(
                fileName,
                "NormalizedOverwrite_ReplaceWorkbookTitle",
                "NormalizedOverwrite_FileAlreadyExistsFormat",
                "NormalizedOverwrite_WorkbookDetail",
                "ShellLoc_ReplaceButton",
                "Common_Cancel",
                "NormalizedOverwrite_ReplaceWorkbookHelpText",
                "NormalizedOverwrite_CancelWorkbookHelpText",
                "WorkbookSaveOverwriteReplaceButton",
                "WorkbookSaveOverwriteCancelButton"),
            _ => throw new ArgumentOutOfRangeException(nameof(targetKind), targetKind, null),
        };
    }
}
