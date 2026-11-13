using System.Windows;
using Free.Shared.AppServices;

namespace Free.Shared.Shell.Wpf;

/// <summary>
/// The per-product half of a WPF autosave host.
/// </summary>
/// <param name="CurrentWindowHasExplicitDocument">
/// True when the window already holds a command-line/file-association document, so startup recovery
/// must route every accepted candidate through <paramref name="RecoverInNewWindow"/> instead of
/// silently replacing it.
/// </param>
/// <param name="RestoreIntoCurrentWindow">Restores an accepted snapshot into this window.</param>
/// <param name="ConfirmRecoveryIntoCurrentWindow">
/// The product's synchronous save/discard dirty gate, run before a MANUAL recovery overwrites this
/// window. Returning false declines the candidate.
/// </param>
/// <param name="RecoverInNewWindow">Opens a candidate in its own window; null means "not possible" (false).</param>
public sealed record WpfAutosaveHostCallbacks<TDocument>(
    Func<bool> CurrentWindowHasExplicitDocument,
    AutosaveRecoveryRestore<TDocument> RestoreIntoCurrentWindow,
    Func<bool> ConfirmRecoveryIntoCurrentWindow,
    Func<AutosaveRecoveryCandidate, bool>? RecoverInNewWindow = null);

/// <summary>
/// WPF scheduling and prompt host for one window's <see cref="AutosaveApplicationSession{TDocument}"/>:
/// the dispatcher timer, clean-exit stop, never-throw emergency snapshot, and the startup / manual
/// recovery wiring over <see cref="WpfAutosaveRecoveryHost"/>. Products bind it with their
/// <see cref="AutosaveApplicationProfile{TDocument}"/>, ports, and
/// <see cref="WpfAutosaveHostCallbacks{TDocument}"/>.
/// </summary>
public sealed class WpfAutosaveHost<TDocument>
{
    private readonly AutosaveApplicationProfile<TDocument> _profile;
    private readonly WpfAutosaveHostCallbacks<TDocument> _callbacks;
    private readonly Func<string, string?>? _getText;
    private readonly WpfAutosaveTimer _timer;
    private readonly IWpfAutosaveRecoveryDialogs _dialogs;

    public WpfAutosaveHost(
        AutosaveApplicationProfile<TDocument> profile,
        AutosaveDocumentPorts<TDocument> ports,
        WpfAutosaveHostCallbacks<TDocument> callbacks,
        Func<string, string?>? getText = null,
        Func<AutosaveDocumentPorts<TDocument>, AutosaveApplicationSession<TDocument>>? sessionFactory = null)
        : this(profile, ports, callbacks, getText, sessionFactory, WpfAutosaveRecoveryDialogs.Instance)
    {
    }

    internal WpfAutosaveHost(
        AutosaveApplicationProfile<TDocument> profile,
        AutosaveDocumentPorts<TDocument> ports,
        WpfAutosaveHostCallbacks<TDocument> callbacks,
        Func<string, string?>? getText,
        Func<AutosaveDocumentPorts<TDocument>, AutosaveApplicationSession<TDocument>>? sessionFactory,
        IWpfAutosaveRecoveryDialogs dialogs)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        _dialogs = dialogs;
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(callbacks);
        ArgumentNullException.ThrowIfNull(callbacks.CurrentWindowHasExplicitDocument);
        ArgumentNullException.ThrowIfNull(callbacks.RestoreIntoCurrentWindow);
        ArgumentNullException.ThrowIfNull(callbacks.ConfirmRecoveryIntoCurrentWindow);

        _profile = profile;
        _callbacks = callbacks;
        _getText = getText;
        Session = sessionFactory?.Invoke(ports) ?? new AutosaveApplicationSession<TDocument>(profile, ports);
        _timer = new WpfAutosaveTimer(profile.Interval, Session.Snapshot);
    }

    /// <summary>The shared autosave session this host schedules.</summary>
    public AutosaveApplicationSession<TDocument> Session { get; }

    public void Start() => _timer.Start();

    /// <summary>
    /// Best-effort emergency snapshot for the crash handler. Must never throw -- delegates to
    /// <see cref="AutosaveApplicationSession{TDocument}.TryEmergencySnapshot"/>, which is never-throw
    /// by design.
    /// </summary>
    public void TryEmergencySnapshot() => Session.TryEmergencySnapshot();

    public void Stop()
    {
        _timer.Stop();
        Session.CompleteCleanExit();
    }

    /// <summary>
    /// Offers every prior-session snapshot on startup. The first accepted document uses this window
    /// unless it already holds an explicitly opened document (a command-line/file-association open
    /// that is not yet dirty), in which case every accepted candidate opens through the new-window
    /// callback; subsequent documents always do.
    /// </summary>
    public bool OfferRecovery(Window? owner)
    {
        var text = _profile.Recovery.ResolveText(_getText);
        return WpfAutosaveRecoveryHost.OfferStartup(
            owner,
            new WpfAutosaveRecoveryMessages(
                text.Title,
                text.NoDocumentsMessage,
                text.FailureMessageFormat),
            _callbacks.CurrentWindowHasExplicitDocument,
            Session.PlanRecoveries,
            (recovery, remainingCount) => _profile.Recovery.CreateOffer(
                recovery,
                remainingCount,
                AutosaveRecoveryPromptMode.Startup).Prompt,
            (recovery, useCurrentWindow) => Complete(
                recovery,
                accepted: true,
                useCurrentWindow,
                AutosaveRecoveryRestoreExceptionPolicy.QuarantineCandidate),
            _dialogs);
    }

    /// <summary>
    /// User-invoked recovery. The manual command may target a window with unrelated unsaved edits,
    /// so restoring into THIS window runs the product's dirty gate before accepting the candidate.
    /// </summary>
    public bool RecoverManually(Window? owner)
    {
        var text = _profile.Recovery.ResolveText(_getText);
        return WpfAutosaveRecoveryHost.RecoverManually(
            owner,
            new WpfAutosaveRecoveryMessages(
                text.Title,
                text.NoDocumentsMessage,
                text.FailureMessageFormat),
            Session.PlanRecoveries,
            (recovery, remainingCount) => _profile.Recovery.CreateOffer(
                recovery,
                remainingCount,
                AutosaveRecoveryPromptMode.Manual).Prompt,
            (recovery, useCurrentWindow) =>
            {
                if (useCurrentWindow && !_callbacks.ConfirmRecoveryIntoCurrentWindow())
                {
                    return Complete(
                        recovery,
                        accepted: false,
                        useCurrentWindow: true,
                        AutosaveRecoveryRestoreExceptionPolicy.PreserveCandidate);
                }

                return Complete(
                    recovery,
                    accepted: true,
                    useCurrentWindow,
                    AutosaveRecoveryRestoreExceptionPolicy.PreserveCandidate);
            },
            _dialogs);
    }

    private bool Complete(
        AutosaveRecoveryPlan recovery,
        bool accepted,
        bool useCurrentWindow,
        AutosaveRecoveryRestoreExceptionPolicy exceptionPolicy) =>
        useCurrentWindow
            ? _callbacks.RestoreIntoCurrentWindow.Complete(Session, recovery, accepted, exceptionPolicy)
            : Session.CompleteRecovery(
                recovery,
                accepted,
                (_, _) => _callbacks.RecoverInNewWindow?.Invoke(recovery.Candidate) ?? false,
                exceptionPolicy);
}
