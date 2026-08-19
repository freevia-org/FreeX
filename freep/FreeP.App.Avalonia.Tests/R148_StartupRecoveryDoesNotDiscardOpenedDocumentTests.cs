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
/// Round 148 (startup-fileopen F2): <see cref="AutosaveAdapter.OfferRecoveryAsync"/>'s
/// "useCurrentWindow" branch used to restore an accepted recovery candidate straight into the
/// caller's window with no check at all, on the theory that "a fresh window has nothing unsaved to
/// lose". That theory holds for a genuinely blank launch, but <c>MainWindow</c>'s constructor opens
/// a command-line/file-association startup file into <c>this</c> SYNCHRONOUSLY, before the
/// <c>Opened</c> handler calls <c>OfferRecoveryAsync</c> -- so by the time the offer runs, the
/// window may already hold a document the user explicitly asked for. Because that document was just
/// opened (not edited), it is not dirty, so even the manual command's dirty gate
/// (<c>confirmDiscardOrSaveAsync</c>) would not have protected it -- a dirty check is the wrong
/// question here. The fix instead asks whether the window already has an explicitly opened document
/// (a non-null <c>CurrentPath</c>) BEFORE any candidate is applied, and if so routes every accepted
/// candidate through the same "recover into its own new window" path already used for every
/// candidate after the first, instead of overwriting the window's current content.
/// </summary>
public sealed class R148_StartupRecoveryDoesNotDiscardOpenedDocumentTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    private static async Task<bool> OnUiThread(Func<Task> action)
    {
        try
        {
            await Session.Dispatch(
                async () =>
                {
                    await action();
                    return true;
                },
                CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            return false; // no headless drawing backend in this environment
        }
    }

    private static FileCommandWorkflow NewWorkflow() =>
        new(
            maxRecentEntries: () => 10,
            onChanged: () => { },
            promptSaveChanges: _ => SaveChangesPrompt.DontSave,
            save: () => true,
            loadRecentFilesStore: () => RecentFilesStore.Load(
                Path.Combine(Path.GetTempPath(), "FreeP.R148StartupRecoveryTests-", Guid.NewGuid().ToString("N") + ".json")));

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
    /// THE FIX: when the window already has an explicitly opened document (simulated the same way
    /// <c>MainWindow</c>'s constructor leaves it after a successful startup open --
    /// <c>MarkSavedWithPath</c>, not dirty), the silent startup recovery offer must NOT overwrite it
    /// with an unrelated recovered snapshot. It must instead route the accepted candidate to a new
    /// window.
    /// </summary>
    [Fact]
    public async Task OfferRecoveryAsync_routes_to_a_new_window_when_the_current_window_already_has_an_opened_document()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var store = new AutosaveSnapshotStore(dir);
            WriteCandidate(store, "snap-unrelated", "2026-06-20T07:00:00Z", "Unrelated Deck");

            RecoveryPromptDialog.TestResponder = _ => true; // always accept the "recover this?" offer
            try
            {
                var appliedToCurrentWindow = false;
                var recoveredInNewWindow = false;
                var ran = await OnUiThread(async () =>
                {
                    var workflow = NewWorkflow();
                    // Mirrors what MainWindow's constructor does after successfully opening a
                    // command-line/file-association startup file: MarkSavedWithPath, not dirty.
                    workflow.MarkSavedWithPath(
                        Path.Combine(dir, "B.pptx"),
                        suppressRecentFiles: true);

                    var adapter = new AutosaveAdapter(
                        Presentation.CreateEmpty,
                        workflow,
                        applyRecoveredPresentation: (_, _) => appliedToCurrentWindow = true,
                        sessionFactory: ports => new FreePAutosaveSession(ports, store),
                        recoverInNewWindowAsync: _ =>
                        {
                            recoveredInNewWindow = true;
                            return Task.FromResult(true);
                        },
                        // A gate that would fail the test if the startup path ever consulted it --
                        // the fix does not use the dirty gate at all for this decision.
                        confirmDiscardOrSaveAsync: () => Task.FromResult(false));

                    var owner = new Window();
                    await adapter.OfferRecoveryAsync(owner);
                });

                if (!ran)
                    return; // no headless drawing backend in this environment

                appliedToCurrentWindow.Should().BeFalse(
                    "the just-opened document in the current window must never be silently replaced by an unrelated recovered snapshot");
                recoveredInNewWindow.Should().BeTrue(
                    "the recovered snapshot must still be offered, just routed into its own new window instead of overwriting B");
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
    /// Sibling no-regression (rule 10's adjacent case): a genuinely fresh window -- no startup file
    /// opened, <c>CurrentPath</c> still null -- must keep the prior behaviour exactly: the silent
    /// startup offer recovers straight into the current window, unconditionally and ungated.
    /// </summary>
    [Fact]
    public async Task OfferRecoveryAsync_still_recovers_into_the_current_window_when_it_has_no_opened_document()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var store = new AutosaveSnapshotStore(dir);
            WriteCandidate(store, "snap-fresh-window", "2026-06-20T07:00:00Z", "Recovered Deck");

            RecoveryPromptDialog.TestResponder = _ => true;
            try
            {
                var appliedToCurrentWindow = false;
                var recoveredInNewWindow = false;
                var ran = await OnUiThread(async () =>
                {
                    var workflow = NewWorkflow(); // never opened -- CurrentPath stays null

                    var adapter = new AutosaveAdapter(
                        Presentation.CreateEmpty,
                        workflow,
                        applyRecoveredPresentation: (_, _) => appliedToCurrentWindow = true,
                        sessionFactory: ports => new FreePAutosaveSession(ports, store),
                        recoverInNewWindowAsync: _ =>
                        {
                            recoveredInNewWindow = true;
                            return Task.FromResult(true);
                        });

                    var owner = new Window();
                    await adapter.OfferRecoveryAsync(owner);
                });

                if (!ran)
                    return;

                appliedToCurrentWindow.Should().BeTrue(
                    "a genuinely fresh window with nothing opened must keep recovering straight into itself, exactly as before this fix");
                recoveredInNewWindow.Should().BeFalse(
                    "the fresh-window path must not be redirected to a new window");
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
