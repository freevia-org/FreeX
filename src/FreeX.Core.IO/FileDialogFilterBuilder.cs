using FileFormatDialogDescriptorAdapter = Free.Shared.IO.FileFormatDialogDescriptorAdapter;
using FileDialogPickerTypeDescriptor = Free.Shared.IO.FileDialogPickerTypeDescriptor;
using SharedFileDialogFilterBuilder = Free.Shared.IO.FileDialogFilterBuilder;

namespace FreeX.Core.IO;

public static class FileDialogFilterBuilder
{
    public static string BuildOpenFilter(IEnumerable<IFileAdapter> adapters) =>
        SharedFileDialogFilterBuilder.BuildOpenFilter(
            FileFormatDialogDescriptorAdapter.ToOpenDialogDescriptors(GetFormats(adapters)));

    public static string BuildSaveFilter(IEnumerable<IFileAdapter> adapters) =>
        SharedFileDialogFilterBuilder.BuildSaveFilter(
            FileFormatDialogDescriptorAdapter.ToSaveDialogDescriptors(GetFormats(adapters)));

    public static IReadOnlyList<FileDialogPickerTypeDescriptor> BuildOpenPickerTypes(
        IEnumerable<IFileAdapter> adapters,
        string allSupportedName = "All supported files") =>
        BuildOpenPickerTypes(
            GetFormats(adapters).Where(static format => format.CanOpen),
            allSupportedName);

    public static IReadOnlyList<FileDialogPickerTypeDescriptor> BuildOpenPickerTypes(
        IEnumerable<FileFormatDescriptor> formats,
        string allSupportedName = "All supported files") =>
        SharedFileDialogFilterBuilder.BuildOpenPickerTypes(
            FileFormatDialogDescriptorAdapter.ToDialogDescriptors(formats),
            allSupportedName);

    public static IReadOnlyList<FileDialogPickerTypeDescriptor> BuildSavePickerTypes(
        IEnumerable<IFileAdapter> adapters,
        string? preferredFirstExtension = null) =>
        BuildSavePickerTypes(
            GetFormats(adapters).Where(static format => format.CanSave),
            preferredFirstExtension);

    public static IReadOnlyList<FileDialogPickerTypeDescriptor> BuildSavePickerTypes(
        IEnumerable<FileFormatDescriptor> formats,
        string? preferredFirstExtension = null) =>
        SharedFileDialogFilterBuilder.BuildSavePickerTypes(
            FileFormatDialogDescriptorAdapter.ToDialogDescriptors(formats),
            preferredFirstExtension);

    public static int FindSaveFilterIndex(IEnumerable<IFileAdapter> adapters, string extension) =>
        SharedFileDialogFilterBuilder.FindSaveFilterIndex(
            FileFormatDialogDescriptorAdapter.ToSaveDialogDescriptors(GetFormats(adapters)),
            extension);

    public static IFileAdapter? FindOpenAdapter(
        IEnumerable<IFileAdapter> adapters,
        string extension,
        out FileFormatDescriptor? format)
    {
        return FileFormatResolver.FindOpenAdapter(adapters, extension, out format);
    }

    public static IFileAdapter? FindSaveAdapter(
        IEnumerable<IFileAdapter> adapters,
        string extension,
        out FileFormatDescriptor? format)
    {
        return FileFormatResolver.FindSaveAdapter(adapters, extension, out format);
    }

    public static string SafeFileTypeFromExtension(string extension) =>
        FileFormatResolver.SafeFileTypeFromExtension(extension);

    private static List<FileFormatDescriptor> GetFormats(IEnumerable<IFileAdapter> adapters) =>
        adapters.SelectMany(adapter => adapter.Formats).ToList();
}
