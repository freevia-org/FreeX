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
            Publisher = "AW"
        };

        var plans = SourceManagementDialogPlanner.BuildEntryFieldPlans(source);

        plans.Select(plan => plan.Field).Should().Equal(
            SourceManagementSourceField.Tag,
            SourceManagementSourceField.Author,
            SourceManagementSourceField.Title,
            SourceManagementSourceField.Year,
            SourceManagementSourceField.Publisher);
        plans.Select(plan => plan.Label).Should().Equal(
            "Tag (short id):",
            "Author:",
            "Title:",
            "Year:",
            "Publisher / Site name (optional):");
        plans.Select(plan => plan.Text).Should().Equal(
            "Knuth1997",
            "Knuth",
            "TAOCP",
            "1997",
            "AW");
    }

    [Fact]
    public void BuildSourceTypeChoices_ExposesTheModeledWordSourceTypes()
    {
        var choices = SourceManagementDialogPlanner.BuildSourceTypeChoices();

        choices.Select(choice => choice.Type).Should().Equal(
            SourceType.Book,
            SourceType.JournalArticle,
            SourceType.WebSite);
        choices.Select(choice => choice.Label).Should().Equal(
            "Book",
            "Journal Article",
            "Web Site");
        SourceManagementDialogPlanner.SourceTypeSelectedIndex(SourceType.JournalArticle).Should().Be(1);
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
            SourceManagementSourceField.Pages);

        var webPlans = SourceManagementDialogPlanner.BuildEntryFieldPlans(SourceType.WebSite);
        webPlans.Select(plan => plan.Field).Should().Equal(
            SourceManagementSourceField.Tag,
            SourceManagementSourceField.Author,
            SourceManagementSourceField.Title,
            SourceManagementSourceField.Year,
            SourceManagementSourceField.Publisher,
            SourceManagementSourceField.Url,
            SourceManagementSourceField.Accessed);
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
                [SourceManagementSourceField.Year] = " 1997 ",
                [SourceManagementSourceField.Url] = " https://example.test "
            });

        entry.Should().Be(new SourceManagementSourceEntry(
            SourceType.WebSite,
            "K97",
            "Knuth",
            string.Empty,
            "1997",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            "https://example.test",
            string.Empty)
        {
            CorporateAuthor = "Knuth"
        });
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
                "Journal",
                "10",
                "2",
                "12-20",
                "https://example.test",
                "3 May 2024"));

        source.Type.Should().Be(SourceType.Book);
        source.Publisher.Should().Be("Publisher");
        source.Journal.Should().BeNull();
        source.Volume.Should().BeNull();
        source.Issue.Should().BeNull();
        source.Pages.Should().BeNull();
        source.Url.Should().BeNull();
        source.Accessed.Should().BeNull();
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
            PersonalAuthors = [SourceAuthorPerson.Create("Ada", string.Empty, "Lovelace")]
        };
        var master = new Source { Tag = "Master", Author = "Master Author" };

        var state = SourceManagementDialogPlanner.BuildInitialState([current], [master]);

        state.CurrentSources.Should().ContainSingle().Which.Should().NotBeSameAs(current);
        state.CurrentSources[0].Tag.Should().Be("Doc");
        state.CurrentSources[0].PersonalAuthors.Should().BeEquivalentTo(current.PersonalAuthors);
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
