using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Free.Shared.AppServices;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Shell;
using FreeW.Core.Model;
using FreeW.Core.IO;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Round 133 fix — Avalonia twin of
/// <c>FreeW.App.Host.Tests.AutosaveCoordinatorWindowIsolationTests</c>: <see cref="AutosaveAdapter"/>
/// used to key its snapshot ID on <see cref="AutosaveSnapshotStore.LaunchId"/> alone, a per-PROCESS
/// static Guid, so two <c>MainWindow</c> instances in the same process (FreeW.App.Avalonia.MainWindow
/// supports "New Window" / report windows, same as the WPF host) shared one snapshot slot. Runs on the
/// shared headless UI thread (construction reads Avalonia-styling state) via
/// <see cref="FreeWHeadlessApp"/>, matching <c>DocumentViewHeadlessTests</c>.
/// </summary>
public sealed class AutosaveAdapterWindowIsolationTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    private static (DocumentView editor, FileCommandWorkflow workflow) NewWindowParts()
    {
        var editor = new DocumentView();
        editor.LoadDocument(TextDocument.CreateEmpty());
        var workflow = new FileCommandWorkflow(
            maxRecentEntries: () => 10,
            onChanged: () => { },
            promptSaveChanges: _ => SaveChangesPrompt.DontSave,
            save: () => true,
            loadRecentFilesStore: () => RecentFilesStore.Load(
                Path.Combine(Path.GetTempPath(), "FreeW.AutosaveAvaloniaIsolationTests", Guid.NewGuid().ToString("N") + ".json")));
        return (editor, workflow);
    }

    /// <summary>
    /// Two windows open in the same process must resolve to two DIFFERENT snapshot IDs. Before the
    /// fix both adapters computed the identical <c>AutosaveSnapshotStore.LaunchId</c> string alone
    /// and therefore the identical ID.
    /// </summary>
    [Fact]
    public async Task Two_windows_in_same_process_get_different_snapshot_ids()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var store = new AutosaveSnapshotStore(dir);
            string? idA = null;
            string? idB = null;

            var ran = await OnUiThread(() =>
            {
                var (editorA, workflowA) = NewWindowParts();
                var adapterA = new AutosaveAdapter(
                    editorA,
                    workflowA,
                    ports => new FreeWAutosaveSession(ports, store));

                var (editorB, workflowB) = NewWindowParts();
                var adapterB = new AutosaveAdapter(
                    editorB,
                    workflowB,
                    ports => new FreeWAutosaveSession(ports, store));

                idA = adapterA.SnapshotIdForTests;
                idB = adapterB.SnapshotIdForTests;
            });

            if (!ran)
                return; // no headless drawing backend in this environment

            idA.Should().NotBeNull();
            idA.Should().NotBe(idB);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Avalonia twin of the WPF host's <c>Each_windows_snapshot_survives_the_others_write_and_cleanup</c>.
    /// <see cref="AutosaveAdapter.SnapshotNowForTests"/> is not exercised here — its
    /// <c>WriteSnapshot</c> re-enters <c>Dispatcher.UIThread.InvokeAsync(...).GetAwaiter().GetResult()</c>
    /// from a callback already running on the headless UI-thread dispatch, which deadlocks/crashes the
    /// headless test host. Instead this drives the exact same shared primitives
    /// <see cref="AutosaveAdapter"/> delegates to (<see cref="AutosaveSnapshotCoordinator.DeleteSnapshot"/>
    /// is <c>_store.DeleteSnapshot(_snapshotId)</c> — see <c>shared/Free.Shared.AppServices/
    /// AutosaveSnapshotCoordinator.cs</c>), at the same fidelity, without the dispatcher re-entrancy
    /// hazard: two adapters resolve to two different IDs, so a snapshot written under A's ID and a
    /// clean-close delete scoped to B's ID cannot collide.
    ///
    /// <para>
    /// IMPORTANT: only construction runs inside <see cref="OnUiThread"/> — its catch-all is there to
    /// skip the test when the headless environment has no drawing backend, and would otherwise also
    /// swallow a genuine assertion failure as a false "skip". The file/store assertions below run as
    /// plain top-level statements so a regression actually fails the test.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Each_windows_snapshot_survives_the_others_write_and_cleanup()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var store = new AutosaveSnapshotStore(dir);
            AutosaveAdapter? adapterA = null;
            AutosaveAdapter? adapterB = null;

            var ran = await OnUiThread(() =>
            {
                var (editorA, workflowA) = NewWindowParts();
                adapterA = new AutosaveAdapter(
                    editorA,
                    workflowA,
                    ports => new FreeWAutosaveSession(ports, store));

                var (editorB, workflowB) = NewWindowParts();
                adapterB = new AutosaveAdapter(
                    editorB,
                    workflowB,
                    ports => new FreeWAutosaveSession(ports, store));
            });

            if (!ran || adapterA is null || adapterB is null)
                return; // no headless drawing backend in this environment

            var snapshotPathA = store.GetSnapshotPath(adapterA.SnapshotIdForTests);
            var snapshotPathB = store.GetSnapshotPath(adapterB.SnapshotIdForTests);

            // Simulate each window's periodic autosave write landing on disk (the file content is
            // irrelevant to this test — only the path/ID scoping is under test).
            File.WriteAllBytes(snapshotPathA, [1]);
            File.WriteAllBytes(snapshotPathB, [2]);

            Assert.True(File.Exists(snapshotPathA));
            Assert.True(File.Exists(snapshotPathB));
            Assert.NotEqual(snapshotPathA, snapshotPathB);

            // Window B "closes" cleanly — the same store.DeleteSnapshot(snapshotId) call
            // AutosaveAdapter.StopAsync makes via its coordinator, scoped to its OWN snapshot ID
            // only, must not touch A's.
            store.DeleteSnapshot(adapterB.SnapshotIdForTests);
            adapterB.Dispose();

            Assert.True(File.Exists(snapshotPathA), "window A's snapshot must survive window B's cleanup");
            Assert.False(File.Exists(snapshotPathB), "window B's own snapshot should be gone after its clean close");

            // Sibling no-regression: window A's own clean close still removes its own snapshot.
            store.DeleteSnapshot(adapterA.SnapshotIdForTests);
            adapterA.Dispose();
            Assert.False(File.Exists(snapshotPathA));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// R133-remediation (gap a, Avalonia twin): the WPF host's
    /// <c>AutosaveCoordinator.OfferRecovery</c> used to select and offer only the single latest
    /// pending snapshot; <see cref="AutosaveAdapter.OfferRecoveryAsync"/> had the exact same bug.
    /// Drives the REAL production entry point with three pending snapshots and a headless
    /// "always accept" <see cref="RecoveryPromptDialog.TestResponder"/>, and asserts every single
    /// one is recovered -- not just the first/latest -- mirroring the WPF host's
    /// <c>AutosaveCoordinatorWindowIsolationTests.OfferRecovery_recovers_every_pending_snapshot_not_just_one</c>.
    ///
    /// <para>
    /// The three pending snapshots are written directly to disk (real .docx + sidecar pairs) rather
    /// than via three live <see cref="AutosaveAdapter"/> instances, to sidestep the
    /// <c>SnapshotNowForTests</c> dispatcher re-entrancy hazard documented on the sibling test
    /// above -- setup needs no Avalonia platform at all. Only the adapter/window construction and
    /// the <see cref="AutosaveAdapter.OfferRecoveryAsync"/> call itself run on the headless UI
    /// thread, matching the codebase's established "async work forced synchronous with
    /// <c>GetAwaiter().GetResult()</c> inside a dispatched action" pattern (see e.g.
    /// <c>BackstageViewTests</c>'s <c>SaveCopyToPathAsync(...).GetAwaiter().GetResult()</c>).
    /// </para>
    /// </summary>
    [Fact]
    public async Task OfferRecoveryAsync_recovers_every_pending_snapshot_not_just_one()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var store = new AutosaveSnapshotStore(dir);

            // Three crashed windows each left a pending snapshot behind.
            WriteCandidate(store, "snap-a", "2026-06-20T07:00:00Z", "Doc A");
            WriteCandidate(store, "snap-b", "2026-06-20T08:00:00Z", "Doc B");
            WriteCandidate(store, "snap-c", "2026-06-20T09:00:00Z", "Doc C");

            store.EnumerateCandidates().Should().HaveCount(3, "three crashed windows each left a snapshot");

            var recoveredInNewWindow = new List<AutosaveRecoveryCandidate>();
            FileCommandWorkflow? owningWorkflow = null;

            RecoveryPromptDialog.TestResponder = _ => true; // always accept every prompt
            try
            {
                var ran = await OnUiThread(() =>
                {
                    var (owningEditor, workflow) = NewWindowParts();
                    owningWorkflow = workflow;

                    // The window that is actually running the recovery UI on relaunch: its adapter
                    // gets a recoverInNewWindowAsync callback (exactly as MainWindow wires it in
                    // production via OpenNewWindowWithRecoveredSnapshotAsync) so accepted candidates
                    // beyond the first are handed off to a new window instead of being dropped.
                    var adapterUnderTest = new AutosaveAdapter(
                        owningEditor,
                        workflow,
                        sessionFactory: ports => new FreeWAutosaveSession(ports, store),
                        recoverInNewWindowAsync: candidate =>
                        {
                            recoveredInNewWindow.Add(candidate);
                            return Task.FromResult(true); // simulates a second/third window recovering
                        });

                    var owningWindow = new Window();
                    adapterUnderTest.OfferRecoveryAsync(owningWindow).GetAwaiter().GetResult();
                });

                if (!ran)
                    return; // no headless drawing backend in this environment

                owningWorkflow.Should().NotBeNull();
                owningWorkflow!.IsDirty.Should().BeTrue(
                    "the first accepted snapshot should have loaded into the owning window");
                // The other two must NOT be silently dropped -- they are handed off via the
                // recoverInNewWindowAsync callback, exactly like production opening extra windows.
                recoveredInNewWindow.Should().HaveCount(2,
                    "OfferRecoveryAsync must offer every pending snapshot, not just the first/latest");
                // Every offered-and-accepted candidate is fully recovered and removed from disk; none
                // are left orphaned for the next launch to trip over.
                store.EnumerateCandidates().Should().BeEmpty(
                    "all three pending snapshots were accepted and recovered, none should remain orphaned");
            }
            finally
            {
                RecoveryPromptDialog.TestResponder = null;
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    private static void WriteCandidate(AutosaveSnapshotStore store, string id, string timestampUtc, string displayName)
    {
        var snapshotPath = store.GetSnapshotPath(id);
        var sidecarPath = store.GetSidecarPath(id);
        DocxWriter.Write(TextDocument.CreateEmpty(), snapshotPath);
        var sidecar = new AutosaveSidecar
        {
            DisplayName = displayName,
            TimestampUtc = timestampUtc,
            SnapshotId = id
        };
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));
    }
}
