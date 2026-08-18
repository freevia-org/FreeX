using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// R143 (shared-undo-boundaries, id freew-autohyperlink-undo-granularity-avalonia): typing a recognizable
/// URL followed by a space auto-links the word (AutoFormat-as-you-type). In WPF/Word this is TWO separate
/// undo steps -- one Ctrl+Z removes only the automatic hyperlink formatting and leaves the typed word in
/// place as plain text, a second Ctrl+Z removes the word itself. The Avalonia shell used to fold the text
/// insertion and the hyperlink formatting into a single <c>_bus.Execute</c> call, so one Ctrl+Z deleted the
/// whole typed word at once. The fix applies the hyperlink as a second, separate <c>_bus.Execute</c> call.
/// </summary>
public sealed class DocumentViewAutoHyperlinkUndoGranularityTests
{
    private static DocumentView EmptyView()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(string.Empty));
        var view = new DocumentView();
        view.LoadDocument(document);
        view.MoveCaretToBlock(0, 0);
        return view;
    }

    [Fact]
    public void TypingUrlThenSpace_AutoLinksTheWord_AsTwoSeparateUndoSteps()
    {
        var view = EmptyView();
        view.InsertText("http://example.com");

        var applied = view.TryAutoCorrectPublic(' ');
        applied.Should().BeTrue("a recognized URL followed by a space must trigger AutoFormat's hyperlink rule");

        var paragraph = (Paragraph)view.Document.Blocks[0];
        paragraph.PlainText.Should().Be("http://example.com ");
        paragraph.Runs.Should().Contain(run => run.HyperlinkUrl == "http://example.com",
            "the auto-recognized word must be wrapped in a hyperlink");

        // First Ctrl+Z: Word/WPF remove only the automatic hyperlink formatting -- the word stays as plain text.
        view.Undo();
        var afterFirstUndo = (Paragraph)view.Document.Blocks[0];
        afterFirstUndo.PlainText.Should().Be(
            "http://example.com ",
            "the first undo must remove only the hyperlink formatting, not the typed word itself");
        afterFirstUndo.Runs.Should().NotContain(run => run.HyperlinkUrl == "http://example.com",
            "the first undo must have removed the hyperlink formatting");

        // Second Ctrl+Z: now the AutoFormat re-emit (delete "http://example.com", insert
        // "http://example.com " as plain text) itself goes away, leaving exactly the plain text this test
        // typed via InsertText before triggering AutoFormat -- proving the link-removal above really was
        // its own, separate undo step rather than being bundled into this one.
        view.Undo();
        var afterSecondUndo = (Paragraph)view.Document.Blocks[0];
        afterSecondUndo.PlainText.Should().Be(
            "http://example.com",
            "the second undo must remove the AutoFormat text re-emit, distinct from the first undo which " +
            "only removed the hyperlink formatting");
    }

    /// <summary>Sibling no-regression: a space that does NOT complete a recognizable URL must still insert
    /// plainly through the normal (non-hyperlink) AutoCorrect/insert path, unaffected by splitting the
    /// hyperlink case into two undo steps.</summary>
    [Fact]
    public void TypingOrdinaryWordThenSpace_DoesNotLink_AndUndoesInOneStep()
    {
        var view = EmptyView();
        view.InsertText("hello");

        var applied = view.TryAutoCorrectPublic(' ');

        var paragraph = (Paragraph)view.Document.Blocks[0];
        if (applied)
        {
            paragraph.PlainText.Should().Be("hello ");
        }
        else
        {
            // No AutoFormat rule fired for a plain word; the caller (OnTextInput) falls through to a normal
            // InsertText(" ") in production. Mirror that here so the assertions below are meaningful either way.
            view.InsertText(" ");
            paragraph.PlainText.Should().Be("hello ");
        }
        paragraph.Runs.Should().NotContain(run => run.HyperlinkUrl != null);

        view.Undo();
        ((Paragraph)view.Document.Blocks[0]).PlainText.Should().Be("hello",
            "an ordinary (non-hyperlink) space-triggered edit must still undo the whole insert in one step");
    }
}
