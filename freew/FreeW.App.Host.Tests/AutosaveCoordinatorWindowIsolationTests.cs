using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.Shell;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Shell;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Round 133 fix: <see cref="AutosaveCoordinator"/> used to key its snapshot ID on
/// <see cref="AutosaveSnapshotStore.LaunchId"/> alone, a per-PROCESS static Guid. Two
/// <c>MainWindow</c> instances in the same process (Feature 5's "New Window" — see
/// <c>MainWindow.OpenNewWindow</c> — and mail-merge report windows) therefore shared one
/// snapshot slot: they overwrote each other's crash-recovery data, and one window's clean
/// close (<see cref="AutosaveCoordinator.Stop"/>) deleted the snapshot the OTHER window still
/// needed. These tests construct two coordinators the same way two live windows would and
/// verify they now own independent, non-colliding snapshot files.
/// </summary>
public sealed class AutosaveCoordinatorWindowIsolationTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeW.AutosaveIsolationTests", Guid.NewGuid().ToString("N"));

    public AutosaveCoordinatorWindowIsolationTests() => Directory.CreateDirectory(_tempDir);

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
        var coordinator = new AutosaveCoordinator(editor, file, store);
        return (coordinator, file);
    }

    /// <summary>
    /// Two windows open in the same process must resolve to two DIFFERENT snapshot IDs. Before
    /// the fix both coordinators computed the identical <c>AutosaveSnapshotStore.LaunchId</c>
    /// string and therefore the identical ID.
    /// </summary>
    [StaFact]
    public void Two_windows_in_same_process_get_different_snapshot_ids()
    {
        var store = new AutosaveSnapshotStore(_tempDir);
        var (coordinatorA, _) = NewWindowHarness(store);
        var (coordinatorB, _) = NewWindowHarness(store);

        Assert.NotEqual(coordinatorA.SnapshotIdForTests, coordinatorB.SnapshotIdForTests);
    }

    /// <summary>
    /// Core regression: dirty window A snapshots, then dirty window B snapshots — A's snapshot
    /// file must still exist afterward (not overwritten by B). Then B closes cleanly (Stop);
    /// A's snapshot must STILL survive B's cleanup — the exact "one window's cleanup deletes the
    /// other's snapshot" data-loss scenario from the bug report.
    /// </summary>
    [StaFact]
    public void Each_windows_snapshot_survives_the_others_write_and_cleanup()
    {
        var store = new AutosaveSnapshotStore(_tempDir);
        var (coordinatorA, fileA) = NewWindowHarness(store);
        var (coordinatorB, fileB) = NewWindowHarness(store);

        fileA.MarkDirty();
        fileB.MarkDirty();

        coordinatorA.SnapshotNowForTests();
        coordinatorB.SnapshotNowForTests();

        var snapshotPathA = store.GetSnapshotPath(coordinatorA.SnapshotIdForTests);
        var snapshotPathB = store.GetSnapshotPath(coordinatorB.SnapshotIdForTests);

        // Both windows' crash-recovery data survived side by side.
        Assert.True(File.Exists(snapshotPathA));
        Assert.True(File.Exists(snapshotPathB));
        Assert.NotEqual(snapshotPathA, snapshotPathB);

        var candidatesBeforeCleanup = store.EnumerateCandidates();
        Assert.Equal(2, candidatesBeforeCleanup.Count);

        // Window B closes cleanly — its Stop() deletes only its OWN snapshot.
        coordinatorB.Stop();

        Assert.True(File.Exists(snapshotPathA), "Window A's snapshot must survive window B's cleanup.");
        Assert.False(File.Exists(snapshotPathB), "Window B's own snapshot should be gone after its clean close.");

        var candidatesAfterCleanup = store.EnumerateCandidates();
        Assert.Single(candidatesAfterCleanup);
        Assert.Equal(coordinatorA.SnapshotIdForTests, candidatesAfterCleanup[0].Sidecar.SnapshotId);

        // Sibling no-regression: window A's own clean close still removes its own snapshot.
        coordinatorA.Stop();
        Assert.False(File.Exists(snapshotPathA));
        Assert.Empty(store.EnumerateCandidates());
    }

    /// <summary>
    /// R133-remediation (gap b): this test used to only call <c>store.EnumerateCandidates()</c> —
    /// it never drove <see cref="AutosaveCoordinator.OfferRecovery"/> at all, so it passed
    /// regardless of whether the recovery UI actually offered more than one snapshot. Rewritten to
    /// exercise the real production entry point: three crashed windows each leave a pending
    /// snapshot, a headless "always accept" message box answers every prompt, and the test asserts
    /// every single candidate is recovered — not just the first/latest — matching the finding's
    /// "enumerate and offer ALL pending snapshots, not assume one" requirement.
    /// <para>
    /// Round134-remediation: each coordinator now calls <see cref="AutosaveCoordinator.SimulateCrashForTests"/>
    /// after writing its snapshot. Without it these three would-be "crashed" windows would still
    /// hold their live-ownership locks (they are just live, never-disposed objects in this same
    /// test process) and the Round134 liveness filter would correctly exclude them from the offer
    /// — which would make this test fail for the WRONG reason (excluded-as-live, not
    /// recovered-as-orphaned). Releasing only the lock (not the snapshot files) is exactly what a
    /// real crash does: the OS releases the process's handles but leaves the files it already
    /// wrote on disk.
    /// </para>
    /// </summary>
    [StaFact]
    public void OfferRecovery_recovers_every_pending_snapshot_not_just_one()
    {
        var store = new AutosaveSnapshotStore(_tempDir);

        // Three crashed windows each left a pending snapshot behind.
        var (coordinatorA, fileA) = NewWindowHarness(store);
        var (coordinatorB, fileB) = NewWindowHarness(store);
        var (coordinatorC, fileC) = NewWindowHarness(store);
        fileA.MarkDirty();
        fileB.MarkDirty();
        fileC.MarkDirty();
        coordinatorA.SnapshotNowForTests();
        coordinatorB.SnapshotNowForTests();
        coordinatorC.SnapshotNowForTests();
        // Simulate each window's process having actually crashed (see Round134-remediation note
        // above): releases the ownership lock without deleting the snapshot/sidecar files.
        coordinatorA.SimulateCrashForTests();
        coordinatorB.SimulateCrashForTests();
        coordinatorC.SimulateCrashForTests();

        store.EnumerateCandidates().Should().HaveCount(3, "three crashed windows each left a snapshot");

        // The window actually running the recovery UI on relaunch: its coordinator gets a
        // recoverInNewWindow callback (exactly as MainWindow wires it in production via
        // OpenNewWindowWithRecoveredSnapshot) so accepted candidates beyond the first are handed
        // off to a new window instead of being silently dropped.
        var recoveredInNewWindow = new List<AutosaveRecoveryCandidate>();
        var owningWindow = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };
        var owningEditor = new DocumentView();
        owningEditor.LoadModel(TextDocument.CreateEmpty());
        var owningFile = new FileCommands(
            owningWindow,
            owningEditor,
            onChanged: () => { },
            loadRecentFilesStore: () => RecentFilesStore.Load(Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".json")));
        var coordinatorUnderTest = new AutosaveCoordinator(
            owningEditor,
            owningFile,
            store,
            recoverInNewWindow: candidate =>
            {
                recoveredInNewWindow.Add(candidate);
                return true; // simulates a second/third window successfully loading its snapshot
            });

        HeadlessMessageBox.Handler = (_, _) => UserMessageResult.Yes; // always accept every prompt
        try
        {
            var anyAccepted = coordinatorUnderTest.OfferRecovery(owningWindow);

            anyAccepted.Should().BeTrue();
            // The first accepted candidate restores directly into the window the command was
            // invoked from.
            owningFile.IsDirty.Should().BeTrue("the first accepted snapshot should have loaded into the owning window");
            // The other two must NOT be silently dropped -- they are handed off via the
            // recoverInNewWindow callback, exactly like production opening extra windows.
            recoveredInNewWindow.Should().HaveCount(2,
                "OfferRecovery must offer every pending snapshot, not just the first/latest");
            // Every offered-and-accepted candidate is fully recovered and removed from disk; none
            // are left orphaned for the next launch to trip over.
            store.EnumerateCandidates().Should().BeEmpty(
                "all three pending snapshots were accepted and recovered, none should remain orphaned");
        }
        finally
        {
            HeadlessMessageBox.Handler = null;
        }
    }

    /// <summary>
    /// Round134 fix: <see cref="AutosaveSnapshotStore.EnumerateCandidates"/> lists every readable
    /// snapshot in the shared Recovery directory with no liveness/ownership filter, so
    /// "Recover Unsaved Documents" invoked from one open window used to list — and, if accepted,
    /// <see cref="AutosaveSnapshotStore.DeleteCandidate"/> — a DIFFERENT window's still-live
    /// snapshot right out from under it. This test builds exactly that two-window scene plus a
    /// third, genuinely orphaned candidate (its owning process is gone) and asserts recovery:
    /// (1) never lists/deletes the live sibling's snapshot, and
    /// (2) still offers and recovers the orphaned one.
    /// </summary>
    [StaFact]
    public void RecoverUnsavedDocuments_NeverListsOrDeletesAnotherLiveWindowsSnapshot_ButStillOffersAnOrphanedOne()
    {
        var store = new AutosaveSnapshotStore(_tempDir);

        // Window A: a second window open in THIS SAME process right now, holding its own dirty,
        // autosaved-but-unrecovered snapshot. It never crashes or closes during this test, so its
        // ownership lock stays held throughout — recovery triggered from a DIFFERENT window must
        // never list or delete this snapshot out from under it.
        var (coordinatorA, fileA) = NewWindowHarness(store);
        fileA.MarkDirty();
        coordinatorA.SnapshotNowForTests();
        var snapshotPathA = store.GetSnapshotPath(coordinatorA.SnapshotIdForTests);
        var sidecarPathA = store.GetSidecarPath(coordinatorA.SnapshotIdForTests);
        File.Exists(snapshotPathA).Should().BeTrue();

        // Window C: a genuinely crashed window from an earlier session. It wrote a snapshot and
        // then its process exited — releasing the OS ownership lock automatically, exactly as a
        // real crash does (no stale marker survives) — leaving the snapshot+sidecar files behind
        // with no live owner.
        var (coordinatorC, fileC) = NewWindowHarness(store);
        fileC.MarkDirty();
        coordinatorC.SnapshotNowForTests();
        var snapshotPathC = store.GetSnapshotPath(coordinatorC.SnapshotIdForTests);
        coordinatorC.SimulateCrashForTests();
        File.Exists(snapshotPathC).Should().BeTrue("a crash must leave the snapshot file behind");

        // Raw, unfiltered disk enumeration sees both — this confirms the scene is set up as
        // intended and that EnumerateCandidates itself is intentionally left unfiltered (see its
        // ExcludeLiveOwned doc comment).
        store.EnumerateCandidates().Should().HaveCount(2,
            "both A's live snapshot and C's orphaned one exist on disk");

        // Window B is the one actually invoking "Recover Unsaved Documents" right now.
        var owningWindow = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };
        var owningEditor = new DocumentView();
        owningEditor.LoadModel(TextDocument.CreateEmpty());
        var owningFile = new FileCommands(
            owningWindow,
            owningEditor,
            onChanged: () => { },
            loadRecentFilesStore: () => RecentFilesStore.Load(Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".json")));
        var coordinatorB = new AutosaveCoordinator(owningEditor, owningFile, store);

        HeadlessMessageBox.Handler = (_, _) => UserMessageResult.Ok; // always accept (RecoverUnsavedDocuments uses OkCancel)
        try
        {
            var anyRecovered = coordinatorB.RecoverUnsavedDocuments(owningWindow);

            anyRecovered.Should().BeTrue("the orphaned snapshot from the crashed window must still be recoverable");
            owningFile.IsDirty.Should().BeTrue("the orphaned snapshot should have been recovered into the invoking window");

            // Core assertion: window A's live snapshot was never listed, so it was never touched.
            File.Exists(snapshotPathA).Should().BeTrue(
                "a live sibling window's snapshot must never be offered or deleted by another window's recovery");
            File.Exists(sidecarPathA).Should().BeTrue(
                "a live sibling window's sidecar must never be deleted by another window's recovery");

            // The orphaned candidate (owner gone) was offered, recovered, and cleaned up.
            File.Exists(snapshotPathC).Should().BeFalse("the orphaned snapshot should be deleted after successful recovery");
        }
        finally
        {
            HeadlessMessageBox.Handler = null;
            coordinatorA.Stop(); // clean up A's still-live snapshot now that the test is done
        }
    }
}
