using Free.Shared.IO;

namespace FreeW.Core.IO;

/// <summary>
/// FreeW adapter-catalog facade over the shared neutral file dialog request planner.
/// </summary>
public static class DocumentFileDialogRequestPlanner
{
    public const string AllSupportedDocumentsName = "All supported documents";

    public static FileOpenDialogPlan BuildOpenDialogPlan(
        IEnumerable<IDocumentFileAdapter> adapters,
        string allSupportedName = "All supported files") =>
        FileDialogRequestPlanner.BuildOpenDialogPlan(
            FileFormatDialogDescriptorAdapter.ToOpenDialogDescriptors(GetFormats(adapters)),
            allSupportedName);

    public static FileSaveDialogPlan BuildSaveDialogPlan(
        IEnumerable<IDocumentFileAdapter> adapters,
        string suggestedFileName,
        string defaultExtensionWithDot) =>
        FileDialogRequestPlanner.BuildSaveDialogPlan(
            FileFormatDialogDescriptorAdapter.ToSaveDialogDescriptors(GetFormats(adapters)),
            suggestedFileName,
            defaultExtensionWithDot);

    public static FileSaveDialogPlan BuildSaveDialogPlanFromSourceName(
        IEnumerable<IDocumentFileAdapter> adapters,
        string? sourceName,
        string fallbackDisplayName,
        string defaultExtensionWithDot) =>
        BuildSaveDialogPlan(
            adapters,
            FileDialogRequestPlanner.BuildSuggestedSaveAsFileName(
                sourceName,
                fallbackDisplayName,
                defaultExtensionWithDot),
            defaultExtensionWithDot);

    public static FileOpenPickerPlan BuildOpenPickerPlan(
        IEnumerable<IDocumentFileAdapter> adapters,
        string allSupportedName = AllSupportedDocumentsName) =>
        FileDialogRequestPlanner.BuildOpenPickerPlan(
            FileFormatDialogDescriptorAdapter.ToOpenDialogDescriptors(GetFormats(adapters)),
            allSupportedName);

    public static FileSavePickerPlan BuildSavePickerPlan(
        IEnumerable<IDocumentFileAdapter> adapters,
        string? sourceName,
        string fallbackDisplayName,
        string defaultExtensionWithDot,
        string? preferredFirstExtension = null) =>
        FileDialogRequestPlanner.BuildSavePickerPlan(
            FileFormatDialogDescriptorAdapter.ToSaveDialogDescriptors(GetFormats(adapters)),
            sourceName,
            fallbackDisplayName,
            defaultExtensionWithDot,
            preferredFirstExtension);

    private static List<FileFormatDescriptor> GetFormats(IEnumerable<IDocumentFileAdapter> adapters) =>
        adapters.SelectMany(adapter => adapter.Formats).ToList();
}
