namespace FreeP.Core.Model;

/// <summary>
/// Read-time snapshot of package entries that are outside the current semantic
/// model. The IO layer uses it as a preserve bag during save.
/// </summary>
public sealed class PptxPackageSnapshot
{
    private readonly Dictionary<string, byte[]> _entries;

    public PptxPackageSnapshot(IEnumerable<KeyValuePair<string, byte[]>> entries)
    {
        _entries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
                continue;

            var normalizedPath = NormalizePath(entry.Key);
            _entries[normalizedPath] = entry.Value.ToArray();
        }
    }

    public IReadOnlyDictionary<string, byte[]> Entries => _entries;

    public bool TryGetEntry(string path, out byte[] bytes) =>
        _entries.TryGetValue(NormalizePath(path), out bytes!);

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');
}
