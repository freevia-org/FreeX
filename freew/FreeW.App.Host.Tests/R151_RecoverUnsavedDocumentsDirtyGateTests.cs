using System;
using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.Shell;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Shell;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Round 151 finding shared-autosave-recovery F2: FreeW's WPF Backstage "Recover Unsaved Documents"
/// command (<see cref="AutosaveCoordinator.RecoverUnsavedDocuments"/>) routed the current-window
/// restore through <c>_session.CompleteRecovery(recovery, accepted: true, _file.RecoverSnapshot)</c>
/// unconditionally. <c>FileCommands.RecoverSnapshot</c> wraps the restore in
/// <c>FileCommandWorkflow.Open</c>, which itself gates on <c>ConfirmDiscardOrSave</c> and returns
/// <c>false</c> -- not an exception -- when the user cancels that save-changes prompt for the
/// CURRENT window's OWN unsaved document. Because <c>accepted</c> was already pinned to
/// <c>true</c>, a plain "Cancel" on that unrelated prompt was indistinguishable from a genuinely
/// unreadable snapshot: <c>AutosaveRecoveryPolicy.ResolveDisposition(true, false)</c> resolved to
/// Quarantine and moved the candidate the user was actually trying to recover out of the Recovery
/// directory forever.
///
/// The fix pre-checks the dirty gate itself (<c>FileCommands.ConfirmCloseAllowed(action)</c>)
/// BEFORE calling <c>CompleteRecovery</c>, and reports a decline as <c>accepted: false</c> (the
/// existing "Keep" disposition — see <see cref="FreeWAutosaveSession.CompleteRecovery"/>) instead
/// of <c>accepted: true</c> with a restore that returns false. Mirrors FreeP's WPF host fix
/// (r146, <c>FreeP.App.Host.AutosaveCoordinator.RecoverUnsavedPresentations</c>).
/// </summary>
public sealed class R151_RecoverUnsavedDocumentsDirtyGateTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeW.R151RecoverUnsavedDirtyGateTests", Guid.NewGuid().ToString("N"));

    public R151_RecoverUnsavedDocumentsDirtyGateTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private (AutosaveCoordinator coordinator, FileCommands file) NewWindowHarness(AutosaveSnapshotStore store)
    {
        var window = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());
        var file = new FileCommands(
            window,
            editor,
            onChanged: () => { },
            loadRecentFilesStore: () => RecentFilesStore.Load(Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".json")));
        var coordinator = new AutosaveCoordinator(
            editor,
            file,
            ports => new FreeWAutosaveSession(ports, store));
        return (coordinator, file);
    }

    /// <summary>
    /// THE FIX: a crashed window (Document A) left a recoverable snapshot. The window running
    /// "Recover Unsaved Documents" right now has its OWN unrelated dirty document (Document B, no
    /// path). Accepting the offer for A raises the save-changes prompt for B; the user answers
    /// "Cancel". Document A's snapshot must survive on disk (not quarantined), and Document B must
    /// be left untouched.
    /// </summary>
    [StaFact]
    public void RecoverUnsavedDocuments_DeclinedCurrentWindowDirtyGate_PreservesTheCandidateAndTheCurrentDocument()
    {
        var store = new AutosaveSnapshotStore(_tempDir);

        // Window "crashed": leaves an unsaved-document-A snapshot behind.
        var (crashed, crashedFile) = NewWindowHarness(store);
        crashedFile.MarkDirty();
        crashed.SnapshotNowForTests();
        var snapshotPathA = store.GetSnapshotPath(crashed.SnapshotIdForTests);
        var sidecarPathA = store.GetSidecarPath(crashed.SnapshotIdForTests);
        File.Exists(snapshotPathA).Should().BeTrue("the crashed window must have left a snapshot behind");
        crashed.SimulateCrashForTests();

        // The window invoking the manual command right now: its OWN unsaved, unrelated
        // Document B (dirty, untitled -- editing this document, not the recovered one).
        var (recovering, recoveringFile) = NewWindowHarness(store);
        recoveringFile.MarkDirty();
        var owningWindow = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };

        // The offer prompt ("Recover unsaved changes to A?") is OkCancel -> answer Ok (accept).
        // The save-changes prompt raised for the CURRENT window's own dirty document is
        // YesNoCancel -> answer Cancel (decline), exactly as DialogMessageHelper/WpfMessageBoxRealizer
        // route buttons for each real dialog.
        HeadlessMessageBox.Handler = (_, buttons) => buttons == UserMessageButtons.OkCancel
            ? UserMessageResult.Ok
            : UserMessageResult.Cancel;
        try
        {
            var anyRecovered = recovering.RecoverUnsavedDocuments(owningWindow);

            anyRecovered.Should().BeFalse("the user declined the save-changes prompt for their current document");

            // Core assertion (the actual bug): the candidate must NOT be quarantined -- it must
            // still be on offer for a future recovery attempt.
            File.Exists(snapshotPathA).Should().BeTrue(
                "declining the CURRENT window's own save prompt must not quarantine an UNRELATED recovery candidate");
            File.Exists(sidecarPathA).Should().BeTrue(
                "the sidecar for the unrelated candidate must survive too");
            store.EnumerateCandidates().Should().ContainSingle(
                "the declined-elsewhere candidate must remain enumerable for a later recovery attempt")
                .Which.Sidecar.SnapshotId.Should().Be(crashed.SnapshotIdForTests);
            Directory.Exists(Path.Combine(_tempDir, "Quarantine")).Should().BeFalse(
                "nothing should have been moved into Quarantine by this decline");

            // Sibling assertion: Document B (the current window's own unsaved work) must be left
            // exactly as it was -- not silently overwritten by A's recovered content.
            recoveringFile.IsDirty.Should().BeTrue("the current window's own unsaved document must be untouched");
        }
        finally
        {
            HeadlessMessageBox.Handler = null;
        }
    }

    /// <summary>
    /// Sibling no-regression (rule 10): when the current window's dirty gate is satisfied (here,
    /// the window is clean -- nothing to lose), the manual recovery command must still actually
    /// recover the snapshot into the current window exactly as before the fix. The fix must not
    /// turn the pre-check into an unconditional block.
    /// </summary>
    [StaFact]
    public void RecoverUnsavedDocuments_CleanCurrentWindow_StillRecoversIntoIt()
    {
        var store = new AutosaveSnapshotStore(_tempDir);

        var (crashed, crashedFile) = NewWindowHarness(store);
        crashedFile.MarkDirty();
        crashed.SnapshotNowForTests();
        var snapshotPathA = store.GetSnapshotPath(crashed.SnapshotIdForTests);
        File.Exists(snapshotPathA).Should().BeTrue();
        crashed.SimulateCrashForTests();

        // Recovering window is clean -- no unsaved edits of its own, so the dirty gate is a no-op.
        var (recovering, recoveringFile) = NewWindowHarness(store);
        var owningWindow = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };

        HeadlessMessageBox.Handler = (_, _) => UserMessageResult.Ok;
        try
        {
            var anyRecovered = recovering.RecoverUnsavedDocuments(owningWindow);

            anyRecovered.Should().BeTrue("a clean current window has nothing to lose, so recovery must proceed");
            recoveringFile.IsDirty.Should().BeTrue("the recovered snapshot should have loaded into the current window");
            store.EnumerateCandidates().Should().BeEmpty("a successfully recovered candidate must be consumed, not left behind");
        }
        finally
        {
            HeadlessMessageBox.Handler = null;
        }
    }
}
