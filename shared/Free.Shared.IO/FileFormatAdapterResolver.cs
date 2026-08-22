namespace Free.Shared.IO;

/// <summary>
/// Resolves the first registered adapter and descriptor that support a normalized file extension.
/// Adapter and descriptor enumeration order are both significant.
/// </summary>
public static class FileFormatAdapterResolver
{
    public static TAdapter? Find<TAdapter>(
        IEnumerable<TAdapter> adapters,
        Func<TAdapter, IEnumerable<FileFormatDescriptor>> getFormats,
        string extension,
        Func<FileFormatDescriptor, bool> predicate,
        out FileFormatDescriptor? format)
        where TAdapter : class
    {
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(getFormats);
        ArgumentNullException.ThrowIfNull(predicate);

        var normalizedExtension = FileDialogFilterBuilder.NormalizeExtension(extension);
        if (normalizedExtension.Length == 0)
        {
            format = null;
            return null;
        }

        foreach (var adapter in adapters)
        {
            foreach (var candidate in getFormats(adapter))
            {
                if (predicate(candidate) &&
                    string.Equals(
                        FileDialogFilterBuilder.NormalizeExtension(candidate.Extension),
                        normalizedExtension,
                        StringComparison.OrdinalIgnoreCase))
                {
                    format = candidate;
                    return adapter;
                }
            }
        }

        format = null;
        return null;
    }
}
