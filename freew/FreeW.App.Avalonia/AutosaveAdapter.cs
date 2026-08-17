using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
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
    // R138: process-wide registry of every live window's adapter, so a crash handler (which has no
    // reference to any particular window) can fan an emergency snapshot out to all of them --
    // mirrors FreeX's Avalonia AvaloniaAutosaveCoordinator.ActiveCoordinators (src/FreeX.App.Avalonia/App.cs).
    private static readonly object ActiveAdaptersGate = new();
    private static readonly List<AutosaveAdapter> ActiveAdapters = [];

    private readonly DocumentView _editor;
    private readonly FileCommandWorkflow _workflow;
    private readonly FreeWAutosaveSession _session;
    private readonly Func<AutosaveRecoveryCandidate, Task<bool>>? _recoverInNewWindowAsync;
    private CancellationTokenSource? _cts;

    public AutosaveAdapter(
        DocumentView editor,
        FileCommandWorkflow workflow,
        Func<FreeWAutosavePorts, FreeWAutosaveSession>? sessionFactory = null,
        Func<AutosaveRecoveryCandidate, Task<bool>>? recoverInNewWindowAsync = null)
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
            ExecuteWithDocument: writeDocument => ExecuteOnUiThreadBounded(editor, writeDocument));
        _session = sessionFactory?.Invoke(ports) ?? new FreeWAutosaveSession(ports);
        _recoverInNewWindowAsync = recoverInNewWindowAsync;

        lock (ActiveAdaptersGate)
            ActiveAdapters.Add(this);
    }

    /// <summary>
    /// Best-effort emergency snapshot for this window's document. Must never throw -- delegates to
    /// <see cref="FreeWAutosaveSession.TryEmergencySnapshot"/>, which is never-throw by design.
    /// </summary>
    public void TryEmergencySnapshot() => _session.TryEmergencySnapshot();

    /// <summary>
    /// Attempts an emergency snapshot for every live window's document. Wired as the Avalonia
    /// desktop profile's crash-handler hook (see FreeW.App.Avalonia/App.cs's DesktopProfile) so a
    /// crash takes the same best-effort snapshot FreeX's Avalonia host does instead of losing every
    /// edit since the last periodic autosave tick.
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
    /// The snapshot itself runs off the UI thread (file I/O); document reads are marshalled back.
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
    /// window's Closing handler can ensure cleanup before the process exits.
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
    public async Task OfferRecoveryAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        try
        {
            await FreeWRecoveryWorkflow.RunAsync(
                _session.PlanRecoveries(),
                FreeWRecoveryPromptMode.StartupQuotedDisplayName,
                offer => new ValueTask<bool>(RecoveryPromptDialog.ShowAsync(owner, offer.Prompt)),
                async (recovery, useCurrentWindow) =>
                {
                    if (useCurrentWindow)
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
    /// R138 REMEDIATION timeout bound for <see cref="ExecuteOnUiThreadBounded"/>'s off-thread
    /// marshal below -- mirrors the WPF sibling's bounded <c>Dispatcher.Invoke(..., 8s)</c>
    /// (see <c>FreeW.App.Host/EmergencySnapshotCrashHandler.cs</c>).
    /// </summary>
    private static readonly TimeSpan EmergencySnapshotDispatcherTimeout = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Runs <paramref name="writeDocument"/> against <paramref name="editor"/>'s document on the
    /// Avalonia UI thread, bounded so a wedged dispatcher pump degrades to "no snapshot" instead
    /// of hanging the process.
    ///
    /// <para>
    /// R138 REMEDIATION: this used to be an unconditional
    /// <c>Dispatcher.UIThread.InvokeAsync(...).GetAwaiter().GetResult()</c>. A crash handler
    /// (<see cref="TryEmergencySnapshot"/>/<see cref="TryEmergencySnapshots"/>) is reached from
    /// <c>AppDomain.UnhandledException</c>, which fires synchronously on the faulting thread and
    /// is very often the UI thread itself, reentrant partway through whatever it was doing --
    /// i.e. NOT inside an active dispatcher loop iteration that could ever come back around and
    /// service a queued continuation. Calling <c>InvokeAsync(...).GetAwaiter().GetResult()</c>
    /// from that same pump-less thread queues work that can only run once this very call
    /// returns, then blocks waiting for it: a permanent, single-thread deadlock. The process
    /// hangs forever instead of exiting having lost recent edits, which is strictly worse than
    /// the data loss the emergency snapshot exists to avoid. A crash handler must always
    /// terminate. We therefore (a) skip the marshal entirely when already on the UI thread --
    /// <c>Dispatcher.UIThread.CheckAccess()</c> is true for exactly the reentrant case above, so
    /// the write just runs inline -- mirroring the WPF sibling's
    /// <c>dispatcher.CheckAccess()</c> shortcut in
    /// <c>FreeW.App.Host/EmergencySnapshotCrashHandler.cs</c>; and (b) bound the wait when we do
    /// have to marshal from a genuinely different thread, so a UI thread wedged for some other
    /// reason still lets the crash handler return within
    /// <see cref="EmergencySnapshotDispatcherTimeout"/> instead of blocking indefinitely. The
    /// bound is implemented with <see cref="ManualResetEventSlim"/> rather than
    /// <c>DispatcherOperation.Wait(TimeSpan)</c>/<c>InvokeAsync(...).GetAwaiter()</c> so a
    /// timeout simply stops waiting -- it does not depend on the posted work being cancellable.
    /// </para>
    ///
    /// <para>
    /// NOTE for future readers: FreeP has no autosave machinery yet. If one is ever added by
    /// copying this Avalonia adapter's shape, keep this bounded-marshal pattern -- do not
    /// reintroduce the naive unconditional <c>InvokeAsync(...).GetAwaiter().GetResult()</c> this
    /// replaced; it deadlocks FreeP's crash handler exactly the way it deadlocked FreeW's.
    /// </para>
    /// </summary>
    private static void ExecuteOnUiThreadBounded(DocumentView editor, Action<TextDocument> writeDocument)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            writeDocument(editor.Document);
            return;
        }

        using var completed = new ManualResetEventSlim(initialState: false);
        Dispatcher.UIThread.Post(
            () =>
            {
                try { writeDocument(editor.Document); }
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
            try { await Task.Delay(FreeWAutosaveSession.DefaultInterval, ct); }
            catch (OperationCanceledException) { break; }

            if (ct.IsCancellationRequested)
                break;

            // The portable session is best-effort and skips unchanged or clean documents.
            _session.Snapshot();
        }
    }

}

/// <summary>
/// Minimal Yes / No prompt for the autosave recovery offer.
/// </summary>
internal sealed partial class RecoveryPromptDialog : FreeWDialogWindow
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
