using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace FreeW.App.Host.Tests;

// Confirmed MED finding (shared-dialog-validation F2): the Insert Bookmark prompt reads "Bookmark name
// (leave blank to remove):" but InsertBookmarkCommand.Execute never seeded the box with the caret
// paragraph's existing bookmark and never removed anything on a blank submit -- the documented gesture
// silently did nothing. The fix adds DocumentView.BookmarkNameAtCaret() (to seed the prompt and to know
// what a blank submit should remove) and wires InsertBookmarkCommand's blank branch to
// DocumentView.RemoveBookmark(existing). These tests exercise the same two DocumentView calls the fixed
// command composes -- the command itself is a private, dialog-bound nested class that cannot be invoked
// headlessly (TextPrompt.Ask shows a real modal ShowDialog()).
public sealed class InsertBookmarkBlankRemovesTests
{
    [StaFact]
    public void BookmarkNameAtCaret_ReturnsTheCaretParagraphsExistingBookmarkName()
    {
        var paragraph = new Paragraph("Target");
        paragraph.BookmarkNames.Add("ExistingMark");
        var document = new TextDocument();
        document.Blocks.Add(paragraph);
        var editor = new DocumentView();
        editor.LoadModel(document);
        editor.CaretPosition = editor.Document.Blocks.OfType<WpfParagraph>().Single().ContentStart;

        editor.BookmarkNameAtCaret().Should().Be("ExistingMark");
    }

    // Reproduces the exact user gesture from the finding: caret on a bookmarked paragraph, the box is
    // cleared (or left blank as the prompt instructs), OK is clicked. The fixed InsertBookmarkCommand
    // captures the existing name via BookmarkNameAtCaret() before prompting, then on a blank result calls
    // RemoveBookmark(existing) instead of silently discarding the outcome.
    [StaFact]
    public void InsertBookmarkCommand_BlankSubmitOnABookmarkedParagraph_RemovesThatBookmark()
    {
        var paragraph = new Paragraph("Target");
        paragraph.BookmarkNames.AddRange(["ExistingMark", "OtherOnSameParagraph"]);
        var sibling = new Paragraph("Sibling");
        sibling.BookmarkNames.Add("SiblingMark");
        var document = new TextDocument();
        document.Blocks.Add(paragraph);
        document.Blocks.Add(sibling);
        var editor = new DocumentView();
        editor.LoadModel(document);
        editor.CaretPosition = editor.Document.Blocks.OfType<WpfParagraph>().First().ContentStart;

        // What InsertBookmarkCommand.Execute now does when the prompt returns "" (blank, not cancelled):
        var existing = editor.BookmarkNameAtCaret();
        existing.Should().Be("ExistingMark");
        if (existing is not null)
            editor.RemoveBookmark(existing);

        editor.Model.Blocks.OfType<Paragraph>().ElementAt(0).BookmarkNames.Should().Equal("OtherOnSameParagraph");
        // Sibling adjacent-case check: removal targets only the named bookmark, leaving an unrelated
        // paragraph's bookmark untouched.
        editor.Model.Blocks.OfType<Paragraph>().ElementAt(1).BookmarkNames.Should().Equal("SiblingMark");
    }

    // Sibling/no-regression case: a paragraph with no bookmark must not report one, so the fixed command's
    // blank branch (`if (existing is not null) editor.RemoveBookmark(existing);`) takes the no-op path
    // instead of calling RemoveBookmark with a bogus value.
    [StaFact]
    public void BookmarkNameAtCaret_OnAParagraphWithNoBookmark_ReturnsNull()
    {
        var paragraph = new Paragraph("Plain");
        var document = new TextDocument();
        document.Blocks.Add(paragraph);
        var editor = new DocumentView();
        editor.LoadModel(document);
        editor.CaretPosition = editor.Document.Blocks.OfType<WpfParagraph>().Single().ContentStart;

        editor.BookmarkNameAtCaret().Should().BeNull();
    }
}
