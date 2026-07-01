using Free.Shared.IO;

namespace Free.Shared.AppServices;

public enum FileDialogSurfaceKind
{
    Open,
    SaveAs
}

public sealed record FileDialogSurfaceChrome(
    string Title,
    string PrimaryCommandText,
    string FileNameLabel,
    string FileTypeLabel);

public sealed record FileDialogSurfaceTypeRow(string DisplayName, IReadOnlyList<string> Patterns);

public sealed record FileDialogSurfaceAutomationIds(
    string OpenDialogAutomationId,
    string SaveAsDialogAutomationId,
    string FileNameBoxAutomationId,
    string FileTypeBoxAutomationId)
{
    public string DialogAutomationIdFor(FileDialogSurfaceKind kind) => kind switch
    {
        FileDialogSurfaceKind.Open => OpenDialogAutomationId,
        FileDialogSurfaceKind.SaveAs => SaveAsDialogAutomationId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}

public record FileDialogSurfacePlan(
    FileDialogSurfaceKind Kind,
    string Title,
    string PrimaryCommandText,
    string FileNameLabel,
    string FileName,
    string FileTypeLabel,
    string DefaultExtension,
    IReadOnlyList<FileDialogSurfaceTypeRow> FileTypes,
    FileDialogSurfaceAutomationIds AutomationIds)
{
    public string DialogAutomationId => AutomationIds.DialogAutomationIdFor(Kind);
}

public static class FileDialogSurfacePlanner
{
    public const double Width = 640;
    public const double Height = 420;

    public static FileDialogSurfacePlan CreateOpenPlan(
        FileDialogSurfaceChrome chrome,
        IReadOnlyList<FileDialogPickerTypeDescriptor> fileTypes,
        FileDialogSurfaceAutomationIds automationIds) =>
        CreatePlan(
            FileDialogSurfaceKind.Open,
            chrome,
            fileName: "",
            defaultExtension: "",
            fileTypes,
            automationIds);

    public static FileDialogSurfacePlan CreateSaveAsPlan(
        FileDialogSurfaceChrome chrome,
        IReadOnlyList<FileDialogPickerTypeDescriptor> fileTypes,
        string fileName,
        string defaultExtension,
        FileDialogSurfaceAutomationIds automationIds) =>
        CreatePlan(
            FileDialogSurfaceKind.SaveAs,
            chrome,
            fileName,
            defaultExtension,
            fileTypes,
            automationIds);

    private static FileDialogSurfacePlan CreatePlan(
        FileDialogSurfaceKind kind,
        FileDialogSurfaceChrome chrome,
        string fileName,
        string defaultExtension,
        IReadOnlyList<FileDialogPickerTypeDescriptor> fileTypes,
        FileDialogSurfaceAutomationIds automationIds) =>
        new(
            kind,
            chrome.Title,
            chrome.PrimaryCommandText,
            chrome.FileNameLabel,
            fileName,
            chrome.FileTypeLabel,
            defaultExtension,
            ToRows(fileTypes),
            automationIds);

    private static IReadOnlyList<FileDialogSurfaceTypeRow> ToRows(
        IReadOnlyList<FileDialogPickerTypeDescriptor> fileTypes) =>
        fileTypes
            .Select(type => new FileDialogSurfaceTypeRow(type.DisplayName, type.Patterns))
            .ToArray();
}
