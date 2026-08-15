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
    private readonly QuickPartStore _store = new();
    private readonly JsonSettingsStore<List<PersistedQuickPart>>? _settingsStore;

    private QuickPartLibrary(JsonSettingsStore<List<PersistedQuickPart>>? settingsStore) =>
        _settingsStore = settingsStore;

    public IReadOnlyList<string> Names => _store.Names;
    public IReadOnlyList<QuickPart> Snippets => _store.Snippets;
    public bool IsEmpty => _store.Count == 0;

    public static QuickPartLibrary Load(IApplicationDataPathProvider? pathProvider = null)
    {
        try
        {
            return LoadFromStore(JsonSettingsStore<List<PersistedQuickPart>>.ForProductFile(
                FileName,
                pathProvider ?? PlatformApplicationDataPathProvider.LocalInstance));
        }
        catch
        {
            return LoadFromStore(null);
        }
    }

    /// <summary>Creates a library over an explicit path; a null path creates an in-memory library.</summary>
    public static QuickPartLibrary LoadFromPath(string? path)
    {
        var settingsStore = string.IsNullOrEmpty(path)
            ? null
            : JsonSettingsStore<List<PersistedQuickPart>>.ForPath(path);
        return LoadFromStore(settingsStore);
    }

    private static QuickPartLibrary LoadFromStore(
        JsonSettingsStore<List<PersistedQuickPart>>? settingsStore)
    {
        var library = new QuickPartLibrary(settingsStore);
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
        if (_settingsStore is null)
            return;

        foreach (var entry in _settingsStore.Load())
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

    private void TrySave()
    {
        if (_settingsStore is null)
            return;

        _settingsStore.Save(_store.Snippets.Select(part => new PersistedQuickPart
        {
            Name = part.Name,
            Lines = [.. part.Lines],
            Gallery = part.Gallery,
            Category = part.Category,
            Description = part.Description,
        }).ToList());
    }

    private sealed class PersistedQuickPart
    {
        public PersistedQuickPart()
        {
        }

        public string Name { get; set; } = string.Empty;
        public List<string>? Lines { get; set; }
        public string? Gallery { get; set; }
        public string? Category { get; set; }
        public string? Description { get; set; }
    }
}
