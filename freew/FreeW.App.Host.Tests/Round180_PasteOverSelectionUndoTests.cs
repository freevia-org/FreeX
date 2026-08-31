using System.Linq;

using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Round 180. Pasting rich content over a selection removed the selected text with a bare native
/// TextRange.Text assignment followed by CommitToModel() -- neither of which pushes anything onto
/// the command bus. The only command that DID get pushed came from TryInsertDocumentAtBodyCaret, and
/// it snapshots the paragraph as it finds it: already shortened. So one Ctrl+Z removed the pasted
/// content and left the replaced text permanently gone, with no further undo to recover it. Render()
/// reassigns Document and discards WPF-native undo, so the bus is the only way back.
///
/// The Avalonia shell has always grouped the delete and the insert into one undoable unit for the
/// identical gesture.
/// </summary>
public sealed class Round180_PasteOverSelectionUndoTests
{
    private static DocumentView BuildView(string text)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(text));

        var view = new DocumentView();
        view.LoadModel(document);
        return view;
    }

    private static TextDocument PasteSource(string text)
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        source.Blocks.Add(new Paragraph(text));
        return source;
    }

    [StaFact]
    public void OneUndoAfterPastingOverASelection_RestoresTheReplacedText()
    {
        var view = BuildView("Hello cat world");
        view.SetSelectionRangeForTest(0, 6, 0, 9); // "cat"

        view.PasteKeepSourceFormattingForTest(PasteSource("dog")).Should().BeTrue();
        view.CommitToModel();
        ((Paragraph)view.Model.Blocks[0]).PlainText.Should().Contain("dog");

        view.Undo();
        view.CommitToModel();

        string.Concat(view.Model.Blocks.OfType<Paragraph>().Select(p => p.PlainText))
            .Should().Be(
                "Hello cat world",
                "one Ctrl+Z must undo the whole paste -- the replaced text has to come back, not just "
                + "the pasted content disappear");
    }

    [StaFact]
    public void OneUndoAfterPastingAtACollapsedCaret_StillWorks()
    {
        // Sibling no-regression: the empty-selection path must be unaffected by the grouping.
        var view = BuildView("Hello world");
        view.SetSelectionRangeForTest(0, 5, 0, 5);

        view.PasteKeepSourceFormattingForTest(PasteSource(" there")).Should().BeTrue();
        view.CommitToModel();

        view.Undo();
        view.CommitToModel();

        string.Concat(view.Model.Blocks.OfType<Paragraph>().Select(p => p.PlainText))
            .Should().Be("Hello world");
    }
}
