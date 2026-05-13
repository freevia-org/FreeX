using System.IO;
using System.Text.Json;

namespace Freexcel.App.Host;

public sealed class RecentFileEntry
{
    public string Path { get; set; } = "";
    public DateTime LastOpened { get; set; }
}

public sealed class RecentFilesStore
{
    private static readonly string StorePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Freexcel", "recent.json");

    private const int MaxEntries = 25;

    public List<RecentFileEntry> Entries { get; private set; } = [];

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
        catch { }
        return store;
    }

    public void AddOrUpdate(string path)
    {
        Entries.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        Entries.Insert(0, new RecentFileEntry { Path = path, LastOpened = DateTime.Now });
        if (Entries.Count > MaxEntries)
            Entries.RemoveRange(MaxEntries, Entries.Count - MaxEntries);
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(Entries));
        }
        catch { }
    }
}
