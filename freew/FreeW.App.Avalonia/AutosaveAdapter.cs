using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Shell;

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
            ExecuteWithDocument: writeDocument => Dispatcher.UIThread.InvokeAsync(() =>
                writeDocument(editor.Document))
                .GetAwaiter()
                .GetResult());
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
