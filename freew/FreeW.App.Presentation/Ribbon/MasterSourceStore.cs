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

        var replacement = SourceRecord.FromSource(source);
        if (SourceManagementTagIdentity.HasIdentity(source.Tag))
        {
            var index = Sources.FindIndex(record => SourceManagementTagIdentity.Equals(record.Tag, source.Tag));
            if (index >= 0)
            {
                Sources.RemoveAll(record => SourceManagementTagIdentity.Equals(record.Tag, source.Tag));
                Sources.Insert(Math.Min(index, Sources.Count), replacement);
                return;
            }
        }

        Sources.Add(replacement);
    }

    public bool Remove(string tag) =>
        Sources.RemoveAll(record => SourceManagementTagIdentity.Equals(record.Tag, tag)) > 0;

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
    public List<SourceAuthorPerson> Editors { get; set; } = [];
    public List<SourceAuthorPerson> Translators { get; set; } = [];
    public string Title { get; set; } = string.Empty;
    public string? BookTitle { get; set; }
    public string? ConferenceName { get; set; }
    public string? Inventor { get; set; }
    public string? Interviewee { get; set; }
    public string? Interviewer { get; set; }
    public string Year { get; set; } = string.Empty;
    public string? Month { get; set; }
    public string? Day { get; set; }
    public string? Institution { get; set; }
    public string? Publisher { get; set; }
    public string? City { get; set; }
    public string? Edition { get; set; }
    public string? StandardNumber { get; set; }
    public string? ChapterNumber { get; set; }
    public string? PatentNumber { get; set; }
    public string? CountryRegion { get; set; }
    public string? StateProvince { get; set; }
    public string? Medium { get; set; }
    public string? SourceKind { get; set; }
    public string? ShortTitle { get; set; }
    public string? Comments { get; set; }
    public string? Journal { get; set; }
    public string? Volume { get; set; }
    public string? Issue { get; set; }
    public string? Pages { get; set; }
    public string? Url { get; set; }
    public string? Accessed { get; set; }
    public string? AccessedDay { get; set; }
    public string? AccessedMonth { get; set; }
    public string? AccessedYear { get; set; }

    public Source ToSource() => new()
    {
        Tag = SourceManagementTagIdentity.Canonicalize(Tag),
        Type = Enum.TryParse<SourceType>(Type, out var sourceType) ? sourceType : SourceType.Book,
        Author = Author,
        PersonalAuthors = PersonalAuthors
            .Where(person => person is not null && !person.IsEmpty)
            .Select(person => SourceAuthorPerson.Create(person.First, person.Middle, person.Last))
            .ToArray(),
        CorporateAuthor = CorporateAuthor,
        Editors = Editors
            .Where(person => person is not null && !person.IsEmpty)
            .Select(person => SourceAuthorPerson.Create(person.First, person.Middle, person.Last))
            .ToArray(),
        Translators = Translators
            .Where(person => person is not null && !person.IsEmpty)
            .Select(person => SourceAuthorPerson.Create(person.First, person.Middle, person.Last))
            .ToArray(),
        Title = Title,
        BookTitle = BookTitle,
        ConferenceName = ConferenceName,
        Inventor = Inventor,
        Interviewee = Interviewee,
        Interviewer = Interviewer,
        Year = Year,
        Month = Month,
        Day = Day,
        Institution = Institution,
        Publisher = Publisher,
        City = City,
        Edition = Edition,
        StandardNumber = StandardNumber,
        ChapterNumber = ChapterNumber,
        PatentNumber = PatentNumber,
        CountryRegion = CountryRegion,
        StateProvince = StateProvince,
        Medium = Medium,
        SourceKind = SourceKind,
        ShortTitle = ShortTitle,
        Comments = Comments,
        Journal = Journal,
        Volume = Volume,
        Issue = Issue,
        Pages = Pages,
        Url = Url,
        Accessed = Accessed,
        AccessedDay = AccessedDay,
        AccessedMonth = AccessedMonth,
        AccessedYear = AccessedYear,
    };

    public static SourceRecord FromSource(Source source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new SourceRecord
        {
            Tag = SourceManagementTagIdentity.Canonicalize(source.Tag),
            Type = source.Type.ToString(),
            Author = source.Author,
            PersonalAuthors = source.PersonalAuthors
                .Where(person => person is not null && !person.IsEmpty)
                .Select(person => SourceAuthorPerson.Create(person.First, person.Middle, person.Last))
                .ToList(),
            CorporateAuthor = source.CorporateAuthor,
            Editors = source.Editors
                .Where(person => person is not null && !person.IsEmpty)
                .Select(person => SourceAuthorPerson.Create(person.First, person.Middle, person.Last))
                .ToList(),
            Translators = source.Translators
                .Where(person => person is not null && !person.IsEmpty)
                .Select(person => SourceAuthorPerson.Create(person.First, person.Middle, person.Last))
                .ToList(),
            Title = source.Title,
            BookTitle = source.BookTitle,
            ConferenceName = source.ConferenceName,
            Inventor = source.Inventor,
            Interviewee = source.Interviewee,
            Interviewer = source.Interviewer,
            Year = source.Year,
            Month = source.Month,
            Day = source.Day,
            Institution = source.Institution,
            Publisher = source.Publisher,
            City = source.City,
            Edition = source.Edition,
            StandardNumber = source.StandardNumber,
            ChapterNumber = source.ChapterNumber,
            PatentNumber = source.PatentNumber,
            CountryRegion = source.CountryRegion,
            StateProvince = source.StateProvince,
            Medium = source.Medium,
            SourceKind = source.SourceKind,
            ShortTitle = source.ShortTitle,
            Comments = source.Comments,
            Journal = source.Journal,
            Volume = source.Volume,
            Issue = source.Issue,
            Pages = source.Pages,
            Url = source.Url,
            Accessed = source.Accessed,
            AccessedDay = source.AccessedDay,
            AccessedMonth = source.AccessedMonth,
            AccessedYear = source.AccessedYear,
        };
    }
}
