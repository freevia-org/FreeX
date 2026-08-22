using Avalonia.Controls;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Shell;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// Avalonia scheduling, dispatch, and prompt adapter for the renderer-neutral
/// <see cref="FreeWAutosaveSession"/>.
///
/// <para>
/// Call <see cref="Start"/> after the window opens and <see cref="StopAsync"/> after the
/// dirty-gate has passed (on close). On startup, call <see cref="OfferRecoveryAsync"/> to
/// surface any snapshot from a previous crashed session.
/// </para>
/// </summary>
internal sealed partial class AutosaveAdapter : IDisposable
{
    private static readonly EmergencySnapshotFanOut<AutosaveAdapter> EmergencySnapshots =
        new(static adapter => adapter.TryEmergencySnapshot());
    private static readonly TimeSpan EmergencySnapshotDispatcherTimeout = TimeSpan.FromSeconds(8);

    private readonly DocumentView _editor;
    private readonly IDisposable _emergencySnapshotRegistration;
    private readonly FileCommandWorkflow _workflow;
    private readonly AutosavePeriodicTaskLoop _periodicLoop;
    private readonly FreeWAutosaveSession _session;
    private readonly Func<AutosaveRecoveryCandidate, Task<bool>>? _recoverInNewWindowAsync;
    private readonly Func<Task<bool>>? _confirmDiscardOrSaveAsync;

    public AutosaveAdapter(
        DocumentView editor,
        FileCommandWorkflow workflow,
        Func<FreeWAutosavePorts, FreeWAutosaveSession>? sessionFactory = null,
        Func<AutosaveRecoveryCandidate, Task<bool>>? recoverInNewWindowAsync = null,
        Func<Task<bool>>? confirmDiscardOrSaveAsync = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(workflow);

        _editor = editor;
        _workflow = workflow;
        var ports = new FreeWAutosavePorts(
            GetOriginalFilePath: () => workflow.CurrentPath,
            GetDisplayName: () => workflow.DisplayName,
            GetIsDirty: () => workflow.IsDirty,
            GetDirtyGeneration: () => workflow.DirtyGeneration,
            ExecuteWithDocument: writeDocument => writeDocument(editor.Document));
        _session = sessionFactory?.Invoke(ports) ?? new FreeWAutosaveSession(ports);
        _periodicLoop = new AutosavePeriodicTaskLoop(
            FreeWAutosaveSession.DefaultInterval,
            () => AvaloniaBoundedDispatcherTransaction.TryExecute(
                _session.Snapshot,
                EmergencySnapshotDispatcherTimeout));
        _recoverInNewWindowAsync = recoverInNewWindowAsync;
        _confirmDiscardOrSaveAsync = confirmDiscardOrSaveAsync;
        _emergencySnapshotRegistration = EmergencySnapshots.Register(this);
    }

    /// <summary>
    /// Best-effort emergency snapshot for this window's document. Must never throw -- delegates to
    /// <see cref="FreeWAutosaveSession.TryEmergencySnapshot"/>, which is never-throw by design.
    /// </summary>
    public void TryEmergencySnapshot() => AvaloniaBoundedDispatcherTransaction.TryExecute(
        _session.TryEmergencySnapshot,
        EmergencySnapshotDispatcherTimeout);

    /// <summary>
    /// Attempts an emergency snapshot for every live window's document. Wired as the Avalonia
    /// desktop profile's crash-handler hook (see FreeW.App.Avalonia/App.cs's DesktopProfile) so a
    /// crash takes the same best-effort snapshot FreeX's Avalonia host does instead of losing every
    /// edit since the last periodic autosave tick.
    /// </summary>
    public static void TryEmergencySnapshots() => EmergencySnapshots.TrySnapshotAll();

    /// <summary>
    /// Start the periodic autosave loop. Safe to call from any thread.
    /// The shared timer remains renderer-neutral; each complete snapshot transaction is marshalled
    /// through the bounded Avalonia dispatcher bridge.
    /// </summary>
    public void Start() => _periodicLoop.Start();

    /// <summary>
    /// Stop the loop and delete the current-session snapshot (clean exit). Awaitable so the
    /// window's Closing handler can ensure cleanup before the process exits.
    /// </summary>
    public async Task StopAsync()
    {
        await _periodicLoop.StopAsync();
        _session.CompleteCleanExit();
    }

    /// <summary>
    /// Check for recovery candidates from a previous session and offer each one in order.
    /// Must be called from the UI thread (it may show an Avalonia dialog).
    /// Errors are swallowed — recovery is best-effort and never blocks startup.
    /// </summary>
    /// <remarks>
    /// startup-fileopen F2: mirrors the fix already applied to FreeP's Avalonia
    /// <c>AutosaveAdapter.OfferRecoveryAsync</c>. The caller's window may already have loaded a
    /// command-line/file-association document before this runs (see <c>MainWindow</c>, which opens
    /// the startup file into <c>this</c> synchronously, then fires this from the <c>Opened</c>
    /// handler): the just-opened document is not dirty, so routing the first accepted candidate
    /// straight into "the current window" would silently replace it. We snapshot whether the window
    /// already has an explicitly opened document (<see cref="FileCommandWorkflow.CurrentPath"/>
    /// non-null) BEFORE any candidate is applied, and if so force every accepted candidate through
    /// the new-window path instead -- the same path already used for every candidate beyond the
    /// first. A genuinely fresh window (no startup file) keeps the prior unconditional behaviour.
    /// </remarks>
    public async Task OfferRecoveryAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        try
        {
            var currentWindowHasExplicitDocument = _workflow.CurrentPath is not null;

            await FreeWRecoveryWorkflow.RunAsync(
                _session.PlanRecoveries(),
                FreeWRecoveryPromptMode.StartupQuotedDisplayName,
                offer => new ValueTask<bool>(RecoveryPromptDialog.ShowAsync(owner, offer.Prompt)),
                async (recovery, useCurrentWindow) =>
                {
                    if (useCurrentWindow && !currentWindowHasExplicitDocument)
                    {
                        var recoveredInCurrentWindow = _session.CompleteDocumentRecovery(recovery, accepted: true, (document, originalPath) =>
                        {
                            _editor.LoadDocument(document);
                            _workflow.MarkDirtyWithPath(originalPath);
                        }, FreeWRecoveryRestoreExceptionPolicy.QuarantineCandidate);
                        return recoveredInCurrentWindow;
                    }

                    var recovered = _recoverInNewWindowAsync is not null &&
                        await _recoverInNewWindowAsync(recovery.Candidate);
                    _session.CompleteRecoveryResult(recovery, accepted: true, recovered);
                    return recovered;
                });
        }
        catch
        {
            // Recovery is best-effort; never block startup on it.
        }
    }

    /// <summary>
    /// Manual Backstage &gt; Open &gt; "Recover Unsaved" command. Unlike <see cref="OfferRecoveryAsync"/>
    /// (the best-effort, silent STARTUP offer -- a fresh window has nothing unsaved to lose), this is
    /// reachable at any point mid-session, possibly against a dirty document. Restoring a recovered
    /// snapshot into THIS window must therefore run the same save/discard dirty gate FreeW's WPF host
    /// runs via <c>FileCommands.RecoverSnapshot</c> (which wraps the restore through
    /// <c>FileCommandWorkflow.Open(...)</c> so <c>ConfirmDiscardOrSave</c> prompts first) -- otherwise
    /// the current unsaved edits are silently discarded. See
    /// <c>AutosaveCoordinator.RecoverUnsavedDocuments</c> on the WPF side for the sibling
    /// implementation this mirrors.
    /// </summary>
    public async Task RecoverUnsavedDocumentsAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var text = AutosaveRecoveryTextCatalog.Resolve(UiText.Get);
        try
        {
            var recoveries = _session.PlanRecoveries();
            if (recoveries.Count == 0)
            {
                await AvaloniaUserMessageDialog.ShowAsync(owner, text.NoDocumentsMessage, text.Title, UserMessageIcon.Information);
                return;
            }

            await FreeWRecoveryWorkflow.RunAsync(
                recoveries,
                FreeWRecoveryPromptMode.Manual,
                offer => new ValueTask<bool>(RecoveryPromptDialog.ShowAsync(owner, offer.Prompt)),
                async (recovery, useCurrentWindow) =>
                {
                    if (useCurrentWindow)
                        return await RecoverIntoCurrentWindowGatedAsync(recovery);

                    var recovered = _recoverInNewWindowAsync is not null &&
                        await _recoverInNewWindowAsync(recovery.Candidate);
                    _session.CompleteRecoveryResult(recovery, accepted: true, recovered);
                    return recovered;
                });
        }
        catch (Exception ex)
        {
            await AvaloniaUserMessageDialog.ShowErrorAsync(
                owner,
                string.Format(System.Globalization.CultureInfo.CurrentCulture, text.FailureMessageFormat, ex.Message),
                text.Title);
        }
    }

    /// <summary>
    /// Restores an accepted recovery into THIS window, gated behind the current document's own
    /// save/discard prompt. If the user cancels that prompt the candidate is left on disk
    /// (accepted:false -> Keep disposition) so it can be revisited from Backstage later, matching the
    /// WPF host and the behaviour of declining the initial "Recover unsaved changes?" offer.
    /// </summary>
    /// <remarks>
    /// Passing accepted:true here instead would resolve to Quarantine
    /// (<see cref="Free.Shared.AppServices.AutosaveRecoveryPolicy.ResolveDisposition"/>: accepted and
    /// not recovered =&gt; Quarantine), which moves the snapshot out of the directory EnumerateCandidates
    /// scans -- so declining to discard the CURRENT document would permanently destroy access to the
    /// OLDER unsaved work the user was trying to recover. Declining one thing must not throw away the
    /// other.
    /// </remarks>
    private async Task<bool> RecoverIntoCurrentWindowGatedAsync(AutosaveRecoveryPlan recovery)
    {
        if (_confirmDiscardOrSaveAsync is not null && !await _confirmDiscardOrSaveAsync())
        {
            _session.CompleteRecoveryResult(recovery, accepted: false, recovered: false);
            return false;
        }

        return _session.CompleteDocumentRecovery(recovery, accepted: true, (document, originalPath) =>
        {
            _editor.LoadDocument(document);
            _workflow.MarkDirtyWithPath(originalPath);
        }, FreeWRecoveryRestoreExceptionPolicy.QuarantineCandidate);
    }

    public void Dispose()
    {
        _emergencySnapshotRegistration.Dispose();
        _periodicLoop.Dispose();
        _session.Dispose();
    }
}

/// <summary>
/// Minimal Yes / No prompt for the autosave recovery offer.
/// </summary>
internal sealed partial class RecoveryPromptDialog : FreeWDialogWindow
{
    private RecoveryPromptDialog(string message)
    {
        var recoveryText = AutosaveRecoveryTextCatalog.Resolve(UiText.Get);
        AvaloniaRecoveryPromptDialogComposer.Compose(
            this,
            message,
            new(recoveryText.Title, recoveryText.RecoverButton, recoveryText.SkipButton),
            response => Close(response));
    }

    /// <summary>Show the prompt and return true if the user chose to recover.</summary>
    public static Task<bool> ShowAsync(Window owner, string message)
    {
        var handled = false;
        var response = false;
        ResolveResponseOverride(message, ref handled, ref response);
        return handled
            ? Task.FromResult(response)
            : new RecoveryPromptDialog(message).ShowDialog<bool>(owner);
    }

    static partial void ResolveResponseOverride(string message, ref bool handled, ref bool response);
}
