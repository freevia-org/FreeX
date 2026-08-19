using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Free.Shared.AppServices;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Round 148 REMEDIATION (startup-fileopen F2, Avalonia sibling): <see
/// cref="AutosaveAdapter.OfferRecoveryAsync"/>'s "useCurrentWindow" branch used to restore an
/// accepted recovery candidate straight into the caller's window with no check at all -- the exact
/// bug already fixed in FreeP's Avalonia <c>AutosaveAdapter.OfferRecoveryAsync</c>
/// (<c>R148_StartupRecoveryDoesNotDiscardOpenedDocumentTests</c>), left standing here.
/// <c>MainWindow</c> opens a command-line/file-association startup file into itself SYNCHRONOUSLY,
/// before the <c>Opened</c> handler calls <c>OfferRecoveryAsync</c>, so the window may already hold
/// a document the user explicitly asked for. That document is not dirty (it was just opened, not
/// edited), so the silent startup offer must ask whether the window already has an explicitly opened
/// document (a non-null <c>CurrentPath</c>) BEFORE any candidate is applied, and route every accepted
/// candidate through the new-window path when it does, exactly like every candidate after the first.
/// </summary>
public sealed class R148_OfferRecoveryDoesNotDiscardOpenedDocumentTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

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

    private static FileCommandWorkflow NewWorkflow(string tempDir) =>
        new(
            maxRecentEntries: () => 10,
            onChanged: () => { },
            promptSaveChanges: _ => SaveChangesPrompt.DontSave,
            save: () => true,
            loadRecentFilesStore: () => RecentFilesStore.Load(
                Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".json")));

    private static void WriteCandidate(AutosaveSnapshotStore store, string id, string timestampUtc, string displayName)
    {
        var snapshotPath = store.GetSnapshotPath(id);
        var sidecarPath = store.GetSidecarPath(id);
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Unrelated recovered content"));
        DocxWriter.Write(doc, snapshotPath);
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
            WriteCandidate(store, "snap-unrelated", "2026-06-20T07:00:00Z", "Unrelated Document");

            RecoveryPromptDialog.TestResponder = _ => true; // always accept the "recover this?" offer
            try
            {
                string? openedPath = null;
                string? currentPathAfter = null;
                var isDirtyAfter = false;
                var recoveredInNewWindow = false;
                var ran = await OnUiThread(async () =>
                {
                    var editor = new DocumentView();
                    editor.LoadDocument(TextDocument.CreateEmpty());
                    var workflow = NewWorkflow(dir);
                    // Mirrors what MainWindow's constructor does after successfully opening a
                    // command-line/file-association startup file: MarkSavedWithPath, not dirty.
                    openedPath = Path.Combine(dir, "B.docx");
                    workflow.MarkSavedWithPath(openedPath, suppressRecentFiles: true);

                    var adapter = new AutosaveAdapter(
                        editor,
                        workflow,
                        sessionFactory: ports => new FreeWAutosaveSession(ports, store),
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
                    currentPathAfter = workflow.CurrentPath;
                    isDirtyAfter = workflow.IsDirty;
                });

                if (!ran)
                    return; // no headless drawing backend in this environment

                currentPathAfter.Should().Be(openedPath,
                    "the just-opened document in the current window must never be silently replaced by an unrelated recovered snapshot");
                isDirtyAfter.Should().BeFalse(
                    "the current window's clean, just-opened document must be untouched by the recovery");
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
            WriteCandidate(store, "snap-fresh-window", "2026-06-20T07:00:00Z", "Recovered Document");

            RecoveryPromptDialog.TestResponder = _ => true;
            try
            {
                var recoveredInNewWindow = false;
                var isDirtyAfter = false;
                var ran = await OnUiThread(async () =>
                {
                    var editor = new DocumentView();
                    editor.LoadDocument(TextDocument.CreateEmpty());
                    var workflow = NewWorkflow(dir); // never opened -- CurrentPath stays null

                    var adapter = new AutosaveAdapter(
                        editor,
                        workflow,
                        sessionFactory: ports => new FreeWAutosaveSession(ports, store),
                        recoverInNewWindowAsync: _ =>
                        {
                            recoveredInNewWindow = true;
                            return Task.FromResult(true);
                        });

                    var owner = new Window();
                    await adapter.OfferRecoveryAsync(owner);
                    isDirtyAfter = workflow.IsDirty;
                });

                if (!ran)
                    return;

                recoveredInNewWindow.Should().BeFalse(
                    "the fresh-window path must not be redirected to a new window");
                isDirtyAfter.Should().BeTrue(
                    "a genuinely fresh window with nothing opened must keep recovering straight into itself, exactly as before this fix");
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
