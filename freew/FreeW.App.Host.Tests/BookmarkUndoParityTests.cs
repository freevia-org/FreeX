using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace FreeW.App.Host.Tests;

public sealed class BookmarkUndoParityTests
{
    [StaFact]
    public void SetAndRemoveBookmark_AreUndoableAndPreserveSiblingNames()
    {
        var paragraph = new Paragraph("Target");
        paragraph.BookmarkNames.AddRange(["old", "sibling"]);
        var document = new TextDocument();
        document.Blocks.Add(paragraph);
        var editor = new DocumentView();
        editor.LoadModel(document);
        editor.CaretPosition = editor.Document.Blocks.OfType<WpfParagraph>().Single().ContentStart;

        editor.SetBookmarkAtCaret("replacement");
        CurrentNames(editor).Should().Equal("replacement", "sibling");
        editor.BookmarkNames().Should().Equal("replacement", "sibling");
        editor.Undo();
        CurrentNames(editor).Should().Equal("old", "sibling");
        editor.Redo();
        CurrentNames(editor).Should().Equal("replacement", "sibling");

        editor.RemoveBookmark("sibling");
        CurrentNames(editor).Should().Equal("replacement");
        editor.Undo();
        CurrentNames(editor).Should().Equal("replacement", "sibling");
        editor.Redo();
        CurrentNames(editor).Should().Equal("replacement");
    }

    // Confirmed HIGH finding: Insert Bookmark allowed a duplicate name. SetBookmarkAtCaret must reject a
    // name already used by a different paragraph (Word's unique-name rule), leaving the original target
    // untouched, instead of silently creating a second bookmark instance sharing that name.
    [StaFact]
    public void SetBookmarkAtCaret_RejectsADuplicateNameAndLeavesTheOriginalTargetInPlace()
    {
        var first = new Paragraph("First");
        first.BookmarkNames.Add("Shared");
        var second = new Paragraph("Second");
        var document = new TextDocument();
        document.Blocks.Add(first);
        document.Blocks.Add(second);
        var editor = new DocumentView();
        editor.LoadModel(document);
        editor.CaretPosition = editor.Document.Blocks.OfType<WpfParagraph>().ElementAt(1).ContentStart;

        editor.SetBookmarkAtCaret("Shared").Should().Be(BookmarkInsertOutcome.DuplicateName);

        editor.Model.Blocks.OfType<Paragraph>().ElementAt(0).BookmarkNames.Should().Equal("Shared");
        editor.Model.Blocks.OfType<Paragraph>().ElementAt(1).BookmarkNames.Should().BeEmpty();
    }

    private static IReadOnlyList<string> CurrentNames(DocumentView editor) =>
        editor.Model.Blocks.OfType<Paragraph>().Single().BookmarkNames;
}
