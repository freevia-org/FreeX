using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class TableOfAuthoritiesDialogPlannerTests
{
    [Fact]
    public void BuildCategoryChoices_ListsAllCategoriesAfterAllChoice()
    {
        var choices = TableOfAuthoritiesDialogPlanner.BuildCategoryChoices();

        choices[0].Category.Should().BeNull();
        choices[0].Label.Should().Be(TableOfAuthoritiesDialogPlanner.AllCategoriesLabel);
        choices.Skip(1).Select(choice => choice.Category!.Value).Should().Equal(Enum.GetValues<CitationCategory>());
        choices.Single(choice => choice.Category == CitationCategory.Statutes).ToString().Should().Be("Statutes");
    }

    [Fact]
    public void BuildTabLeaderChoices_MapsWordLeaderOptions()
    {
        var choices = TableOfAuthoritiesDialogPlanner.BuildTabLeaderChoices();

        choices.Select(choice => choice.Leader).Should().Equal(
            ToaTabLeader.Dots,
            ToaTabLeader.Dashes,
            ToaTabLeader.Underline,
            ToaTabLeader.None);
        choices.Single(choice => choice.Leader == ToaTabLeader.None).ToString().Should().Be("(None)");
    }

    [Fact]
    public void BuildInitialStateAndOptions_RoundTripToaOptions()
    {
        var options = new ToaOptions
        {
            UsePassim = true,
            KeepOriginalFormatting = true,
            CategoryFilter = CitationCategory.Rules,
            TabLeader = ToaTabLeader.Dashes
        };

        var state = TableOfAuthoritiesDialogPlanner.BuildInitialState(options);
        var rebuilt = TableOfAuthoritiesDialogPlanner.BuildOptions(state);

        rebuilt.UsePassim.Should().BeTrue();
        rebuilt.KeepOriginalFormatting.Should().BeTrue();
        rebuilt.CategoryFilter.Should().Be(CitationCategory.Rules);
        rebuilt.TabLeader.Should().Be(ToaTabLeader.Dashes);
    }

    [Fact]
    public void SelectIndexes_FallBackToDefaultsWhenRequestedValueIsMissing()
    {
        TableOfAuthoritiesDialogPlanner.SelectCategoryIndex(
                [new TableOfAuthoritiesCategoryChoice(CitationCategory.Cases, "Cases")],
                CitationCategory.Statutes)
            .Should().Be(0);

        TableOfAuthoritiesDialogPlanner.SelectTabLeaderIndex(
                [new TableOfAuthoritiesTabLeaderChoice(ToaTabLeader.Dots, "Dots")],
            ToaTabLeader.Underline)
            .Should().Be(0);
    }

    [Fact]
    public void PlanAcceptance_ProjectsNativeControlStateIntoOptions()
    {
        var categories = TableOfAuthoritiesDialogPlanner.BuildCategoryChoices();
        var leaders = TableOfAuthoritiesDialogPlanner.BuildTabLeaderChoices();

        var acceptance = TableOfAuthoritiesDialogPlanner.PlanAcceptance(
            new TableOfAuthoritiesDialogInput(
                UsePassim: true,
                KeepOriginalFormatting: null,
                categories.Single(choice => choice.Category == CitationCategory.Rules),
                leaders.Single(choice => choice.Leader == ToaTabLeader.Underline)));

        acceptance.IsAccepted.Should().BeTrue();
        acceptance.Validation.Should().BeNull();
        acceptance.Options.Should().BeEquivalentTo(new ToaOptions
        {
            UsePassim = true,
            KeepOriginalFormatting = false,
            CategoryFilter = CitationCategory.Rules,
            TabLeader = ToaTabLeader.Underline
        });
    }

    [Fact]
    public void PlanAcceptance_RejectsMissingCategorySelection()
    {
        var leader = TableOfAuthoritiesDialogPlanner.BuildTabLeaderChoices()[0];

        var acceptance = TableOfAuthoritiesDialogPlanner.PlanAcceptance(
            new TableOfAuthoritiesDialogInput(false, false, null, leader));

        acceptance.IsAccepted.Should().BeFalse();
        acceptance.Options.Should().BeNull();
        acceptance.Validation.Should().Be(new TableOfAuthoritiesDialogValidation(
            TableOfAuthoritiesDialogField.Category,
            TableOfAuthoritiesDialogPlanner.MissingCategoryMessage));
    }

    [Fact]
    public void PlanAcceptance_RejectsMissingTabLeaderSelection()
    {
        var category = TableOfAuthoritiesDialogPlanner.BuildCategoryChoices()[0];

        var acceptance = TableOfAuthoritiesDialogPlanner.PlanAcceptance(
            new TableOfAuthoritiesDialogInput(false, false, category, null));

        acceptance.IsAccepted.Should().BeFalse();
        acceptance.Options.Should().BeNull();
        acceptance.Validation.Should().Be(new TableOfAuthoritiesDialogValidation(
            TableOfAuthoritiesDialogField.TabLeader,
            TableOfAuthoritiesDialogPlanner.MissingTabLeaderMessage));
    }

    [Theory]
    [InlineData("initial", false, false, null, ToaTabLeader.Dots)]
    [InlineData("populated", true, true, CitationCategory.Statutes, ToaTabLeader.Dashes)]
    [InlineData("validation-error", false, false, null, ToaTabLeader.Dots)]
    public void BuildEvidenceOptions_uses_one_state_contract_for_both_hosts(
        string state,
        bool usePassim,
        bool keepOriginalFormatting,
        CitationCategory? categoryFilter,
        ToaTabLeader tabLeader)
    {
        var options = TableOfAuthoritiesDialogPlanner.BuildEvidenceOptions(state);

        options.UsePassim.Should().Be(usePassim);
        options.KeepOriginalFormatting.Should().Be(keepOriginalFormatting);
        options.CategoryFilter.Should().Be(categoryFilter);
        options.TabLeader.Should().Be(tabLeader);
    }
}

public sealed class TableOfAuthoritiesDialogSourceOwnershipTests
{
    [Fact]
    public void Renderers_DelegateAcceptancePolicyToPortablePlanner()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "TableOfAuthoritiesDialog.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "TableOfAuthoritiesDialog.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("TableOfAuthoritiesDialogPlanner.PlanAcceptance(");
            source.Should().Contain("new TableOfAuthoritiesDialogInput(");
            source.Should().NotContain("new TableOfAuthoritiesDialogState(");
            source.Should().NotContain("TableOfAuthoritiesDialogPlanner.BuildOptions(");
            source.Should().NotContain("?.Leader ?? ToaTabLeader.Dots");
        }
    }

    [Fact]
    public void PortablePlanner_HasNoRendererDependencies()
    {
        var source = ReadSource(
            "freew", "FreeW.App.Presentation", "Ribbon", "TableOfAuthoritiesDialogPlanner.cs");

        source.Should().NotContain("using Avalonia");
        source.Should().NotContain("using System.Windows");
        source.Should().NotContain("Avalonia.Controls");
        source.Should().NotContain("System.Windows.Controls");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
