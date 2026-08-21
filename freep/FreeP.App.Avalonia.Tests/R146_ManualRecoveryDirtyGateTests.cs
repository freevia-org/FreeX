using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Free.Shared.AppServices;
using FreeP.App.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia.Tests;

/// <summary>
/// Round 146 remediation: FreeP's Avalonia Backstage "Recover Unsaved Presentations" command used
/// to reuse <see cref="AutosaveAdapter.OfferRecoveryAsync"/> -- the exact same best-effort, UNGATED
/// path used for the silent STARTUP offer -- so accepting an older crash snapshot for the CURRENT
/// window silently overwrote the current presentation's unsaved edits with no save/discard prompt.
/// FreeP's WPF host never had this bug: its manual command
/// (<c>AutosaveCoordinator.RecoverUnsavedPresentations</c>) routes the current-window restore
/// through <c>PresentationFileCommandSession.ConfirmCloseAllowedAsync</c> before overwriting. FreeW's
/// Avalonia shell was fixed the same way in the same round
/// (<c>AutosaveAdapter.RecoverUnsavedDocumentsAsync</c>).
///
/// <see cref="AutosaveAdapter.RecoverUnsavedPresentationsAsync"/> is the gated Avalonia twin added
/// here to fix this, wired to FreeP's Backstage "Recover Unsaved Presentations" command in
/// <c>MainWindow</c> instead of the ungated <c>OfferRecoveryAsync</c>.
/// </summary>
public sealed class R146_ManualRecoveryDirtyGateTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too.
    private static Task<bool> OnUiThread(Func<Task> action) => HeadlessUiThread.RunAsync(action);

    private static FileCommandWorkflow NewWorkflow() =>
        new(
            maxRecentEntries: () => 10,
            onChanged: () => { },
            promptSaveChanges: _ => SaveChangesPrompt.DontSave,
            save: () => true,
            loadRecentFilesStore: () => RecentFilesStore.Load(
                Path.Combine(Path.GetTempPath(), "FreeP.R146ManualRecoveryDirtyGateTests-", Guid.NewGuid().ToString("N") + ".json")));

    private static void WriteCandidate(AutosaveSnapshotStore store, string id, string timestampUtc, string displayName)
    {
        var snapshotPath = store.GetSnapshotPath(id);
        var sidecarPath = store.GetSidecarPath(id);
        FreeP.Core.IO.PptxPackageWriter.Write(Presentation.CreateEmpty(), snapshotPath);
        var sidecar = new AutosaveSidecar
        {
            DisplayName = displayName,
            TimestampUtc = timestampUtc,
            SnapshotId = id
        };
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));
    }

    /// <summary>
    /// THE FIX: a manual recovery accepted into the current window must consult the dirty gate
    /// first. When the gate reports "cancelled" (the user declined the save/discard prompt for
    /// their current unsaved edits), the current presentation must be left untouched -- it must NOT
    /// be silently loaded over the top, which is what <c>OfferRecoveryAsync</c> would have done here
    /// before this fix.
    /// </summary>
    [Fact]
    public async Task RecoverUnsavedPresentationsAsync_consults_the_dirty_gate_and_preserves_the_current_presentation_when_declined()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var store = new AutosaveSnapshotStore(dir);
            WriteCandidate(store, "snap-dirty-decline", "2026-06-20T07:00:00Z", "Recovered Deck");

            RecoveryPromptDialog.TestResponder = _ => true; // always accept the "recover this?" offer
            try
            {
                var gateCalls = 0;
                var applied = false;
                var ran = await OnUiThread(async () =>
                {
                    var workflow = NewWorkflow();
                    var adapter = new AutosaveAdapter(
                        Presentation.CreateEmpty,
                        workflow,
                        applyRecoveredPresentation: (_, _) => applied = true,
                        sessionFactory: ports => new FreePAutosaveSession(ports, store),
                        confirmDiscardOrSaveAsync: () =>
                        {
                            gateCalls++;
                            return Task.FromResult(false); // simulate the user cancelling the save/discard prompt
                        });

                    var owner = new Window();
                    await adapter.RecoverUnsavedPresentationsAsync(owner);

                    gateCalls.Should().Be(1, "the dirty gate must be consulted before overwriting the current window");
                    applied.Should().BeFalse(
                        "the current presentation must NOT be silently replaced when the user declines the dirty gate");
                });

                if (!ran)
                    return; // no headless drawing backend in this environment

                // Declining is NOT the same as a failed restore. There IS a separate disposition for it:
                // AutosaveRecoveryPolicy.ResolveDisposition maps !accepted to Keep, which is what the WPF
                // host does on this very branch. The user declined to discard the CURRENT presentation;
                // they said nothing about the OLDER unsaved work they were trying to recover, so it has
                // to stay on offer. Quarantining here moves it out of the directory EnumerateCandidates
                // scans, and no normal recovery UI ever shows it again -- losing the very data the
                // command exists to rescue.
                store.EnumerateCandidates().Should().ContainSingle(
                    "declining to discard the current presentation must leave the recovery candidate on offer")
                    .Which.SnapshotPath.Should().Contain("snap-dirty-decline");
                Directory.Exists(Path.Combine(dir, "Quarantine")).Should().BeFalse(
                    "a declined candidate is kept in place, not moved aside");
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

    /// <summary>
    /// Happy-path sibling: when the dirty gate allows the restore (presentation was clean, or the
    /// user chose to proceed), the manual recovery command must still actually recover the snapshot
    /// into the current window -- the fix must not turn the gate into an unconditional block.
    /// </summary>
    [Fact]
    public async Task RecoverUnsavedPresentationsAsync_recovers_into_the_current_window_when_the_dirty_gate_allows_it()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var store = new AutosaveSnapshotStore(dir);
            WriteCandidate(store, "snap-clean-allow", "2026-06-20T07:00:00Z", "Recovered Deck");

            RecoveryPromptDialog.TestResponder = _ => true;
            try
            {
                var applied = false;
                var ran = await OnUiThread(async () =>
                {
                    var workflow = NewWorkflow();
                    var adapter = new AutosaveAdapter(
                        Presentation.CreateEmpty,
                        workflow,
                        applyRecoveredPresentation: (_, _) => applied = true,
                        sessionFactory: ports => new FreePAutosaveSession(ports, store),
                        confirmDiscardOrSaveAsync: () => Task.FromResult(true)); // clean deck / user chose to proceed

                    var owner = new Window();
                    await adapter.RecoverUnsavedPresentationsAsync(owner);

                    applied.Should().BeTrue(
                        "when the dirty gate allows it, the manual recovery command must still restore into the current window");
                });

                if (!ran)
                    return;

                store.EnumerateCandidates().Should().BeEmpty("an accepted, recovered candidate should be consumed, not left behind");
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

    /// <summary>
    /// Adjacent case (rule 10): the STARTUP offer (<see cref="AutosaveAdapter.OfferRecoveryAsync"/>)
    /// is deliberately left ungated -- a freshly opened window has nothing unsaved to lose, so
    /// gating it would just add a pointless prompt (and, worse, a supplied
    /// <c>confirmDiscardOrSaveAsync</c> gate must never be consulted for it). This must keep working
    /// exactly as before the fix.
    /// </summary>
    [Fact]
    public async Task OfferRecoveryAsync_startup_path_remains_ungated_after_the_fix()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var store = new AutosaveSnapshotStore(dir);
            WriteCandidate(store, "snap-startup", "2026-06-20T07:00:00Z", "Recovered Deck");

            RecoveryPromptDialog.TestResponder = _ => true;
            try
            {
                var gateCalls = 0;
                var applied = false;
                var ran = await OnUiThread(async () =>
                {
                    var workflow = NewWorkflow();
                    var adapter = new AutosaveAdapter(
                        Presentation.CreateEmpty,
                        workflow,
                        applyRecoveredPresentation: (_, _) => applied = true,
                        sessionFactory: ports => new FreePAutosaveSession(ports, store),
                        // A gate that would fail the test if it were ever consulted by the startup path.
                        confirmDiscardOrSaveAsync: () =>
                        {
                            gateCalls++;
                            return Task.FromResult(false);
                        });

                    var owner = new Window();
                    await adapter.OfferRecoveryAsync(owner);

                    gateCalls.Should().Be(0, "the silent startup offer must never consult the manual command's dirty gate");
                    applied.Should().BeTrue(
                        "the startup offer must keep recovering unconditionally, exactly as before this fix");
                });

                if (!ran)
                    return;

                store.EnumerateCandidates().Should().BeEmpty();
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
}
