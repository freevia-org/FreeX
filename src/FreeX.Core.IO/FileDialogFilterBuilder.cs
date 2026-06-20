using SharedFileDialogFilterBuilder = Free.Shared.IO.FileDialogFilterBuilder;
using SharedFileDialogFormatDescriptor = Free.Shared.IO.FileDialogFormatDescriptor;
using SharedFileDialogPickerTypeDescriptor = Free.Shared.IO.FileDialogPickerTypeDescriptor;

namespace FreeX.Core.IO;

public static class FileDialogFilterBuilder
{
    public static string BuildOpenFilter(IEnumerable<IFileAdapter> adapters) =>
        SharedFileDialogFilterBuilder.BuildOpenFilter(
            GetFormats(adapters, static format => format.CanOpen).Select(ToSharedDescriptor));

    public static string BuildSaveFilter(IEnumerable<IFileAdapter> adapters) =>
        SharedFileDialogFilterBuilder.BuildSaveFilter(
            GetFormats(adapters, static format => format.CanSave).Select(ToSharedDescriptor));

    public static IReadOnlyList<FilePickerTypeDescriptor> BuildOpenPickerTypes(
        IEnumerable<IFileAdapter> adapters,
        string allSupportedName = "All supported files") =>
        BuildOpenPickerTypes(
            GetFormats(adapters, static format => format.CanOpen),
            allSupportedName);

    public static IReadOnlyList<FilePickerTypeDescriptor> BuildOpenPickerTypes(
        IEnumerable<FileFormatDescriptor> formats,
        string allSupportedName = "All supported files") =>
        MapPickerTypes(
            SharedFileDialogFilterBuilder.BuildOpenPickerTypes(
                formats.Select(ToSharedDescriptor),
                allSupportedName));

    public static IReadOnlyList<FilePickerTypeDescriptor> BuildSavePickerTypes(
        IEnumerable<IFileAdapter> adapters,
        string? preferredFirstExtension = null) =>
        BuildSavePickerTypes(
            GetFormats(adapters, static format => format.CanSave),
            preferredFirstExtension);

    public static IReadOnlyList<FilePickerTypeDescriptor> BuildSavePickerTypes(
        IEnumerable<FileFormatDescriptor> formats,
        string? preferredFirstExtension = null) =>
        MapPickerTypes(
            SharedFileDialogFilterBuilder.BuildSavePickerTypes(
                formats.Select(ToSharedDescriptor),
                preferredFirstExtension));

    public static int FindSaveFilterIndex(IEnumerable<IFileAdapter> adapters, string extension) =>
        SharedFileDialogFilterBuilder.FindSaveFilterIndex(
            GetFormats(adapters, static format => format.CanSave).Select(ToSharedDescriptor),
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

    private static List<FileFormatDescriptor> GetFormats(
        IEnumerable<IFileAdapter> adapters,
        Func<FileFormatDescriptor, bool> predicate) =>
        adapters.SelectMany(adapter => adapter.Formats).Where(predicate).ToList();

    private static SharedFileDialogFormatDescriptor ToSharedDescriptor(FileFormatDescriptor format) =>
        new(format.Extension, format.FormatName, format.CanOpen, format.CanSave);

    private static IReadOnlyList<FilePickerTypeDescriptor> MapPickerTypes(
        IEnumerable<SharedFileDialogPickerTypeDescriptor> descriptors) =>
        descriptors
            .Select(descriptor => new FilePickerTypeDescriptor(descriptor.DisplayName, descriptor.Patterns))
            .ToList();
}
