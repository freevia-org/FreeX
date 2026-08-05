using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class MarkIndexEntryDialogPlannerTests
{
    [Fact]
    public void BuildInitialState_SeedsMainEntryAndDefaultsToCurrentPage()
    {
        var state = MarkIndexEntryDialogPlanner.BuildInitialState("  Animals  ");

        state.Should().Be(new MarkIndexEntryDialogState(
            "Animals",
            string.Empty,
            UseCrossReference: false,
            MarkIndexEntryDialogPlanner.DefaultCrossReference,
            BoldPageNumber: false,
            ItalicPageNumber: false));
    }

    [Fact]
    public void TryBuildMark_TrimsHierarchyAndCarriesCrossReference()
    {
        MarkIndexEntryDialogPlanner.TryBuildMark(
                new MarkIndexEntryDialogState(" Animals ", " Cats:Longhair ", true, " See Pet care ", true, true),
                out var mark,
                out var validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        mark.Should().Be(new IndexMark("Animals", "Cats:Longhair", "See Pet care"));
    }

    [Theory]
    [InlineData("", false, "See Vehicles", MarkIndexEntryDialogPlanner.MissingMainEntryMessage)]
    [InlineData("Transportation", true, "  ", MarkIndexEntryDialogPlanner.MissingCrossReferenceMessage)]
    public void TryBuildMark_RejectsMissingRequiredText(
        string mainEntry,
        bool useCrossReference,
        string crossReference,
        string message)
    {
        MarkIndexEntryDialogPlanner.TryBuildMark(
                new MarkIndexEntryDialogState(mainEntry, string.Empty, useCrossReference, crossReference, false, false),
                out var mark,
                out var validation)
            .Should().BeFalse();

        mark.Should().BeNull();
        validation.Should().Be(new MarkIndexEntryValidation(message));
    }

    [Fact]
    public void TryBuildMark_CarriesPageNumberFormattingOnlyForCurrentPageOption()
    {
        MarkIndexEntryDialogPlanner.TryBuildMark(
                new MarkIndexEntryDialogState("Alpha", string.Empty, false, "See Other", true, true),
                out var pageMark,
                out _)
            .Should().BeTrue();
        pageMark.Should().Be(new IndexMark("Alpha", BoldPageNumber: true, ItalicPageNumber: true));

        MarkIndexEntryDialogPlanner.TryBuildMark(
                new MarkIndexEntryDialogState("Alpha", string.Empty, true, "See Other", true, true),
                out var crossReferenceMark,
                out _)
            .Should().BeTrue();
        crossReferenceMark.Should().Be(new IndexMark("Alpha", CrossReference: "See Other"));
    }
}
