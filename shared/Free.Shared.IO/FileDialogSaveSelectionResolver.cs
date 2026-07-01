namespace Free.Shared.IO;

/// <summary>
/// Resolves the save adapter behind a native Save dialog result. When multiple save formats share
/// an extension, the selected filter row wins only if it still matches the filename extension the
/// user typed; otherwise extension resolution remains authoritative.
/// </summary>
public static class FileDialogSaveSelectionResolver
{
    public static TAdapter? ResolveAdapter<TAdapter>(
        IEnumerable<TAdapter> adapters,
        Func<TAdapter, IEnumerable<FileFormatDescriptor>> getFormats,
        Func<IEnumerable<TAdapter>, string, TAdapter?> resolveByExtension,
        string chosenExtension,
        int filterIndex)
        where TAdapter : class
    {
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(getFormats);
        ArgumentNullException.ThrowIfNull(resolveByExtension);

        var adapterRows = adapters.ToList();
        var selectedFormat = FindSelectedSaveFormat(adapterRows, getFormats, filterIndex);
        if (selectedFormat is not null &&
            AdapterSupportsExtension(selectedFormat.Adapter, getFormats, chosenExtension))
        {
            return selectedFormat.Adapter;
        }

        return resolveByExtension(adapterRows, chosenExtension);
    }

    private static SaveFormatSelection<TAdapter>? FindSelectedSaveFormat<TAdapter>(
        IReadOnlyList<TAdapter> adapters,
        Func<TAdapter, IEnumerable<FileFormatDescriptor>> getFormats,
        int filterIndex)
        where TAdapter : class
    {
        var index = filterIndex - 1;
        if (index < 0)
            return null;

        var saveIndex = 0;
        foreach (var adapter in adapters)
        {
            foreach (var format in getFormats(adapter))
            {
                if (!format.CanSave)
                    continue;

                if (saveIndex == index)
                    return new SaveFormatSelection<TAdapter>(adapter, format.Extension);

                saveIndex++;
            }
        }

        return null;
    }

    private static bool ExtensionsMatch(string candidateExtension, string chosenExtension) =>
        string.Equals(
            FileDialogFilterBuilder.NormalizeExtension(candidateExtension),
            FileDialogFilterBuilder.NormalizeExtension(chosenExtension),
            StringComparison.OrdinalIgnoreCase);

    private static bool AdapterSupportsExtension<TAdapter>(
        TAdapter adapter,
        Func<TAdapter, IEnumerable<FileFormatDescriptor>> getFormats,
        string chosenExtension)
        where TAdapter : class =>
        getFormats(adapter).Any(format => format.CanSave && ExtensionsMatch(format.Extension, chosenExtension));

    private sealed record SaveFormatSelection<TAdapter>(TAdapter Adapter, string Extension)
        where TAdapter : class;
}
