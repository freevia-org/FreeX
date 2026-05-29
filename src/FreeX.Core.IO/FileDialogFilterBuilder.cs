namespace FreeX.Core.IO;

public static class FileDialogFilterBuilder
{
    private const string AllFilesFilterEntry = "All files (*.*)|*.*";

    public static string BuildOpenFilter(IEnumerable<IFileAdapter> adapters)
    {
        var formats = GetFormats(adapters, static format => format.CanOpen);
        return BuildFilter(formats, includeAllSupported: true, includeAllFiles: true);
    }

    public static string BuildSaveFilter(IEnumerable<IFileAdapter> adapters)
    {
        var formats = GetFormats(adapters, static format => format.CanSave);
        return BuildFilter(formats, includeAllSupported: false, includeAllFiles: false);
    }

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
            .Select(format => FileFormatResolver.NormalizeExtension(format.Extension))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(extension => $"*{extension}"));

        return $"All supported files ({allSupported})|{allSupported}";
    }

    private static string BuildFormatFilterEntry(FileFormatDescriptor format)
    {
        var extension = FileFormatResolver.NormalizeExtension(format.Extension);
        return $"{format.FormatName} (*{extension})|*{extension}";
    }
}
