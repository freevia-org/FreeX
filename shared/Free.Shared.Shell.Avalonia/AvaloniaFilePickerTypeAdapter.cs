using Avalonia.Platform.Storage;
using Free.Shared.IO;

namespace Free.Shared.Shell.Avalonia;

public static class AvaloniaFilePickerTypeAdapter
{
    public static FilePickerFileType CreateFileType(
        string displayName,
        IEnumerable<string> patterns,
        IEnumerable<string>? mimeTypes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(patterns);

        var fileType = new FilePickerFileType(displayName)
        {
            Patterns = patterns.ToArray(),
        };

        if (mimeTypes is not null)
            fileType.MimeTypes = mimeTypes.ToArray();

        return fileType;
    }

    public static IReadOnlyList<FilePickerFileType> ToFileTypes(
        IEnumerable<FileDialogPickerTypeDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        return descriptors
            .Select(ToFileType)
            .ToArray();
    }

    public static FilePickerFileType ToFileType(FileDialogPickerTypeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return CreateFileType(descriptor.DisplayName, descriptor.Patterns, descriptor.MimeTypes);
    }
}
