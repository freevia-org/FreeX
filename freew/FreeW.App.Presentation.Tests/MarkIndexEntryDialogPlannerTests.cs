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
            string.Empty,
            IndexEntryReferenceKind.CurrentPage,
            BookmarkName: string.Empty,
            MarkIndexEntryDialogPlanner.DefaultCrossReference,
            BoldPageNumber: false,
            ItalicPageNumber: false));
    }

    [Fact]
    public void TryBuildMark_TrimsHierarchyAndCarriesCrossReference()
    {
        MarkIndexEntryDialogPlanner.TryBuildMark(
                new MarkIndexEntryDialogState(
                    " Animals ",
                    " Cats:Longhair ",
                    " People ",
                    IndexEntryReferenceKind.CrossReference,
                    string.Empty,
                    " See Pet care ",
                    true,
                    true),
                out var mark,
                out var validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        mark.Should().Be(new IndexMark(
            "Animals",
            "Cats:Longhair",
            "See Pet care",
            Identifier: "People"));
    }

    [Theory]
    [InlineData("", IndexEntryReferenceKind.CurrentPage, "", "See Vehicles", MarkIndexEntryDialogPlanner.MissingMainEntryMessage)]
    [InlineData("Transportation", IndexEntryReferenceKind.CrossReference, "", "  ", MarkIndexEntryDialogPlanner.MissingCrossReferenceMessage)]
    [InlineData("Transportation", IndexEntryReferenceKind.PageRange, "  ", "See Vehicles", MarkIndexEntryDialogPlanner.MissingBookmarkMessage)]
    public void TryBuildMark_RejectsMissingRequiredText(
        string mainEntry,
        IndexEntryReferenceKind referenceKind,
        string bookmarkName,
        string crossReference,
        string message)
    {
        MarkIndexEntryDialogPlanner.TryBuildMark(
                new MarkIndexEntryDialogState(
                    mainEntry,
                    string.Empty,
                    string.Empty,
                    referenceKind,
                    bookmarkName,
                    crossReference,
                    false,
                    false),
                out var mark,
                out var validation)
            .Should().BeFalse();

        mark.Should().BeNull();
        validation.Should().Be(new MarkIndexEntryValidation(message));
    }

    [Fact]
    public void TryBuildMark_CarriesPageNumberFormattingForPageOptionsOnly()
    {
        MarkIndexEntryDialogPlanner.TryBuildMark(
                new MarkIndexEntryDialogState(
                    "Alpha",
                    string.Empty,
                    string.Empty,
                    IndexEntryReferenceKind.CurrentPage,
                    string.Empty,
                    "See Other",
                    true,
                    true),
                out var pageMark,
                out _)
            .Should().BeTrue();
        pageMark.Should().Be(new IndexMark("Alpha", BoldPageNumber: true, ItalicPageNumber: true));

        MarkIndexEntryDialogPlanner.TryBuildMark(
                new MarkIndexEntryDialogState(
                    "Alpha",
                    string.Empty,
                    "People",
                    IndexEntryReferenceKind.PageRange,
                    " TopicRange ",
                    "See Other",
                    true,
                    true),
                out var rangeMark,
                out _)
            .Should().BeTrue();
        rangeMark.Should().Be(new IndexMark(
            "Alpha",
            BoldPageNumber: true,
            ItalicPageNumber: true,
            BookmarkName: "TopicRange",
            Identifier: "People"));

        MarkIndexEntryDialogPlanner.TryBuildMark(
                new MarkIndexEntryDialogState(
                    "Alpha",
                    string.Empty,
                    string.Empty,
                    IndexEntryReferenceKind.CrossReference,
                    string.Empty,
                    "See Other",
                    true,
                    true),
                out var crossReferenceMark,
                out _)
            .Should().BeTrue();
        crossReferenceMark.Should().Be(new IndexMark("Alpha", CrossReference: "See Other"));
    }

    [Theory]
    [InlineData("Alpha", IndexEntryReferenceKind.CurrentPage, true)]
    [InlineData("Alpha", IndexEntryReferenceKind.CrossReference, true)]
    [InlineData("Alpha", IndexEntryReferenceKind.PageRange, false)]
    [InlineData("  ", IndexEntryReferenceKind.CurrentPage, false)]
    [InlineData(null, IndexEntryReferenceKind.CurrentPage, false)]
    public void CanMarkAll_RequiresSelectedSourceTextAndNonRangeOption(
        string? selectedText,
        IndexEntryReferenceKind referenceKind,
        bool expected)
    {
        MarkIndexEntryDialogPlanner.CanMarkAll(selectedText, referenceKind).Should().Be(expected);
    }
}
