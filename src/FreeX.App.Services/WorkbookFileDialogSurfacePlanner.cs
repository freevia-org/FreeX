using FreeX.Core.IO;

namespace FreeX.App.Services;

public enum WorkbookFileDialogSurfaceKind
{
    Open,
    SaveAs
}

public sealed record WorkbookFileDialogTypeRow(string DisplayName, IReadOnlyList<string> Patterns);

public sealed record WorkbookFileDialogSurfacePlan(
    WorkbookFileDialogSurfaceKind Kind,
    string Title,
    string PrimaryCommandText,
    string FileNameLabel,
    string FileName,
    string FileTypeLabel,
    string DefaultExtension,
    IReadOnlyList<WorkbookFileDialogTypeRow> FileTypes)
{
    public string DialogAutomationId =>
        Kind == WorkbookFileDialogSurfaceKind.Open
            ? WorkbookFileDialogSurfacePlanner.OpenDialogAutomationId
            : WorkbookFileDialogSurfacePlanner.SaveAsDialogAutomationId;
}

public static class WorkbookFileDialogSurfacePlanner
{
    public const double Width = 640;
    public const double Height = 420;
    public const string OpenDialogAutomationId = "OpenWorkbookDialog";
    public const string SaveAsDialogAutomationId = "SaveAsWorkbookDialog";
    public const string FileNameBoxAutomationId = "WorkbookFileDialogFileNameBox";
    public const string FileTypeBoxAutomationId = "WorkbookFileDialogFileTypeBox";

    public static WorkbookFileDialogSurfacePlan CreateOpenPlan(WorkbookOpenPickerPlan pickerPlan) =>
        new(
            WorkbookFileDialogSurfaceKind.Open,
            Title: "Open Workbook",
            PrimaryCommandText: "Open",
            FileNameLabel: "File name:",
            FileName: "",
            FileTypeLabel: "File type:",
            DefaultExtension: "",
            FileTypes: ToRows(pickerPlan.FileTypes));

    public static WorkbookFileDialogSurfacePlan CreateSaveAsPlan(WorkbookSavePickerPlan pickerPlan) =>
        new(
            WorkbookFileDialogSurfaceKind.SaveAs,
            Title: "Save Workbook",
            PrimaryCommandText: "Save",
            FileNameLabel: "File name:",
            FileName: pickerPlan.SuggestedFileName,
            FileTypeLabel: "Save as type:",
            DefaultExtension: pickerPlan.DefaultExtensionWithoutDot,
            FileTypes: ToRows(pickerPlan.FileTypes));

    private static IReadOnlyList<WorkbookFileDialogTypeRow> ToRows(IReadOnlyList<FilePickerTypeDescriptor> fileTypes) =>
        fileTypes
            .Select(type => new WorkbookFileDialogTypeRow(type.DisplayName, type.Patterns))
            .ToArray();
}
