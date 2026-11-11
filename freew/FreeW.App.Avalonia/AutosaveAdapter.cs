using Avalonia.Controls;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Shell;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// FreeW's binding of the shared <see cref="AvaloniaAutosaveHost{TDocument}"/> (periodic loop,
/// emergency fan-out, clean-exit stop, startup/manual recovery, recovery prompt) to its editor and
/// file workflow. Only the document ports, the guarded restore, and the new-window / dirty-gate
/// callbacks are FreeW-specific.
///
/// <para>
/// Call <see cref="Start"/> after the window opens and <see cref="StopAsync"/> after the
/// dirty-gate has passed (on close). On startup, call <see cref="OfferRecoveryAsync"/> to
/// surface any snapshot from a previous crashed session.
/// </para>
/// </summary>
internal sealed partial class AutosaveAdapter : IDisposable
{
    private readonly AvaloniaAutosaveHost<TextDocument> _host;

    public AutosaveAdapter(
        DocumentView editor,
        FileCommandWorkflow workflow,
        Func<AutosaveDocumentPorts<TextDocument>, FreeWAutosaveSession>? sessionFactory = null,
        Func<AutosaveRecoveryCandidate, Task<bool>>? recoverInNewWindowAsync = null,
        Func<Task<bool>>? confirmDiscardOrSaveAsync = null,
        Func<TextDocument?>? getMailMergeTemplate = null,
        // Round-172 finding shared-autosave-recovery/F3: the window's REAL FreeWDocumentFileWorkflow
        // -- the object that owns the external-modification write-time guard
        // (_currentFileSourceLastWriteTimeUtc) and whose OpenSnapshotAsync is the only place that
        // re-arms it correctly on recovery (see the restore callback below). MainWindow always
        // supplies its own _documentFileWorkflow here so recovery shares the SAME FileCommandWorkflow
        // identity and guard state that the window's real Ctrl+S save goes through. Left null (as
        // every pre-existing test double here does), a fresh, fully-functional workflow is built over
        // the same `workflow`/`editor` -- still a genuine guarded OpenSnapshotAsync, just not the
        // window's shared instance, which is irrelevant for tests that never assert cross-object guard
        // state.
        FreeWDocumentFileWorkflow? documentFileWorkflow = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(workflow);

        var documentFiles = documentFileWorkflow ?? new FreeWDocumentFileWorkflow(
            workflow,
            new DocumentPersistenceWorkflow(),
            new FreeWDocumentFilePorts(
                GetDocument: () => getMailMergeTemplate?.Invoke() ?? editor.Document,
                LoadDocumentAsync: (document, _) =>
                {
                    editor.LoadDocument(document);
                    return ValueTask.CompletedTask;
                }));
        var ports = new AutosaveDocumentPorts<TextDocument>(
            GetOriginalFilePath: () => workflow.CurrentPath,
            GetDisplayName: () => workflow.DisplayName,
            GetIsDirty: () => workflow.IsDirty,
            GetDirtyGeneration: () => workflow.DirtyGeneration,
            // Same guard the WPF host's AutosaveCoordinator applies in its own ExecuteWithDocument port:
            // while Mailings > Preview Results is active, editor.Document is the merged, single-record
            // preview document that MailMergeEngine loaded for on-screen display (Realize ->
            // DocumentView.LoadDocument), NOT the mail-merge template. Autosave snapshots -- and the
            // crash-recovery snapshot that reuses this same port via TryEmergencySnapshot -- must persist
            // the still-live template, or an untimely tick/crash leaves the recovered document with every
            // merge field baked away into one recipient's literal values. When no preview is active the
            // accessor returns null and the live editor document is snapshotted exactly as before.
            ExecuteWithDocument: writeDocument =>
                writeDocument(getMailMergeTemplate?.Invoke() ?? editor.Document));

        // Round-172 finding shared-autosave-recovery/F3: recovery is a form of OPENING a document, so
        // the accepted snapshot is restored through FreeWDocumentFileWorkflow.OpenSnapshotAsync -- the
        // one place that re-arms the external-modification write-time guard from the ORIGINAL file's
        // current on-disk write time -- rather than a hand-rolled, unguarded load beside it (which let
        // the first save after recovery silently overwrite a copy that changed on disk while the app
        // was gone). OpenSnapshotAsync never throws (it reports failure via Succeeded), so a
        // corrupt/truncated snapshot still surfaces as recovered: false.
        var restore = AutosaveRecoveryRestore<TextDocument>.FromSnapshotPath(
            (snapshotPath, originalPath) =>
                documentFiles.OpenSnapshotAsync(snapshotPath, originalPath)
                    .GetAwaiter().GetResult().Succeeded);

        _host = new AvaloniaAutosaveHost<TextDocument>(
            FreeWAutosaveProfile.Session,
            ports,
            new AvaloniaAutosaveHostCallbacks<TextDocument>(
                // startup-fileopen F2: a command-line/file-association document opened into this
                // window before the startup offer runs is not dirty, so it must not be replaced.
                CurrentWindowHasExplicitDocument: () => workflow.CurrentPath is not null,
                RestoreIntoCurrentWindow: restore,
                RecoverInNewWindowAsync: recoverInNewWindowAsync,
                // Manual Backstage > Open > "Recover Unsaved" may run against a dirty document, so it
                // runs the same save/discard dirty gate the WPF host does.
                ConfirmDiscardOrSaveAsync: confirmDiscardOrSaveAsync,
                PromptResponseOverride: ResolvePromptResponse),
            UiText.Get,
            sessionFactory: sessionPorts => sessionFactory?.Invoke(sessionPorts) ?? new FreeWAutosaveSession(sessionPorts));
    }

    /// <summary>
    /// Best-effort emergency snapshot for this window's document. Must never throw.
    /// </summary>
    public void TryEmergencySnapshot() => _host.TryEmergencySnapshot();

    /// <summary>
    /// Attempts an emergency snapshot for every live window's document. Wired as the Avalonia
    /// desktop profile's crash-handler hook (see FreeW.App.Avalonia/App.cs's DesktopProfile) so a
    /// crash takes the same best-effort snapshot FreeX's Avalonia host does instead of losing every
    /// edit since the last periodic autosave tick.
    /// </summary>
    public static void TryEmergencySnapshots() => AvaloniaAutosaveHost<TextDocument>.TrySnapshotAll();

    /// <summary>Start the periodic autosave loop. Safe to call from any thread.</summary>
    public void Start() => _host.Start();

    /// <summary>
    /// Stop the loop and delete the current-session snapshot (clean exit). Awaitable so the
    /// window's Closing handler can ensure cleanup before the process exits.
    /// </summary>
    public Task StopAsync() => _host.StopAsync();

    /// <summary>
    /// Check for recovery candidates from a previous session and offer each one in order.
    /// Must be called from the UI thread (it may show an Avalonia dialog).
    /// Errors are swallowed -- recovery is best-effort and never blocks startup.
    /// </summary>
    public Task OfferRecoveryAsync(Window owner) => _host.OfferRecoveryAsync(owner);

    /// <summary>
    /// Manual Backstage &gt; Open &gt; "Recover Unsaved" command. Reachable at any point mid-session,
    /// possibly against a dirty document, so restoring into THIS window runs the save/discard dirty
    /// gate supplied as <c>confirmDiscardOrSaveAsync</c> first.
    /// </summary>
    public Task RecoverUnsavedDocumentsAsync(Window owner) => _host.RecoverManuallyAsync(owner);

    public void Dispose() => _host.Dispose();

    private static bool? ResolvePromptResponse(string message)
    {
        Func<string, bool>? responder = null;
        ResolvePromptResponder(ref responder);
        return responder?.Invoke(message);
    }

    /// <summary>Headless-test seam: lets tests answer the recovery prompt without a real modal.</summary>
    static partial void ResolvePromptResponder(ref Func<string, bool>? responder);
}
