using Free.Shared.IO;

namespace FreeW.Core.IO;

/// <summary>
/// Builds Win32 and platform-neutral file-picker filters from document adapters, so dialogs stay a pure
/// function of the registered formats.
/// </summary>
public static class DocumentFileDialogFilterBuilder
{
    public static string BuildOpenFilter(IEnumerable<IDocumentFileAdapter> adapters) =>
        FileDialogFilterBuilder.BuildOpenFilter(
            GetFormats(adapters, static format => format.CanOpen).Select(ToSharedDescriptor));

    public static string BuildSaveFilter(IEnumerable<IDocumentFileAdapter> adapters) =>
        FileDialogFilterBuilder.BuildSaveFilter(
            GetFormats(adapters, static format => format.CanSave).Select(ToSharedDescriptor));

    public static IReadOnlyList<FileDialogPickerTypeDescriptor> BuildOpenPickerTypes(
        IEnumerable<IDocumentFileAdapter> adapters,
        string allSupportedName = "All supported files") =>
        FileDialogFilterBuilder.BuildOpenPickerTypes(
            GetFormats(adapters, static format => format.CanOpen).Select(ToSharedDescriptor),
            allSupportedName);

    public static IReadOnlyList<FileDialogPickerTypeDescriptor> BuildSavePickerTypes(
        IEnumerable<IDocumentFileAdapter> adapters) =>
        FileDialogFilterBuilder.BuildSavePickerTypes(
            GetFormats(adapters, static format => format.CanSave).Select(ToSharedDescriptor));

    /// <summary>
    /// 1-based index of the save filter row whose extension matches <paramref name="extension"/>, or 1 when
    /// there is no match (so a Save-As of an unknown/empty current path defaults to the first format).
    /// </summary>
    public static int FindSaveFilterIndex(IEnumerable<IDocumentFileAdapter> adapters, string extension) =>
        FileDialogFilterBuilder.FindSaveFilterIndex(
            GetFormats(adapters, static format => format.CanSave).Select(ToSharedDescriptor),
            extension);

    private static List<FileFormatDescriptor> GetFormats(
        IEnumerable<IDocumentFileAdapter> adapters,
        Func<FileFormatDescriptor, bool> predicate) =>
        adapters.SelectMany(adapter => adapter.Formats).Where(predicate).ToList();

    private static FileDialogFormatDescriptor ToSharedDescriptor(FileFormatDescriptor format) =>
        new(format.Extension, format.FormatName, format.CanOpen, format.CanSave);
}
