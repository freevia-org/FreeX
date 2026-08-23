using Avalonia.Controls;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

/// <summary>
/// Avalonia scheduling, dispatch, and prompt adapter for the renderer-neutral
/// <see cref="FreePAutosaveSession"/>. Mirrors FreeW's <c>AutosaveAdapter</c>.
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

    private readonly Action<Presentation, string?> _applyRecoveredPresentation;
    private readonly IDisposable _emergencySnapshotRegistration;
    private readonly AutosavePeriodicTaskLoop _periodicLoop;
    private readonly FreePAutosaveSession _session;
    private readonly Func<AutosaveRecoveryCandidate, Task<bool>>? _recoverInNewWindowAsync;
    private readonly Func<Task<bool>>? _confirmDiscardOrSaveAsync;
    private readonly Func<string?> _getCurrentPath;

    /// <summary>
    /// Takes the shared <see cref="FileCommandWorkflow"/> rather than FreeP's
    /// <c>PresentationFileCommandSession</c> because dirty/path state is all this adapter reads from
    /// the file layer -- restoring a recovered deck goes through
    /// <paramref name="applyRecoveredPresentation"/>, which the window owns. Mirrors FreeW's
    /// <c>AutosaveAdapter</c>, and keeps the type cheap enough to construct in headless tests.
    /// </summary>
    public AutosaveAdapter(
        Func<Presentation> getPresentation,
        FileCommandWorkflow workflow,
        Action<Presentation, string?> applyRecoveredPresentation,
        Func<FreePAutosavePorts, FreePAutosaveSession>? sessionFactory = null,
        Func<AutosaveRecoveryCandidate, Task<bool>>? recoverInNewWindowAsync = null,
        Func<Task<bool>>? confirmDiscardOrSaveAsync = null)
    {
        ArgumentNullException.ThrowIfNull(getPresentation);
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(applyRecoveredPresentation);

        _applyRecoveredPresentation = applyRecoveredPresentation;
        var ports = new FreePAutosavePorts(
            GetOriginalFilePath: () => workflow.CurrentPath,
            GetDisplayName: () => workflow.DisplayName,
            GetIsDirty: () => workflow.IsDirty,
            GetDirtyGeneration: () => workflow.DirtyGeneration,
            ExecuteWithPresentation: writePresentation =>
                writePresentation(getPresentation()));
        _session = sessionFactory?.Invoke(ports) ?? new FreePAutosaveSession(ports);
        _periodicLoop = new AutosavePeriodicTaskLoop(
            FreePAutosaveSession.DefaultInterval,
            () => AvaloniaBoundedDispatcherTransaction.TryExecute(
                _session.Snapshot,
                EmergencySnapshotDispatcherTimeout));
        _recoverInNewWindowAsync = recoverInNewWindowAsync;
        _confirmDiscardOrSaveAsync = confirmDiscardOrSaveAsync;
        _getCurrentPath = () => workflow.CurrentPath;
        _emergencySnapshotRegistration = EmergencySnapshots.Register(this);
    }

    /// <summary>
    /// Best-effort emergency snapshot for this window's presentation. Must never throw -- delegates
    /// to <see cref="FreePAutosaveSession.TryEmergencySnapshot"/>, which is never-throw by design.
    /// </summary>
    public void TryEmergencySnapshot() => AvaloniaBoundedDispatcherTransaction.TryExecute(
        _session.TryEmergencySnapshot,
        EmergencySnapshotDispatcherTimeout);

    /// <summary>
    /// Attempts an emergency snapshot for every live window's presentation. Wired as the Avalonia
    /// desktop profile's crash-handler hook (see App.cs's DesktopProfile) so a crash takes the same
    /// best-effort snapshot FreeX's and FreeW's Avalonia hosts do instead of losing every edit since
    /// the last periodic autosave tick.
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
    /// window's close gate can ensure cleanup before the process exits.
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
    /// startup-fileopen F2: this used to restore an accepted candidate straight into <paramref
    /// name="owner"/> whenever it was the first accepted candidate (<c>useCurrentWindow</c>),
    /// reasoning that "a fresh window has nothing unsaved to lose". That precondition breaks once
    /// the caller's window has already loaded a command-line/file-association document before this
    /// runs (see <c>MainWindow</c>'s constructor, which opens the startup file into <c>this</c>
    /// synchronously, then fires this from the <c>Opened</c> handler): the just-opened document is
    /// not dirty, so the manual command's <see cref="_confirmDiscardOrSaveAsync"/> dirty gate would
    /// not protect it either -- it would pass silently. So instead we snapshot whether the window
    /// already has an explicitly opened document (<see cref="_getCurrentPath"/> non-null: a blank
    /// new presentation has no path, an opened one does) BEFORE any candidate is applied, and if so
    /// force every accepted candidate through <see cref="_recoverInNewWindowAsync"/> -- the same
    /// "recover into its own window" path already used for every candidate beyond the first -- so
    /// the just-opened document is never silently replaced. A genuinely fresh window (no startup
    /// file) keeps the prior unconditional, ungated behaviour.
    /// </remarks>
    public async Task OfferRecoveryAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        await AvaloniaAutosaveRecoveryHost.OfferStartupAsync(
            owner,
            currentWindowHasExplicitDocument: () => _getCurrentPath() is not null,
            _session.PlanRecoveries,
            createOffer: static (recovery, remainingCount) => new FreePRecoveryOffer(
                recovery,
                remainingCount,
                FreePRecoveryPromptMode.StartupQuotedDisplayName),
            promptAsync: offer => new ValueTask<bool>(RecoveryPromptDialog.ShowAsync(owner, offer.Prompt)),
            recoverInCurrentWindow: recovery => _session.CompletePresentationRecovery(
                recovery,
                accepted: true,
                _applyRecoveredPresentation,
                FreePRecoveryRestoreExceptionPolicy.QuarantineCandidate),
            recoverInNewWindowAsync: recovery => _recoverInNewWindowAsync is null
                ? Task.FromResult(false)
                : _recoverInNewWindowAsync(recovery.Candidate),
            completeRecoveryResult: (recovery, accepted, recovered) =>
                _session.CompleteRecoveryResult(recovery, accepted, recovered));
    }

    /// <summary>
    /// Manual Backstage "Recover Unsaved Presentations" command. Unlike <see cref="OfferRecoveryAsync"/>
    /// (the best-effort, silent STARTUP offer -- a fresh window has nothing unsaved to lose), this is
    /// reachable at any point mid-session, possibly against a dirty presentation. Restoring a
    /// recovered snapshot into THIS window must therefore run the same save/discard dirty gate FreeP's
    /// WPF host runs via <c>AutosaveCoordinator.RecoverUnsavedPresentations</c> (which routes the
    /// current-window restore through <c>PresentationFileCommandSession.ConfirmCloseAllowedAsync</c>
    /// before overwriting) -- otherwise the current unsaved edits are silently discarded. It must also
    /// tell the user when there is nothing to recover, and surface failures instead of swallowing
    /// them, unlike the silent startup offer. Mirrors FreeW's Avalonia
    /// <c>AutosaveAdapter.RecoverUnsavedDocumentsAsync</c>.
    /// </summary>
    public async Task RecoverUnsavedPresentationsAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var text = AutosaveRecoveryTextCatalog.Resolve(UiText.Get);

        await AvaloniaAutosaveRecoveryHost.RecoverManuallyAsync(
            owner,
            new(text.Title, text.NoDocumentsMessage, text.FailureMessageFormat),
            _session.PlanRecoveries,
            createOffer: static (recovery, remainingCount) => new FreePRecoveryOffer(
                recovery,
                remainingCount,
                FreePRecoveryPromptMode.Manual),
            promptAsync: offer => new ValueTask<bool>(RecoveryPromptDialog.ShowAsync(owner, offer.Prompt)),
            _confirmDiscardOrSaveAsync,
            recoverInCurrentWindow: recovery => _session.CompletePresentationRecovery(
                recovery,
                accepted: true,
                _applyRecoveredPresentation,
                FreePRecoveryRestoreExceptionPolicy.QuarantineCandidate),
            recoverInNewWindowAsync: recovery => _recoverInNewWindowAsync is null
                ? Task.FromResult(false)
                : _recoverInNewWindowAsync(recovery.Candidate),
            completeRecoveryResult: (recovery, accepted, recovered) =>
                _session.CompleteRecoveryResult(recovery, accepted, recovered));
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
internal sealed partial class RecoveryPromptDialog : FreePDialogWindow
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
