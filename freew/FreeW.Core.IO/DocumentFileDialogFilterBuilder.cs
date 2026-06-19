namespace FreeW.Core.IO;

/// <summary>
/// Builds the Win32 <c>OpenFileDialog</c>/<c>SaveFileDialog</c> filter strings from a set of document
/// adapters, so the dialogs stay a pure function of the registered formats. Ported from the sibling FreeX
/// app's filter builder (retyped to the document adapter); the open filter leads with an "All supported
/// files" row and ends with "All files", while the save filter lists only writable formats.
/// </summary>
public static class DocumentFileDialogFilterBuilder
{
    private const string AllFilesFilterEntry = "All files (*.*)|*.*";

    public static string BuildOpenFilter(IEnumerable<IDocumentFileAdapter> adapters)
    {
        var formats = GetFormats(adapters, static format => format.CanOpen);
        return BuildFilter(formats, includeAllSupported: true, includeAllFiles: true);
    }

    public static string BuildSaveFilter(IEnumerable<IDocumentFileAdapter> adapters)
    {
        var formats = GetFormats(adapters, static format => format.CanSave);
        return BuildFilter(formats, includeAllSupported: false, includeAllFiles: false);
    }

    /// <summary>
    /// 1-based index of the save filter row whose extension matches <paramref name="extension"/>, or 1 when
    /// there is no match (so a Save-As of an unknown/empty current path defaults to the first format).
    /// </summary>
    public static int FindSaveFilterIndex(IEnumerable<IDocumentFileAdapter> adapters, string extension)
    {
        var normalizedExtension = DocumentFileFormatResolver.NormalizeExtension(extension);
        if (normalizedExtension.Length == 0)
            return 1;

        var formats = GetFormats(adapters, static format => format.CanSave);
        for (var i = 0; i < formats.Count; i++)
        {
            if (string.Equals(
                    DocumentFileFormatResolver.NormalizeExtension(formats[i].Extension),
                    normalizedExtension,
                    StringComparison.OrdinalIgnoreCase))
            {
                return i + 1;
            }
        }

        return 1;
    }

    private static List<FileFormatDescriptor> GetFormats(
        IEnumerable<IDocumentFileAdapter> adapters,
        Func<FileFormatDescriptor, bool> predicate) =>
        adapters.SelectMany(adapter => adapter.Formats).Where(predicate).ToList();

    private static string BuildFilter(
        IReadOnlyCollection<FileFormatDescriptor> formats,
        bool includeAllSupported,
        bool includeAllFiles)
    {
        var parts = new List<string>(formats.Count + 2);

        if (includeAllSupported && formats.Count > 0)
            parts.Add(BuildAllSupportedFilterEntry(formats));

        parts.AddRange(formats.Select(BuildFormatFilterEntry));

        if (includeAllFiles)
            parts.Add(AllFilesFilterEntry);

        return string.Join('|', parts);
    }

    private static string BuildAllSupportedFilterEntry(IEnumerable<FileFormatDescriptor> formats)
    {
        var allSupported = string.Join(';', formats
            .Select(format => DocumentFileFormatResolver.NormalizeExtension(format.Extension))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(extension => $"*{extension}"));

        return $"All supported files ({allSupported})|{allSupported}";
    }

    private static string BuildFormatFilterEntry(FileFormatDescriptor format)
    {
        var extension = DocumentFileFormatResolver.NormalizeExtension(format.Extension);
        return $"{format.FormatName} (*{extension})|*{extension}";
    }
}
