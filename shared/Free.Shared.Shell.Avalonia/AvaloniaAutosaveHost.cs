using Avalonia.Controls;
using Free.Shared.AppServices;

namespace Free.Shared.Shell.Avalonia;

/// <summary>
/// The per-product half of an Avalonia autosave host: how the current window answers "do I already
/// hold an explicitly opened document", how an accepted snapshot is restored into it, and how the
/// product opens a recovered snapshot in a new window / confirms discarding unsaved edits.
/// </summary>
/// <param name="CurrentWindowHasExplicitDocument">
/// True when the window already holds a command-line/file-association document, so startup recovery
/// must route every accepted candidate through <paramref name="RecoverInNewWindowAsync"/> instead of
/// silently replacing it.
/// </param>
/// <param name="RestoreIntoCurrentWindow">Restores an accepted snapshot into this window.</param>
/// <param name="RecoverInNewWindowAsync">Opens a candidate in its own window; null means "not possible" (false).</param>
/// <param name="ConfirmDiscardOrSaveAsync">The manual-recovery dirty gate; null means no gate.</param>
/// <param name="PromptResponseOverride">
/// Optional non-interactive answer to the recovery prompt (headless tests); null result = show the dialog.
/// </param>
public sealed record AvaloniaAutosaveHostCallbacks<TDocument>(
    Func<bool> CurrentWindowHasExplicitDocument,
    AutosaveRecoveryRestore<TDocument> RestoreIntoCurrentWindow,
    Func<AutosaveRecoveryCandidate, Task<bool>>? RecoverInNewWindowAsync = null,
    Func<Task<bool>>? ConfirmDiscardOrSaveAsync = null,
    Func<string, bool?>? PromptResponseOverride = null);

/// <summary>
/// Avalonia scheduling, dispatch, and prompt host for one window's
/// <see cref="AutosaveApplicationSession{TDocument}"/>: the periodic loop (each snapshot transaction
/// marshalled through the bounded UI-thread bridge), the process-wide emergency fan-out registration,
/// clean-exit stop, and the startup / manual recovery wiring. Products bind it with their
/// <see cref="AutosaveApplicationProfile{TDocument}"/>, ports, and
/// <see cref="AvaloniaAutosaveHostCallbacks{TDocument}"/>.
/// </summary>
public sealed class AvaloniaAutosaveHost<TDocument> : IDisposable
{
    private static readonly EmergencySnapshotFanOut<AvaloniaAutosaveHost<TDocument>> EmergencySnapshots =
        new(static host => host.TryEmergencySnapshot());
    private static readonly TimeSpan EmergencySnapshotDispatcherTimeout = TimeSpan.FromSeconds(8);

    private readonly AutosaveApplicationProfile<TDocument> _profile;
    private readonly AvaloniaAutosaveHostCallbacks<TDocument> _callbacks;
    private readonly Func<string, string?>? _getText;
    private readonly IDisposable _emergencySnapshotRegistration;
    private readonly AutosavePeriodicTaskLoop _periodicLoop;

    public AvaloniaAutosaveHost(
        AutosaveApplicationProfile<TDocument> profile,
        AutosaveDocumentPorts<TDocument> ports,
        AvaloniaAutosaveHostCallbacks<TDocument> callbacks,
        Func<string, string?>? getText = null,
        Func<AutosaveDocumentPorts<TDocument>, AutosaveApplicationSession<TDocument>>? sessionFactory = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(callbacks);
        ArgumentNullException.ThrowIfNull(callbacks.CurrentWindowHasExplicitDocument);
        ArgumentNullException.ThrowIfNull(callbacks.RestoreIntoCurrentWindow);

        _profile = profile;
        _callbacks = callbacks;
        _getText = getText;
        Session = sessionFactory?.Invoke(ports) ?? new AutosaveApplicationSession<TDocument>(profile, ports);
        _periodicLoop = new AutosavePeriodicTaskLoop(
            profile.Interval,
            () => AvaloniaBoundedDispatcherTransaction.TryExecute(
                Session.Snapshot,
                EmergencySnapshotDispatcherTimeout));
        _emergencySnapshotRegistration = EmergencySnapshots.Register(this);
    }

    /// <summary>The shared autosave session this host schedules.</summary>
    public AutosaveApplicationSession<TDocument> Session { get; }

    /// <summary>Number of live hosts of this document type eligible for emergency fan-out.</summary>
    public static int ActiveCount => EmergencySnapshots.ActiveCount;

    /// <summary>
    /// Best-effort emergency snapshot for this window's document. Must never throw -- delegates to
    /// <see cref="AutosaveApplicationSession{TDocument}.TryEmergencySnapshot"/>, which is never-throw
    /// by design.
    /// </summary>
    public void TryEmergencySnapshot() => AvaloniaBoundedDispatcherTransaction.TryExecute(
        Session.TryEmergencySnapshot,
        EmergencySnapshotDispatcherTimeout);

    /// <summary>
    /// Attempts an emergency snapshot for every live window's document. Wired as the Avalonia
    /// desktop profile's crash-handler hook.
    /// </summary>
    public static void TrySnapshotAll() => EmergencySnapshots.TrySnapshotAll();

    /// <summary>
    /// Start the periodic autosave loop. Safe to call from any thread. The shared timer remains
    /// renderer-neutral; each complete snapshot transaction is marshalled through the bounded
    /// Avalonia dispatcher bridge.
    /// </summary>
    public void Start() => _periodicLoop.Start();

    /// <summary>
    /// Stop the loop and delete the current-session snapshot (clean exit). Awaitable so the window's
    /// close gate can ensure cleanup before the process exits.
    /// </summary>
    public async Task StopAsync()
    {
        await _periodicLoop.StopAsync();
        Session.CompleteCleanExit();
    }

    /// <summary>
    /// Offers every prior-session snapshot on startup. Must be called from the UI thread (it may show
    /// an Avalonia dialog); errors are swallowed because recovery never blocks startup. The first
    /// accepted candidate uses this window unless it already holds an explicitly opened document, in
    /// which case every accepted candidate opens through the new-window callback.
    /// </summary>
    public async Task<bool> OfferRecoveryAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return await AvaloniaAutosaveRecoveryHost.OfferStartupAsync(
            owner,
            _callbacks.CurrentWindowHasExplicitDocument,
            Session.PlanRecoveries,
            createOffer: (recovery, remainingCount) => _profile.Recovery.CreateOffer(
                recovery,
                remainingCount,
                AutosaveRecoveryPromptMode.StartupQuotedDisplayName),
            promptAsync: offer => PromptAsync(owner, offer),
            recoverInCurrentWindow: RecoverInCurrentWindow,
            recoverInNewWindowAsync: RecoverInNewWindowAsync,
            completeRecoveryResult: CompleteRecoveryResult);
    }

    /// <summary>
    /// User-invoked recovery of unsaved documents. Unlike the silent startup offer it is reachable
    /// mid-session against a dirty document, so restoring into THIS window runs the product's
    /// save/discard dirty gate first, tells the user when there is nothing to recover, and surfaces
    /// failures instead of swallowing them.
    /// </summary>
    public async Task<bool> RecoverManuallyAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var text = _profile.Recovery.ResolveText(_getText);
        return await AvaloniaAutosaveRecoveryHost.RecoverManuallyAsync(
            owner,
            new(text.Title, text.NoDocumentsMessage, text.FailureMessageFormat),
            Session.PlanRecoveries,
            createOffer: (recovery, remainingCount) => _profile.Recovery.CreateOffer(
                recovery,
                remainingCount,
                AutosaveRecoveryPromptMode.Manual),
            promptAsync: offer => PromptAsync(owner, offer),
            _callbacks.ConfirmDiscardOrSaveAsync,
            recoverInCurrentWindow: RecoverInCurrentWindow,
            recoverInNewWindowAsync: RecoverInNewWindowAsync,
            completeRecoveryResult: CompleteRecoveryResult);
    }

    public void Dispose()
    {
        _emergencySnapshotRegistration.Dispose();
        _periodicLoop.Dispose();
        Session.Dispose();
    }

    private void CompleteRecoveryResult(AutosaveRecoveryPlan recovery, bool accepted, bool recovered) =>
        Session.CompleteRecoveryResult(recovery, accepted, recovered);

    private ValueTask<bool> PromptAsync(Window owner, AutosaveRecoveryOffer offer)
    {
        if (_callbacks.PromptResponseOverride?.Invoke(offer.Prompt) is { } response)
            return new ValueTask<bool>(response);

        return new ValueTask<bool>(
            AvaloniaAutosaveRecoveryPromptDialog.ShowAsync(owner, offer.Prompt, _profile.Recovery, _getText));
    }

    private bool RecoverInCurrentWindow(AutosaveRecoveryPlan recovery) =>
        _callbacks.RestoreIntoCurrentWindow.Complete(
            Session,
            recovery,
            accepted: true,
            AutosaveRecoveryRestoreExceptionPolicy.QuarantineCandidate);

    private Task<bool> RecoverInNewWindowAsync(AutosaveRecoveryPlan recovery) =>
        _callbacks.RecoverInNewWindowAsync is null
            ? Task.FromResult(false)
            : _callbacks.RecoverInNewWindowAsync(recovery.Candidate);
}

/// <summary>
/// The shared Yes / No prompt for the autosave recovery offer: window chrome and button text come
/// from the product's <see cref="AutosaveRecoveryProfile"/>.
/// </summary>
internal sealed class AvaloniaAutosaveRecoveryPromptDialog : AvaloniaDialogWindow
{
    private AvaloniaAutosaveRecoveryPromptDialog(
        string message,
        AutosaveRecoveryProfile recovery,
        Func<string, string?>? getText)
    {
        var text = recovery.ResolveText(getText);
        AvaloniaRecoveryPromptDialogComposer.Compose(
            this,
            message,
            new(text.Title, text.RecoverButton, text.SkipButton),
            response => Close(response));
    }

    /// <summary>Show the prompt and return true if the user chose to recover.</summary>
    public static Task<bool> ShowAsync(
        Window owner,
        string message,
        AutosaveRecoveryProfile recovery,
        Func<string, string?>? getText) =>
        new AvaloniaAutosaveRecoveryPromptDialog(message, recovery, getText).ShowDialog<bool>(owner);
}
