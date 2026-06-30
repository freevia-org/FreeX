using Free.Shared.AppServices;
using Free.Shared.IO;

namespace FreeX.App.Services;

public sealed record WorkbookFileDialogSurfacePlan : FileDialogSurfacePlan
{
    public WorkbookFileDialogSurfacePlan(
        FileDialogSurfaceKind kind,
        string title,
        string primaryCommandText,
        string fileNameLabel,
        string fileName,
        string fileTypeLabel,
        string defaultExtension,
        IReadOnlyList<FileDialogSurfaceTypeRow> fileTypes,
        FileDialogSurfaceAutomationIds automationIds)
        : base(
            kind,
            title,
            primaryCommandText,
            fileNameLabel,
            fileName,
            fileTypeLabel,
            defaultExtension,
            fileTypes,
            automationIds)
    {
    }

    internal WorkbookFileDialogSurfacePlan(FileDialogSurfacePlan plan)
        : this(
            plan.Kind,
            plan.Title,
            plan.PrimaryCommandText,
            plan.FileNameLabel,
            plan.FileName,
            plan.FileTypeLabel,
            plan.DefaultExtension,
            plan.FileTypes,
            plan.AutomationIds)
    {
    }
}

public static class WorkbookFileDialogSurfacePlanner
{
    public const double Width = FileDialogSurfacePlanner.Width;
    public const double Height = FileDialogSurfacePlanner.Height;
    public const string OpenDialogAutomationId = "OpenWorkbookDialog";
    public const string SaveAsDialogAutomationId = "SaveAsWorkbookDialog";
    public const string FileNameBoxAutomationId = "WorkbookFileDialogFileNameBox";
    public const string FileTypeBoxAutomationId = "WorkbookFileDialogFileTypeBox";

    private static readonly FileDialogSurfaceAutomationIds AutomationIds = new(
        OpenDialogAutomationId,
        SaveAsDialogAutomationId,
        FileNameBoxAutomationId,
        FileTypeBoxAutomationId);

    private static readonly FileDialogSurfaceChrome OpenChrome = new(
        Title: "Open Workbook",
        PrimaryCommandText: "Open",
        FileNameLabel: "File name:",
        FileTypeLabel: "File type:");

    private static readonly FileDialogSurfaceChrome SaveAsChrome = new(
        Title: "Save Workbook",
        PrimaryCommandText: "Save",
        FileNameLabel: "File name:",
        FileTypeLabel: "Save as type:");

    public static WorkbookFileDialogSurfacePlan CreateOpenPlan(FileOpenPickerPlan pickerPlan) =>
        new(FileDialogSurfacePlanner.CreateOpenPlan(OpenChrome, pickerPlan.FileTypes, AutomationIds));

    public static WorkbookFileDialogSurfacePlan CreateSaveAsPlan(FileSavePickerPlan pickerPlan) =>
        new(FileDialogSurfacePlanner.CreateSaveAsPlan(
            SaveAsChrome,
            pickerPlan.FileTypes,
            pickerPlan.SuggestedFileName,
            pickerPlan.DefaultExtensionWithoutDot,
            AutomationIds));
}
