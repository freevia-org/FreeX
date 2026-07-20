using System.Text.Json;
using Free.Shared.AppServices;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.QuickParts;

/// <summary>
/// Cross-platform persistence wrapper for FreeW Quick Parts. Persistence is best-effort so an
/// unavailable or corrupt user-data store never interrupts document editing.
/// </summary>
public sealed class QuickPartLibrary
{
    private const string FileName = "quickparts.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly QuickPartStore _store = new();
    private readonly string? _storePath;

    private QuickPartLibrary(string? storePath) => _storePath = storePath;

    public IReadOnlyList<string> Names => _store.Names;
    public IReadOnlyList<QuickPart> Snippets => _store.Snippets;
    public bool IsEmpty => _store.Count == 0;

    public static QuickPartLibrary Load(IApplicationDataPathProvider? pathProvider = null)
    {
        string? path = null;
        try
        {
            path = Path.Combine(
                (pathProvider ?? PlatformApplicationDataPathProvider.LocalInstance)
                    .GetApplicationDataDirectory(),
                AppStoragePathPlanner.ProductDirectoryName,
                FileName);
        }
        catch
        {
            // Fall back to a session-only library when the platform path is unavailable.
        }

        return LoadFromPath(path);
    }

    /// <summary>Creates a library over an explicit path; a null path creates an in-memory library.</summary>
    public static QuickPartLibrary LoadFromPath(string? path)
    {
        var library = new QuickPartLibrary(path);
        library.TryLoad();
        return library;
    }

    public QuickPart? Get(string name) => _store.Get(name);

    public void Save(QuickPart part)
    {
        _store.Add(part);
        TrySave();
    }

    public void Remove(string name)
    {
        if (_store.Remove(name))
            TrySave();
    }

    private void TryLoad()
    {
        if (string.IsNullOrEmpty(_storePath) || !File.Exists(_storePath))
            return;

        try
        {
            var entries = JsonSerializer.Deserialize<List<PersistedQuickPart>>(
                File.ReadAllText(_storePath));
            if (entries is null)
                return;

            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name))
                    continue;

                _store.Add(new QuickPart(
                    entry.Name,
                    entry.Lines ?? [],
                    entry.Gallery,
                    entry.Category,
                    entry.Description));
            }
        }
        catch
        {
            // Treat unreadable content as an empty library.
        }
    }

    private void TrySave()
    {
        if (string.IsNullOrEmpty(_storePath))
            return;

        try
        {
            var directory = Path.GetDirectoryName(_storePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var entries = _store.Snippets.Select(part => new PersistedQuickPart
            {
                Name = part.Name,
                Lines = [.. part.Lines],
                Gallery = part.Gallery,
                Category = part.Category,
                Description = part.Description,
            });
            File.WriteAllText(_storePath, JsonSerializer.Serialize(entries, JsonOptions));
        }
        catch
        {
            // Persistence remains best-effort.
        }
    }

    private sealed class PersistedQuickPart
    {
        public string Name { get; set; } = string.Empty;
        public List<string>? Lines { get; set; }
        public string? Gallery { get; set; }
        public string? Category { get; set; }
        public string? Description { get; set; }
    }
}
