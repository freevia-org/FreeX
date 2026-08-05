using FreeX.Core.IO;
using FilePathPolicy = Free.Shared.IO.FilePathPolicy;
using FileFormatDialogDescriptorAdapter = Free.Shared.IO.FileFormatDialogDescriptorAdapter;
using FileOpenDialogPlan = Free.Shared.IO.FileOpenDialogPlan;
using FileOpenPickerPlan = Free.Shared.IO.FileOpenPickerPlan;
using FileDialogRequestPlanner = Free.Shared.IO.FileDialogRequestPlanner;
using FileDialogSaveSelectionResolver = Free.Shared.IO.FileDialogSaveSelectionResolver;
using FileSaveDialogPlan = Free.Shared.IO.FileSaveDialogPlan;
using FileSavePickerPlan = Free.Shared.IO.FileSavePickerPlan;

namespace FreeX.App.Services;

/// <summary>
/// UI-free planning for workbook open/save picker requests. WPF and Avalonia still build native dialogs;
/// this owns the format ordering, default-extension, and suggested-file-name decisions.
/// </summary>
public static class WorkbookFilePickerPlanner
{
    public const string AllSupportedWorkbooksName = "All supported workbooks";

    public static FileOpenDialogPlan BuildOpenDialogPlan(IEnumerable<IFileAdapter> adapters) =>
        FileDialogRequestPlanner.BuildOpenDialogPlan(
            FileFormatDialogDescriptorAdapter.ToOpenDialogDescriptors(GetFormats(adapters)));

    public static FileSaveDialogPlan BuildSaveDialogPlan(
        IEnumerable<IFileAdapter> adapters,
        string workbookName,
        string? preferredDefaultFormat)
    {
        var defaultExtension = ResolveSaveDialogDefaultExtension(adapters, preferredDefaultFormat);
        return FileDialogRequestPlanner.BuildSaveDialogPlan(
            FileFormatDialogDescriptorAdapter.ToSaveDialogDescriptors(GetFormats(adapters)),
            workbookName,
            defaultExtension);
    }

    public static bool TryResolveSaveDialogTarget(
        IEnumerable<IFileAdapter> adapters,
        string path,
        out FileSaveTarget? target) =>
        TryResolveSaveDialogTarget(adapters, path, filterIndex: 0, out target);

    public static bool TryResolveSaveDialogTarget(
        IEnumerable<IFileAdapter> adapters,
        string path,
        int filterIndex,
        out FileSaveTarget? target) =>
        TryResolveSaveDialogTargetCore(adapters, path, filterIndex, out target);

    public static FileOpenPickerPlan BuildOpenPickerPlan(IEnumerable<FileFormatDescriptor> openFormats) =>
        FileDialogRequestPlanner.BuildOpenPickerPlan(
            FileFormatDialogDescriptorAdapter.ToDialogDescriptors(openFormats),
            AllSupportedWorkbooksName);

    public static FileSavePickerPlan BuildSavePickerPlan(
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
        return plan;
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

    private static bool TryResolveSaveDialogTargetCore(
        IEnumerable<IFileAdapter> adapters,
        string path,
        int filterIndex,
        out FileSaveTarget? target)
    {
        target = null;
        var adapterRows = adapters.ToList();
        if (!FileSavePlanner.TryResolveExistingPath(path, adapterRows, out var resolvedTarget) ||
            resolvedTarget is null)
        {
            return false;
        }

        var chosenExtension = FilePathPolicy.GetExtensionOrEmpty(resolvedTarget.Path);
        var adapter = FileDialogSaveSelectionResolver.ResolveAdapter(
            adapterRows,
            static candidate => candidate.Formats,
            static (candidateAdapters, extension) => FileFormatResolver.FindSaveAdapter(candidateAdapters, extension, out _),
            chosenExtension,
            filterIndex);
        if (adapter is null)
            return false;

        target = new FileSaveTarget(resolvedTarget.Path, adapter);
        return true;
    }
}
