using System;
using System.IO;
using System.Reflection;
using Free.Shared.AppServices;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Options;
using FreeW.Core.IO;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// R175-shared-undo-across-save-F1: undoing back to exactly the content on disk must clear the
/// dirty flag (and therefore the close-changes prompt), the same way FreeX's WorkbookSession
/// already does via WorkbookDocumentState.SavedUndoDepth/TryMarkCleanIfAtSavePoint. FreeW's body
/// editor has no such stack to query directly (WPF's native RichTextBox undo manager exposes no
/// depth/identity token), so <see cref="MainWindow.OnDocumentEditorChanged"/> rebuilds an
/// equivalent stamped shadow stack from <see cref="System.Windows.Controls.TextChangedEventArgs.UndoAction"/>
/// and feeds it through the same shared FileCommandSession.MarkSavedAtUndoDepth /
/// TryMarkCleanIfAtSavePoint API FreeX uses.
///
/// These drive the real production entry points: <see cref="DocumentView.Selection"/> text
/// assignment (the same native RichTextBox content mutation ordinary typing performs whenever
/// DocumentView.CanUseNativeUntrackedTextInput() is true -- the default, untracked-changes case)
/// and <see cref="DocumentView.Undo"/>/<see cref="DocumentView.Redo"/> (exactly what MainWindow's
/// own Undo()/Redo() -- wired to Ctrl+Z/Ctrl+Y and the ribbon/QAT buttons -- call). The window is
/// shown (off-screen) because WPF's RichTextBox only attaches its native undo manager once its
/// template has actually been applied -- an unshown Window/RichTextBox pair never records native
/// undo at all, which would make every assertion here vacuously true for the wrong reason.
/// </summary>
public sealed class R175_UndoBackToSavePointClearsDirtyTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeW.R175UndoSavePoint-");
    private string TempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    private (MainWindow window, DocumentView editor, RecordingUserMessageService messages) CreateOpenedHarness()
    {
        var messages = new RecordingUserMessageService { NextResult = UserMessageResult.Cancel };
        var window = new MainWindow(new FreeWOptions(), messageService: messages);
        window.Show();
        var path = WriteDocx("Base.docx", "Hello");
        Assert.True(GetFileCommands(window).OpenPath(path));
        var editor = window.ActiveDocumentEditorForTests;
        editor.Focus();
        return (window, editor, messages);
    }

    [StaFact]
    public void UndoTypedCharacterBackToSavedContent_ClearsDirtyAndAllowsCloseWithoutPrompt()
    {
        var (window, editor, _) = CreateOpenedHarness();

        TypeAtEnd(editor, "!");
        Assert.True(GetFileCommands(window).Save());
        Assert.False(GetFileCommands(window).IsDirty);

        // The exact finding gesture: one more edit, then undo exactly that edit.
        TypeAtEnd(editor, "?");
        Assert.True(GetFileCommands(window).IsDirty);

        editor.Undo();

        Assert.False(GetFileCommands(window).IsDirty);
        // The close prompt keys off the same shared dirty flag (FileCommands.ConfirmCloseAllowed ->
        // SisterWpfFileCommandWorkflow -> FileCommandWorkflow.ConfirmDiscardOrSave), so a clean undo
        // must let the window close without asking.
        Assert.True(GetFileCommands(window).ConfirmCloseAllowed());
        window.Close();
    }

    [StaFact]
    public void UndoPastSavePointThenRedoBackToIt_ClearsDirty()
    {
        var (window, editor, _) = CreateOpenedHarness();

        TypeAtEnd(editor, "A");
        TypeAtEnd(editor, "B");
        Assert.True(GetFileCommands(window).Save());
        Assert.False(GetFileCommands(window).IsDirty);

        // Undo PAST the save point (back to the original "Hello", before either A or B).
        editor.Undo();
        editor.Undo();
        Assert.True(GetFileCommands(window).IsDirty);
        Assert.False(GetFileCommands(window).ConfirmCloseAllowed());

        // ... and back again: redo both, landing exactly on the save point.
        editor.Redo();
        editor.Redo();

        Assert.False(GetFileCommands(window).IsDirty);
        Assert.True(GetFileCommands(window).ConfirmCloseAllowed());
        window.Close();
    }

    [StaFact]
    public void RedoUpToSavePoint_ClearsDirty()
    {
        var (window, editor, _) = CreateOpenedHarness();

        TypeAtEnd(editor, "A");
        Assert.True(GetFileCommands(window).Save());
        Assert.False(GetFileCommands(window).IsDirty);

        editor.Undo();
        Assert.True(GetFileCommands(window).IsDirty);
        Assert.False(GetFileCommands(window).ConfirmCloseAllowed());

        editor.Redo();

        Assert.False(GetFileCommands(window).IsDirty);
        Assert.True(GetFileCommands(window).ConfirmCloseAllowed());
        window.Close();
    }

    [StaFact]
    public void SaveWhileSomeEditsAreUndone_RecordsCurrentPositionNotDocumentStart()
    {
        var (window, editor, _) = CreateOpenedHarness();

        TypeAtEnd(editor, "A");
        TypeAtEnd(editor, "B");

        // Undo B before saving: the save point must become "Hello + A", not "Hello" (depth 0) and
        // not "Hello + A + B" (the never-reached depth 2).
        editor.Undo();
        Assert.True(GetFileCommands(window).IsDirty);
        Assert.True(GetFileCommands(window).Save());
        Assert.False(GetFileCommands(window).IsDirty);

        // A fresh edit then undoing exactly that edit must return to the just-recorded save point.
        TypeAtEnd(editor, "C");
        Assert.True(GetFileCommands(window).IsDirty);
        editor.Undo();
        Assert.False(GetFileCommands(window).IsDirty);
        Assert.True(GetFileCommands(window).ConfirmCloseAllowed());

        // Redoing B (the entry this save point deliberately left behind) must NOT be mistaken for
        // clean: it is new, unsaved content relative to the "Hello + A" save point.
        editor.Redo();
        Assert.True(GetFileCommands(window).IsDirty);
        Assert.False(GetFileCommands(window).ConfirmCloseAllowed());
        window.Close();
    }

    /// <summary>
    /// Sibling no-regression (R175 boundary case: "an operation that cannot be undone at all"):
    /// Accept/Reject Revision, the Notes/Header-Footer panes, and the Properties dialog all route
    /// through MainWindow.MarkDirtyOutsideBodyUndo instead of the tracked body-edit path, so a later
    /// undo/redo of a completely separate, still-tracked body edit must never be mistaken for
    /// "back to clean" while that untracked change remains unsaved -- only an explicit Save may
    /// clear it, exactly as before this fix.
    /// </summary>
    [StaFact]
    public void UntrackedEditAfterSave_IsNotClearedByUnrelatedBodyUndo()
    {
        var (window, editor, _) = CreateOpenedHarness();

        TypeAtEnd(editor, "A");
        Assert.True(GetFileCommands(window).Save());
        Assert.False(GetFileCommands(window).IsDirty);

        // A change the shadow stack cannot represent (Properties dialog OK, Accept/Reject Revision,
        // Notes/Header-Footer apply -- all funnel through the same private MarkDirtyOutsideBodyUndo).
        InvokeMarkDirtyOutsideBodyUndo(window);
        Assert.True(GetFileCommands(window).IsDirty);

        // A separate, still-tracked body edit, immediately undone -- back to the exact depth/version
        // recorded at the last Save.
        TypeAtEnd(editor, "B");
        editor.Undo();

        // The untracked Properties/Revision-style change is still unsaved: must remain dirty and
        // still prompt on close.
        Assert.True(GetFileCommands(window).IsDirty);
        Assert.False(GetFileCommands(window).ConfirmCloseAllowed());
        window.Close();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static void TypeAtEnd(DocumentView editor, string text)
    {
        // The same native RichTextBox content mutation ordinary typing performs when
        // DocumentView.CanUseNativeUntrackedTextInput() is true (no track changes, not read-only,
        // no selection, not inside a locked content control -- the default case this finding's
        // gesture is about). Raising a real WPF key/text-composition event is unreliable in a
        // headless test host (see DocumentView.ApplyNativeFallbackDeleteAndPruneOrphanedAnchors'
        // own doc comment); assigning Selection.Text goes through the identical TextRange/
        // TextContainer machinery WPF's own native RichTextBox undo manager records either way,
        // once the control's template has been applied (see the class's window.Show() above).
        var end = editor.Document.ContentEnd;
        editor.Selection.Select(end, end);
        editor.Selection.Text = text;
        editor.CaretPosition = editor.Selection.End;
    }

    private static FileCommands GetFileCommands(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_file", BindingFlags.Instance | BindingFlags.NonPublic);
        return (FileCommands)field!.GetValue(window)!;
    }

    private static void InvokeMarkDirtyOutsideBodyUndo(MainWindow window)
    {
        var method = typeof(MainWindow).GetMethod(
            "MarkDirtyOutsideBodyUndo",
            BindingFlags.Instance | BindingFlags.NonPublic,
            Type.EmptyTypes);
        method!.Invoke(window, null);
    }

    private string WriteDocx(string name, string text)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(text));
        var path = Path.Combine(TempDir, name);
        DocxWriter.Write(doc, path);
        return path;
    }
}
