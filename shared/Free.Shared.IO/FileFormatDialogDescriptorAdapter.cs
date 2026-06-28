namespace Free.Shared.IO;

public static class FileFormatDialogDescriptorAdapter
{
    public static FileDialogFormatDescriptor ToDialogDescriptor(FileFormatDescriptor format)
    {
        ArgumentNullException.ThrowIfNull(format);

        return new FileDialogFormatDescriptor(
            format.Extension,
            format.FormatName,
            format.CanOpen,
            format.CanSave);
    }

    public static IReadOnlyList<FileDialogFormatDescriptor> ToDialogDescriptors(
        IEnumerable<FileFormatDescriptor> formats)
    {
        ArgumentNullException.ThrowIfNull(formats);

        return formats.Select(ToDialogDescriptor).ToList();
    }

    public static IReadOnlyList<FileDialogFormatDescriptor> ToOpenDialogDescriptors(
        IEnumerable<FileFormatDescriptor> formats)
    {
        ArgumentNullException.ThrowIfNull(formats);

        return formats
            .Where(static format => format.CanOpen)
            .Select(ToDialogDescriptor)
            .ToList();
    }

    public static IReadOnlyList<FileDialogFormatDescriptor> ToSaveDialogDescriptors(
        IEnumerable<FileFormatDescriptor> formats)
    {
        ArgumentNullException.ThrowIfNull(formats);

        return formats
            .Where(static format => format.CanSave)
            .Select(ToDialogDescriptor)
            .ToList();
    }
}
