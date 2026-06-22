using Avalonia.Platform.Storage;
using Free.Shared.IO;
using FreeW.Core.IO;

namespace FreeW.App.Avalonia;

/// <summary>
/// Builds Avalonia <see cref="FilePickerFileType"/> filters from the
/// <see cref="DocumentFileAdapterCatalog"/> so the shell's Open/Save dialogs expose every catalog format
/// rather than a hard-coded <c>.docx</c> entry. Pure data transform (no UI thread, no storage provider) so
/// it can be unit-tested directly.
/// </summary>
internal static class DocumentFilePickerTypes
{
    /// <summary>
    /// One <see cref="FilePickerFileType"/> per <see cref="FileFormatDescriptor.CanOpen"/> format, preceded
    /// by an "All supported documents" group whose patterns are the union of every openable extension.
    /// </summary>
    public static IReadOnlyList<FilePickerFileType> BuildOpenTypes(IEnumerable<IDocumentFileAdapter> adapters)
    {
        return DocumentFileDialogRequestPlanner
            .BuildOpenPickerPlan(adapters)
            .FileTypes
            .Select(ToFileType)
            .ToList();
    }

    /// <summary>One <see cref="FilePickerFileType"/> per <see cref="FileFormatDescriptor.CanSave"/> format.</summary>
    public static IReadOnlyList<FilePickerFileType> BuildSaveTypes(IEnumerable<IDocumentFileAdapter> adapters) =>
        DocumentFileDialogRequestPlanner
            .BuildSavePickerPlan(adapters, sourceName: null, fallbackDisplayName: "Document", defaultExtensionWithDot: ".docx")
            .FileTypes
            .Select(ToFileType)
            .ToList();

    internal static FilePickerFileType ToFileType(FileDialogPickerTypeDescriptor descriptor) =>
        new(descriptor.DisplayName) { Patterns = descriptor.Patterns };
}
