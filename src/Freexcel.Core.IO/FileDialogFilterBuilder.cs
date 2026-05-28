namespace Freexcel.Core.IO;

public static class FileDialogFilterBuilder
{
    public static string BuildOpenFilter(IEnumerable<IFileAdapter> adapters)
    {
        var formats = GetFormats(adapters, static format => format.CanOpen).ToList();

        var parts = new List<string>();

        if (formats.Count > 0)
            parts.Add(BuildAllSupportedFilterEntry(formats));

        parts.AddRange(formats.Select(BuildFormatFilterEntry));
        parts.Add("All files (*.*)|*.*");
        return string.Join('|', parts);
    }

    public static string BuildSaveFilter(IEnumerable<IFileAdapter> adapters)
    {
        var parts = GetFormats(adapters, static format => format.CanSave)
            .Select(BuildFormatFilterEntry);

        return string.Join('|', parts);
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

    private static IEnumerable<FileFormatDescriptor> GetFormats(
        IEnumerable<IFileAdapter> adapters,
        Func<FileFormatDescriptor, bool> predicate) =>
        adapters.SelectMany(adapter => adapter.Formats).Where(predicate);

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
