using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
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
    // Process-wide registry of every live window's adapter, so a crash handler (which has no
    // reference to any particular window) can fan an emergency snapshot out to all of them --
    // mirrors FreeW's AutosaveAdapter.ActiveAdapters and FreeX's AvaloniaAutosaveCoordinator.
    private static readonly object ActiveAdaptersGate = new();
    private static readonly List<AutosaveAdapter> ActiveAdapters = [];

    private readonly Action<Presentation, string?> _applyRecoveredPresentation;
    private readonly FreePAutosaveSession _session;
    private readonly Func<AutosaveRecoveryCandidate, Task<bool>>? _recoverInNewWindowAsync;
    private readonly Func<Task<bool>>? _confirmDiscardOrSaveAsync;
    private readonly Func<string?> _getCurrentPath;
    private CancellationTokenSource? _cts;

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
                ExecuteOnUiThreadBounded(getPresentation, writePresentation));
        _session = sessionFactory?.Invoke(ports) ?? new FreePAutosaveSession(ports);
        _recoverInNewWindowAsync = recoverInNewWindowAsync;
        _confirmDiscardOrSaveAsync = confirmDiscardOrSaveAsync;
        _getCurrentPath = () => workflow.CurrentPath;

        lock (ActiveAdaptersGate)
            ActiveAdapters.Add(this);
    }

    /// <summary>
    /// Best-effort emergency snapshot for this window's presentation. Must never throw -- delegates
    /// to <see cref="FreePAutosaveSession.TryEmergencySnapshot"/>, which is never-throw by design.
    /// </summary>
    public void TryEmergencySnapshot() => _session.TryEmergencySnapshot();

    /// <summary>
    /// Attempts an emergency snapshot for every live window's presentation. Wired as the Avalonia
    /// desktop profile's crash-handler hook (see App.cs's DesktopProfile) so a crash takes the same
    /// best-effort snapshot FreeX's and FreeW's Avalonia hosts do instead of losing every edit since
    /// the last periodic autosave tick.
    /// </summary>
    public static void TryEmergencySnapshots()
    {
        AutosaveAdapter[] adapters;
        lock (ActiveAdaptersGate)
            adapters = ActiveAdapters.ToArray();

        foreach (var adapter in adapters)
            adapter.TryEmergencySnapshot();
    }

    /// <summary>
    /// Start the periodic autosave loop. Safe to call from any thread.
    /// The snapshot itself runs off the UI thread (file I/O); model reads are marshalled back.
    /// </summary>
    public void Start()
    {
        if (_cts is not null)
            return; // already running

        _cts = new CancellationTokenSource();
        _ = RunLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Stop the loop and delete the current-session snapshot (clean exit). Awaitable so the
    /// window's close gate can ensure cleanup before the process exits.
    /// </summary>
    public async Task StopAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
            _cts = null;
        }

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

        try
        {
            var currentWindowHasExplicitDocument = _getCurrentPath() is not null;

            await FreePRecoveryWorkflow.RunAsync(
                _session.PlanRecoveries(),
                FreePRecoveryPromptMode.StartupQuotedDisplayName,
                offer => new ValueTask<bool>(RecoveryPromptDialog.ShowAsync(owner, offer.Prompt)),
                async (recovery, useCurrentWindow) =>
                {
                    if (useCurrentWindow && !currentWindowHasExplicitDocument)
                    {
                        return _session.CompletePresentationRecovery(
                            recovery,
                            accepted: true,
                            _applyRecoveredPresentation,
                            FreePRecoveryRestoreExceptionPolicy.QuarantineCandidate);
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
        try
        {
            var recoveries = _session.PlanRecoveries();
            if (recoveries.Count == 0)
            {
                await AvaloniaUserMessageDialog.ShowAsync(owner, text.NoDocumentsMessage, text.Title, UserMessageIcon.Information);
                return;
            }

            await FreePRecoveryWorkflow.RunAsync(
                recoveries,
                FreePRecoveryPromptMode.Manual,
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
    /// Restores an accepted recovery into THIS window, gated behind the current presentation's own
    /// save/discard prompt. If the user cancels that prompt the candidate is left on disk
    /// (accepted:false -> Keep disposition) so it can be revisited from Backstage later, matching the
    /// WPF host and the behaviour of declining the initial "Recover unsaved changes?" offer.
    /// </summary>
    /// <remarks>
    /// Passing accepted:true here instead would resolve to Quarantine
    /// (<see cref="Free.Shared.AppServices.AutosaveRecoveryPolicy.ResolveDisposition"/>: accepted and
    /// not recovered =&gt; Quarantine), which moves the snapshot out of the directory EnumerateCandidates
    /// scans -- so declining to discard the CURRENT presentation would permanently destroy access to
    /// the OLDER unsaved work the user was trying to recover. Declining one thing must not throw away
    /// the other.
    /// </remarks>
    private async Task<bool> RecoverIntoCurrentWindowGatedAsync(AutosaveRecoveryPlan recovery)
    {
        if (_confirmDiscardOrSaveAsync is not null && !await _confirmDiscardOrSaveAsync())
        {
            _session.CompleteRecoveryResult(recovery, accepted: false, recovered: false);
            return false;
        }

        return _session.CompletePresentationRecovery(
            recovery,
            accepted: true,
            _applyRecoveredPresentation,
            FreePRecoveryRestoreExceptionPolicy.QuarantineCandidate);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _session.Dispose();

        lock (ActiveAdaptersGate)
            ActiveAdapters.Remove(this);
    }

    // ── Private ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Timeout bound for <see cref="ExecuteOnUiThreadBounded"/>'s off-thread marshal -- mirrors the
    /// WPF sibling's bounded <c>Dispatcher.Invoke(..., 8s)</c> (see
    /// <c>FreeP.App.Host/EmergencySnapshotCrashHandler.cs</c>).
    /// </summary>
    private static readonly TimeSpan EmergencySnapshotDispatcherTimeout = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Runs <paramref name="writePresentation"/> against the window's presentation on the Avalonia
    /// UI thread, bounded so a wedged dispatcher pump degrades to "no snapshot" instead of hanging
    /// the process.
    ///
    /// <para>
    /// This deliberately reproduces FreeW's R138-remediation shape rather than the naive
    /// <c>Dispatcher.UIThread.InvokeAsync(...).GetAwaiter().GetResult()</c> it replaced -- FreeW's
    /// own source carries a note warning FreeP not to reintroduce that form. A crash handler is
    /// reached from <c>AppDomain.UnhandledException</c>, which fires synchronously on the faulting
    /// thread and is very often the UI thread itself, reentrant partway through whatever it was
    /// doing and NOT inside an active dispatcher loop iteration that could service a queued
    /// continuation. Blocking on posted work from that same pump-less thread is a permanent
    /// single-thread deadlock: the process hangs forever instead of exiting having lost recent
    /// edits, which is strictly worse than the data loss the emergency snapshot exists to avoid.
    /// So we (a) skip the marshal entirely when already on the UI thread, and (b) bound the wait
    /// when we do have to marshal from a genuinely different thread. The bound uses
    /// <see cref="ManualResetEventSlim"/> rather than a dispatcher-operation wait so a timeout
    /// simply stops waiting -- it does not depend on the posted work being cancellable.
    /// </para>
    /// </summary>
    private static void ExecuteOnUiThreadBounded(
        Func<Presentation> getPresentation,
        Action<Presentation> writePresentation)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            writePresentation(getPresentation());
            return;
        }

        using var completed = new ManualResetEventSlim(initialState: false);
        Dispatcher.UIThread.Post(
            () =>
            {
                try { writePresentation(getPresentation()); }
                finally { completed.Set(); }
            },
            DispatcherPriority.Send);

        // Timeout => the UI thread's pump is wedged. The posted write may still run later and is
        // harmless if it does, but the crash handler does not wait on it any further -- "no
        // snapshot" is the correct best-effort outcome here, not "process never exits".
        completed.Wait(EmergencySnapshotDispatcherTimeout);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(FreePAutosaveSession.DefaultInterval, ct); }
            catch (OperationCanceledException) { break; }

            if (ct.IsCancellationRequested)
                break;

            // The portable session is best-effort and skips unchanged or clean presentations.
            _session.Snapshot();
        }
    }
}

/// <summary>
/// Minimal Yes / No prompt for the autosave recovery offer.
/// </summary>
internal sealed partial class RecoveryPromptDialog : FreePDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;

    private RecoveryPromptDialog(string message)
    {
        var recoveryText = AutosaveRecoveryTextCatalog.Resolve(UiText.Get);
        Title = recoveryText.Title;
        Width = 420;
        Height = 160;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 16, 16, 20),
        };

        var yes = new Button { Content = recoveryText.RecoverButton, MinWidth = 82, IsDefault = true };
        AvaloniaCompactDialogChrome.ApplyButton(yes, DialogChromeStyle, minWidth: 82, isDefault: true);
        yes.Click += (_, _) => Close(true);

        var no = new Button { Content = recoveryText.SkipButton, MinWidth = 82, IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(no, DialogChromeStyle, minWidth: 82);
        no.Click += (_, _) => Close(false);

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([yes, no], new Thickness(16, 0, 16, 16));

        Content = new StackPanel { Children = { text, buttons } };
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
