namespace FreeX.Core.IO;

public static class FileFormatResolver
{
    public static IFileAdapter? FindOpenAdapter(
        IEnumerable<IFileAdapter> adapters,
        string extension,
        out FileFormatDescriptor? format) =>
        FindAdapter(adapters, extension, candidate => candidate.CanOpen, out format);

    public static IFileAdapter? FindSaveAdapter(
        IEnumerable<IFileAdapter> adapters,
        string extension,
        out FileFormatDescriptor? format) =>
        FindAdapter(adapters, extension, candidate => candidate.CanSave, out format);

    public static string SafeFileTypeFromExtension(string extension)
    {
        var normalizedExtension = NormalizeExtension(extension);
        if (normalizedExtension.Length <= 1)
            return "unknown";

        var token = normalizedExtension[1..];
        return token.All(char.IsLetterOrDigit)
            ? token.ToLowerInvariant()
            : "unknown";
    }

    public static string NormalizeExtension(string extension)
    {
        extension = extension.Trim();
        if (extension.Length == 0)
            return "";

        if (extension.StartsWith("*.", StringComparison.Ordinal))
            extension = extension[1..];

        return extension.StartsWith(".", StringComparison.Ordinal)
            ? extension
            : $".{extension}";
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
}
