using Free.Shared.IO;
using FreeX.Core.IO;

namespace FreeX.App.Services;

public sealed record WorkbookFileCommandReadinessPlan(
    bool CanContinue,
    string Message)
{
    public static WorkbookFileCommandReadinessPlan Ready { get; } = new(true, "");

    public static WorkbookFileCommandReadinessPlan Blocked(string message) => new(false, message);
}

public sealed record WorkbookOpenCommandPickerPlan(
    WorkbookFileCommandReadinessPlan Readiness,
    FileOpenPickerPlan Picker)
{
    public bool CanShowPicker => Readiness.CanContinue;

    public string Message => Readiness.Message;

    public IReadOnlyList<FileDialogPickerTypeDescriptor> FileTypes => Picker.FileTypes;
}

public sealed record WorkbookSaveAsCommandPickerPlan(
    WorkbookFileCommandReadinessPlan Readiness,
    FileSavePickerPlan Picker)
{
    public bool CanShowPicker => Readiness.CanContinue;

    public string Message => Readiness.Message;

    public IReadOnlyList<FileDialogPickerTypeDescriptor> FileTypes => Picker.FileTypes;

    public string SuggestedFileName => Picker.SuggestedFileName;

    public string DefaultExtensionWithoutDot => Picker.DefaultExtensionWithoutDot;
}

/// <summary>
/// UI-free command planning for FreeX workbook open/save workflows. Shells still own native picker
/// display, file-access permissions, progress rendering, and workbook I/O dispatch.
/// </summary>
public static class WorkbookFileCommandPlanner
{
    public const string OpenUnavailableMessage = "Open unavailable on this platform.";
    public const string NoOpenFormatsMessage = "No open formats are available.";
    public const string SaveAsUnavailableMessage = "Save As unavailable on this platform.";
    public const string NoSaveFormatsMessage = "No save formats are available.";

    public static WorkbookOpenCommandPickerPlan PlanOpenPicker(
        bool canOpen,
        IEnumerable<FileFormatDescriptor> openFormats)
    {
        ArgumentNullException.ThrowIfNull(openFormats);

        if (!canOpen)
            return new WorkbookOpenCommandPickerPlan(
                WorkbookFileCommandReadinessPlan.Blocked(OpenUnavailableMessage),
                new FileOpenPickerPlan([]));

        var picker = WorkbookFilePickerPlanner.BuildOpenPickerPlan(openFormats);
        return picker.FileTypes.Count == 0
            ? new WorkbookOpenCommandPickerPlan(
                WorkbookFileCommandReadinessPlan.Blocked(NoOpenFormatsMessage),
                picker)
            : new WorkbookOpenCommandPickerPlan(
                WorkbookFileCommandReadinessPlan.Ready,
                picker);
    }

    public static WorkbookSaveAsCommandPickerPlan PlanSaveAsPicker(
        bool canSave,
        IEnumerable<FileFormatDescriptor> saveFormats,
        string sourceName,
        string fallbackDisplayName,
        string preferredExtension)
    {
        ArgumentNullException.ThrowIfNull(saveFormats);

        if (!canSave)
            return new WorkbookSaveAsCommandPickerPlan(
                WorkbookFileCommandReadinessPlan.Blocked(SaveAsUnavailableMessage),
                EmptySavePicker(sourceName, fallbackDisplayName, preferredExtension));

        var picker = WorkbookFilePickerPlanner.BuildSavePickerPlan(
            saveFormats,
            sourceName,
            fallbackDisplayName,
            preferredExtension);
        return picker.FileTypes.Count == 0
            ? new WorkbookSaveAsCommandPickerPlan(
                WorkbookFileCommandReadinessPlan.Blocked(NoSaveFormatsMessage),
                picker)
            : new WorkbookSaveAsCommandPickerPlan(
                WorkbookFileCommandReadinessPlan.Ready,
                picker);
    }

    private static FileSavePickerPlan EmptySavePicker(
        string sourceName,
        string fallbackDisplayName,
        string preferredExtension)
    {
        var normalizedExtension = FileFormatResolver.NormalizeExtension(preferredExtension);
        var suggestedName = WorkbookFilePickerPlanner.BuildSuggestedSaveAsFileName(
            sourceName,
            fallbackDisplayName,
            preferredExtension);
        return new FileSavePickerPlan(
            [],
            suggestedName,
            normalizedExtension,
            normalizedExtension.TrimStart('.'));
    }
}
