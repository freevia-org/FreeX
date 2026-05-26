namespace Freexcel.Core.IO;

public static class FileDialogFilterBuilder
{
    public static string BuildOpenFilter(IEnumerable<IFileAdapter> adapters)
    {
        var formats = adapters
            .SelectMany(adapter => adapter.Formats)
            .Where(format => format.CanOpen)
            .ToList();

        var parts = new List<string>();

        if (formats.Count > 0)
        {
            var allSupported = string.Join(';', formats
                .Select(format => FileFormatResolver.NormalizeExtension(format.Extension))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(extension => $"*{extension}"));
            parts.Add($"All supported files ({allSupported})|{allSupported}");
        }

        parts.AddRange(formats.Select(format => $"{format.FormatName} (*{format.Extension})|*{format.Extension}"));
        parts.Add("All files (*.*)|*.*");
        return string.Join('|', parts);
    }

    public static string BuildSaveFilter(IEnumerable<IFileAdapter> adapters)
    {
        var parts = adapters
            .SelectMany(adapter => adapter.Formats)
            .Where(format => format.CanSave)
            .Select(format => $"{format.FormatName} (*{format.Extension})|*{format.Extension}");

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
}
