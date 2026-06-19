using System.Linq;
using Avalonia.Platform.Storage;
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
        var formats = adapters
            .SelectMany(a => a.Formats)
            .Where(f => f.CanOpen)
            .ToList();

        var perFormat = formats.Select(ToFileType).ToList();

        var allPatterns = formats
            .Select(f => Pattern(f.Extension))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = new List<FilePickerFileType>(perFormat.Count + 1);
        if (allPatterns.Length > 0)
            result.Add(new FilePickerFileType("All supported documents") { Patterns = allPatterns });
        result.AddRange(perFormat);
        return result;
    }

    /// <summary>One <see cref="FilePickerFileType"/> per <see cref="FileFormatDescriptor.CanSave"/> format.</summary>
    public static IReadOnlyList<FilePickerFileType> BuildSaveTypes(IEnumerable<IDocumentFileAdapter> adapters) =>
        adapters
            .SelectMany(a => a.Formats)
            .Where(f => f.CanSave)
            .Select(ToFileType)
            .ToList();

    private static FilePickerFileType ToFileType(FileFormatDescriptor format) =>
        new(format.FormatName) { Patterns = [Pattern(format.Extension)] };

    private static string Pattern(string extension)
    {
        var normalized = DocumentFileFormatResolver.NormalizeExtension(extension);
        return normalized.Length == 0 ? "*" : $"*{normalized}";
    }
}
