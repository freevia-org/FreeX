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
            "Publisher (optional):");
        plans.Select(plan => plan.Text).Should().Equal(
            "Knuth1997",
            "Knuth",
            "TAOCP",
            "1997",
            "AW");
    }

    [Fact]
    public void CreateEntry_TrimsDialogTextAndDefaultsMissingFields()
    {
        var entry = SourceManagementDialogPlanner.CreateEntry(
            new Dictionary<SourceManagementSourceField, string?>
            {
                [SourceManagementSourceField.Tag] = "  K97  ",
                [SourceManagementSourceField.Author] = " Knuth ",
                [SourceManagementSourceField.Title] = null,
                [SourceManagementSourceField.Year] = " 1997 "
            });

        entry.Should().Be(new SourceManagementSourceEntry("K97", "Knuth", string.Empty, "1997", string.Empty));
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
    public void TryBuildCitationSource_RequiresAuthorTitleOrYearAndNormalizesPublisher()
    {
        SourceManagementDialogPlanner.TryBuildCitationSource(
                new SourceManagementSourceEntry("TagOnly", string.Empty, string.Empty, string.Empty, "PublisherOnly"),
                out var rejected,
                out var validation)
            .Should().BeFalse();

        rejected.Should().BeNull();
        validation.Should().Be(new SourceManagementValidation(
            SourceManagementValidationTarget.SourceFields,
            SourceManagementDialogPlanner.MissingCitationSourceDataMessage));

        SourceManagementDialogPlanner.TryBuildCitationSource(
                new SourceManagementSourceEntry("K97", "Knuth", "TAOCP", "1997", "  "),
                out var source,
                out validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        source.Should().NotBeNull();
        source!.Tag.Should().Be("K97");
        source.Publisher.Should().BeNull();
    }

    [Fact]
    public void TryBuildManagedSource_AcceptsAnySourceFieldAndPreservesTypeSpecificFieldsWhenEditing()
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

        SourceManagementDialogPlanner.TryBuildManagedSource(
                new SourceManagementSourceEntry("New", string.Empty, string.Empty, string.Empty, string.Empty),
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
        source.Url.Should().Be("https://example.test");
        source.Accessed.Should().Be("2024-01-02");
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
        var current = new Source { Tag = "Doc", Author = "Doc Author" };
        var master = new Source { Tag = "Master", Author = "Master Author" };

        var state = SourceManagementDialogPlanner.BuildInitialState([current], [master]);

        state.CurrentSources.Should().ContainSingle().Which.Should().NotBeSameAs(current);
        state.CurrentSources[0].Tag.Should().Be("Doc");
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
