using System.Linq;
using FreeW.App.Presentation.Panes;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Window-level notes workflow: the pane opens at a note, the rich sub-editor commits as ONE undoable
/// document edit when the user leaves it, and the Footnotes/Endnotes selector drives the listing.
/// </summary>
public sealed class NotesPaneWindowWorkflowTests
{
    [StaFact]
    public void OpeningThePaneAtANote_ThenLeavingIt_CommitsTheTypedNoteAsOneUndoUnit()
    {
        ReusableFreeWMainWindowSession.Run(window =>
        {
            try
            {
                window.BodyEditorForTests.LoadModel(BuildDocument());

                window.OpenNotesPaneAtNoteForTests(footnote: true, id: 1);

                window.IsNotesPaneVisibleForTests.Should().BeTrue();
                window.NotesPaneStateForTests.SelectedNote!.Key.Should().Be(new DocumentNoteKey(true, 1));
                window.ApplyPendingNoteEditForTests().Should().BeFalse("loading a note is not an edit");

                var sub = window.NotesSubEditorForTests;
                sub.CaretPosition = sub.Document.ContentEnd;
                sub.CaretPosition.InsertTextInRun(" plus typed");

                window.ApplyPendingNoteEditForTests().Should().BeTrue();
                var body = window.BodyEditorForTests;
                body.Model.Footnotes[1].PlainText.Should().Be("first footnote plus typed");
                window.ApplyPendingNoteEditForTests().Should().BeFalse("the edit was already committed");

                body.Undo();
                body.Model.Footnotes[1].PlainText.Should().Be("first footnote");
                body.Redo();
                body.Model.Footnotes[1].PlainText.Should().Be("first footnote plus typed");
            }
            finally
            {
                if (window.IsNotesPaneVisibleForTests)
                    window.ToggleNotesPaneForTests();
            }
        });
    }

    [StaFact]
    public void ClosingThePane_KeepsWhatWasTyped()
    {
        ReusableFreeWMainWindowSession.Run(window =>
        {
            try
            {
                window.BodyEditorForTests.LoadModel(BuildDocument());
                window.OpenNotesPaneAtNoteForTests(footnote: false, id: 1);
                var sub = window.NotesSubEditorForTests;
                sub.CaretPosition = sub.Document.ContentEnd;
                sub.CaretPosition.InsertTextInRun("!");

                window.ToggleNotesPaneForTests();

                window.IsNotesPaneVisibleForTests.Should().BeFalse();
                window.BodyEditorForTests.Model.Endnotes[1].PlainText.Should().Be("only endnote!");
            }
            finally
            {
                if (window.IsNotesPaneVisibleForTests)
                    window.ToggleNotesPaneForTests();
            }
        });
    }

    [StaFact]
    public void Selector_ListsOnlyTheChosenNoteKind_AndFollowsAnActivatedReference()
    {
        ReusableFreeWMainWindowSession.Run(window =>
        {
            try
            {
                window.BodyEditorForTests.LoadModel(BuildDocument());
                window.OpenNotesPaneAtNoteForTests(footnote: true, id: 2);

                window.SelectNotesFilterForTests(2); // All Endnotes
                window.NotesPaneStateForTests.Filter.Should().Be(DocumentNotesPaneFilter.Endnotes);
                window.NotesPaneStateForTests.Items.Select(item => item.Key)
                    .Should().Equal(new DocumentNoteKey(false, 1));

                window.OpenNotesPaneAtNoteForTests(footnote: true, id: 2);
                window.NotesPaneStateForTests.Filter.Should().Be(DocumentNotesPaneFilter.Footnotes);
                window.NotesPaneStateForTests.SelectedNote!.Key.Should().Be(new DocumentNoteKey(true, 2));
                window.NotesSubEditorForTests.Model.Blocks.OfType<Paragraph>().Single().PlainText
                    .Should().Be("second footnote");
            }
            finally
            {
                window.SelectNotesFilterForTests(0);
                if (window.IsNotesPaneVisibleForTests)
                    window.ToggleNotesPaneForTests();
            }
        });
    }

    private static TextDocument BuildDocument()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var body = new Paragraph();
        body.Runs.Add(new Run("body "));
        body.Runs.Add(Run.FootnoteReference(1));
        body.Runs.Add(new Run(" more "));
        body.Runs.Add(Run.FootnoteReference(2));
        body.Runs.Add(new Run(" end "));
        body.Runs.Add(Run.EndnoteReference(1));
        document.Blocks.Add(body);
        document.Footnotes[1] = new Footnote(1, "first footnote");
        document.Footnotes[2] = new Footnote(2, "second footnote");
        document.Endnotes[1] = new Endnote(1, "only endnote");
        return document;
    }
}
