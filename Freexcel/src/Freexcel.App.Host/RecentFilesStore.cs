using System.IO;
using System.Text.Json;

namespace Freexcel.App.Host;

public sealed class RecentFileEntry
{
    public string Path { get; set; } = "";
    public DateTime LastOpened { get; set; }
    public bool IsPinned { get; set; }
}

public sealed class RecentFilesStore
{
    private static readonly string StorePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Freexcel", "recent.json");

    private const int MaxEntries = 25;

    public List<RecentFileEntry> Entries { get; private set; } = [];

    public IEnumerable<RecentFileEntry> PinnedEntries =>
        Entries.Where(e => e.IsPinned);

    public static RecentFilesStore Load()
    {
        var store = new RecentFilesStore();
        try
        {
            if (File.Exists(StorePath))
            {
                var json = File.ReadAllText(StorePath);
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
        var existing = Entries.FirstOrDefault(e =>
            string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        bool wasPinned = existing?.IsPinned ?? false;
        Entries.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        Entries.Insert(0, new RecentFileEntry { Path = path, LastOpened = DateTime.Now, IsPinned = wasPinned });
        if (Entries.Count > MaxEntries)
            Entries.RemoveRange(MaxEntries, Entries.Count - MaxEntries);
        Save();
    }

    public void Pin(string path)
    {
        var entry = Entries.FirstOrDefault(e =>
            string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        if (entry != null)
        {
            entry.IsPinned = true;
            Save();
        }
    }

    public void Unpin(string path)
    {
        var entry = Entries.FirstOrDefault(e =>
            string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        if (entry != null)
        {
            entry.IsPinned = false;
            Save();
        }
    }

    public void Remove(string path)
    {
        Entries.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(Entries));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RecentFiles] Failed to save: {ex.Message}");
        }
    }
}
