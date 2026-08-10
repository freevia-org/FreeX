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
        if (SourceTagIdentity.HasIdentity(source.Tag))
        {
            var index = Sources.FindIndex(record => SourceTagIdentity.Equals(record.Tag, source.Tag));
            if (index >= 0)
            {
                Sources.RemoveAll(record => SourceTagIdentity.Equals(record.Tag, source.Tag));
                Sources.Insert(Math.Min(index, Sources.Count), replacement);
                return;
            }
        }

        Sources.Add(replacement);
    }

    public bool Remove(string tag) =>
        Sources.RemoveAll(record => SourceTagIdentity.Equals(record.Tag, tag)) > 0;

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
    public string? Artist { get; set; }
    public string? Composer { get; set; }
    public string? Conductor { get; set; }
    public string? Director { get; set; }
    public string? Performer { get; set; }
    public string? ProducerName { get; set; }
    public string? Writer { get; set; }
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
    public string? CaseNumber { get; set; }
    public string? Court { get; set; }
    public string? Reporter { get; set; }
    public string? CountryRegion { get; set; }
    public string? StateProvince { get; set; }
    public string? Medium { get; set; }
    public string? SourceKind { get; set; }
    public string? AlbumTitle { get; set; }
    public string? ProductionCompany { get; set; }
    public string? RecordingNumber { get; set; }
    public string? Theater { get; set; }
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
        Tag = SourceTagIdentity.Canonicalize(Tag),
        Type = Enum.TryParse<SourceType>(Type, out var sourceType) ? sourceType : SourceType.Book,
        Author = Author,
        PersonalAuthors = SourceAuthorPerson.Canonicalize(PersonalAuthors),
        CorporateAuthor = CorporateAuthor,
        Editors = SourceAuthorPerson.Canonicalize(Editors),
        Translators = SourceAuthorPerson.Canonicalize(Translators),
        Title = Title,
        BookTitle = BookTitle,
        ConferenceName = ConferenceName,
        Inventor = Inventor,
        Interviewee = Interviewee,
        Interviewer = Interviewer,
        Artist = Artist,
        Composer = Composer,
        Conductor = Conductor,
        Director = Director,
        Performer = Performer,
        ProducerName = ProducerName,
        Writer = Writer,
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
        CaseNumber = CaseNumber,
        Court = Court,
        Reporter = Reporter,
        CountryRegion = CountryRegion,
        StateProvince = StateProvince,
        Medium = Medium,
        SourceKind = SourceKind,
        AlbumTitle = AlbumTitle,
        ProductionCompany = ProductionCompany,
        RecordingNumber = RecordingNumber,
        Theater = Theater,
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
            Tag = SourceTagIdentity.Canonicalize(source.Tag),
            Type = source.Type.ToString(),
            Author = source.Author,
            PersonalAuthors = SourceAuthorPerson.Canonicalize(source.PersonalAuthors).ToList(),
            CorporateAuthor = source.CorporateAuthor,
            Editors = SourceAuthorPerson.Canonicalize(source.Editors).ToList(),
            Translators = SourceAuthorPerson.Canonicalize(source.Translators).ToList(),
            Title = source.Title,
            BookTitle = source.BookTitle,
            ConferenceName = source.ConferenceName,
            Inventor = source.Inventor,
            Interviewee = source.Interviewee,
            Interviewer = source.Interviewer,
            Artist = source.Artist,
            Composer = source.Composer,
            Conductor = source.Conductor,
            Director = source.Director,
            Performer = source.Performer,
            ProducerName = source.ProducerName,
            Writer = source.Writer,
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
            CaseNumber = source.CaseNumber,
            Court = source.Court,
            Reporter = source.Reporter,
            CountryRegion = source.CountryRegion,
            StateProvince = source.StateProvince,
            Medium = source.Medium,
            SourceKind = source.SourceKind,
            AlbumTitle = source.AlbumTitle,
            ProductionCompany = source.ProductionCompany,
            RecordingNumber = source.RecordingNumber,
            Theater = source.Theater,
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
