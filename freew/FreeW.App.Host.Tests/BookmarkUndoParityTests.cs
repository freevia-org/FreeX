using FreeW.App.Host.Editing;
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

    private static IReadOnlyList<string> CurrentNames(DocumentView editor) =>
        editor.Model.Blocks.OfType<Paragraph>().Single().BookmarkNames;
}
