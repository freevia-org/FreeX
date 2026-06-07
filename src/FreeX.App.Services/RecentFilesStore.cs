using System.Text.Json;

namespace FreeX.App.Services;

public sealed class RecentFileEntry
{
    public string Path { get; set; } = "";
    public DateTimeOffset LastOpened { get; set; }
    public bool IsPinned { get; set; }
}

public sealed class RecentFilesStore
{
    private const int MaxEntries = 25;

    private readonly Func<DateTimeOffset> _clock;
    private readonly string _storePath;

    private RecentFilesStore(string storePath, Func<DateTimeOffset>? clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);

        _storePath = storePath;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public static string DefaultStorePath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FreeX",
        "recent.json");

    public List<RecentFileEntry> Entries { get; private set; } = [];

    public IEnumerable<RecentFileEntry> PinnedEntries =>
        Entries.Where(entry => entry.IsPinned);

    public static RecentFilesStore Load() => Load(DefaultStorePath);

    public static RecentFilesStore Load(string storePath, Func<DateTimeOffset>? clock = null)
    {
        var store = new RecentFilesStore(storePath, clock);
        try
        {
            if (File.Exists(storePath))
            {
                var json = File.ReadAllText(storePath);
                store.Entries = JsonSerializer.Deserialize<List<RecentFileEntry>>(json) ?? [];
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RecentFiles] Failed to load: {ex.Message}");
        }

        return store;
    }

    public void AddOrUpdate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var existing = Entries.FirstOrDefault(entry =>
            string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));
        var wasPinned = existing?.IsPinned ?? false;
        Entries.RemoveAll(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));
        Entries.Insert(0, new RecentFileEntry { Path = path, LastOpened = _clock(), IsPinned = wasPinned });
        if (Entries.Count > MaxEntries)
            Entries.RemoveRange(MaxEntries, Entries.Count - MaxEntries);

        Save();
    }

    public void Pin(string path)
    {
        var entry = Entries.FirstOrDefault(entry =>
            string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return;

        entry.IsPinned = true;
        Save();
    }

    public void Unpin(string path)
    {
        var entry = Entries.FirstOrDefault(entry =>
            string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return;

        entry.IsPinned = false;
        Save();
    }

    public void Remove(string path)
    {
        Entries.RemoveAll(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    private void Save()
    {
        try
        {
            AtomicFileWriter.WriteAllText(_storePath, JsonSerializer.Serialize(Entries));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RecentFiles] Failed to save: {ex.Message}");
        }
    }
}
