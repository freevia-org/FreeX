using Free.Shared.AppServices;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

/// <summary>
/// Persisted app-level master bibliography source list shared across FreeW shells.
/// Stored as JSON in the FreeW product data directory.
/// </summary>
public sealed class MasterSourceStore
{
    private const string FileName = "master-sources.json";

    private static JsonSettingsStore<MasterSourceStore>? s_store;

    public List<SourceRecord> Sources { get; set; } = [];

    public static MasterSourceStore Load() => Store().Load();

    public static bool Save(MasterSourceStore store) => Store().Save(store);

    public IReadOnlyList<Source> ToSources() =>
        Sources.Select(record => record.ToSource()).ToArray();

    public void AddOrUpdate(Source source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var index = Sources.FindIndex(record => record.Tag == source.Tag);
        var replacement = SourceRecord.FromSource(source);
        if (index >= 0)
            Sources[index] = replacement;
        else
            Sources.Add(replacement);
    }

    public bool Remove(string tag) =>
        Sources.RemoveAll(record => record.Tag == tag) > 0;

    private static JsonSettingsStore<MasterSourceStore> Store() =>
        s_store ??= JsonSettingsStore<MasterSourceStore>.ForProductFile(FileName);
}

/// <summary>JSON-serializable representation of a bibliography source.</summary>
public sealed class SourceRecord
{
    public string Tag { get; set; } = string.Empty;
    public string Type { get; set; } = "Book";
    public string Author { get; set; } = string.Empty;
    public List<SourceAuthorPerson> PersonalAuthors { get; set; } = [];
    public string? CorporateAuthor { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Year { get; set; } = string.Empty;
    public string? Institution { get; set; }
    public string? Publisher { get; set; }
    public string? City { get; set; }
    public string? Edition { get; set; }
    public string? StandardNumber { get; set; }
    public string? ShortTitle { get; set; }
    public string? Comments { get; set; }
    public string? Journal { get; set; }
    public string? Volume { get; set; }
    public string? Issue { get; set; }
    public string? Pages { get; set; }
    public string? Url { get; set; }
    public string? Accessed { get; set; }

    public Source ToSource() => new()
    {
        Tag = Tag,
        Type = Enum.TryParse<SourceType>(Type, out var sourceType) ? sourceType : SourceType.Book,
        Author = Author,
        PersonalAuthors = PersonalAuthors
            .Where(person => person is not null && !person.IsEmpty)
            .Select(person => SourceAuthorPerson.Create(person.First, person.Middle, person.Last))
            .ToArray(),
        CorporateAuthor = CorporateAuthor,
        Title = Title,
        Year = Year,
        Institution = Institution,
        Publisher = Publisher,
        City = City,
        Edition = Edition,
        StandardNumber = StandardNumber,
        ShortTitle = ShortTitle,
        Comments = Comments,
        Journal = Journal,
        Volume = Volume,
        Issue = Issue,
        Pages = Pages,
        Url = Url,
        Accessed = Accessed,
    };

    public static SourceRecord FromSource(Source source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new SourceRecord
        {
            Tag = source.Tag,
            Type = source.Type.ToString(),
            Author = source.Author,
            PersonalAuthors = source.PersonalAuthors
                .Where(person => person is not null && !person.IsEmpty)
                .Select(person => SourceAuthorPerson.Create(person.First, person.Middle, person.Last))
                .ToList(),
            CorporateAuthor = source.CorporateAuthor,
            Title = source.Title,
            Year = source.Year,
            Institution = source.Institution,
            Publisher = source.Publisher,
            City = source.City,
            Edition = source.Edition,
            StandardNumber = source.StandardNumber,
            ShortTitle = source.ShortTitle,
            Comments = source.Comments,
            Journal = source.Journal,
            Volume = source.Volume,
            Issue = source.Issue,
            Pages = source.Pages,
            Url = source.Url,
            Accessed = source.Accessed,
        };
    }
}
