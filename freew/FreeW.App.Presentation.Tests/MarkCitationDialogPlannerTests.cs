using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class MarkCitationDialogPlannerTests
{
    [Fact]
    public void Geometry_UsesTheSharedMarkCitationBaseline()
    {
        MarkCitationDialogPlanner.DialogWidth.Should().Be(380);
        MarkCitationDialogPlanner.ContentHorizontalMargin.Should().Be(16);
        MarkCitationDialogPlanner.ContentTopMargin.Should().Be(16);
        MarkCitationDialogPlanner.LabelBottomMargin.Should().Be(4);
        MarkCitationDialogPlanner.FieldBottomMargin.Should().Be(10);
    }

    [Fact]
    public void BuildCategoryChoices_UsesTableOfAuthoritiesCategoryLabels()
    {
        var choices = MarkCitationDialogPlanner.BuildCategoryChoices();

        choices.Select(choice => choice.Category).Should().Equal(Enum.GetValues<CitationCategory>());
        choices.Single(choice => choice.Category == CitationCategory.OtherAuthorities)
            .ToString().Should().Be("Other Authorities");
    }

    [Fact]
    public void BuildInitialState_TrimsSelectionSeedAndDefaultsToCases()
    {
        var state = MarkCitationDialogPlanner.BuildInitialState("  Brown v. Board  ");

        state.Category.Should().Be(CitationCategory.Cases);
        state.LongCitation.Should().Be("Brown v. Board");
        state.ShortCitation.Should().BeEmpty();
    }

    [Fact]
    public void TryBuildCitation_RejectsBlankLongCitation()
    {
        MarkCitationDialogPlanner.TryBuildCitation(
                new MarkCitationDialogState(CitationCategory.Cases, "  ", "Brown"),
                out var citation,
                out var validation)
            .Should().BeFalse();

        citation.Should().BeNull();
        validation.Should().Be(new MarkCitationValidation(
            MarkCitationDialogPlanner.MissingLongCitationMessage));
    }

    [Fact]
    public void TryBuildCitation_TrimsDialogTextAndCarriesCategory()
    {
        MarkCitationDialogPlanner.TryBuildCitation(
                new MarkCitationDialogState(
                    CitationCategory.Statutes,
                    "  17 U.S.C. 107  ",
                    "  fair use  "),
                out var citation,
                out var validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        citation.Should().NotBeNull();
        citation!.Category.Should().Be(CitationCategory.Statutes);
        citation.LongCitation.Should().Be("17 U.S.C. 107");
        citation.ShortCitation.Should().Be("fair use");
    }
}
