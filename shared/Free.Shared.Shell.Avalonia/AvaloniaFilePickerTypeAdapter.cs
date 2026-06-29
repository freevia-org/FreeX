using Avalonia.Platform.Storage;
using Free.Shared.IO;

namespace Free.Shared.Shell.Avalonia;

public static class AvaloniaFilePickerTypeAdapter
{
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

        return new FilePickerFileType(descriptor.DisplayName)
        {
            Patterns = descriptor.Patterns.ToArray(),
        };
    }
}
