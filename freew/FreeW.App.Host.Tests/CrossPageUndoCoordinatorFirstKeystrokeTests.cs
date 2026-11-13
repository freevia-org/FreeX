using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regression coverage for sweep93 F1 (the first keystroke after an Undo/Redo in PagedEdit used to be
/// swallowed) on top of the W7 bus-backed undo: page edits are written to the source editor's
/// DocumentCommandBus as one unit per burst, so these tests observe the shared history directly.
///
/// <para>Runs on STA because tests create real WPF RichTextBox / FlowDocument instances.</para>
/// </summary>
public sealed class CrossPageUndoCoordinatorFirstKeystrokeTests
{
    [StaFact]
    public void Undo_ThenSingleKeystroke_IsCapturedImmediately()
    {
        var (panel, editor) = BuildOnePageEditablePanel();
        var undoCoord = panel.UndoCoordinator;

        Append(panel.PageBoxes[0], " X");
        PaginatedCommitCoordinator.Commit(panel, editor);

        undoCoord.CanUndo.Should().BeTrue("the committed burst is an undo unit on the shared bus");

        undoCoord.Undo().Should().BeTrue();
        undoCoord.CanUndo.Should().BeFalse("the only undo unit was just reverted");

        Append(panel.PageBoxes[0], "Y");

        undoCoord.CanUndo.Should().BeTrue(
            "the first keystroke after an Undo must count as a new edit, not be swallowed");
        undoCoord.Undo().Should().BeTrue();
        ModelText(editor).Should().Be("Page 1 content",
            "undoing that keystroke returns the document to its original text");
    }

    [StaFact]
    public void Redo_ThenSingleKeystroke_IsCapturedImmediately()
    {
        var (panel, editor) = BuildOnePageEditablePanel();
        var undoCoord = panel.UndoCoordinator;

        Append(panel.PageBoxes[0], " X");
        PaginatedCommitCoordinator.Commit(panel, editor);

        undoCoord.Undo().Should().BeTrue();
        undoCoord.CanRedo.Should().BeTrue();
        undoCoord.Redo().Should().BeTrue();
        ModelText(editor).Should().Be("Page 1 content X");

        Append(panel.PageBoxes[0], "Z");
        undoCoord.CanRedo.Should().BeFalse("a new keystroke invalidates redo, as on the bus");

        undoCoord.Undo().Should().BeTrue();
        ModelText(editor).Should().Be("Page 1 content X",
            "the keystroke after the Redo is its own undo unit, reverted first");
        undoCoord.Undo().Should().BeTrue();
        ModelText(editor).Should().Be("Page 1 content");
    }

    [StaFact]
    public void NormalBurst_TwoKeystrokesWithNoInterveningUndoRedo_IsOneUndoUnit()
    {
        var (panel, editor) = BuildOnePageEditablePanel();
        var undoCoord = panel.UndoCoordinator;

        undoCoord.CanUndo.Should().BeFalse("nothing has been edited yet");

        Append(panel.PageBoxes[0], "A");
        Append(panel.PageBoxes[0], "B");
        PaginatedCommitCoordinator.Commit(panel, editor);

        undoCoord.Undo().Should().BeTrue();
        ModelText(editor).Should().Be("Page 1 content",
            "two keystrokes in one uninterrupted burst collapse into a single undo unit");
        undoCoord.CanUndo.Should().BeFalse();
    }

    private static void Append(PageBox box, string text)
    {
        box.Body.Focus();
        box.Body.CaretPosition = box.Body.Document.ContentEnd;
        box.Body.AppendText(text);
    }

    private static string ModelText(DocumentView editor) =>
        string.Join("|", editor.Model.Blocks.OfType<Paragraph>().Select(p => p.PlainText.TrimEnd('\r', '\n')));

    private static (PaginatedEditorPanel panel, DocumentView editor) BuildOnePageEditablePanel()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Page 1 content"));

        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();

        var panel = PaginatedEditorPanel.Build(editor);
        return (panel, editor);
    }
}
