using FreeW.App.Host;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

public sealed class MarkIndexEntryDialogTests
{
    [StaFact]
    public void Dialog_DefaultsToCurrentPageAndSeedsMainEntry()
    {
        var dialog = MarkIndexEntryDialog.CreateForTest("  Animals  ");

        dialog.CrossReferenceEnabledForTest.Should().BeFalse();
        dialog.BookmarkSelectorEnabledForTest.Should().BeFalse();
        dialog.PageNumberFormattingEnabledForTest.Should().BeTrue();
        dialog.AcceptForTest().Should().BeTrue();
        dialog.ResultForTest!.Mark.Identifier.Should().BeEmpty();
        dialog.ResultForTest!.Mark.Should().Be(new IndexMark("Animals"));
    }

    [StaFact]
    public void Dialog_CarriesTrimmedOptionalIdentifierThroughSharedPlanner()
    {
        var dialog = MarkIndexEntryDialog.CreateForTest();
        dialog.SetForTest("Alpha", null, false, null, identifier: " People ");

        dialog.AcceptForTest().Should().BeTrue();
        dialog.ResultForTest!.Mark.Should().Be(new IndexMark(
            "Alpha",
            Identifier: "People"));
    }

    [StaFact]
    public void Dialog_CarriesSubentryAndCrossReferenceThroughSharedPlanner()
    {
        var dialog = MarkIndexEntryDialog.CreateForTest();
        dialog.SetForTest(" Animals ", " Cats ", true, " See Pet care ");

        dialog.CrossReferenceEnabledForTest.Should().BeTrue();
        dialog.BookmarkSelectorEnabledForTest.Should().BeFalse();
        dialog.PageNumberFormattingEnabledForTest.Should().BeFalse();
        dialog.AcceptForTest().Should().BeTrue();
        dialog.ResultForTest!.Mark.Should().Be(new IndexMark("Animals", "Cats", "See Pet care"));
    }

    [StaFact]
    public void Dialog_BlankCrossReferenceStaysOpenWithoutResult()
    {
        var dialog = MarkIndexEntryDialog.CreateForTest();
        dialog.SetForTest("Transportation", null, true, "  ");

        dialog.AcceptForTest().Should().BeFalse();
        dialog.ResultForTest.Should().BeNull();
    }

    [StaFact]
    public void Dialog_CarriesBoldAndItalicCurrentPageFormatting()
    {
        var dialog = MarkIndexEntryDialog.CreateForTest();
        dialog.SetForTest("Alpha", null, false, null, boldPageNumber: true, italicPageNumber: true);

        dialog.AcceptForTest().Should().BeTrue();
        dialog.ResultForTest!.Mark.Should().Be(new IndexMark(
            "Alpha",
            BoldPageNumber: true,
            ItalicPageNumber: true));
    }

    [StaFact]
    public void Dialog_MarkAllRequiresSelectionAndReturnsRequestedAction()
    {
        MarkIndexEntryDialog.CreateForTest().MarkAllEnabledForTest.Should().BeFalse();
        var dialog = MarkIndexEntryDialog.CreateForTest("Alpha");

        dialog.MarkAllEnabledForTest.Should().BeTrue();
        dialog.AcceptAllForTest().Should().BeTrue();
        dialog.ResultForTest!.MarkAll.Should().BeTrue();
        dialog.ResultForTest.Mark.Should().Be(new IndexMark("Alpha"));
    }

    [StaFact]
    public void Dialog_PageRangeReturnsSelectedBookmarkAndKeepsPageNumberFormatting()
    {
        var dialog = MarkIndexEntryDialog.CreateForTest("Animals", ["ChapterOne", "ChapterTwo"]);
        dialog.SetReferenceForTest(
            "Animals",
            "Cats",
            IndexEntryReferenceKind.PageRange,
            "ChapterTwo",
            null,
            boldPageNumber: true,
            italicPageNumber: true);

        dialog.BookmarkNamesForTest.Should().Equal("ChapterOne", "ChapterTwo");
        dialog.BookmarkSelectorEnabledForTest.Should().BeTrue();
        dialog.CrossReferenceEnabledForTest.Should().BeFalse();
        dialog.PageNumberFormattingEnabledForTest.Should().BeTrue();
        dialog.AcceptForTest().Should().BeTrue();
        dialog.ResultForTest!.Mark.Should().Be(new IndexMark(
            "Animals",
            "Cats",
            BoldPageNumber: true,
            ItalicPageNumber: true,
            BookmarkName: "ChapterTwo"));
    }

    [StaFact]
    public void Dialog_PageRangeRequiresBookmarkSelection()
    {
        var dialog = MarkIndexEntryDialog.CreateForTest("Animals", ["ChapterOne"]);
        dialog.SetReferenceForTest(
            "Animals",
            null,
            IndexEntryReferenceKind.PageRange,
            null,
            null);

        dialog.AcceptForTest().Should().BeFalse();
        dialog.ResultForTest.Should().BeNull();
    }

    [StaFact]
    public void Dialog_MarkAllIsSuppressedForPageRangeAndRestoredForOtherOptions()
    {
        var dialog = MarkIndexEntryDialog.CreateForTest("Animals", ["ChapterOne"]);

        dialog.SetReferenceForTest(
            "Animals",
            null,
            IndexEntryReferenceKind.PageRange,
            "ChapterOne",
            null);
        dialog.MarkAllEnabledForTest.Should().BeFalse();
        dialog.AcceptAllForTest().Should().BeFalse();

        dialog.SetReferenceForTest(
            "Animals",
            null,
            IndexEntryReferenceKind.CrossReference,
            null,
            "See Creatures");
        dialog.MarkAllEnabledForTest.Should().BeTrue();

        dialog.SetReferenceForTest(
            "Animals",
            null,
            IndexEntryReferenceKind.CurrentPage,
            null,
            null);
        dialog.MarkAllEnabledForTest.Should().BeTrue();
    }
}
