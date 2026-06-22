using Free.Shared.IO;

namespace FreeW.Core.IO;

/// <summary>
/// Builds Win32 and platform-neutral file-picker filters from document adapters, so dialogs stay a pure
/// function of the registered formats.
/// </summary>
public static class DocumentFileDialogFilterBuilder
{
    public static string BuildOpenFilter(IEnumerable<IDocumentFileAdapter> adapters) =>
        DocumentFileDialogRequestPlanner.BuildOpenDialogPlan(adapters).Filter;

    public static string BuildSaveFilter(IEnumerable<IDocumentFileAdapter> adapters) =>
        DocumentFileDialogRequestPlanner.BuildSaveDialogPlan(adapters, "", ".docx").Filter;

    public static IReadOnlyList<FileDialogPickerTypeDescriptor> BuildOpenPickerTypes(
        IEnumerable<IDocumentFileAdapter> adapters,
        string allSupportedName = "All supported files") =>
        DocumentFileDialogRequestPlanner.BuildOpenPickerPlan(adapters, allSupportedName).FileTypes;

    public static IReadOnlyList<FileDialogPickerTypeDescriptor> BuildSavePickerTypes(
        IEnumerable<IDocumentFileAdapter> adapters) =>
        DocumentFileDialogRequestPlanner
            .BuildSavePickerPlan(adapters, sourceName: null, fallbackDisplayName: "Document", defaultExtensionWithDot: ".docx")
            .FileTypes;

    /// <summary>
    /// 1-based index of the save filter row whose extension matches <paramref name="extension"/>, or 1 when
    /// there is no match (so a Save-As of an unknown/empty current path defaults to the first format).
    /// </summary>
    public static int FindSaveFilterIndex(IEnumerable<IDocumentFileAdapter> adapters, string extension) =>
        DocumentFileDialogRequestPlanner.BuildSaveDialogPlan(adapters, "", extension).FilterIndex;
}
