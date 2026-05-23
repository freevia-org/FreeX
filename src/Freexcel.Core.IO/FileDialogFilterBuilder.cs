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
                .Select(format => NormalizeExtension(format.Extension))
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
        return FindAdapter(adapters, extension, candidate => candidate.CanOpen, out format);
    }

    public static IFileAdapter? FindSaveAdapter(
        IEnumerable<IFileAdapter> adapters,
        string extension,
        out FileFormatDescriptor? format)
    {
        return FindAdapter(adapters, extension, candidate => candidate.CanSave, out format);
    }

    private static IFileAdapter? FindAdapter(
        IEnumerable<IFileAdapter> adapters,
        string extension,
        Func<FileFormatDescriptor, bool> predicate,
        out FileFormatDescriptor? format)
    {
        var normalizedExtension = NormalizeExtension(extension);
        foreach (var adapter in adapters)
        {
            format = adapter.Formats.FirstOrDefault(candidate =>
                predicate(candidate) &&
                string.Equals(NormalizeExtension(candidate.Extension), normalizedExtension, StringComparison.OrdinalIgnoreCase));
            if (format is not null)
                return adapter;
        }

        format = null;
        return null;
    }

    private static string NormalizeExtension(string extension)
    {
        extension = extension.Trim();
        return extension.StartsWith(".", StringComparison.Ordinal)
            ? extension
            : $".{extension}";
    }
}
