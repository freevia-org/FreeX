using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class SourceManagementDialogPlannerTests
{
    [Fact]
    public void BuildEntryFieldPlans_UsesSourceDialogOrderLabelsAndSeedValues()
    {
        var source = new Source
        {
            Tag = "Knuth1997",
            Author = "Knuth",
            Editors = [SourceAuthorPerson.Create("Alice", "Q.", "Editor")],
            Translators = [SourceAuthorPerson.Create("Boris", string.Empty, "Translator")],
            Title = "TAOCP",
            Year = "1997",
            Publisher = "AW",
            City = "Reading",
            Edition = "3",
            StandardNumber = "978-0201896831",
            ShortTitle = "TAOCP",
            Comments = "Classic reference"
        };

        var plans = SourceManagementDialogPlanner.BuildEntryFieldPlans(source);

        plans.Select(plan => plan.Field).Should().Equal(
            SourceManagementSourceField.Tag,
            SourceManagementSourceField.Author,
            SourceManagementSourceField.Editor,
            SourceManagementSourceField.Translator,
            SourceManagementSourceField.Title,
            SourceManagementSourceField.Year,
            SourceManagementSourceField.City,
            SourceManagementSourceField.Publisher,
            SourceManagementSourceField.Edition,
            SourceManagementSourceField.StandardNumber,
            SourceManagementSourceField.ShortTitle,
            SourceManagementSourceField.Comments);
        plans.Select(plan => plan.Label).Should().Equal(
            "Tag (short id):",
            "Author:",
            "Editor:",
            "Translator:",
            "Title:",
            "Year:",
            "City:",
            "Publisher / Site name (optional):",
            "Edition:",
            "Standard number:",
            "Short title:",
            "Comments:");
        plans.Select(plan => plan.Text).Should().Equal(
            "Knuth1997",
            "Knuth",
            "Alice Q. Editor",
            "Boris Translator",
            "TAOCP",
            "1997",
            "Reading",
            "AW",
            "3",
            "978-0201896831",
            "TAOCP",
            "Classic reference");
    }

    [Fact]
    public void BuildSourceTypeChoices_ExposesTheModeledWordSourceTypes()
    {
        var choices = SourceManagementDialogPlanner.BuildSourceTypeChoices();

        choices.Select(choice => choice.ToString()).Should().Equal(
            choices.Select(choice => choice.Label));

        choices.Select(choice => choice.Type).Should().Equal(
            SourceType.Book,
            SourceType.JournalArticle,
            SourceType.WebSite,
            SourceType.Report,
            SourceType.BookSection,
            SourceType.ConferenceProceedings,
            SourceType.ArticleInPeriodical,
            SourceType.ElectronicSource,
            SourceType.Patent,
            SourceType.Interview,
            SourceType.Misc,
            SourceType.Film,
            SourceType.SoundRecording,
            SourceType.Art,
            SourceType.InternetSite,
            SourceType.Performance,
            SourceType.Case);
        choices.Select(choice => choice.Label).Should().Equal(
            "Book",
            "Journal Article",
            "Web Site",
            "Report",
            "Book Section",
            "Conference Proceedings",
            "Article in a Periodical",
            "Electronic Source",
            "Patent",
            "Interview",
            "Miscellaneous",
            "Film",
            "Sound Recording",
            "Art",
            "Internet Site",
            "Performance",
            "Case");
        SourceManagementDialogPlanner.SourceTypeSelectedIndex(SourceType.JournalArticle).Should().Be(1);
        SourceManagementDialogPlanner.SourceTypeSelectedIndex(SourceType.Report).Should().Be(3);
        SourceManagementDialogPlanner.SourceTypeSelectedIndex(SourceType.BookSection).Should().Be(4);
        SourceManagementDialogPlanner.SourceTypeSelectedIndex(SourceType.ConferenceProceedings).Should().Be(5);
        SourceManagementDialogPlanner.SourceTypeSelectedIndex(SourceType.ArticleInPeriodical).Should().Be(6);
        SourceManagementDialogPlanner.SourceTypeSelectedIndex(SourceType.ElectronicSource).Should().Be(7);
        SourceManagementDialogPlanner.SourceTypeSelectedIndex(SourceType.Patent).Should().Be(8);
        SourceManagementDialogPlanner.SourceTypeSelectedIndex(SourceType.Interview).Should().Be(9);
        SourceManagementDialogPlanner.SourceTypeSelectedIndex(SourceType.Misc).Should().Be(10);
        SourceManagementDialogPlanner.SourceTypeSelectedIndex(SourceType.Film).Should().Be(11);
        SourceManagementDialogPlanner.SourceTypeSelectedIndex(SourceType.SoundRecording).Should().Be(12);
        SourceManagementDialogPlanner.SourceTypeSelectedIndex(SourceType.Art).Should().Be(13);
        SourceManagementDialogPlanner.SourceTypeSelectedIndex(SourceType.InternetSite).Should().Be(14);
        SourceManagementDialogPlanner.SourceTypeSelectedIndex(SourceType.Performance).Should().Be(15);
        SourceManagementDialogPlanner.SourceTypeSelectedIndex(SourceType.Case).Should().Be(16);
    }

    [Fact]
    public void BuildEntryFieldPlans_UsesTypeSpecificFields()
    {
        var journalPlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(SourceType.JournalArticle);
        journalPlans.Select(plan => plan.Field).Should().Equal(
            SourceManagementSourceField.Tag,
            SourceManagementSourceField.Author,
            SourceManagementSourceField.Title,
            SourceManagementSourceField.Year,
            SourceManagementSourceField.Journal,
            SourceManagementSourceField.Volume,
            SourceManagementSourceField.Issue,
            SourceManagementSourceField.Pages,
            SourceManagementSourceField.StandardNumber,
            SourceManagementSourceField.ShortTitle,
            SourceManagementSourceField.Comments);

        var webPlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(SourceType.WebSite);
        webPlans.Select(plan => plan.Field).Should().Equal(
            SourceManagementSourceField.Tag,
            SourceManagementSourceField.Author,
            SourceManagementSourceField.Title,
            SourceManagementSourceField.Year,
            SourceManagementSourceField.Publisher,
            SourceManagementSourceField.Url,
            SourceManagementSourceField.AccessedDay,
            SourceManagementSourceField.AccessedMonth,
            SourceManagementSourceField.AccessedYear,
            SourceManagementSourceField.ShortTitle,
            SourceManagementSourceField.Comments);

        var reportPlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(SourceType.Report);
        reportPlans.Select(plan => plan.Field).Should().Equal(
            SourceManagementSourceField.Tag,
            SourceManagementSourceField.Author,
            SourceManagementSourceField.Title,
            SourceManagementSourceField.Year,
            SourceManagementSourceField.Institution,
            SourceManagementSourceField.City,
            SourceManagementSourceField.Publisher,
            SourceManagementSourceField.StandardNumber,
            SourceManagementSourceField.ShortTitle,
            SourceManagementSourceField.Comments);
        reportPlans.Should().NotContain(plan => plan.Field == SourceManagementSourceField.Pages);

        var bookSectionPlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(SourceType.BookSection);
        bookSectionPlans.Select(plan => plan.Field).Should().Equal(
            SourceManagementSourceField.Tag,
            SourceManagementSourceField.Author,
            SourceManagementSourceField.Editor,
            SourceManagementSourceField.Translator,
            SourceManagementSourceField.Title,
            SourceManagementSourceField.BookTitle,
            SourceManagementSourceField.Year,
            SourceManagementSourceField.ChapterNumber,
            SourceManagementSourceField.Pages,
            SourceManagementSourceField.City,
            SourceManagementSourceField.Publisher,
            SourceManagementSourceField.Edition,
            SourceManagementSourceField.StandardNumber,
            SourceManagementSourceField.ShortTitle,
            SourceManagementSourceField.Comments);

        var conferencePlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(SourceType.ConferenceProceedings);
        conferencePlans.Select(plan => plan.Field).Should().Equal(
            SourceManagementSourceField.Tag,
            SourceManagementSourceField.Author,
            SourceManagementSourceField.Title,
            SourceManagementSourceField.ConferenceName,
            SourceManagementSourceField.Year,
            SourceManagementSourceField.Pages,
            SourceManagementSourceField.City,
            SourceManagementSourceField.Publisher,
            SourceManagementSourceField.StandardNumber,
            SourceManagementSourceField.ShortTitle,
            SourceManagementSourceField.Comments);
        conferencePlans.Single(plan => plan.Field == SourceManagementSourceField.ConferenceName)
            .Label.Should().Be("Conference name:");

        var periodicalPlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(SourceType.ArticleInPeriodical);
        periodicalPlans.Select(plan => plan.Field).Should().Equal(
            SourceManagementSourceField.Tag,
            SourceManagementSourceField.Author,
            SourceManagementSourceField.Title,
            SourceManagementSourceField.Year,
            SourceManagementSourceField.Journal,
            SourceManagementSourceField.Volume,
            SourceManagementSourceField.Issue,
            SourceManagementSourceField.Pages,
            SourceManagementSourceField.StandardNumber,
            SourceManagementSourceField.ShortTitle,
            SourceManagementSourceField.Comments);

        var electronicPlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(SourceType.ElectronicSource);
        electronicPlans.Select(plan => plan.Field).Should().Equal(
            SourceManagementSourceField.Tag,
            SourceManagementSourceField.Author,
            SourceManagementSourceField.Title,
            SourceManagementSourceField.Year,
            SourceManagementSourceField.Publisher,
            SourceManagementSourceField.Url,
            SourceManagementSourceField.AccessedDay,
            SourceManagementSourceField.AccessedMonth,
            SourceManagementSourceField.AccessedYear,
            SourceManagementSourceField.ShortTitle,
            SourceManagementSourceField.Comments);

        var patentPlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(SourceType.Patent);
        patentPlans.Select(plan => plan.Field).Should().Equal(
            SourceManagementSourceField.Tag,
            SourceManagementSourceField.Inventor,
            SourceManagementSourceField.Title,
            SourceManagementSourceField.Year,
            SourceManagementSourceField.Month,
            SourceManagementSourceField.Day,
            SourceManagementSourceField.PatentNumber,
            SourceManagementSourceField.CountryRegion,
            SourceManagementSourceField.StateProvince,
            SourceManagementSourceField.ShortTitle,
            SourceManagementSourceField.Comments);

        var interviewPlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(SourceType.Interview);
        interviewPlans.Select(plan => plan.Field).Should().Equal(
            SourceManagementSourceField.Tag,
            SourceManagementSourceField.Interviewee,
            SourceManagementSourceField.Interviewer,
            SourceManagementSourceField.Title,
            SourceManagementSourceField.Year,
            SourceManagementSourceField.Month,
            SourceManagementSourceField.Day,
            SourceManagementSourceField.Medium,
            SourceManagementSourceField.ShortTitle,
            SourceManagementSourceField.Comments);

        var miscPlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(SourceType.Misc);
        miscPlans.Select(plan => plan.Field).Should().Equal(
            SourceManagementSourceField.Tag,
            SourceManagementSourceField.Author,
            SourceManagementSourceField.Title,
            SourceManagementSourceField.Year,
            SourceManagementSourceField.Month,
            SourceManagementSourceField.Day,
            SourceManagementSourceField.Medium,
            SourceManagementSourceField.SourceKind,
            SourceManagementSourceField.ShortTitle,
            SourceManagementSourceField.Comments);

        var filmPlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(SourceType.Film);
        filmPlans.Select(plan => plan.Field).Should().Equal(
            SourceManagementSourceField.Tag,
            SourceManagementSourceField.Director,
            SourceManagementSourceField.ProducerName,
            SourceManagementSourceField.Writer,
            SourceManagementSourceField.Performer,
            SourceManagementSourceField.Title,
            SourceManagementSourceField.Year,
            SourceManagementSourceField.Medium,
            SourceManagementSourceField.ProductionCompany,
            SourceManagementSourceField.ShortTitle,
            SourceManagementSourceField.Comments);

        var recordingPlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(SourceType.SoundRecording);
        recordingPlans.Select(plan => plan.Field).Should().Equal(
            SourceManagementSourceField.Tag,
            SourceManagementSourceField.Artist,
            SourceManagementSourceField.Composer,
            SourceManagementSourceField.Conductor,
            SourceManagementSourceField.Performer,
            SourceManagementSourceField.ProducerName,
            SourceManagementSourceField.Title,
            SourceManagementSourceField.AlbumTitle,
            SourceManagementSourceField.Year,
            SourceManagementSourceField.Medium,
            SourceManagementSourceField.RecordingNumber,
            SourceManagementSourceField.ShortTitle,
            SourceManagementSourceField.Comments);

        var artPlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(SourceType.Art);
        artPlans.Select(plan => plan.Field).Should().Equal(
            SourceManagementSourceField.Tag,
            SourceManagementSourceField.Artist,
            SourceManagementSourceField.Title,
            SourceManagementSourceField.Year,
            SourceManagementSourceField.Medium,
            SourceManagementSourceField.Institution,
            SourceManagementSourceField.City,
            SourceManagementSourceField.ShortTitle,
            SourceManagementSourceField.Comments);

        var internetSitePlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(SourceType.InternetSite);
        internetSitePlans.Select(plan => plan.Field).Should().Equal(
            SourceManagementSourceField.Tag,
            SourceManagementSourceField.Author,
            SourceManagementSourceField.Title,
            SourceManagementSourceField.Year,
            SourceManagementSourceField.Publisher,
            SourceManagementSourceField.Url,
            SourceManagementSourceField.AccessedDay,
            SourceManagementSourceField.AccessedMonth,
            SourceManagementSourceField.AccessedYear,
            SourceManagementSourceField.ShortTitle,
            SourceManagementSourceField.Comments);

        var performancePlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(SourceType.Performance);
        performancePlans.Select(plan => plan.Field).Should().Equal(
            SourceManagementSourceField.Tag,
            SourceManagementSourceField.Performer,
            SourceManagementSourceField.Conductor,
            SourceManagementSourceField.Title,
            SourceManagementSourceField.Year,
            SourceManagementSourceField.Month,
            SourceManagementSourceField.Day,
            SourceManagementSourceField.Theater,
            SourceManagementSourceField.City,
            SourceManagementSourceField.Medium,
            SourceManagementSourceField.ShortTitle,
            SourceManagementSourceField.Comments);

        var casePlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(SourceType.Case);
        casePlans.Select(plan => plan.Field).Should().Equal(
            SourceManagementSourceField.Tag,
            SourceManagementSourceField.Author,
            SourceManagementSourceField.Title,
            SourceManagementSourceField.CaseNumber,
            SourceManagementSourceField.Court,
            SourceManagementSourceField.Reporter,
            SourceManagementSourceField.Year,
            SourceManagementSourceField.Month,
            SourceManagementSourceField.Day,
            SourceManagementSourceField.CountryRegion,
            SourceManagementSourceField.StateProvince,
            SourceManagementSourceField.City,
            SourceManagementSourceField.ShortTitle,
            SourceManagementSourceField.Comments);
        casePlans.Single(plan => plan.Field == SourceManagementSourceField.Court)
            .Label.Should().Be("Court:");
    }

    [Fact]
    public void CreateEntry_TrimsDialogTextAndDefaultsMissingFields()
    {
        var entry = SourceManagementDialogPlanner.CreateEntry(
            SourceType.WebSite,
            new Dictionary<SourceManagementSourceField, string?>
            {
                [SourceManagementSourceField.Tag] = "  K97  ",
                [SourceManagementSourceField.Author] = " Knuth ",
                [SourceManagementSourceField.Title] = null,
                [SourceManagementSourceField.BookTitle] = " Example Book ",
                [SourceManagementSourceField.Year] = " 1997 ",
                [SourceManagementSourceField.ChapterNumber] = " 4 ",
                [SourceManagementSourceField.Institution] = " Example Institute ",
                [SourceManagementSourceField.Url] = " https://example.test ",
                [SourceManagementSourceField.ShortTitle] = " TAOCP ",
                [SourceManagementSourceField.Comments] = " Notes "
            });

        entry.Type.Should().Be(SourceType.WebSite);
        entry.Tag.Should().Be("K97");
        entry.Author.Should().Be("Knuth");
        entry.Title.Should().BeEmpty();
        entry.BookTitle.Should().Be("Example Book");
        entry.Year.Should().Be("1997");
        entry.ChapterNumber.Should().Be("4");
        entry.Institution.Should().Be("Example Institute");
        entry.Url.Should().Be("https://example.test");
        entry.ShortTitle.Should().Be("TAOCP");
        entry.Comments.Should().Be("Notes");
        entry.CorporateAuthor.Should().Be("Knuth");
    }

    [Fact]
    public void ProjectEntry_SeedsReportInstitutionFieldPlan()
    {
        var source = new Source
        {
            Type = SourceType.Report,
            Tag = "R1",
            Institution = "National Bureau of Standards",
            City = "Washington",
            Publisher = "Government Printing Office",
            StandardNumber = "NBS-1"
        };

        var entry = SourceManagementDialogPlanner.ProjectEntry(source);
        var plans = SourceManagementDialogPlanner.BuildEntryFieldPlans(entry);

        entry.Institution.Should().Be("National Bureau of Standards");
        plans.Single(plan => plan.Field == SourceManagementSourceField.Institution)
            .Text.Should().Be("National Bureau of Standards");
    }

    [Fact]
    public void ProjectEntry_SeedsBookSectionFields()
    {
        var source = new Source
        {
            Type = SourceType.BookSection,
            Tag = "Ch1",
            Author = "Chapter Author",
            Editors = [SourceAuthorPerson.Create("Edna", "Q.", "Editor")],
            Translators = [SourceAuthorPerson.Create("Tara", string.Empty, "Translator")],
            Title = "The Chapter",
            BookTitle = "The Containing Book",
            Year = "2026",
            ChapterNumber = "3",
            Pages = "12-20",
            City = "London",
            Publisher = "Test Press",
            Edition = "2",
            StandardNumber = "ISBN-1"
        };

        var entry = SourceManagementDialogPlanner.ProjectEntry(source);
        var plans = SourceManagementDialogPlanner.BuildEntryFieldPlans(entry);

        entry.BookTitle.Should().Be("The Containing Book");
        entry.Editor.Should().Be("Edna Q. Editor");
        entry.Translator.Should().Be("Tara Translator");
        entry.ChapterNumber.Should().Be("3");
        plans.Single(plan => plan.Field == SourceManagementSourceField.Editor)
            .Text.Should().Be("Edna Q. Editor");
        plans.Single(plan => plan.Field == SourceManagementSourceField.Translator)
            .Text.Should().Be("Tara Translator");
        plans.Single(plan => plan.Field == SourceManagementSourceField.BookTitle)
            .Text.Should().Be("The Containing Book");
        plans.Single(plan => plan.Field == SourceManagementSourceField.ChapterNumber)
            .Text.Should().Be("3");
        plans.Single(plan => plan.Field == SourceManagementSourceField.Pages)
            .Text.Should().Be("12-20");
    }

    [Fact]
    public void ProjectEntry_SeedsConferenceProceedingsFields()
    {
        var source = new Source
        {
            Type = SourceType.ConferenceProceedings,
            Tag = "Conf2026",
            Author = "Paper Author",
            Title = "Proceedings Paper",
            ConferenceName = "Proceedings of the Example Conference",
            Year = "2026",
            Pages = "101-109",
            City = "Berlin",
            Publisher = "ACM",
            StandardNumber = "ISBN-CP-1"
        };

        var entry = SourceManagementDialogPlanner.ProjectEntry(source);
        var plans = SourceManagementDialogPlanner.BuildEntryFieldPlans(entry);

        entry.ConferenceName.Should().Be("Proceedings of the Example Conference");
        plans.Single(plan => plan.Field == SourceManagementSourceField.ConferenceName)
            .Text.Should().Be("Proceedings of the Example Conference");
        plans.Single(plan => plan.Field == SourceManagementSourceField.Pages)
            .Text.Should().Be("101-109");
    }

    [Fact]
    public void ProjectEntry_SeedsNewWordSourceTypeFields()
    {
        var periodical = SourceManagementDialogPlanner.ProjectEntry(new Source
        {
            Type = SourceType.ArticleInPeriodical,
            Tag = "Periodical2026",
            Title = "City Desk",
            Journal = "Daily Planet",
            Volume = "12",
            Issue = "4",
            Pages = "5-7",
            StandardNumber = "ISSN 1234-5678"
        });
        var periodicalPlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(periodical);

        periodical.Journal.Should().Be("Daily Planet");
        periodicalPlans.Single(plan => plan.Field == SourceManagementSourceField.Journal)
            .Text.Should().Be("Daily Planet");
        periodicalPlans.Single(plan => plan.Field == SourceManagementSourceField.Pages)
            .Text.Should().Be("5-7");

        var electronic = SourceManagementDialogPlanner.ProjectEntry(new Source
        {
            Type = SourceType.ElectronicSource,
            Tag = "Electronic2026",
            Title = "Online Notes",
            Publisher = "Example Archive",
            Url = "https://example.test/notes",
            AccessedDay = "4",
            AccessedMonth = "July",
            AccessedYear = "2026"
        });
        var electronicPlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(electronic);

        electronic.Url.Should().Be("https://example.test/notes");
        electronicPlans.Single(plan => plan.Field == SourceManagementSourceField.Url)
            .Text.Should().Be("https://example.test/notes");
        electronicPlans.Single(plan => plan.Field == SourceManagementSourceField.AccessedYear)
            .Text.Should().Be("2026");
    }

    [Fact]
    public void ProjectEntry_SeedsSourceManagerBreadthTypeFields()
    {
        var patent = SourceManagementDialogPlanner.ProjectEntry(new Source
        {
            Type = SourceType.Patent,
            Tag = "Patent2026",
            Inventor = "Lovelace, Ada",
            Title = "Analytical Engine Control",
            PatentNumber = "GB-1843-1",
            CountryRegion = "United Kingdom",
            StateProvince = "London",
            Month = "July",
            Day = "4",
            Year = "1843"
        });
        var patentPlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(patent);

        patent.Inventor.Should().Be("Lovelace, Ada");
        patentPlans.Single(plan => plan.Field == SourceManagementSourceField.PatentNumber)
            .Text.Should().Be("GB-1843-1");
        patentPlans.Single(plan => plan.Field == SourceManagementSourceField.CountryRegion)
            .Text.Should().Be("United Kingdom");

        var interview = SourceManagementDialogPlanner.ProjectEntry(new Source
        {
            Type = SourceType.Interview,
            Tag = "Interview2026",
            Interviewee = "Hopper, Grace",
            Interviewer = "Mauchly, Jean",
            Medium = "Recorded interview"
        });
        var interviewPlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(interview);

        interview.Interviewee.Should().Be("Hopper, Grace");
        interviewPlans.Single(plan => plan.Field == SourceManagementSourceField.Interviewer)
            .Text.Should().Be("Mauchly, Jean");
        interviewPlans.Single(plan => plan.Field == SourceManagementSourceField.Medium)
            .Text.Should().Be("Recorded interview");

        var misc = SourceManagementDialogPlanner.ProjectEntry(new Source
        {
            Type = SourceType.Misc,
            Tag = "Misc2026",
            Author = "Example Archive",
            SourceKind = "Manuscript",
            Medium = "Scan"
        });
        var miscPlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(misc);

        misc.SourceKind.Should().Be("Manuscript");
        miscPlans.Single(plan => plan.Field == SourceManagementSourceField.SourceKind)
            .Text.Should().Be("Manuscript");

        var film = SourceManagementDialogPlanner.ProjectEntry(new Source
        {
            Type = SourceType.Film,
            Tag = "Film2026",
            Director = "Kubrick, Stanley",
            ProducerName = "MGM",
            Writer = "Clarke, Arthur C.",
            Performer = "Dullea, Keir",
            ProductionCompany = "Metro-Goldwyn-Mayer",
            Medium = "Film"
        });
        var filmPlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(film);

        film.Director.Should().Be("Kubrick, Stanley");
        filmPlans.Single(plan => plan.Field == SourceManagementSourceField.ProductionCompany)
            .Text.Should().Be("Metro-Goldwyn-Mayer");

        var recording = SourceManagementDialogPlanner.ProjectEntry(new Source
        {
            Type = SourceType.SoundRecording,
            Tag = "Recording2026",
            Artist = "Holiday, Billie",
            Composer = "Strange, Lewis Allan",
            AlbumTitle = "Lady Sings",
            RecordingNumber = "RS-1",
            Medium = "LP"
        });
        var recordingPlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(recording);

        recording.Artist.Should().Be("Holiday, Billie");
        recordingPlans.Single(plan => plan.Field == SourceManagementSourceField.AlbumTitle)
            .Text.Should().Be("Lady Sings");

        var art = SourceManagementDialogPlanner.ProjectEntry(new Source
        {
            Type = SourceType.Art,
            Tag = "Art2026",
            Artist = "Kahlo, Frida",
            Medium = "Oil on masonite",
            Institution = "Museo Dolores Olmedo",
            City = "Mexico City"
        });
        var artPlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(art);

        art.Artist.Should().Be("Kahlo, Frida");
        artPlans.Single(plan => plan.Field == SourceManagementSourceField.Institution)
            .Text.Should().Be("Museo Dolores Olmedo");

        var internetSite = SourceManagementDialogPlanner.ProjectEntry(new Source
        {
            Type = SourceType.InternetSite,
            Tag = "Site2026",
            Author = "Example Archive",
            Publisher = "Example Site",
            Url = "https://example.test",
            AccessedYear = "2026"
        });
        var internetSitePlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(internetSite);

        internetSite.Url.Should().Be("https://example.test");
        internetSitePlans.Single(plan => plan.Field == SourceManagementSourceField.AccessedYear)
            .Text.Should().Be("2026");

        var performance = SourceManagementDialogPlanner.ProjectEntry(new Source
        {
            Type = SourceType.Performance,
            Tag = "Performance2026",
            Performer = "Royal Shakespeare Company",
            Conductor = "Doe, Jane",
            Theater = "Globe Theatre",
            City = "London",
            Month = "May",
            Day = "8"
        });
        var performancePlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(performance);

        performance.Performer.Should().Be("Royal Shakespeare Company");
        performancePlans.Single(plan => plan.Field == SourceManagementSourceField.Theater)
            .Text.Should().Be("Globe Theatre");

        var caseSource = SourceManagementDialogPlanner.ProjectEntry(new Source
        {
            Type = SourceType.Case,
            Tag = "Case2026",
            Author = "Brown",
            Title = "Brown v. Board of Education",
            CaseNumber = "1",
            Court = "U.S. Supreme Court",
            Reporter = "347 U.S. 483",
            CountryRegion = "United States",
            StateProvince = "District of Columbia",
            City = "Washington",
            Month = "May",
            Day = "17",
            Year = "1954"
        });
        var casePlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(caseSource);

        caseSource.CaseNumber.Should().Be("1");
        casePlans.Single(plan => plan.Field == SourceManagementSourceField.Court)
            .Text.Should().Be("U.S. Supreme Court");
        casePlans.Single(plan => plan.Field == SourceManagementSourceField.Reporter)
            .Text.Should().Be("347 U.S. 483");
    }

    [Fact]
    public void BuildSource_PreservesSourceManagerBreadthFieldsByType()
    {
        var patentEntry = SourceManagementDialogPlanner.CreateEntry(
            SourceType.Patent,
            new Dictionary<SourceManagementSourceField, string?>
            {
                [SourceManagementSourceField.Inventor] = " Lovelace, Ada ",
                [SourceManagementSourceField.Title] = " Analytical Engine Control ",
                [SourceManagementSourceField.PatentNumber] = " GB-1843-1 ",
                [SourceManagementSourceField.CountryRegion] = " United Kingdom ",
                [SourceManagementSourceField.StateProvince] = " London ",
                [SourceManagementSourceField.Month] = " July ",
                [SourceManagementSourceField.Day] = " 4 ",
                [SourceManagementSourceField.Year] = " 1843 ",
                [SourceManagementSourceField.Url] = " ignored "
            });
        var patent = SourceManagementDialogPlanner.BuildSource(patentEntry);

        patent.Type.Should().Be(SourceType.Patent);
        patent.Inventor.Should().Be("Lovelace, Ada");
        patent.PatentNumber.Should().Be("GB-1843-1");
        patent.CountryRegion.Should().Be("United Kingdom");
        patent.StateProvince.Should().Be("London");
        patent.Month.Should().Be("July");
        patent.Day.Should().Be("4");
        patent.Url.Should().BeNull();

        var interview = SourceManagementDialogPlanner.BuildSource(SourceManagementDialogPlanner.CreateEntry(
            SourceType.Interview,
            new Dictionary<SourceManagementSourceField, string?>
            {
                [SourceManagementSourceField.Interviewee] = " Hopper, Grace ",
                [SourceManagementSourceField.Interviewer] = " Mauchly, Jean ",
                [SourceManagementSourceField.Medium] = " Recorded interview ",
                [SourceManagementSourceField.PatentNumber] = " ignored "
            }));

        interview.Interviewee.Should().Be("Hopper, Grace");
        interview.Interviewer.Should().Be("Mauchly, Jean");
        interview.Medium.Should().Be("Recorded interview");
        interview.PatentNumber.Should().BeNull();

        var film = SourceManagementDialogPlanner.BuildSource(SourceManagementDialogPlanner.CreateEntry(
            SourceType.Film,
            new Dictionary<SourceManagementSourceField, string?>
            {
                [SourceManagementSourceField.Director] = " Kubrick, Stanley ",
                [SourceManagementSourceField.ProducerName] = " MGM ",
                [SourceManagementSourceField.Writer] = " Clarke, Arthur C. ",
                [SourceManagementSourceField.Performer] = " Dullea, Keir ",
                [SourceManagementSourceField.ProductionCompany] = " Metro-Goldwyn-Mayer ",
                [SourceManagementSourceField.Medium] = " Film ",
                [SourceManagementSourceField.Url] = " ignored "
            }));

        film.Director.Should().Be("Kubrick, Stanley");
        film.ProducerName.Should().Be("MGM");
        film.Writer.Should().Be("Clarke, Arthur C.");
        film.Performer.Should().Be("Dullea, Keir");
        film.ProductionCompany.Should().Be("Metro-Goldwyn-Mayer");
        film.Medium.Should().Be("Film");
        film.Url.Should().BeNull();

        var recording = SourceManagementDialogPlanner.BuildSource(SourceManagementDialogPlanner.CreateEntry(
            SourceType.SoundRecording,
            new Dictionary<SourceManagementSourceField, string?>
            {
                [SourceManagementSourceField.Artist] = " Holiday, Billie ",
                [SourceManagementSourceField.Composer] = " Strange, Lewis Allan ",
                [SourceManagementSourceField.Conductor] = " Jones, Quincy ",
                [SourceManagementSourceField.Performer] = " Holiday, Billie ",
                [SourceManagementSourceField.ProducerName] = " Norman Granz ",
                [SourceManagementSourceField.AlbumTitle] = " Lady Sings ",
                [SourceManagementSourceField.RecordingNumber] = " RS-1 ",
                [SourceManagementSourceField.Medium] = " LP ",
                [SourceManagementSourceField.Theater] = " ignored "
            }));

        recording.Artist.Should().Be("Holiday, Billie");
        recording.Composer.Should().Be("Strange, Lewis Allan");
        recording.Conductor.Should().Be("Jones, Quincy");
        recording.Performer.Should().Be("Holiday, Billie");
        recording.ProducerName.Should().Be("Norman Granz");
        recording.AlbumTitle.Should().Be("Lady Sings");
        recording.RecordingNumber.Should().Be("RS-1");
        recording.Medium.Should().Be("LP");
        recording.Theater.Should().BeNull();

        var art = SourceManagementDialogPlanner.BuildSource(SourceManagementDialogPlanner.CreateEntry(
            SourceType.Art,
            new Dictionary<SourceManagementSourceField, string?>
            {
                [SourceManagementSourceField.Artist] = " Kahlo, Frida ",
                [SourceManagementSourceField.Institution] = " Museo Dolores Olmedo ",
                [SourceManagementSourceField.City] = " Mexico City ",
                [SourceManagementSourceField.Medium] = " Oil on masonite ",
                [SourceManagementSourceField.ProducerName] = " ignored "
            }));

        art.Artist.Should().Be("Kahlo, Frida");
        art.Institution.Should().Be("Museo Dolores Olmedo");
        art.City.Should().Be("Mexico City");
        art.Medium.Should().Be("Oil on masonite");
        art.ProducerName.Should().BeNull();

        var internetSite = SourceManagementDialogPlanner.BuildSource(SourceManagementDialogPlanner.CreateEntry(
            SourceType.InternetSite,
            new Dictionary<SourceManagementSourceField, string?>
            {
                [SourceManagementSourceField.Author] = " Example Archive ",
                [SourceManagementSourceField.Publisher] = " Example Site ",
                [SourceManagementSourceField.Url] = " https://example.test ",
                [SourceManagementSourceField.AccessedYear] = " 2026 ",
                [SourceManagementSourceField.RecordingNumber] = " ignored "
            }));

        internetSite.Author.Should().Be("Example Archive");
        internetSite.Publisher.Should().Be("Example Site");
        internetSite.Url.Should().Be("https://example.test");
        internetSite.AccessedYear.Should().Be("2026");
        internetSite.RecordingNumber.Should().BeNull();

        var performance = SourceManagementDialogPlanner.BuildSource(SourceManagementDialogPlanner.CreateEntry(
            SourceType.Performance,
            new Dictionary<SourceManagementSourceField, string?>
            {
                [SourceManagementSourceField.Performer] = " Royal Shakespeare Company ",
                [SourceManagementSourceField.Conductor] = " Doe, Jane ",
                [SourceManagementSourceField.Theater] = " Globe Theatre ",
                [SourceManagementSourceField.City] = " London ",
                [SourceManagementSourceField.Month] = " May ",
                [SourceManagementSourceField.Day] = " 8 ",
                [SourceManagementSourceField.Medium] = " Stage performance ",
                [SourceManagementSourceField.AlbumTitle] = " ignored "
            }));

        performance.Performer.Should().Be("Royal Shakespeare Company");
        performance.Conductor.Should().Be("Doe, Jane");
        performance.Theater.Should().Be("Globe Theatre");
        performance.City.Should().Be("London");
        performance.Month.Should().Be("May");
        performance.Day.Should().Be("8");
        performance.Medium.Should().Be("Stage performance");
        performance.AlbumTitle.Should().BeNull();

        var caseSource = SourceManagementDialogPlanner.BuildSource(SourceManagementDialogPlanner.CreateEntry(
            SourceType.Case,
            new Dictionary<SourceManagementSourceField, string?>
            {
                [SourceManagementSourceField.Author] = " Brown ",
                [SourceManagementSourceField.Title] = " Brown v. Board of Education ",
                [SourceManagementSourceField.CaseNumber] = " 1 ",
                [SourceManagementSourceField.Court] = " U.S. Supreme Court ",
                [SourceManagementSourceField.Reporter] = " 347 U.S. 483 ",
                [SourceManagementSourceField.CountryRegion] = " United States ",
                [SourceManagementSourceField.StateProvince] = " District of Columbia ",
                [SourceManagementSourceField.City] = " Washington ",
                [SourceManagementSourceField.Month] = " May ",
                [SourceManagementSourceField.Day] = " 17 ",
                [SourceManagementSourceField.Year] = " 1954 ",
                [SourceManagementSourceField.PatentNumber] = " ignored ",
                [SourceManagementSourceField.Url] = " ignored "
            }));

        caseSource.Type.Should().Be(SourceType.Case);
        caseSource.Author.Should().Be("Brown");
        caseSource.Title.Should().Be("Brown v. Board of Education");
        caseSource.CaseNumber.Should().Be("1");
        caseSource.Court.Should().Be("U.S. Supreme Court");
        caseSource.Reporter.Should().Be("347 U.S. 483");
        caseSource.CountryRegion.Should().Be("United States");
        caseSource.StateProvince.Should().Be("District of Columbia");
        caseSource.City.Should().Be("Washington");
        caseSource.Month.Should().Be("May");
        caseSource.Day.Should().Be("17");
        caseSource.Year.Should().Be("1954");
        caseSource.PatentNumber.Should().BeNull();
        caseSource.Url.Should().BeNull();
    }

    [Fact]
    public void CreateEntry_ImportsSemicolonSeparatedPersonalAuthors()
    {
        var entry = SourceManagementDialogPlanner.CreateEntry(
            SourceType.Book,
            new Dictionary<SourceManagementSourceField, string?>
            {
                [SourceManagementSourceField.Author] = " Jane Q. Doe ; ; Smith, Alex "
            });

        entry.Author.Should().Be("Jane Q. Doe; Alex Smith");
        entry.PersonalAuthors.Should().Equal(
            SourceAuthorPerson.Create("Jane", "Q.", "Doe"),
            SourceAuthorPerson.Create("Alex", string.Empty, "Smith"));
        entry.CorporateAuthor.Should().BeNull();
    }

    [Fact]
    public void CreateEntry_KeepsCorporateAndAmbiguousAuthorsAsCorporate()
    {
        var entry = SourceManagementDialogPlanner.CreateEntry(
            SourceType.Book,
            new Dictionary<SourceManagementSourceField, string?>
            {
                [SourceManagementSourceField.Author] = "World Health Organization"
            });

        entry.Author.Should().Be("World Health Organization");
        entry.PersonalAuthors.Should().BeEmpty();
        entry.CorporateAuthor.Should().Be("World Health Organization");

        var ambiguous = SourceManagementDialogPlanner.CreateEntry(
            SourceType.Book,
            new Dictionary<SourceManagementSourceField, string?>
            {
                [SourceManagementSourceField.Author] = "NASA; ESA"
            });

        ambiguous.PersonalAuthors.Should().BeEmpty();
        ambiguous.CorporateAuthor.Should().Be("NASA; ESA");
    }

    [Fact]
    public void PrimaryAuthorEditor_PersonalRowsNormalizeAndApplyToEntry()
    {
        var state = new SourceManagementAuthorEditorState(
            SourceManagementAuthorEditorMode.Personal,
            [
                new SourceManagementAuthorPersonRow(" Jane ", " Q. ", " Doe "),
                new SourceManagementAuthorPersonRow(" ", " ", " "),
                new SourceManagementAuthorPersonRow(" Alex ", string.Empty, " Smith ")
            ],
            "Ignored Organization");

        var normalized = SourceManagementDialogPlanner.NormalizePrimaryAuthorEditorState(state);
        var entry = SourceManagementDialogPlanner.ApplyPrimaryAuthorEditorState(
            new SourceManagementSourceEntry("Ref", "Old Author", string.Empty, string.Empty, string.Empty),
            state);

        normalized.Mode.Should().Be(SourceManagementAuthorEditorMode.Personal);
        normalized.PersonalRows.Should().Equal(
            new SourceManagementAuthorPersonRow("Jane", "Q.", "Doe"),
            new SourceManagementAuthorPersonRow("Alex", string.Empty, "Smith"));
        normalized.CorporateAuthor.Should().BeEmpty();
        SourceManagementDialogPlanner.BuildPrimaryAuthorDisplayText(state)
            .Should().Be("Jane Q. Doe; Alex Smith");
        entry.Author.Should().Be("Jane Q. Doe; Alex Smith");
        entry.PersonalAuthors.Should().Equal(
            SourceAuthorPerson.Create("Jane", "Q.", "Doe"),
            SourceAuthorPerson.Create("Alex", string.Empty, "Smith"));
        entry.CorporateAuthor.Should().BeNull();
    }

    [Fact]
    public void PrimaryAuthorEditor_CorporateModeAppliesAuthorAndClearsPersonalRows()
    {
        var state = new SourceManagementAuthorEditorState(
            SourceManagementAuthorEditorMode.Corporate,
            [new SourceManagementAuthorPersonRow("Jane", "Q.", "Doe")],
            " World Health Organization ");

        var entry = SourceManagementDialogPlanner.ApplyPrimaryAuthorEditorState(
            new SourceManagementSourceEntry("Ref", "Jane Q. Doe", string.Empty, string.Empty, string.Empty)
            {
                PersonalAuthors = [SourceAuthorPerson.Create("Jane", "Q.", "Doe")]
            },
            state);

        SourceManagementDialogPlanner.BuildPrimaryAuthorDisplayText(state)
            .Should().Be("World Health Organization");
        entry.Author.Should().Be("World Health Organization");
        entry.PersonalAuthors.Should().BeEmpty();
        entry.CorporateAuthor.Should().Be("World Health Organization");
    }

    [Fact]
    public void PrimaryAuthorEditorSession_ProjectsModeEnablementAndGuaranteedPersonalRow()
    {
        var session = new SourceManagementAuthorEditorSession(
            new SourceManagementAuthorEditorState(
                SourceManagementAuthorEditorMode.Corporate,
                [],
                "World Health Organization"));

        var plan = session.CurrentPlan;

        plan.Mode.Should().Be(SourceManagementAuthorEditorMode.Corporate);
        plan.PersonalRows.Should().ContainSingle()
            .Which.Should().Be(new SourceManagementAuthorPersonRow(string.Empty, string.Empty, string.Empty));
        plan.CorporateAuthor.Should().Be("World Health Organization");
        plan.PersonalAuthorFieldsEnabled.Should().BeFalse();
        plan.CorporateAuthorFieldEnabled.Should().BeTrue();
    }

    [Fact]
    public void PrimaryAuthorRowCollection_RendersAndReadsThroughNativeAdapters()
    {
        var nativeRows = new List<string[]>();
        var rows = new SourceManagementAuthorRowCollection<string[]>(
            row => [row.First, row.Middle, row.Last],
            row => new SourceManagementAuthorPersonRow(row[0], row[1], row[2]),
            nativeRows.Add,
            nativeRows.Clear);

        rows.Render(
        [
            new SourceManagementAuthorPersonRow("Ada", string.Empty, "Lovelace"),
            new SourceManagementAuthorPersonRow("Grace", "B.", "Hopper")
        ]);
        nativeRows[0][1] = "Augusta";

        rows.Read().Should().Equal(
            new SourceManagementAuthorPersonRow("Ada", "Augusta", "Lovelace"),
            new SourceManagementAuthorPersonRow("Grace", "B.", "Hopper"));

        rows.Render([new SourceManagementAuthorPersonRow("Katherine", string.Empty, "Johnson")]);
        nativeRows.Should().ContainSingle();
        rows.Read().Should().ContainSingle()
            .Which.Should().Be(new SourceManagementAuthorPersonRow("Katherine", string.Empty, "Johnson"));
    }

    [Fact]
    public void PrimaryAuthorEditorSession_AddsRemovesAndClearsFinalPersonalRow()
    {
        var session = new SourceManagementAuthorEditorSession(
            new SourceManagementAuthorEditorState(
                SourceManagementAuthorEditorMode.Personal,
                [],
                string.Empty));
        var ada = new SourceManagementAuthorPersonRow("Ada", string.Empty, "Lovelace");
        var grace = new SourceManagementAuthorPersonRow("Grace", "B.", "Hopper");

        var added = session.AddPersonalAuthorRow([ada], " In-progress organization ");
        var removed = session.RemoveFinalPersonalAuthorRow([ada, grace], added.CorporateAuthor);
        var cleared = session.RemoveFinalPersonalAuthorRow([ada], removed.CorporateAuthor);

        added.PersonalRows.Should().Equal(
            ada,
            new SourceManagementAuthorPersonRow(string.Empty, string.Empty, string.Empty));
        added.CorporateAuthor.Should().Be(" In-progress organization ");
        removed.PersonalRows.Should().Equal(ada);
        cleared.PersonalRows.Should().ContainSingle()
            .Which.Should().Be(new SourceManagementAuthorPersonRow(string.Empty, string.Empty, string.Empty));
    }

    [Fact]
    public void PrimaryAuthorEditorSession_ModeTransitionsPreserveLiveInputsAndPlanEnablement()
    {
        var session = new SourceManagementAuthorEditorSession(
            new SourceManagementAuthorEditorState(
                SourceManagementAuthorEditorMode.Personal,
                [],
                string.Empty));
        var person = new SourceManagementAuthorPersonRow(" Ada ", string.Empty, " Lovelace ");

        var corporate = session.SelectMode(
            SourceManagementAuthorEditorMode.Corporate,
            [person],
            " Analytical Engine Society ");
        var personal = session.SelectMode(
            SourceManagementAuthorEditorMode.Personal,
            corporate.PersonalRows,
            corporate.CorporateAuthor);

        corporate.PersonalRows.Should().Equal(person);
        corporate.CorporateAuthor.Should().Be(" Analytical Engine Society ");
        corporate.PersonalAuthorFieldsEnabled.Should().BeFalse();
        corporate.CorporateAuthorFieldEnabled.Should().BeTrue();
        personal.PersonalRows.Should().Equal(person);
        personal.CorporateAuthor.Should().Be(" Analytical Engine Society ");
        personal.PersonalAuthorFieldsEnabled.Should().BeTrue();
        personal.CorporateAuthorFieldEnabled.Should().BeFalse();
    }

    [Fact]
    public void PrimaryAuthorEditorSession_AcceptNormalizesTheSelectedMode()
    {
        var corporateSession = new SourceManagementAuthorEditorSession(
            new SourceManagementAuthorEditorState(
                SourceManagementAuthorEditorMode.Personal,
                [],
                string.Empty));
        corporateSession.SelectMode(
            SourceManagementAuthorEditorMode.Corporate,
            [new SourceManagementAuthorPersonRow("Ada", string.Empty, "Lovelace")],
            " Analytical Engine Society ");

        var corporate = corporateSession.Accept(
            [new SourceManagementAuthorPersonRow("Ignored", string.Empty, "Person")],
            " Analytical Engine Society ");
        var personalSession = new SourceManagementAuthorEditorSession(
            new SourceManagementAuthorEditorState(
                SourceManagementAuthorEditorMode.Personal,
                [],
                string.Empty));
        var personal = personalSession.Accept(
            [
                new SourceManagementAuthorPersonRow(" Ada ", string.Empty, " Lovelace "),
                new SourceManagementAuthorPersonRow(" ", " ", " ")
            ],
            "Ignored organization");

        corporate.Mode.Should().Be(SourceManagementAuthorEditorMode.Corporate);
        corporate.PersonalRows.Should().BeEmpty();
        corporate.CorporateAuthor.Should().Be("Analytical Engine Society");
        personal.Mode.Should().Be(SourceManagementAuthorEditorMode.Personal);
        personal.PersonalRows.Should().Equal(
            new SourceManagementAuthorPersonRow("Ada", string.Empty, "Lovelace"));
        personal.CorporateAuthor.Should().BeEmpty();
    }

    [Fact]
    public void PrimaryAuthorEditor_ProjectsExistingStructuredAuthorsThroughFieldRefresh()
    {
        var source = new Source
        {
            Type = SourceType.Book,
            Tag = "Ada1843",
            Author = "Ada Lovelace",
            PersonalAuthors = [SourceAuthorPerson.Create("Ada", string.Empty, "Lovelace")],
            Title = "Notes"
        };
        var entry = SourceManagementDialogPlanner.ProjectEntry(source);

        var state = SourceManagementDialogPlanner.ProjectPrimaryAuthorEditorState(entry);
        var applied = SourceManagementDialogPlanner.ApplyPrimaryAuthorEditorState(entry, state);
        var refreshedValues = SourceManagementDialogPlanner
            .BuildEntryFieldPlans(applied)
            .ToDictionary(plan => plan.Field, plan => (string?)plan.Text);
        var refreshedEntry = SourceManagementDialogPlanner.CreateEntry(
            SourceType.Book,
            refreshedValues,
            applied);

        state.Mode.Should().Be(SourceManagementAuthorEditorMode.Personal);
        state.PersonalRows.Should().ContainSingle()
            .Which.Should().Be(new SourceManagementAuthorPersonRow("Ada", string.Empty, "Lovelace"));
        refreshedEntry.Author.Should().Be("Ada Lovelace");
        refreshedEntry.PersonalAuthors.Should().ContainSingle()
            .Which.Should().Be(SourceAuthorPerson.Create("Ada", string.Empty, "Lovelace"));
        refreshedEntry.CorporateAuthor.Should().BeNull();
    }

    [Fact]
    public void PrimaryAuthorEditor_FreeTextFallbackKeepsTextboxCreateEntryPolicy()
    {
        var entry = SourceManagementDialogPlanner.CreateEntry(
            SourceType.Book,
            new Dictionary<SourceManagementSourceField, string?>
            {
                [SourceManagementSourceField.Author] = "Ada Lovelace"
            });

        var state = SourceManagementDialogPlanner.ProjectPrimaryAuthorEditorState(entry);

        entry.Author.Should().Be("Ada Lovelace");
        entry.PersonalAuthors.Should().BeEmpty();
        entry.CorporateAuthor.Should().Be("Ada Lovelace");
        state.Mode.Should().Be(SourceManagementAuthorEditorMode.Corporate);
        state.CorporateAuthor.Should().Be("Ada Lovelace");

        var blankState = SourceManagementDialogPlanner.ProjectPrimaryAuthorEditorState(
            new SourceManagementSourceEntry(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty));
        blankState.Mode.Should().Be(SourceManagementAuthorEditorMode.Personal);
        blankState.PersonalRows.Should().BeEmpty();
        blankState.CorporateAuthor.Should().BeEmpty();
    }

    [Fact]
    public void CreateEntry_PreservesExistingSinglePersonRowsWhenDisplayIsUnchanged()
    {
        var previous = SourceManagementDialogPlanner.ProjectEntry(new Source
        {
            Author = "Ada Lovelace",
            PersonalAuthors = [SourceAuthorPerson.Create("Ada", string.Empty, "Lovelace")]
        });

        var entry = SourceManagementDialogPlanner.CreateEntry(
            SourceType.Book,
            new Dictionary<SourceManagementSourceField, string?>
            {
                [SourceManagementSourceField.Author] = "Ada Lovelace"
            },
            previous);

        entry.PersonalAuthors.Should().ContainSingle()
            .Which.Should().Be(SourceAuthorPerson.Create("Ada", string.Empty, "Lovelace"));
        entry.CorporateAuthor.Should().BeNull();
    }

    [Fact]
    public void CreateEntry_PreservesExistingSinglePersonRowsWhenEdited()
    {
        var previous = SourceManagementDialogPlanner.ProjectEntry(new Source
        {
            Author = "Ada Lovelace",
            PersonalAuthors = [SourceAuthorPerson.Create("Ada", string.Empty, "Lovelace")]
        });

        var entry = SourceManagementDialogPlanner.CreateEntry(
            SourceType.Book,
            new Dictionary<SourceManagementSourceField, string?>
            {
                [SourceManagementSourceField.Author] = "Augusta Ada King"
            },
            previous);

        entry.Author.Should().Be("Augusta Ada King");
        entry.PersonalAuthors.Should().ContainSingle()
            .Which.Should().Be(SourceAuthorPerson.Create("Augusta", "Ada", "King"));
        entry.CorporateAuthor.Should().BeNull();
    }

    [Fact]
    public void CreateEntry_ImportsEditorAndTranslatorContributorRows()
    {
        var entry = SourceManagementDialogPlanner.CreateEntry(
            SourceType.Book,
            new Dictionary<SourceManagementSourceField, string?>
            {
                [SourceManagementSourceField.Editor] = " Jane Q. Doe ; Smith, Alex ",
                [SourceManagementSourceField.Translator] = " Plato "
            });

        entry.Editor.Should().Be("Jane Q. Doe; Alex Smith");
        entry.Editors.Should().Equal(
            SourceAuthorPerson.Create("Jane", "Q.", "Doe"),
            SourceAuthorPerson.Create("Alex", string.Empty, "Smith"));
        entry.Translator.Should().Be("Plato");
        entry.Translators.Should().ContainSingle()
            .Which.Should().Be(SourceAuthorPerson.Create(null, null, "Plato"));
    }

    [Fact]
    public void DescribeSource_FormatsAuthorYearTitleAndGracefulFallbacks()
    {
        SourceManagementDialogPlanner.DescribeSource(new Source
            {
                Author = " Knuth ",
                Year = " 1997 ",
                Title = " TAOCP "
            })
            .Should().Be("Knuth (1997) - TAOCP");

        SourceManagementDialogPlanner.DescribeSource(new Source { Tag = "Anon1" })
            .Should().Be("Anon1");

        SourceManagementDialogPlanner.DescribeSource(new Source())
            .Should().Be(SourceManagementDialogPlanner.UntitledSourceLabel);
    }

    [Fact]
    public void TryBuildCitationSource_RequiresDetailsBeyondTagAndBuildsTypedSource()
    {
        SourceManagementDialogPlanner.TryBuildCitationSource(
                new SourceManagementSourceEntry("TagOnly", string.Empty, string.Empty, string.Empty, string.Empty),
                out var rejected,
                out var validation)
            .Should().BeFalse();

        rejected.Should().BeNull();
        validation.Should().Be(new SourceManagementValidation(
            SourceManagementValidationTarget.SourceFields,
            SourceManagementDialogPlanner.MissingCitationSourceDataMessage));

        SourceManagementDialogPlanner.TryBuildCitationSource(
                new SourceManagementSourceEntry(
                    SourceType.WebSite,
                    "K97",
                    "Knuth",
                    "TAOCP",
                    "1997",
                    "  Site ",
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    " https://example.test ",
                    " 3 May 2024 "),
                out var source,
                out validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        source.Should().NotBeNull();
        source!.Tag.Should().Be("K97");
        source.Type.Should().Be(SourceType.WebSite);
        source.Publisher.Should().Be("Site");
        source.Url.Should().Be("https://example.test");
        source.Accessed.Should().Be("3 May 2024");

        SourceManagementDialogPlanner.TryBuildCitationSource(
                SourceManagementDialogPlanner.CreateEntry(
                    SourceType.WebSite,
                    new Dictionary<SourceManagementSourceField, string?>
                    {
                        [SourceManagementSourceField.Title] = " Structured web source ",
                        [SourceManagementSourceField.AccessedDay] = " 3 ",
                        [SourceManagementSourceField.AccessedMonth] = " May ",
                        [SourceManagementSourceField.AccessedYear] = " 2024 "
                    }),
                out source,
                out validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        source.Should().NotBeNull();
        source!.Accessed.Should().BeNull();
        source.AccessedDay.Should().Be("3");
        source.AccessedMonth.Should().Be("May");
        source.AccessedYear.Should().Be("2024");

        SourceManagementDialogPlanner.TryBuildCitationSource(
                new SourceManagementSourceEntry(
                    SourceType.Report,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty)
                {
                    Institution = "National Bureau of Standards"
                },
                out source,
                out validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        source.Should().NotBeNull();
        source!.Type.Should().Be(SourceType.Report);
        source.Institution.Should().Be("National Bureau of Standards");

        SourceManagementDialogPlanner.TryBuildCitationSource(
                SourceManagementDialogPlanner.CreateEntry(
                    SourceType.Book,
                    new Dictionary<SourceManagementSourceField, string?>
                    {
                        [SourceManagementSourceField.Editor] = " Jane Editor ",
                        [SourceManagementSourceField.Translator] = " Taylor, Sam "
                    }),
                out source,
                out validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        source.Should().NotBeNull();
        source!.Editors.Should().ContainSingle()
            .Which.Should().Be(SourceAuthorPerson.Create("Jane", string.Empty, "Editor"));
        source.Translators.Should().ContainSingle()
            .Which.Should().Be(SourceAuthorPerson.Create("Sam", string.Empty, "Taylor"));
    }

    [Fact]
    public void TryBuildManagedSource_AcceptsAnySourceFieldAndBuildsSelectedType()
    {
        var existing = new Source
        {
            Tag = "Old",
            Type = SourceType.JournalArticle,
            Journal = "Journal",
            Volume = "10",
            Issue = "2",
            Pages = "12-20",
            Url = "https://example.test",
            Accessed = "2024-01-02"
        };

        var entry = SourceManagementDialogPlanner.ProjectEntry(existing) with { Tag = "New" };
        SourceManagementDialogPlanner.TryBuildManagedSource(
                entry,
                existing,
                out var source,
                out var validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        source.Should().NotBeNull();
        source!.Tag.Should().Be("New");
        source.Type.Should().Be(SourceType.JournalArticle);
        source.Journal.Should().Be("Journal");
        source.Volume.Should().Be("10");
        source.Issue.Should().Be("2");
        source.Pages.Should().Be("12-20");
        source.Url.Should().BeNull();
        source.Accessed.Should().BeNull();
    }

    [Fact]
    public void TryBuildManagedSource_PreservesExistingStructuredAuthorsWhenPlainEntryTextIsUnchanged()
    {
        var existing = new Source
        {
            Tag = "Ada1843",
            Author = "Ada Lovelace",
            PersonalAuthors = [SourceAuthorPerson.Create("Ada", string.Empty, "Lovelace")],
            Title = "Notes"
        };
        var entry = new SourceManagementSourceEntry(
            SourceType.Book,
            "Ada1843",
            "Ada Lovelace",
            "Notes revised",
            "1843",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);

        SourceManagementDialogPlanner.TryBuildManagedSource(entry, existing, out var source, out var validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        source.Should().NotBeNull();
        source!.Author.Should().Be("Ada Lovelace");
        source.PersonalAuthors.Should().ContainSingle()
            .Which.Should().Be(SourceAuthorPerson.Create("Ada", string.Empty, "Lovelace"));
        source.CorporateAuthor.Should().BeNull();
        source.Title.Should().Be("Notes revised");
    }

    [Fact]
    public void BuildSource_ClearsFieldsThatDoNotApplyToSelectedType()
    {
        var source = SourceManagementDialogPlanner.BuildSource(
            new SourceManagementSourceEntry(
                SourceType.Book,
                "B",
                "Author",
                "Title",
                "2026",
                "Publisher",
                "Reading",
                "2",
                "978-0000000000",
                "Short",
                "Notes",
                "Journal",
                "10",
                "2",
                "12-20",
                "https://example.test",
                "3 May 2024")
            {
                Institution = "Research Institute"
            });

        source.Type.Should().Be(SourceType.Book);
        source.Institution.Should().BeNull();
        source.BookTitle.Should().BeNull();
        source.ConferenceName.Should().BeNull();
        source.ChapterNumber.Should().BeNull();
        source.Publisher.Should().Be("Publisher");
        source.City.Should().Be("Reading");
        source.Edition.Should().Be("2");
        source.StandardNumber.Should().Be("978-0000000000");
        source.ShortTitle.Should().Be("Short");
        source.Comments.Should().Be("Notes");
        source.Journal.Should().BeNull();
        source.Volume.Should().BeNull();
        source.Issue.Should().BeNull();
        source.Pages.Should().BeNull();
        source.Url.Should().BeNull();
        source.Accessed.Should().BeNull();
        source.AccessedDay.Should().BeNull();
        source.AccessedMonth.Should().BeNull();
        source.AccessedYear.Should().BeNull();

        var webSource = SourceManagementDialogPlanner.BuildSource(
            SourceManagementDialogPlanner.ProjectEntry(source) with
            {
                Type = SourceType.WebSite,
                Publisher = "Site",
                Url = "https://example.test",
                Accessed = "3 May 2024",
                AccessedDay = "3",
                AccessedMonth = "May",
                AccessedYear = "2024"
            });

        webSource.Type.Should().Be(SourceType.WebSite);
        webSource.Publisher.Should().Be("Site");
        webSource.BookTitle.Should().BeNull();
        webSource.ConferenceName.Should().BeNull();
        webSource.ChapterNumber.Should().BeNull();
        webSource.City.Should().BeNull();
        webSource.Edition.Should().BeNull();
        webSource.StandardNumber.Should().BeNull();
        webSource.ShortTitle.Should().Be("Short");
        webSource.Comments.Should().Be("Notes");
        webSource.Url.Should().Be("https://example.test");
        webSource.Accessed.Should().Be("3 May 2024");
        webSource.AccessedDay.Should().Be("3");
        webSource.AccessedMonth.Should().Be("May");
        webSource.AccessedYear.Should().Be("2024");

        var reportSource = SourceManagementDialogPlanner.BuildSource(
            SourceManagementDialogPlanner.ProjectEntry(source) with
            {
                Type = SourceType.Report,
                Institution = "Research Institute",
                City = "Geneva",
                Publisher = "Reports Office",
                StandardNumber = "R-2026-01",
                Journal = "Journal",
                Pages = "12-20",
                Url = "https://example.test",
                Accessed = "3 May 2024",
                AccessedDay = "3",
                AccessedMonth = "May",
                AccessedYear = "2024"
            });

        reportSource.Type.Should().Be(SourceType.Report);
        reportSource.Institution.Should().Be("Research Institute");
        reportSource.BookTitle.Should().BeNull();
        reportSource.ConferenceName.Should().BeNull();
        reportSource.ChapterNumber.Should().BeNull();
        reportSource.City.Should().Be("Geneva");
        reportSource.Publisher.Should().Be("Reports Office");
        reportSource.StandardNumber.Should().Be("R-2026-01");
        reportSource.ShortTitle.Should().Be("Short");
        reportSource.Comments.Should().Be("Notes");
        reportSource.Edition.Should().BeNull();
        reportSource.Journal.Should().BeNull();
        reportSource.Volume.Should().BeNull();
        reportSource.Issue.Should().BeNull();
        reportSource.Pages.Should().BeNull();
        reportSource.Url.Should().BeNull();
        reportSource.Accessed.Should().BeNull();
        reportSource.AccessedDay.Should().BeNull();
        reportSource.AccessedMonth.Should().BeNull();
        reportSource.AccessedYear.Should().BeNull();
    }

    [Fact]
    public void BuildSource_BookSectionPreservesContainingBookAndBookPublicationFields()
    {
        var entry = SourceManagementDialogPlanner.CreateEntry(
            SourceType.BookSection,
            new Dictionary<SourceManagementSourceField, string?>
            {
                [SourceManagementSourceField.Tag] = " Ch1 ",
                [SourceManagementSourceField.Author] = " Chapter Author ",
                [SourceManagementSourceField.Editor] = " Ellen Editor ",
                [SourceManagementSourceField.Translator] = " Theo Translator ",
                [SourceManagementSourceField.Title] = " Chapter Title ",
                [SourceManagementSourceField.BookTitle] = " Containing Book ",
                [SourceManagementSourceField.Year] = " 2026 ",
                [SourceManagementSourceField.ChapterNumber] = " 3 ",
                [SourceManagementSourceField.Pages] = " 12-20 ",
                [SourceManagementSourceField.City] = " London ",
                [SourceManagementSourceField.Publisher] = " Test Press ",
                [SourceManagementSourceField.Edition] = " 2 ",
                [SourceManagementSourceField.StandardNumber] = " ISBN-1 ",
                [SourceManagementSourceField.Journal] = " Journal ",
                [SourceManagementSourceField.Url] = " https://example.test ",
                [SourceManagementSourceField.Accessed] = " 3 May 2024 "
            })
            with
            {
                Institution = "Research Institute"
            };

        var source = SourceManagementDialogPlanner.BuildSource(entry);

        source.Type.Should().Be(SourceType.BookSection);
        source.Author.Should().Be("Chapter Author");
        source.Editors.Should().ContainSingle()
            .Which.Should().Be(SourceAuthorPerson.Create("Ellen", string.Empty, "Editor"));
        source.Translators.Should().ContainSingle()
            .Which.Should().Be(SourceAuthorPerson.Create("Theo", string.Empty, "Translator"));
        source.Title.Should().Be("Chapter Title");
        source.BookTitle.Should().Be("Containing Book");
        source.Year.Should().Be("2026");
        source.ChapterNumber.Should().Be("3");
        source.Pages.Should().Be("12-20");
        source.City.Should().Be("London");
        source.Publisher.Should().Be("Test Press");
        source.Edition.Should().Be("2");
        source.StandardNumber.Should().Be("ISBN-1");
        source.Institution.Should().BeNull();
        source.Journal.Should().BeNull();
        source.Url.Should().BeNull();
        source.Accessed.Should().BeNull();
    }

    [Fact]
    public void BuildSource_ConferenceProceedingsPreservesConferenceNameAndPublicationFields()
    {
        var entry = SourceManagementDialogPlanner.CreateEntry(
            SourceType.ConferenceProceedings,
            new Dictionary<SourceManagementSourceField, string?>
            {
                [SourceManagementSourceField.Tag] = " Conf2026 ",
                [SourceManagementSourceField.Author] = " Paper Author ",
                [SourceManagementSourceField.Title] = " Proceedings Paper ",
                [SourceManagementSourceField.BookTitle] = " Containing Book ",
                [SourceManagementSourceField.ConferenceName] = " Proceedings of the Example Conference ",
                [SourceManagementSourceField.Year] = " 2026 ",
                [SourceManagementSourceField.ChapterNumber] = " 3 ",
                [SourceManagementSourceField.Pages] = " 101-109 ",
                [SourceManagementSourceField.City] = " Berlin ",
                [SourceManagementSourceField.Publisher] = " ACM ",
                [SourceManagementSourceField.Edition] = " 2 ",
                [SourceManagementSourceField.StandardNumber] = " ISBN-CP-1 ",
                [SourceManagementSourceField.Journal] = " Journal ",
                [SourceManagementSourceField.Url] = " https://example.test ",
                [SourceManagementSourceField.Accessed] = " 3 May 2024 "
            })
            with
            {
                Institution = "Research Institute"
            };

        var source = SourceManagementDialogPlanner.BuildSource(entry);

        source.Type.Should().Be(SourceType.ConferenceProceedings);
        source.Author.Should().Be("Paper Author");
        source.Title.Should().Be("Proceedings Paper");
        source.ConferenceName.Should().Be("Proceedings of the Example Conference");
        source.Year.Should().Be("2026");
        source.Pages.Should().Be("101-109");
        source.City.Should().Be("Berlin");
        source.Publisher.Should().Be("ACM");
        source.StandardNumber.Should().Be("ISBN-CP-1");
        source.BookTitle.Should().BeNull();
        source.ChapterNumber.Should().BeNull();
        source.Institution.Should().BeNull();
        source.Edition.Should().BeNull();
        source.Journal.Should().BeNull();
        source.Url.Should().BeNull();
        source.Accessed.Should().BeNull();
    }

    [Fact]
    public void BuildSource_NewWordSourceTypesPreserveOnlyApplicableFields()
    {
        var periodical = SourceManagementDialogPlanner.BuildSource(
            SourceManagementDialogPlanner.CreateEntry(
                SourceType.ArticleInPeriodical,
                new Dictionary<SourceManagementSourceField, string?>
                {
                    [SourceManagementSourceField.Tag] = " Periodical2026 ",
                    [SourceManagementSourceField.Author] = " Roe ",
                    [SourceManagementSourceField.Title] = " City Desk ",
                    [SourceManagementSourceField.Year] = " 2026 ",
                    [SourceManagementSourceField.Journal] = " Daily Planet ",
                    [SourceManagementSourceField.Volume] = " 12 ",
                    [SourceManagementSourceField.Issue] = " 4 ",
                    [SourceManagementSourceField.Pages] = " 5-7 ",
                    [SourceManagementSourceField.StandardNumber] = " ISSN 1234-5678 ",
                    [SourceManagementSourceField.Publisher] = " Not applicable ",
                    [SourceManagementSourceField.Url] = " https://example.test "
                }));

        periodical.Type.Should().Be(SourceType.ArticleInPeriodical);
        periodical.Author.Should().Be("Roe");
        periodical.Title.Should().Be("City Desk");
        periodical.Journal.Should().Be("Daily Planet");
        periodical.Volume.Should().Be("12");
        periodical.Issue.Should().Be("4");
        periodical.Pages.Should().Be("5-7");
        periodical.StandardNumber.Should().Be("ISSN 1234-5678");
        periodical.Publisher.Should().BeNull();
        periodical.Url.Should().BeNull();

        var electronic = SourceManagementDialogPlanner.BuildSource(
            SourceManagementDialogPlanner.CreateEntry(
                SourceType.ElectronicSource,
                new Dictionary<SourceManagementSourceField, string?>
                {
                    [SourceManagementSourceField.Tag] = " Electronic2026 ",
                    [SourceManagementSourceField.Author] = " Ada ",
                    [SourceManagementSourceField.Title] = " Online Notes ",
                    [SourceManagementSourceField.Year] = " 2026 ",
                    [SourceManagementSourceField.Publisher] = " Example Archive ",
                    [SourceManagementSourceField.Url] = " https://example.test/notes ",
                    [SourceManagementSourceField.AccessedDay] = " 4 ",
                    [SourceManagementSourceField.AccessedMonth] = " July ",
                    [SourceManagementSourceField.AccessedYear] = " 2026 ",
                    [SourceManagementSourceField.Journal] = " Not applicable ",
                    [SourceManagementSourceField.Pages] = " 5-7 "
                }));

        electronic.Type.Should().Be(SourceType.ElectronicSource);
        electronic.Author.Should().Be("Ada");
        electronic.Title.Should().Be("Online Notes");
        electronic.Publisher.Should().Be("Example Archive");
        electronic.Url.Should().Be("https://example.test/notes");
        electronic.AccessedDay.Should().Be("4");
        electronic.AccessedMonth.Should().Be("July");
        electronic.AccessedYear.Should().Be("2026");
        electronic.Journal.Should().BeNull();
        electronic.Pages.Should().BeNull();
    }

    [Fact]
    public void BuildSource_ClearsContributorRolesOutsideBookFamilies()
    {
        var source = SourceManagementDialogPlanner.BuildSource(
            SourceManagementDialogPlanner.ProjectEntry(new Source
            {
                Type = SourceType.Book,
                Editors = [SourceAuthorPerson.Create("Ellen", string.Empty, "Editor")],
                Translators = [SourceAuthorPerson.Create("Theo", string.Empty, "Translator")]
            }) with
            {
                Type = SourceType.WebSite,
                Title = "Web Source",
                Url = "https://example.test"
            });

        source.Type.Should().Be(SourceType.WebSite);
        source.Editors.Should().BeEmpty();
        source.Translators.Should().BeEmpty();
    }

    [Fact]
    public void BuildSource_ProjectsStructuredAndCorporateAuthors()
    {
        var personal = SourceManagementDialogPlanner.BuildSource(
            SourceManagementDialogPlanner.CreateEntry(
                SourceType.Book,
                new Dictionary<SourceManagementSourceField, string?>
                {
                    [SourceManagementSourceField.Author] = "Jane Q. Doe; Alex Smith"
                }));

        personal.Author.Should().Be("Jane Q. Doe; Alex Smith");
        personal.PersonalAuthors.Should().HaveCount(2);
        personal.CorporateAuthor.Should().BeNull();

        var corporate = SourceManagementDialogPlanner.BuildSource(
            new SourceManagementSourceEntry("Org", "World Health Organization", string.Empty, string.Empty, string.Empty));

        corporate.PersonalAuthors.Should().BeEmpty();
        corporate.CorporateAuthor.Should().Be("World Health Organization");
    }

    [Fact]
    public void TryBuildManagedSource_RejectsBlankEntriesWithPlannerValidation()
    {
        SourceManagementDialogPlanner.TryBuildManagedSource(
                new SourceManagementSourceEntry(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty),
                existing: null,
                out var source,
                out var validation)
            .Should().BeFalse();

        source.Should().BeNull();
        validation.Should().Be(new SourceManagementValidation(
            SourceManagementValidationTarget.SourceFields,
            SourceManagementDialogPlanner.MissingManagedSourceDataMessage));
    }

    [Fact]
    public void BuildInitialState_ClonesCurrentAndMasterSources()
    {
        var current = new Source
        {
            Tag = "Doc",
            Author = "Ada Lovelace",
            PersonalAuthors = [SourceAuthorPerson.Create("Ada", string.Empty, "Lovelace")],
            Editors = [SourceAuthorPerson.Create("Edna", string.Empty, "Editor")],
            Translators = [SourceAuthorPerson.Create("Tara", string.Empty, "Translator")],
            Type = SourceType.Report,
            Institution = "Analytical Society",
            BookTitle = "Collected Notes",
            City = "London",
            Edition = "Annotated",
            StandardNumber = "ISBN-1",
            ChapterNumber = "4",
            ShortTitle = "Notes",
            Comments = "Master note"
        };
        var master = new Source { Tag = "Master", Author = "Master Author" };

        var state = SourceManagementDialogPlanner.BuildInitialState([current], [master]);

        state.CurrentSources.Should().ContainSingle().Which.Should().NotBeSameAs(current);
        state.CurrentSources[0].Tag.Should().Be("Doc");
        state.CurrentSources[0].Type.Should().Be(SourceType.Report);
        state.CurrentSources[0].PersonalAuthors.Should().BeEquivalentTo(current.PersonalAuthors);
        state.CurrentSources[0].Editors.Should().BeEquivalentTo(current.Editors);
        state.CurrentSources[0].Translators.Should().BeEquivalentTo(current.Translators);
        state.CurrentSources[0].Institution.Should().Be("Analytical Society");
        state.CurrentSources[0].BookTitle.Should().Be("Collected Notes");
        state.CurrentSources[0].City.Should().Be("London");
        state.CurrentSources[0].Edition.Should().Be("Annotated");
        state.CurrentSources[0].StandardNumber.Should().Be("ISBN-1");
        state.CurrentSources[0].ChapterNumber.Should().Be("4");
        state.CurrentSources[0].ShortTitle.Should().Be("Notes");
        state.CurrentSources[0].Comments.Should().Be("Master note");
        state.MasterSources.Should().ContainSingle().Which.Should().NotBeSameAs(master);
        state.MasterSources[0].Tag.Should().Be("Master");
    }

    [Fact]
    public void AddMasterSource_AddsOrReplacesByTagAndSelectsLastVisibleRow()
    {
        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources: [],
            masterSources:
            [
                new Source { Tag = "A", Author = "Old A" },
                new Source { Tag = "B", Author = "Old B" }
            ]);

        var plan = SourceManagementDialogPlanner.AddMasterSource(
            state,
            new SourceManagementSourceEntry("A", "New A", string.Empty, string.Empty, string.Empty));

        plan.Validation.Should().BeNull();
        plan.State.MasterSources.Should().HaveCount(2);
        plan.State.MasterSources[0].Author.Should().Be("New A");
        plan.SelectedIndex.Should().Be(1);
    }

    [Fact]
    public void EditMasterSource_ReplacesSelectedItemAndRemovesDuplicateCanonicalTags()
    {
        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources: [],
            masterSources:
            [
                new Source { Tag = "Keep", Author = "Keep" },
                new Source { Tag = "Smith2020", Author = "Old Smith" },
                new Source
                {
                    Tag = " Smith2020 ",
                    Type = SourceType.Report,
                    Author = "Duplicate Smith",
                    Title = "Original Report",
                    Institution = "Analytical Society"
                },
                new Source { Tag = "Tail", Author = "Tail" }
            ]);

        var entry = SourceManagementDialogPlanner.ProjectEntry(state.MasterSources[2]) with
        {
            Author = "Updated Smith",
            Title = "Updated Report"
        };
        var plan = SourceManagementDialogPlanner.EditMasterSource(state, selectedIndex: 2, entry);

        plan.Validation.Should().BeNull();
        plan.State.MasterSources.Select(source => source.Tag).Should().Equal("Keep", "Smith2020", "Tail");
        plan.State.MasterSources[1].Author.Should().Be("Updated Smith");
        plan.State.MasterSources[1].Title.Should().Be("Updated Report");
        plan.State.MasterSources[1].Type.Should().Be(SourceType.Report);
        plan.State.MasterSources[1].Institution.Should().Be("Analytical Society");
        plan.SelectedIndex.Should().Be(1);
    }

    [Fact]
    public void EditMasterSource_InvalidIndexIsNoOp()
    {
        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources: [],
            masterSources: [new Source { Tag = "A", Author = "A" }]);

        var plan = SourceManagementDialogPlanner.EditMasterSource(
            state,
            selectedIndex: -1,
            new SourceManagementSourceEntry("B", "B", string.Empty, string.Empty, string.Empty));

        plan.State.Should().BeSameAs(state);
        plan.Validation.Should().BeNull();
        plan.SelectedIndex.Should().Be(-1);
        plan.State.MasterSources.Should().ContainSingle().Which.Tag.Should().Be("A");
    }

    [Fact]
    public void EditMasterSource_ValidationFailureKeepsListsUnchanged()
    {
        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources: [new Source { Tag = "Doc", Author = "Doc" }],
            masterSources: [new Source { Tag = "Master", Author = "Master" }]);

        var plan = SourceManagementDialogPlanner.EditMasterSource(
            state,
            selectedIndex: 0,
            new SourceManagementSourceEntry(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty));

        plan.State.Should().BeSameAs(state);
        plan.Validation.Should().Be(new SourceManagementValidation(
            SourceManagementValidationTarget.SourceFields,
            SourceManagementDialogPlanner.MissingManagedSourceDataMessage));
        plan.SelectedIndex.Should().Be(0);
        plan.State.CurrentSources.Should().ContainSingle().Which.Tag.Should().Be("Doc");
        plan.State.MasterSources.Should().ContainSingle().Which.Tag.Should().Be("Master");
    }

    [Fact]
    public void AddCitationSource_AddsNewSourceToCurrentAndMasterSources()
    {
        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources: [new Source { Tag = "Doc", Author = "Document Author" }],
            masterSources: [new Source { Tag = "Master", Author = "Master Author" }]);

        var plan = SourceManagementDialogPlanner.AddCitationSource(
            state,
            new SourceManagementSourceEntry("Lovelace2026", "Ada Lovelace", "Notes", "2026", "Analytical Press"));

        plan.Validation.Should().BeNull();
        plan.Source.Should().NotBeNull();
        plan.Source!.Tag.Should().Be("Lovelace2026");
        plan.Source.Author.Should().Be("Ada Lovelace");
        plan.State.CurrentSources.Select(source => source.Tag).Should().Equal("Doc", "Lovelace2026");
        plan.State.MasterSources.Select(source => source.Tag).Should().Equal("Master", "Lovelace2026");
        plan.State.CurrentSources[1].Author.Should().Be("Ada Lovelace");
        plan.State.MasterSources[1].Author.Should().Be("Ada Lovelace");
    }

    [Fact]
    public void AddCitationSource_UpsertsSameCanonicalTagInCurrentAndMasterSources()
    {
        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources:
            [
                new Source { Tag = "Keep", Author = "Keep" },
                new Source { Tag = " Smith2020 ", Author = "Old Current" },
                new Source { Tag = "Tail", Author = "Tail" }
            ],
            masterSources:
            [
                new Source { Tag = "Smith2020", Author = "Old Master" },
                new Source { Tag = "Other", Author = "Other" },
                new Source { Tag = " Smith2020 ", Author = "Duplicate Master" }
            ]);

        var plan = SourceManagementDialogPlanner.AddCitationSource(
            state,
            new SourceManagementSourceEntry(" Smith2020 ", "Updated Smith", "Updated Title", "2026", string.Empty));

        plan.Validation.Should().BeNull();
        plan.Source.Should().NotBeNull();
        plan.Source!.Tag.Should().Be("Smith2020");
        plan.State.CurrentSources.Select(source => source.Tag).Should().Equal("Keep", "Smith2020", "Tail");
        plan.State.CurrentSources[1].Author.Should().Be("Updated Smith");
        plan.State.CurrentSources[1].Title.Should().Be("Updated Title");
        plan.State.MasterSources.Select(source => source.Tag).Should().Equal("Smith2020", "Other");
        plan.State.MasterSources[0].Author.Should().Be("Updated Smith");
        plan.State.MasterSources[0].Title.Should().Be("Updated Title");
    }

    [Fact]
    public void AddCitationSource_DoesNotCollapseUntaggedSources()
    {
        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources: [new Source { Tag = string.Empty, Author = "Current Untagged" }],
            masterSources: [new Source { Tag = " ", Author = "Master Untagged" }]);

        var plan = SourceManagementDialogPlanner.AddCitationSource(
            state,
            new SourceManagementSourceEntry(string.Empty, "New Untagged", "New Title", string.Empty, string.Empty));

        plan.Validation.Should().BeNull();
        plan.Source.Should().NotBeNull();
        plan.Source!.Tag.Should().BeEmpty();
        plan.State.CurrentSources.Should().HaveCount(2);
        plan.State.MasterSources.Should().HaveCount(2);
        plan.State.CurrentSources.Select(source => source.Author).Should().Equal("Current Untagged", "New Untagged");
        plan.State.MasterSources.Select(source => source.Author).Should().Equal("Master Untagged", "New Untagged");
    }

    [Fact]
    public void AddCitationSource_RejectsTagOnlyEntryWithoutChangingLists()
    {
        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources: [new Source { Tag = "Doc", Author = "Document Author" }],
            masterSources: [new Source { Tag = "Master", Author = "Master Author" }]);

        var plan = SourceManagementDialogPlanner.AddCitationSource(
            state,
            new SourceManagementSourceEntry("TagOnly", string.Empty, string.Empty, string.Empty, string.Empty));

        plan.Source.Should().BeNull();
        plan.Validation.Should().Be(new SourceManagementValidation(
            SourceManagementValidationTarget.SourceFields,
            SourceManagementDialogPlanner.MissingCitationSourceDataMessage));
        plan.State.CurrentSources.Should().ContainSingle().Which.Tag.Should().Be("Doc");
        plan.State.MasterSources.Should().ContainSingle().Which.Tag.Should().Be("Master");
    }

    [Fact]
    public void CopyMasterToCurrent_AppendsNonDuplicateAndNoOpsIdenticalSameTagSource()
    {
        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources: [new Source { Tag = "Existing", Author = "Shared", Title = "Same" }],
            masterSources:
            [
                new Source { Tag = " Existing ", Author = "Shared", Title = "Same" },
                new Source { Tag = "New", Author = "Master New" }
            ]);

        var duplicate = SourceManagementDialogPlanner.CopyMasterToCurrent(
            state,
            masterSelectedIndex: 0,
            currentSelectedIndex: 0);

        duplicate.State.CurrentSources.Should().HaveCount(1);
        duplicate.State.CurrentSources[0].Author.Should().Be("Shared");
        duplicate.Conflict.Should().BeNull();
        duplicate.SelectedIndex.Should().Be(0);

        var added = SourceManagementDialogPlanner.CopyMasterToCurrent(
            state,
            masterSelectedIndex: 1,
            currentSelectedIndex: 0);

        added.State.CurrentSources.Should().HaveCount(2);
        added.State.CurrentSources[1].Tag.Should().Be("New");
        added.State.CurrentSources[1].Should().NotBeSameAs(state.MasterSources[1]);
        added.SelectedIndex.Should().Be(1);
    }

    [Fact]
    public void CopyMasterToCurrent_WhitespaceOnlyPayloadDifferencesAreSafeNoOp()
    {
        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources:
            [
                new Source
                {
                    Tag = "Existing",
                    Author = "Shared",
                    Title = "Same",
                    Publisher = "Test Press",
                    PersonalAuthors = [SourceAuthorPerson.Create("Ada", string.Empty, "Lovelace")]
                }
            ],
            masterSources:
            [
                new Source
                {
                    Tag = " Existing ",
                    Author = " Shared ",
                    Title = " Same ",
                    Publisher = " Test Press ",
                    PersonalAuthors = [new SourceAuthorPerson(" Ada ", string.Empty, " Lovelace ")]
                }
            ]);

        var plan = SourceManagementDialogPlanner.CopyMasterToCurrent(
            state,
            masterSelectedIndex: 0,
            currentSelectedIndex: 0);

        plan.Conflict.Should().BeNull();
        plan.State.Should().BeSameAs(state);
        plan.State.CurrentSources.Should().ContainSingle().Which.Author.Should().Be("Shared");
        plan.SelectedIndex.Should().Be(0);
    }

    [Fact]
    public void CopyMasterToCurrent_SameCanonicalTagDifferentPayloadReturnsConflict()
    {
        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources: [new Source { Tag = "Existing", Author = "Current Existing", Title = "Current Title" }],
            masterSources: [new Source { Tag = " Existing ", Author = "Master Existing", Title = "Master Title" }]);

        var plan = SourceManagementDialogPlanner.CopyMasterToCurrent(
            state,
            masterSelectedIndex: 0,
            currentSelectedIndex: 0);

        plan.State.Should().BeSameAs(state);
        plan.State.CurrentSources.Should().ContainSingle().Which.Author.Should().Be("Current Existing");
        plan.SelectedIndex.Should().Be(0);
        plan.Conflict.Should().NotBeNull();
        plan.Conflict!.Tag.Should().Be("Existing");
        plan.Conflict.CurrentSource.Author.Should().Be("Current Existing");
        plan.Conflict.MasterSource.Author.Should().Be("Master Existing");
        plan.Conflict.KeepAction.Should().Be(SourceManagementSourceConflictResolutionAction.KeepCurrent);
        plan.Conflict.ReplaceAction.Should().Be(
            SourceManagementSourceConflictResolutionAction.ReplaceCurrentFromMaster);
        SourceManagementDialogPlanner.BuildSourceConflictResolutionChoices(plan.Conflict)
            .Select(choice => choice.Action)
            .Should()
            .Equal(
                SourceManagementSourceConflictResolutionAction.KeepCurrent,
                SourceManagementSourceConflictResolutionAction.ReplaceCurrentFromMaster);
    }

    [Fact]
    public void ResolveSourceConflict_CanKeepCurrent()
    {
        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources: [new Source { Tag = "Existing", Author = "Current Existing" }],
            masterSources: [new Source { Tag = "Existing", Author = "Master Existing" }]);
        var conflict = SourceManagementDialogPlanner.CopyMasterToCurrent(
            state,
            masterSelectedIndex: 0,
            currentSelectedIndex: 0).Conflict!;

        var resolved = SourceManagementDialogPlanner.ResolveSourceConflict(
            state,
            conflict,
            SourceManagementSourceConflictResolutionAction.KeepCurrent);

        resolved.Conflict.Should().BeNull();
        resolved.State.Should().BeSameAs(state);
        resolved.State.CurrentSources.Should().ContainSingle().Which.Author.Should().Be("Current Existing");
        resolved.SelectedIndex.Should().Be(0);
    }

    [Fact]
    public void ResolveSourceConflict_CanReplaceCurrentFromMaster()
    {
        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources:
            [
                new Source { Tag = "Existing", Author = "Current Existing" },
                new Source { Tag = "Other", Author = "Other" },
                new Source { Tag = " Existing ", Author = "Duplicate Current" }
            ],
            masterSources: [new Source { Tag = " Existing ", Author = "Master Existing", Title = "Master Title" }]);
        var conflict = SourceManagementDialogPlanner.CopyMasterToCurrent(
            state,
            masterSelectedIndex: 0,
            currentSelectedIndex: 0).Conflict!;

        var resolved = SourceManagementDialogPlanner.ResolveSourceConflict(
            state,
            conflict,
            SourceManagementSourceConflictResolutionAction.ReplaceCurrentFromMaster);

        resolved.Conflict.Should().BeNull();
        resolved.State.CurrentSources.Select(source => source.Tag).Should().Equal("Existing", "Other");
        resolved.State.CurrentSources[0].Author.Should().Be("Master Existing");
        resolved.State.CurrentSources[0].Title.Should().Be("Master Title");
        resolved.State.MasterSources.Should().ContainSingle().Which.Author.Should().Be("Master Existing");
        resolved.SelectedIndex.Should().Be(0);
    }

    [Fact]
    public void CopyMasterToCurrent_UsesTrimmedTagIdentityAndKeepsCurrentSelection()
    {
        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources: [new Source { Tag = "Smith2020", Author = "Current Smith" }],
            masterSources:
            [
                new Source { Tag = " Smith2020 ", Author = "Current Smith" },
                new Source { Tag = "smith2020", Author = "Lowercase Smith" }
            ]);

        var duplicate = SourceManagementDialogPlanner.CopyMasterToCurrent(
            state,
            masterSelectedIndex: 0,
            currentSelectedIndex: 0);

        duplicate.State.CurrentSources.Should().ContainSingle();
        duplicate.State.CurrentSources[0].Tag.Should().Be("Smith2020");
        duplicate.State.CurrentSources[0].Author.Should().Be("Current Smith");
        duplicate.Conflict.Should().BeNull();
        duplicate.SelectedIndex.Should().Be(0);

        var distinctCase = SourceManagementDialogPlanner.CopyMasterToCurrent(
            duplicate.State,
            masterSelectedIndex: 1,
            currentSelectedIndex: duplicate.SelectedIndex);

        distinctCase.State.CurrentSources.Should().HaveCount(2);
        distinctCase.State.CurrentSources[1].Tag.Should().Be("smith2020");
        distinctCase.State.CurrentSources[1].Author.Should().Be("Lowercase Smith");
        distinctCase.SelectedIndex.Should().Be(1);
    }

    [Fact]
    public void CopyCurrentToMaster_AppendsDocumentSourceAndPreservesFields()
    {
        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources:
            [
                new Source
                {
                    Tag = "DocReport",
                    Type = SourceType.Report,
                    Author = "Ada",
                    Title = "Document Report",
                    Institution = "Analytical Society",
                    City = "London",
                    StandardNumber = "NBS-1"
                }
            ],
            masterSources: [new Source { Tag = "Master", Author = "Master" }]);

        var plan = SourceManagementDialogPlanner.CopyCurrentToMaster(
            state,
            currentSelectedIndex: 0,
            masterSelectedIndex: 0);

        plan.State.MasterSources.Should().HaveCount(2);
        plan.State.MasterSources[1].Should().NotBeSameAs(state.CurrentSources[0]);
        plan.State.MasterSources[1].Tag.Should().Be("DocReport");
        plan.State.MasterSources[1].Type.Should().Be(SourceType.Report);
        plan.State.MasterSources[1].Institution.Should().Be("Analytical Society");
        plan.State.MasterSources[1].City.Should().Be("London");
        plan.State.MasterSources[1].StandardNumber.Should().Be("NBS-1");
        plan.SelectedIndex.Should().Be(1);
    }

    [Fact]
    public void CopyCurrentToMaster_IdenticalSameTagSourceIsSafeNoOp()
    {
        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources: [new Source { Tag = " Existing ", Author = "Shared Existing", Title = "Same" }],
            masterSources:
            [
                new Source { Tag = "Existing", Author = "Shared Existing", Title = "Same" },
                new Source { Tag = "Other", Author = "Other" }
            ]);

        var plan = SourceManagementDialogPlanner.CopyCurrentToMaster(
            state,
            currentSelectedIndex: 0,
            masterSelectedIndex: 1);

        plan.State.Should().BeSameAs(state);
        plan.State.MasterSources.Select(source => source.Tag).Should().Equal("Existing", "Other");
        plan.State.MasterSources[0].Author.Should().Be("Shared Existing");
        plan.Conflict.Should().BeNull();
        plan.SelectedIndex.Should().Be(0);
    }

    [Fact]
    public void CopyCurrentToMaster_SameCanonicalTagDifferentPayloadReturnsConflict()
    {
        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources: [new Source { Tag = " Existing ", Author = "Current Existing", Title = "Updated" }],
            masterSources:
            [
                new Source { Tag = "Existing", Author = "Old Existing", Title = "Old" },
                new Source { Tag = "Other", Author = "Other" },
                new Source { Tag = " Existing ", Author = "Duplicate Existing" }
            ]);

        var plan = SourceManagementDialogPlanner.CopyCurrentToMaster(
            state,
            currentSelectedIndex: 0,
            masterSelectedIndex: 1);

        plan.State.Should().BeSameAs(state);
        plan.State.MasterSources.Select(source => source.Author)
            .Should()
            .Equal("Old Existing", "Other", "Duplicate Existing");
        plan.SelectedIndex.Should().Be(0);
        plan.Conflict.Should().NotBeNull();
        plan.Conflict!.Tag.Should().Be("Existing");
        plan.Conflict.CurrentSource.Author.Should().Be("Current Existing");
        plan.Conflict.MasterSource.Author.Should().Be("Old Existing");
        plan.Conflict.KeepAction.Should().Be(SourceManagementSourceConflictResolutionAction.KeepMaster);
        plan.Conflict.ReplaceAction.Should().Be(
            SourceManagementSourceConflictResolutionAction.ReplaceMasterFromCurrent);
    }

    [Fact]
    public void ResolveSourceConflict_CanKeepMaster()
    {
        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources: [new Source { Tag = "Existing", Author = "Current Existing" }],
            masterSources: [new Source { Tag = "Existing", Author = "Master Existing" }]);
        var conflict = SourceManagementDialogPlanner.CopyCurrentToMaster(
            state,
            currentSelectedIndex: 0,
            masterSelectedIndex: 0).Conflict!;

        var resolved = SourceManagementDialogPlanner.ResolveSourceConflict(
            state,
            conflict,
            SourceManagementSourceConflictResolutionAction.KeepMaster);

        resolved.Conflict.Should().BeNull();
        resolved.State.Should().BeSameAs(state);
        resolved.State.MasterSources.Should().ContainSingle().Which.Author.Should().Be("Master Existing");
        resolved.SelectedIndex.Should().Be(0);
    }

    [Fact]
    public void ResolveSourceConflict_CanReplaceMasterFromCurrent()
    {
        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources: [new Source { Tag = " Existing ", Author = "Current Existing", Title = "Updated" }],
            masterSources:
            [
                new Source { Tag = "Existing", Author = "Old Existing" },
                new Source { Tag = "Other", Author = "Other" },
                new Source { Tag = " Existing ", Author = "Duplicate Existing" }
            ]);
        var conflict = SourceManagementDialogPlanner.CopyCurrentToMaster(
            state,
            currentSelectedIndex: 0,
            masterSelectedIndex: 1).Conflict!;

        var resolved = SourceManagementDialogPlanner.ResolveSourceConflict(
            state,
            conflict,
            SourceManagementSourceConflictResolutionAction.ReplaceMasterFromCurrent);

        resolved.Conflict.Should().BeNull();
        resolved.State.MasterSources.Select(source => source.Tag).Should().Equal("Existing", "Other");
        resolved.State.MasterSources[0].Author.Should().Be("Current Existing");
        resolved.State.MasterSources[0].Title.Should().Be("Updated");
        resolved.State.CurrentSources.Should().ContainSingle().Which.Author.Should().Be("Current Existing");
        resolved.SelectedIndex.Should().Be(0);
    }

    [Fact]
    public void CopyCurrentToMaster_InvalidIndexIsNoOp()
    {
        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources: [new Source { Tag = "Doc", Author = "Doc" }],
            masterSources: [new Source { Tag = "Master", Author = "Master" }]);

        var plan = SourceManagementDialogPlanner.CopyCurrentToMaster(
            state,
            currentSelectedIndex: 1,
            masterSelectedIndex: 0);

        plan.State.Should().BeSameAs(state);
        plan.Validation.Should().BeNull();
        plan.SelectedIndex.Should().Be(0);
        plan.State.MasterSources.Should().ContainSingle().Which.Tag.Should().Be("Master");
    }

    [Fact]
    public void CurrentSourceMutations_DoNotCollapseBlankTags()
    {
        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources: [],
            masterSources:
            [
                new Source { Tag = string.Empty, Author = "Master Untagged" },
                new Source { Tag = " ", Author = "Second Master Untagged" }
            ]);

        var first = SourceManagementDialogPlanner.AddCurrentSource(
            state,
            new SourceManagementSourceEntry(string.Empty, "First Untagged", "First", string.Empty, string.Empty));
        var second = SourceManagementDialogPlanner.AddCurrentSource(
            first.State,
            new SourceManagementSourceEntry(" ", "Second Untagged", "Second", string.Empty, string.Empty));

        second.State.CurrentSources.Should().HaveCount(2);
        second.State.CurrentSources.Select(source => source.Tag).Should().Equal(string.Empty, string.Empty);
        second.State.CurrentSources.Select(source => source.Author).Should().Equal("First Untagged", "Second Untagged");

        var copiedBlank = SourceManagementDialogPlanner.CopyMasterToCurrent(
            second.State,
            masterSelectedIndex: 0,
            currentSelectedIndex: second.SelectedIndex);

        copiedBlank.State.CurrentSources.Should().HaveCount(3);
        copiedBlank.State.CurrentSources[2].Author.Should().Be("Master Untagged");

        var deleted = SourceManagementDialogPlanner.DeleteCurrentSource(copiedBlank.State, selectedIndex: 1);

        deleted.State.CurrentSources.Should().HaveCount(2);
        deleted.State.CurrentSources.Select(source => source.Author).Should().Equal("First Untagged", "Master Untagged");
    }

    [Fact]
    public void EditAndDeleteCurrentSource_PreserveSelectionThroughPlanner()
    {
        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources:
            [
                new Source { Tag = "A", Author = "A" },
                new Source { Tag = "B", Author = "B" }
            ],
            masterSources: []);

        var edited = SourceManagementDialogPlanner.EditCurrentSource(
            state,
            selectedIndex: 1,
            new SourceManagementSourceEntry("B2", "Bee", string.Empty, string.Empty, string.Empty));

        edited.State.CurrentSources[1].Tag.Should().Be("B2");
        edited.State.CurrentSources[1].Author.Should().Be("Bee");
        edited.SelectedIndex.Should().Be(1);

        var deleted = SourceManagementDialogPlanner.DeleteCurrentSource(edited.State, selectedIndex: 1);

        deleted.State.CurrentSources.Should().ContainSingle().Which.Tag.Should().Be("A");
        deleted.SelectedIndex.Should().Be(0);
    }

    [Fact]
    public void BuildResult_ReturnsClonedCurrentAndMasterSources()
    {
        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources: [new Source { Tag = "Doc" }],
            masterSources: [new Source { Tag = "Master" }]);

        var result = SourceManagementDialogPlanner.BuildResult(state);

        result.CurrentSources.Should().ContainSingle().Which.Should().NotBeSameAs(state.CurrentSources[0]);
        result.MasterSources.Should().ContainSingle().Which.Should().NotBeSameAs(state.MasterSources[0]);
    }
}
