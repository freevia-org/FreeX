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
            ToSharedDescriptors(GetFormats(adapters, static format => format.CanOpen)),
            allSupportedName);

    public static FileSaveDialogPlan BuildSaveDialogPlan(
        IEnumerable<IDocumentFileAdapter> adapters,
        string suggestedFileName,
        string defaultExtensionWithDot) =>
        FileDialogRequestPlanner.BuildSaveDialogPlan(
            ToSharedDescriptors(GetFormats(adapters, static format => format.CanSave)),
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
            ToSharedDescriptors(GetFormats(adapters, static format => format.CanOpen)),
            allSupportedName);

    public static FileSavePickerPlan BuildSavePickerPlan(
        IEnumerable<IDocumentFileAdapter> adapters,
        string? sourceName,
        string fallbackDisplayName,
        string defaultExtensionWithDot,
        string? preferredFirstExtension = null) =>
        FileDialogRequestPlanner.BuildSavePickerPlan(
            ToSharedDescriptors(GetFormats(adapters, static format => format.CanSave)),
            sourceName,
            fallbackDisplayName,
            defaultExtensionWithDot,
            preferredFirstExtension);

    private static List<FileFormatDescriptor> GetFormats(
        IEnumerable<IDocumentFileAdapter> adapters,
        Func<FileFormatDescriptor, bool> predicate) =>
        adapters.SelectMany(adapter => adapter.Formats).Where(predicate).ToList();

    private static IEnumerable<FileDialogFormatDescriptor> ToSharedDescriptors(IEnumerable<FileFormatDescriptor> formats) =>
        formats.Select(ToSharedDescriptor);

    private static FileDialogFormatDescriptor ToSharedDescriptor(FileFormatDescriptor format) =>
        new(format.Extension, format.FormatName, format.CanOpen, format.CanSave);
}
