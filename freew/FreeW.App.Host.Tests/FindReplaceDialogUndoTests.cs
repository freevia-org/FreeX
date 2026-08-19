using Free.Shared.AppServices;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Word's Replace All is one undoable action: a single Ctrl+Z restores every replaced occurrence.
/// FreeW's <see cref="FindReplaceDialog"/> WPF command host must wrap its replace-all loop in a
/// <see cref="DocumentCommandBus"/> undo group (mirroring the coordinators that already do this, e.g.
/// FreeW.App.Presentation/DocumentView/MultilevelListMutationCoordinator.cs) so N replacements collapse
/// into one undo-stack entry instead of N. See freew/FreeW.App.Host/FindReplaceDialog.cs ReplaceAll().
/// </summary>
public sealed class FindReplaceDialogUndoTests
{
    private static DocumentView BuildView(string text)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(text));

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static Paragraph ParagraphOf(DocumentView view)
    {
        view.CommitToModel();
        return (Paragraph)view.Model.Blocks[0];
    }

    [StaFact]
    public void ReplaceAll_ThenOneUndo_RevertsEveryOccurrenceInOneStep()
    {
        var view = BuildView("foo bar foo baz foo");

        var dialog = new FindReplaceDialog(null!, view, FindReplaceOpenMode.Replace);
        try
        {
            dialog.Show();
            dialog.SetFindTextForTest("foo");
            dialog.SetReplaceTextForTest("XXX");

            dialog.ReplaceAllForTest();

            ParagraphOf(view).PlainText.Should().Be("XXX bar XXX baz XXX");
            dialog.StatusForTest.Should().Contain("3");

            view.CanUndo.Should().BeTrue();
            view.Undo();

            // A single Undo must restore every replacement made by Replace All, not just the last one.
            ParagraphOf(view).PlainText.Should().Be("foo bar foo baz foo");
        }
        finally
        {
            dialog.Close();
        }
    }

    [StaFact]
    public void Replace_SingleOccurrence_StillOneUndoStepNoRegression()
    {
        // Sibling case: the single Replace path (not Replace All) must keep behaving as one undo step,
        // exactly as before this fix -- it already only ever executes one command per invocation.
        var view = BuildView("Hello cat world");
        view.SetSelectionRangeForTest(0, 6, 0, 9);

        var dialog = new FindReplaceDialog(null!, view, FindReplaceOpenMode.Replace);
        try
        {
            dialog.Show();
            dialog.SetFindTextForTest("cat");
            dialog.SetReplaceTextForTest("dog");

            dialog.ReplaceForTest();

            ParagraphOf(view).PlainText.Should().Be("Hello dog world");

            view.CanUndo.Should().BeTrue();
            view.Undo();

            ParagraphOf(view).PlainText.Should().Be("Hello cat world");
            // And nothing further to undo from this single replacement.
            view.Commands.CanUndo.Should().BeFalse();
        }
        finally
        {
            dialog.Close();
        }
    }
}
