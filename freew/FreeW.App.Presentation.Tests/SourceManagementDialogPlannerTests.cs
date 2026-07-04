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

        choices.Select(choice => choice.Type).Should().Equal(
            SourceType.Book,
            SourceType.JournalArticle,
            SourceType.WebSite,
            SourceType.Report,
            SourceType.BookSection,
            SourceType.ConferenceProceedings,
            SourceType.ArticleInPeriodical,
            SourceType.ElectronicSource);
        choices.Select(choice => choice.Label).Should().Equal(
            "Book",
            "Journal Article",
            "Web Site",
            "Report",
            "Book Section",
            "Conference Proceedings",
            "Article in a Periodical",
            "Electronic Source");
        SourceManagementDialogPlanner.SourceTypeSelectedIndex(SourceType.JournalArticle).Should().Be(1);
        SourceManagementDialogPlanner.SourceTypeSelectedIndex(SourceType.Report).Should().Be(3);
        SourceManagementDialogPlanner.SourceTypeSelectedIndex(SourceType.BookSection).Should().Be(4);
        SourceManagementDialogPlanner.SourceTypeSelectedIndex(SourceType.ConferenceProceedings).Should().Be(5);
        SourceManagementDialogPlanner.SourceTypeSelectedIndex(SourceType.ArticleInPeriodical).Should().Be(6);
        SourceManagementDialogPlanner.SourceTypeSelectedIndex(SourceType.ElectronicSource).Should().Be(7);
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
        entry.ChapterNumber.Should().Be("3");
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
    public void CopyMasterToCurrent_AppendsNonDuplicateAndSkipsDuplicateTags()
    {
        var state = SourceManagementDialogPlanner.BuildInitialState(
            currentSources: [new Source { Tag = "Existing" }],
            masterSources:
            [
                new Source { Tag = "Existing", Author = "Master Existing" },
                new Source { Tag = "New", Author = "Master New" }
            ]);

        var duplicate = SourceManagementDialogPlanner.CopyMasterToCurrent(
            state,
            masterSelectedIndex: 0,
            currentSelectedIndex: 0);

        duplicate.State.CurrentSources.Should().HaveCount(1);
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
