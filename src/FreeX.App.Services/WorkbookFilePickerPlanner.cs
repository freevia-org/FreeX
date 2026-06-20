using FreeX.Core.IO;

namespace FreeX.App.Services;

public sealed record WorkbookOpenDialogPlan(string Filter);

public sealed record WorkbookSaveDialogPlan(
    string Filter,
    string SuggestedFileName,
    string DefaultExtensionWithDot,
    int FilterIndex);

public sealed record WorkbookOpenPickerPlan(IReadOnlyList<FilePickerTypeDescriptor> FileTypes);

public sealed record WorkbookSavePickerPlan(
    IReadOnlyList<FilePickerTypeDescriptor> FileTypes,
    string SuggestedFileName,
    string DefaultExtensionWithoutDot);

/// <summary>
/// UI-free planning for workbook open/save picker requests. WPF and Avalonia still build native dialogs;
/// this owns the format ordering, default-extension, and suggested-file-name decisions.
/// </summary>
public static class WorkbookFilePickerPlanner
{
    public const string AllSupportedWorkbooksName = "All supported workbooks";

    public static WorkbookOpenDialogPlan BuildOpenDialogPlan(IEnumerable<IFileAdapter> adapters) =>
        new(FileDialogFilterBuilder.BuildOpenFilter(adapters));

    public static WorkbookSaveDialogPlan BuildSaveDialogPlan(
        IEnumerable<IFileAdapter> adapters,
        string workbookName,
        string? preferredDefaultFormat)
    {
        var defaultExtension = ResolveSaveDialogDefaultExtension(adapters, preferredDefaultFormat);
        return new WorkbookSaveDialogPlan(
            FileDialogFilterBuilder.BuildSaveFilter(adapters),
            workbookName,
            defaultExtension,
            FileDialogFilterBuilder.FindSaveFilterIndex(adapters, defaultExtension));
    }

    public static bool TryResolveSaveDialogTarget(
        IEnumerable<IFileAdapter> adapters,
        string path,
        out FileSaveTarget? target) =>
        FileSavePlanner.TryResolveExistingPath(path, adapters, out target);

    public static WorkbookOpenPickerPlan BuildOpenPickerPlan(IEnumerable<FileFormatDescriptor> openFormats) =>
        new(FileDialogFilterBuilder.BuildOpenPickerTypes(openFormats, AllSupportedWorkbooksName));

    public static WorkbookSavePickerPlan BuildSavePickerPlan(
        IEnumerable<FileFormatDescriptor> saveFormats,
        string sourceName,
        string fallbackDisplayName,
        string preferredExtension)
    {
        var normalizedExtension = FileFormatResolver.NormalizeExtension(preferredExtension);
        return new WorkbookSavePickerPlan(
            FileDialogFilterBuilder.BuildSavePickerTypes(saveFormats, preferredFirstExtension: normalizedExtension),
            BuildSuggestedSaveAsFileName(sourceName, fallbackDisplayName, normalizedExtension),
            normalizedExtension[1..]);
    }

    public static string BuildSuggestedSaveAsFileName(
        string? sourceName,
        string fallbackDisplayName,
        string defaultExtension)
    {
        var normalizedExtension = FileFormatResolver.NormalizeExtension(defaultExtension);
        var effectiveSourceName = string.IsNullOrWhiteSpace(sourceName)
            ? fallbackDisplayName
            : sourceName;
        var baseName = Path.GetFileNameWithoutExtension(effectiveSourceName);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "Workbook";

        return baseName + normalizedExtension;
    }

    public static string ResolveSaveDialogDefaultExtension(
        IEnumerable<IFileAdapter> adapters,
        string? preferredDefaultFormat)
    {
        var preferredExtension = AppOptions.NormalizeDefaultFormat(preferredDefaultFormat);
        return FileDialogFilterBuilder.FindSaveAdapter(adapters, preferredExtension, out _) is null
            ? AppOptions.XlsxDefaultFormat
            : preferredExtension;
    }
}
