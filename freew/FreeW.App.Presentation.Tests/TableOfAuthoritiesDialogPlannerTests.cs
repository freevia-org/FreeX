using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class TableOfAuthoritiesDialogPlannerTests
{
    [Fact]
    public void VisualMetrics_capture_the_Wpf_authority_layout()
    {
        var metrics = TableOfAuthoritiesDialogPlanner.VisualMetrics;

        metrics.DialogWidth.Should().Be(380);
        metrics.OuterInset.Should().Be(16);
        metrics.LabelBottomMargin.Should().Be(4);
        metrics.ComboBoxHeight.Should().Be(24);
        metrics.ComboBottomMargin.Should().Be(8);
        metrics.PassimBottomMargin.Should().Be(6);
        metrics.KeepFormattingBottomMargin.Should().Be(8);
        metrics.ActionTopMargin.Should().Be(12);
        metrics.ActionButtonWidth.Should().Be(80);
        metrics.ActionSpacing.Should().Be(14);
        metrics.AvaloniaComboBoxHeightCompensation.Should().Be(-2);
        metrics.AvaloniaOuterRightCompensation.Should().Be(1);
        metrics.AvaloniaActionTopCompensation.Should().Be(1);
    }

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
