using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// R175-shared-undo-across-save-F2: undoing back to exactly what was last saved must clear the
/// dirty flag (and therefore the close-changes prompt), the same way FreeX's WorkbookSession
/// already does via WorkbookDocumentState.SavedUndoDepth/TryMarkCleanIfAtSavePoint. FreeP has a
/// single unified <see cref="PresentationCommandBus"/> (unlike FreeW's hybrid native+command-bus
/// body editor), so <see cref="PresentationWorkareaSession"/> reads the bus's own
/// <see cref="PresentationCommandBus.UndoDepth"/>/<see cref="PresentationCommandBus.Version"/>
/// directly instead of shadowing them, and forwards them through two new operations
/// (<see cref="PresentationWorkareaOperation.MarkSavedAtUndoDepth"/>,
/// <see cref="PresentationWorkareaOperation.TryMarkCleanIfAtSavePoint"/>) to the SAME shared
/// FileCommandSession.MarkSavedAtUndoDepth / TryMarkCleanIfAtSavePoint API FreeX and FreeW's fix
/// both use. <see cref="FileTrackingEndpoint"/> below wires those operations to a real
/// <see cref="FileCommandWorkflow"/> exactly the way freep/FreeP.App.Host/MainWindow.WorkareaEndpoint.cs
/// wires them in production.
/// </summary>
public sealed class R175_PresentationWorkareaUndoSavePointTests
{
    [Fact]
    public void UndoLastEditBackToSavedContent_ClearsDirtyAndAllowsCloseWithoutPrompt()
    {
        var (session, file, endpoint) = CreateHarness();

        session.Editor.SetSlideTitle(0, "Saved title").Should().BeTrue();
        session.NotifySaved();
        file.IsDirty.Should().BeFalse();

        // The exact finding gesture: one more edit (move/retype), then undo exactly that edit.
        session.Editor.SetSlideTitle(0, "Unsaved title").Should().BeTrue();
        file.IsDirty.Should().BeTrue();

        session.ExecuteCommand(FreePKeyboardCommand.Undo);

        file.IsDirty.Should().BeFalse();
        // The close prompt keys off the same shared dirty flag (ConfirmCloseAllowed ->
        // ConfirmDiscardOrSave), so a clean undo must let the window close without asking.
        file.ConfirmCloseAllowed().Should().BeTrue();
        endpoint.NativeCommands.Should().BeEmpty(); // Cancel-prompt would only fire on a dirty close.
    }

    [Fact]
    public void UndoPastSavePointThenRedoBackToIt_ClearsDirty()
    {
        var (session, file, _) = CreateHarness();

        session.Editor.SetSlideTitle(0, "A").Should().BeTrue();
        session.Editor.SetSlideTitle(0, "B").Should().BeTrue();
        session.NotifySaved();
        file.IsDirty.Should().BeFalse();

        // Undo PAST the save point (back to the original empty title, before either A or B).
        session.ExecuteCommand(FreePKeyboardCommand.Undo);
        session.ExecuteCommand(FreePKeyboardCommand.Undo);
        file.IsDirty.Should().BeTrue();
        file.ConfirmCloseAllowed().Should().BeFalse();

        // ... and back again: redo both, landing exactly on the save point.
        session.ExecuteCommand(FreePKeyboardCommand.Redo);
        session.ExecuteCommand(FreePKeyboardCommand.Redo);

        file.IsDirty.Should().BeFalse();
        file.ConfirmCloseAllowed().Should().BeTrue();
    }

    [Fact]
    public void RedoUpToSavePoint_ClearsDirty()
    {
        var (session, file, _) = CreateHarness();

        session.Editor.SetSlideTitle(0, "A").Should().BeTrue();
        session.NotifySaved();
        file.IsDirty.Should().BeFalse();

        session.ExecuteCommand(FreePKeyboardCommand.Undo);
        file.IsDirty.Should().BeTrue();
        file.ConfirmCloseAllowed().Should().BeFalse();

        session.ExecuteCommand(FreePKeyboardCommand.Redo);

        file.IsDirty.Should().BeFalse();
        file.ConfirmCloseAllowed().Should().BeTrue();
    }

    [Fact]
    public void SaveWhileSomeEditsAreUndone_RecordsCurrentPositionNotDocumentStart()
    {
        var (session, file, _) = CreateHarness();

        session.Editor.SetSlideTitle(0, "A").Should().BeTrue();
        session.Editor.SetSlideTitle(0, "B").Should().BeTrue();

        // Undo B before saving: the save point must become "A", not depth 0 (the document's
        // original state) and not depth 2 (B, which was never actually saved).
        session.ExecuteCommand(FreePKeyboardCommand.Undo);
        file.IsDirty.Should().BeTrue();
        session.NotifySaved();
        file.IsDirty.Should().BeFalse();

        // A fresh edit then undoing exactly that edit must return to the just-recorded save point.
        session.Editor.SetSlideTitle(0, "C").Should().BeTrue();
        file.IsDirty.Should().BeTrue();
        session.ExecuteCommand(FreePKeyboardCommand.Undo);
        file.IsDirty.Should().BeFalse();
        file.ConfirmCloseAllowed().Should().BeTrue();

        // Redoing B (the entry this save point deliberately left behind) must NOT be mistaken for
        // clean: it is new, unsaved content relative to the "A" save point.
        session.ExecuteCommand(FreePKeyboardCommand.Redo);
        file.IsDirty.Should().BeTrue();
        file.ConfirmCloseAllowed().Should().BeFalse();
    }

    /// <summary>
    /// Sibling no-regression (R175 boundary case: "an operation that cannot be undone at all", e.g.
    /// freep/FreeP.App.Host/MainWindow.cs's several direct <c>_fileSession.MarkDirty()</c> call sites
    /// that bypass <see cref="PresentationWorkareaSession"/> entirely -- autosave recovery,
    /// external-modification handling). Such a mark leaves the bus's depth/version untouched, so a
    /// later Undo/Redo of a SEPARATE tracked edit that does not return the bus to the exact recorded
    /// save point correctly leaves the presentation dirty, which is the common case this test proves.
    /// <para>
    /// <b>Known shared limitation (matches FreeX, not introduced by this fix):</b> if a later tracked
    /// Undo/Redo happens to return the bus to precisely the depth/version that WAS the save point,
    /// <see cref="TryMarkCleanIfAtSavePoint"/> cannot distinguish that from "nothing untracked
    /// happened" and clears dirty anyway -- FreeX's own WorkbookSession.MarkDirtyFromHost/
    /// MarkDirtyForRecovery (src/FreeX.App.Services/WorkbookSession.cs) has the identical
    /// characteristic: neither invalidates WorkbookDocumentState.SavedUndoDepth, and MarkDirty()
    /// cannot invalidate it unconditionally without breaking the intended case (Undo/Redo's own
    /// MarkDirty-then-correct sequence, which relies on the save point surviving that MarkDirty).
    /// Closing this gap would require every untracked MarkDirty call site to also know how to
    /// invalidate the save point -- out of scope here; not a regression versus pre-fix behavior's
    /// worse default (dirty never self-cleared, period).
    /// </para>
    /// </summary>
    [Fact]
    public void UntrackedMarkDirty_StaysDirtyWhileUnrelatedUndoDoesNotReachTheSavePoint()
    {
        var (session, file, endpoint) = CreateHarness();

        session.Editor.SetSlideTitle(0, "A").Should().BeTrue();
        session.NotifySaved();
        file.IsDirty.Should().BeFalse();

        // A change the bus never recorded (e.g. a renderer-only edit that still must dirty the
        // document) -- applied directly, the same operation MarkDirty-only call sites use.
        endpoint.Apply(
            PresentationWorkareaOperation.MarkDirty,
            new PresentationWorkareaContext(PresentationWorkareaTransition.EditorChanged, session.Snapshot));
        file.IsDirty.Should().BeTrue();

        // A separate, still-tracked edit, undone only partway (bus depth is now 1 -- "A" -- which is
        // ALSO where the save point sits; see RedoUpToSavePoint_ClearsDirty for that exact-match case
        // handled elsewhere). Here we redo it back out to depth 2, away from the save point, so this
        // assertion exercises only the unambiguous "nowhere near the save point" case.
        session.Editor.SetSlideTitle(0, "B").Should().BeTrue();
        session.ExecuteCommand(FreePKeyboardCommand.Undo);
        session.ExecuteCommand(FreePKeyboardCommand.Redo);

        // The untracked change is still unsaved: dirty must remain true.
        file.IsDirty.Should().BeTrue();
        file.ConfirmCloseAllowed().Should().BeFalse();
    }

    private static (PresentationWorkareaSession Session, FileCommandWorkflow File, FileTrackingEndpoint Endpoint)
        CreateHarness()
    {
        var endpoint = new FileTrackingEndpoint();
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Add(new Slide { Title = "Original" });
        var session = new PresentationWorkareaSession(endpoint, presentation);
        return (session, endpoint.File, endpoint);
    }

    /// <summary>
    /// Wires the two new operations to a real <see cref="FileCommandWorkflow"/> exactly the way
    /// freep/FreeP.App.Host/MainWindow.WorkareaEndpoint.cs does in production (reading
    /// <c>editor.Bus.UndoDepth</c>/<c>.Version</c> from the same <see cref="EditingSession"/> the
    /// BindEditor operation already carries), plus MarkDirty so IsDirty reflects real edits.
    /// </summary>
    private sealed class FileTrackingEndpoint : IPresentationWorkareaEndpoint
    {
        public readonly FileCommandWorkflow File = new(
            maxRecentEntries: () => 10,
            onChanged: () => { },
            promptSaveChanges: _ => SaveChangesPrompt.Cancel,
            save: () => false);

        public List<PresentationWorkareaNativeCommand> NativeCommands { get; } = [];

        public void Apply(PresentationWorkareaOperation operation, PresentationWorkareaContext context)
        {
            switch (operation)
            {
                case PresentationWorkareaOperation.MarkDirty:
                    File.MarkDirty();
                    break;
                case PresentationWorkareaOperation.MarkSavedAtUndoDepth:
                    File.MarkSavedAtUndoDepth(
                        context.Snapshot.Editor.Bus.UndoDepth,
                        context.Snapshot.Editor.Bus.Version);
                    break;
                case PresentationWorkareaOperation.TryMarkCleanIfAtSavePoint:
                    File.TryMarkCleanIfAtSavePoint(
                        context.Snapshot.Editor.Bus.UndoDepth,
                        context.Snapshot.Editor.Bus.Version);
                    break;
            }
        }

        public void ExecuteNativeCommand(PresentationWorkareaNativeCommand command) =>
            NativeCommands.Add(command);
    }
}
