using System.Collections;
using System.Reflection;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regression coverage for sweep93 F1: the first genuine keystroke typed immediately after an
/// Undo (or Redo) in PagedEdit mode used to be silently swallowed by
/// <see cref="CrossPageUndoCoordinator"/>'s (now removed) <c>_suppressNextCapture</c> flag,
/// because <see cref="PaginatedEditorPanel.Rebuild"/> never raises a synthetic TextChanged for
/// that flag to consume -- it constructs brand-new <see cref="PageBox"/> instances with their
/// restored content already set, and only hooks TextChanged afterward. The swallowed keystroke's
/// TextChanged handler used to return early without ever setting <c>_pendingBurstCaptured</c> or
/// pushing an undo-stack entry, so the burst's pre-edit snapshot never got captured at all.
///
/// <para>
/// These tests observe the effect directly on the undo/redo stacks (via a small reflection
/// helper) rather than asserting exact restored text content, because the coordinator has a
/// separate, pre-existing, unrelated characteristic outside the scope of this finding: since
/// <c>TextChanged</c> fires only after WPF has already applied the edit, the very first
/// keystroke of *any* fresh burst -- including the first edit made in a session, well before any
/// Undo/Redo is ever involved -- is already reflected in the model by the time its own
/// "pre-edit" snapshot is captured and committed. That is orthogonal to this finding and must
/// not be conflated with it.
/// </para>
///
/// <para>Runs on STA because tests create real WPF RichTextBox / FlowDocument instances.</para>
/// </summary>
public sealed class CrossPageUndoCoordinatorFirstKeystrokeTests
{
    /// <summary>
    /// Edit, Undo (stack now empty), then type exactly one further keystroke. That single
    /// keystroke's TextChanged must be treated as the start of a new burst and captured --
    /// <see cref="CrossPageUndoCoordinator.CanUndo"/> must already be true after just that one
    /// keystroke, not only after a second one.
    /// </summary>
    [StaFact]
    public void Undo_ThenSingleKeystroke_IsCapturedImmediately()
    {
        var (panel, editor) = BuildOnePageEditablePanel();
        var undoCoord = panel.UndoCoordinator;

        var box0 = panel.PageBoxes[0];
        box0.Body.Focus();
        box0.Body.CaretPosition = box0.Body.Document.ContentEnd;
        box0.Body.AppendText(" X");
        PaginatedCommitCoordinator.Commit(panel, editor);

        undoCoord.CanUndo.Should().BeTrue("appending text must have captured a pre-edit snapshot");

        undoCoord.Undo().Should().BeTrue();
        undoCoord.CanUndo.Should().BeFalse("the only undo entry was just popped by Undo()");

        // The single genuine keystroke the finding is about. Rebuild() (invoked by Undo() above)
        // never raises a TextChanged of its own, so this is the FIRST TextChanged the coordinator
        // sees since the Undo -- it must not be swallowed.
        var boxAfterUndo = panel.PageBoxes[0];
        boxAfterUndo.Body.Focus();
        boxAfterUndo.Body.CaretPosition = boxAfterUndo.Body.Document.ContentEnd;
        boxAfterUndo.Body.AppendText("Y");

        undoCoord.CanUndo.Should().BeTrue(
            "the first keystroke after an Undo must be captured as the start of a new burst, " +
            "not swallowed while waiting for a synthetic TextChanged that Rebuild() never raises");
    }

    /// <summary>
    /// Sibling: the same swallow pattern applied to <c>Redo</c> (finding evidence line :152).
    /// Unlike the Undo case, <see cref="CrossPageUndoCoordinator.CanUndo"/> is already true right
    /// after Redo() itself (it pushes the pre-redo state for the reverse direction), so the
    /// discriminator here is the undo-stack *count* growing by one on the next keystroke rather
    /// than staying flat.
    /// </summary>
    [StaFact]
    public void Redo_ThenSingleKeystroke_IsCapturedImmediately()
    {
        var (panel, editor) = BuildOnePageEditablePanel();
        var undoCoord = panel.UndoCoordinator;

        var box0 = panel.PageBoxes[0];
        box0.Body.Focus();
        box0.Body.CaretPosition = box0.Body.Document.ContentEnd;
        box0.Body.AppendText(" X");
        PaginatedCommitCoordinator.Commit(panel, editor);

        undoCoord.CanUndo.Should().BeTrue();
        undoCoord.Undo().Should().BeTrue();
        undoCoord.CanRedo.Should().BeTrue();

        undoCoord.Redo().Should().BeTrue();
        int undoStackCountAfterRedo = GetUndoStackCount(undoCoord);
        undoStackCountAfterRedo.Should().Be(1, "Redo() itself pushes the pre-redo state for the reverse direction");

        // The single genuine keystroke immediately after the Redo.
        var boxAfterRedo = panel.PageBoxes[0];
        boxAfterRedo.Body.Focus();
        boxAfterRedo.Body.CaretPosition = boxAfterRedo.Body.Document.ContentEnd;
        boxAfterRedo.Body.AppendText("Z");

        GetUndoStackCount(undoCoord).Should().Be(undoStackCountAfterRedo + 1,
            "the first keystroke after a Redo must be captured as the start of a new burst, " +
            "growing the undo stack immediately instead of being silently swallowed");
    }

    /// <summary>
    /// Adjacent-case regression guard: ordinary burst-collapsing (no Undo/Redo involved at all)
    /// must be unaffected by removing the suppression flag. Two keystrokes typed back-to-back in
    /// the same burst must still push exactly one undo-stack entry, not two.
    /// </summary>
    [StaFact]
    public void NormalBurst_TwoKeystrokesWithNoInterveningUndoRedo_CapturesOnlyOnce()
    {
        var (panel, _) = BuildOnePageEditablePanel();
        var undoCoord = panel.UndoCoordinator;

        GetUndoStackCount(undoCoord).Should().Be(0, "nothing has been edited yet");

        var box0 = panel.PageBoxes[0];
        box0.Body.Focus();
        box0.Body.CaretPosition = box0.Body.Document.ContentEnd;
        box0.Body.AppendText("A");
        box0.Body.AppendText("B");

        GetUndoStackCount(undoCoord).Should().Be(1,
            "two keystrokes in the same uninterrupted burst must collapse into a single undo entry");
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────

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

    /// <summary>
    /// Reads the private <c>_undoStack</c> field's count via reflection. The coordinator does not
    /// expose the exact depth publicly (only <see cref="CrossPageUndoCoordinator.CanUndo"/>, a
    /// boolean), and after a Redo() the boolean alone cannot discriminate "swallowed" from
    /// "captured" for the very next keystroke since it is already true beforehand.
    /// </summary>
    private static int GetUndoStackCount(CrossPageUndoCoordinator coordinator)
    {
        var field = typeof(CrossPageUndoCoordinator)
            .GetField("_undoStack", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull("CrossPageUndoCoordinator must still have a private _undoStack field");
        var stack = (ICollection)field!.GetValue(coordinator)!;
        return stack.Count;
    }
}
