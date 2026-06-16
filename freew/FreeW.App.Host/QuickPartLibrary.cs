using System.IO;
using System.Text.Json;
using Free.Shared.AppServices;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// The WPF/IO persistence wrapper around the pure <see cref="QuickPartStore"/>. It loads/saves the
/// snippets as a small JSON file (<c>quickparts.json</c>) under FreeW's own data folder — the same
/// location pattern the recent-files store and autosave use (AppData/Local › FreeW, via
/// <see cref="PlatformApplicationDataPathProvider.LocalInstance"/> and
/// <see cref="AppStoragePathPlanner.ProductDirectoryName"/>, which is "FreeW" because Program.Main set
/// AppProduct = "FreeW"). All persistence is best-effort: a failed load yields an empty library and a
/// failed save is swallowed, so a snippet operation never disrupts editing. If the JSON file cannot be
/// reached at all, the library simply behaves as an in-memory store for the session.
/// </summary>
internal sealed class QuickPartLibrary
{
    private const string FileName = "quickparts.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly QuickPartStore _store = new();
    private readonly string? _storePath;

    private QuickPartLibrary(string? storePath) => _storePath = storePath;

    /// <summary>The snippet names (case-insensitive alphabetical order) currently in the library.</summary>
    public IReadOnlyList<string> Names => _store.Names;

    /// <summary>True when no snippets are stored.</summary>
    public bool IsEmpty => _store.Count == 0;

    /// <summary>
    /// Load the library from FreeW's data folder (creating an empty one if the file is missing or
    /// unreadable). Never throws.
    /// </summary>
    public static QuickPartLibrary Load()
    {
        string? path = null;
        try
        {
            path = Path.Combine(
                PlatformApplicationDataPathProvider.LocalInstance.GetApplicationDataDirectory(),
                AppStoragePathPlanner.ProductDirectoryName,
                FileName);
        }
        catch
        {
            // Could not resolve the data folder — fall back to an in-memory (session-only) library.
        }

        var library = new QuickPartLibrary(path);
        library.TryLoad();
        return library;
    }

    private void TryLoad()
    {
        if (string.IsNullOrEmpty(_storePath) || !File.Exists(_storePath))
            return;
        try
        {
            var json = File.ReadAllText(_storePath);
            var entries = JsonSerializer.Deserialize<List<PersistedQuickPart>>(json);
            if (entries is null)
                return;
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name))
                    continue;
                _store.Add(new QuickPart(entry.Name, entry.Lines ?? []));
            }
        }
        catch
        {
            // Corrupt/unreadable store: start from empty rather than blocking the app.
        }
    }

    /// <summary>Look up a snippet by name (case-insensitive), or null when none is stored.</summary>
    public QuickPart? Get(string name) => _store.Get(name);

    /// <summary>Add (or overwrite by name) a snippet, then persist. Persistence is best-effort.</summary>
    public void Save(QuickPart part)
    {
        _store.Add(part);
        TrySave();
    }

    /// <summary>Remove a snippet by name (case-insensitive), then persist. Persistence is best-effort.</summary>
    public void Remove(string name)
    {
        if (_store.Remove(name))
            TrySave();
    }

    private void TrySave()
    {
        if (string.IsNullOrEmpty(_storePath))
            return; // in-memory session-only fallback
        try
        {
            var directory = Path.GetDirectoryName(_storePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var entries = _store.Snippets
                .Select(p => new PersistedQuickPart { Name = p.Name, Lines = [.. p.Lines] })
                .ToList();
            File.WriteAllText(_storePath, JsonSerializer.Serialize(entries, JsonOptions));
        }
        catch
        {
            // Persistence is best-effort; never block editing on a failed snippet write.
        }
    }

    // The on-disk shape of a stored snippet: just a name and its plain-text paragraph lines.
    private sealed class PersistedQuickPart
    {
        public string Name { get; set; } = string.Empty;
        public List<string>? Lines { get; set; }
    }
}
