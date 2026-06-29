using FreeX.Core.IO;
using FileFormatDialogDescriptorAdapter = Free.Shared.IO.FileFormatDialogDescriptorAdapter;
using FileDialogRequestPlanner = Free.Shared.IO.FileDialogRequestPlanner;
using FileDialogPickerTypeDescriptor = Free.Shared.IO.FileDialogPickerTypeDescriptor;

namespace FreeX.App.Services;

public sealed record WorkbookOpenDialogPlan(string Filter, string DefaultExtensionWithDot);

public sealed record WorkbookSaveDialogPlan(
    string Filter,
    string SuggestedFileName,
    string DefaultExtensionWithDot,
    int FilterIndex);

public sealed record WorkbookOpenPickerPlan(IReadOnlyList<FileDialogPickerTypeDescriptor> FileTypes);

public sealed record WorkbookSavePickerPlan(
    IReadOnlyList<FileDialogPickerTypeDescriptor> FileTypes,
    string SuggestedFileName,
    string DefaultExtensionWithoutDot);

/// <summary>
/// UI-free planning for workbook open/save picker requests. WPF and Avalonia still build native dialogs;
/// this owns the format ordering, default-extension, and suggested-file-name decisions.
/// </summary>
public static class WorkbookFilePickerPlanner
{
    public const string AllSupportedWorkbooksName = "All supported workbooks";

    public static WorkbookOpenDialogPlan BuildOpenDialogPlan(IEnumerable<IFileAdapter> adapters)
    {
        var plan = FileDialogRequestPlanner.BuildOpenDialogPlan(
            FileFormatDialogDescriptorAdapter.ToOpenDialogDescriptors(GetFormats(adapters)));
        return new WorkbookOpenDialogPlan(plan.Filter, plan.DefaultExtensionWithDot);
    }

    public static WorkbookSaveDialogPlan BuildSaveDialogPlan(
        IEnumerable<IFileAdapter> adapters,
        string workbookName,
        string? preferredDefaultFormat)
    {
        var defaultExtension = ResolveSaveDialogDefaultExtension(adapters, preferredDefaultFormat);
        var plan = FileDialogRequestPlanner.BuildSaveDialogPlan(
            FileFormatDialogDescriptorAdapter.ToSaveDialogDescriptors(GetFormats(adapters)),
            workbookName,
            defaultExtension);
        return new WorkbookSaveDialogPlan(
            plan.Filter,
            plan.SuggestedFileName,
            plan.DefaultExtensionWithDot,
            plan.FilterIndex);
    }

    public static bool TryResolveSaveDialogTarget(
        IEnumerable<IFileAdapter> adapters,
        string path,
        out FileSaveTarget? target) =>
        FileSavePlanner.TryResolveExistingPath(path, adapters, out target);

    public static WorkbookOpenPickerPlan BuildOpenPickerPlan(IEnumerable<FileFormatDescriptor> openFormats)
    {
        var plan = FileDialogRequestPlanner.BuildOpenPickerPlan(
            FileFormatDialogDescriptorAdapter.ToDialogDescriptors(openFormats),
            AllSupportedWorkbooksName);
        return new WorkbookOpenPickerPlan(plan.FileTypes);
    }

    public static WorkbookSavePickerPlan BuildSavePickerPlan(
        IEnumerable<FileFormatDescriptor> saveFormats,
        string sourceName,
        string fallbackDisplayName,
        string preferredExtension)
    {
        var normalizedExtension = FileFormatResolver.NormalizeExtension(preferredExtension);
        var plan = FileDialogRequestPlanner.BuildSavePickerPlan(
            FileFormatDialogDescriptorAdapter.ToDialogDescriptors(saveFormats),
            sourceName,
            fallbackDisplayName,
            normalizedExtension,
            preferredFirstExtension: normalizedExtension);
        return new WorkbookSavePickerPlan(
            plan.FileTypes,
            plan.SuggestedFileName,
            plan.DefaultExtensionWithoutDot);
    }

    public static string BuildSuggestedSaveAsFileName(
        string? sourceName,
        string fallbackDisplayName,
        string defaultExtension)
    {
        var normalizedExtension = FileFormatResolver.NormalizeExtension(defaultExtension);
        var effectiveFallbackDisplayName = string.IsNullOrWhiteSpace(sourceName) &&
            string.IsNullOrWhiteSpace(fallbackDisplayName)
            ? "Workbook"
            : fallbackDisplayName;
        return FileDialogRequestPlanner.BuildSuggestedSaveAsFileName(
            sourceName,
            effectiveFallbackDisplayName,
            normalizedExtension);
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

    private static List<FileFormatDescriptor> GetFormats(IEnumerable<IFileAdapter> adapters) =>
        adapters.SelectMany(adapter => adapter.Formats).ToList();
}
