using System;
using System.Collections.Generic;
using System.Linq;
using Free.Shared.AppServices;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Persisted app-level master bibliography source list — shared across all documents.
/// Stored as a JSON file in the FreeW product data directory via <see cref="JsonSettingsStore{T}"/>.
/// </summary>
public sealed class MasterSourceStore
{
    private const string FileName = "master-sources.json";

    /// <summary>The master list of bibliography sources. Serialized to JSON.</summary>
    public List<SourceRecord> Sources { get; set; } = new();

    // ── persistence ───────────────────────────────────────────────────────────────────────────────

    private static JsonSettingsStore<MasterSourceStore>? _store;

    private static JsonSettingsStore<MasterSourceStore> GetStore()
        => _store ??= JsonSettingsStore<MasterSourceStore>.ForProductFile(FileName);

    /// <summary>Loads the master store from disk (or returns empty if the file is missing).</summary>
    public static MasterSourceStore Load() => GetStore().Load();

    /// <summary>Saves <paramref name="store"/> to disk. Returns true on success.</summary>
    public static bool Save(MasterSourceStore store) => GetStore().Save(store);

    // ── conversion helpers ────────────────────────────────────────────────────────────────────────

    /// <summary>Converts the stored records to <see cref="Source"/> model objects.</summary>
    public IReadOnlyList<Source> ToSources() =>
        Sources.Select(r => r.ToSource()).ToArray();

    /// <summary>Adds or replaces a source by Tag.</summary>
    public void AddOrUpdate(Source source)
    {
        var idx = Sources.FindIndex(r => r.Tag == source.Tag);
        var record = SourceRecord.FromSource(source);
        if (idx >= 0) Sources[idx] = record;
        else Sources.Add(record);
    }

    /// <summary>Removes a source by Tag.</summary>
    public bool Remove(string tag) =>
        Sources.RemoveAll(r => r.Tag == tag) > 0;
}

/// <summary>JSON-serializable representation of a <see cref="Source"/>.</summary>
public sealed class SourceRecord
{
    public string Tag { get; set; } = string.Empty;
    public string Type { get; set; } = "Book";
    public string Author { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Year { get; set; } = string.Empty;
    public string? Publisher { get; set; }
    public string? Journal { get; set; }
    public string? Volume { get; set; }
    public string? Issue { get; set; }
    public string? Pages { get; set; }
    public string? Url { get; set; }
    public string? Accessed { get; set; }

    public Source ToSource() => new()
    {
        Tag = Tag,
        Type = Enum.TryParse<SourceType>(Type, out var t) ? t : SourceType.Book,
        Author = Author,
        Title = Title,
        Year = Year,
        Publisher = Publisher,
        Journal = Journal,
        Volume = Volume,
        Issue = Issue,
        Pages = Pages,
        Url = Url,
        Accessed = Accessed
    };

    public static SourceRecord FromSource(Source s) => new()
    {
        Tag = s.Tag,
        Type = s.Type.ToString(),
        Author = s.Author,
        Title = s.Title,
        Year = s.Year,
        Publisher = s.Publisher,
        Journal = s.Journal,
        Volume = s.Volume,
        Issue = s.Issue,
        Pages = s.Pages,
        Url = s.Url,
        Accessed = s.Accessed
    };
}
