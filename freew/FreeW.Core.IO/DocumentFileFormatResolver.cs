using SharedFileDialogFilterBuilder = Free.Shared.IO.FileDialogFilterBuilder;

namespace FreeW.Core.IO;

/// <summary>
/// Maps a file extension to the <see cref="IDocumentFileAdapter"/> that can open or save it, honouring each
/// format's <see cref="FileFormatDescriptor.CanOpen"/>/<see cref="FileFormatDescriptor.CanSave"/> flags.
/// Ported from the sibling FreeX app's resolver, retyped to the document adapter.
/// </summary>
public static class DocumentFileFormatResolver
{
    public static IDocumentFileAdapter? FindOpenAdapter(
        IEnumerable<IDocumentFileAdapter> adapters,
        string extension,
        out FileFormatDescriptor? format) =>
        FindAdapter(adapters, extension, static candidate => candidate.CanOpen, out format);

    public static IDocumentFileAdapter? FindSaveAdapter(
        IEnumerable<IDocumentFileAdapter> adapters,
        string extension,
        out FileFormatDescriptor? format) =>
        FindAdapter(adapters, extension, static candidate => candidate.CanSave, out format);

    /// <summary>
    /// Normalizes a user/path extension to a leading-dot form (<c>docx</c> / <c>*.docx</c> → <c>.docx</c>),
    /// returning "" for empty input. Comparison elsewhere is case-insensitive.
    /// </summary>
    public static string NormalizeExtension(string extension) =>
        SharedFileDialogFilterBuilder.NormalizeExtension(extension);

    private static IDocumentFileAdapter? FindAdapter(
        IEnumerable<IDocumentFileAdapter> adapters,
        string extension,
        Func<FileFormatDescriptor, bool> predicate,
        out FileFormatDescriptor? format)
    {
        var normalizedExtension = NormalizeExtension(extension);
        if (normalizedExtension.Length != 0)
        {
            foreach (var adapter in adapters)
            {
                foreach (var candidate in adapter.Formats)
                {
                    if (predicate(candidate) &&
                        string.Equals(NormalizeExtension(candidate.Extension), normalizedExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        format = candidate;
                        return adapter;
                    }
                }
            }
        }

        format = null;
        return null;
    }
}
