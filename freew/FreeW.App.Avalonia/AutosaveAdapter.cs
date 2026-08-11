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
internal sealed class AutosaveAdapter : IDisposable
{
    private readonly DocumentView _editor;
    private readonly FileCommandWorkflow _workflow;
    private readonly FreeWAutosaveSession _session;
    private readonly Func<AutosaveRecoveryCandidate, Task<bool>>? _recoverInNewWindowAsync;
    private CancellationTokenSource? _cts;

    public AutosaveAdapter(
        DocumentView editor,
        FileCommandWorkflow workflow,
        AutosaveSnapshotStore? store = null,
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
        _session = store is null
            ? new FreeWAutosaveSession(ports)
            : new FreeWAutosaveSession(ports, store);
        _recoverInNewWindowAsync = recoverInNewWindowAsync;
    }

    internal string SnapshotIdForTests => _session.SnapshotId;
    internal void SnapshotNowForTests() => _session.Snapshot();

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
        if (_cts is null)
            return;

        await _cts.CancelAsync();
        _cts.Dispose();
        _cts = null;

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
            var recoveries = _session.PlanRecoveries();
            var anyAccepted = false;
            for (var index = 0; index < recoveries.Count; index++)
            {
                var recovery = recoveries[index];
                var remaining = recoveries.Count - index;
                var prompt = remaining > 1
                    ? $"FreeW found unsaved changes to \"{recovery.DisplayName}\" from a previous session ({remaining} unsaved documents found). Recover this one?"
                    : $"FreeW found unsaved changes to \"{recovery.DisplayName}\" from a previous session. Recover them?";
                if (!await RecoveryPromptDialog.ShowAsync(owner, prompt))
                    continue;

                var firstAccepted = !anyAccepted;
                anyAccepted = true;
                if (firstAccepted)
                {
                    _session.CompleteDocumentRecovery(recovery, accepted: true, (document, originalPath) =>
                    {
                        _editor.LoadDocument(document);
                        _workflow.MarkDirtyWithPath(originalPath);
                    }, FreeWRecoveryRestoreExceptionPolicy.QuarantineCandidate);
                    continue;
                }

                var recovered = _recoverInNewWindowAsync is not null &&
                    await _recoverInNewWindowAsync(recovery.Candidate);
                _session.CompleteRecoveryResult(recovery, accepted: true, recovered);
            }
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
internal sealed class RecoveryPromptDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;

    private RecoveryPromptDialog(string message)
    {
        Title = "FreeW – Recover";
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

        var yes = new Button { Content = "Recover", MinWidth = 82, IsDefault = true };
        AvaloniaCompactDialogChrome.ApplyButton(yes, DialogChromeStyle, minWidth: 82, isDefault: true);
        yes.Click += (_, _) => Close(true);

        var no = new Button { Content = "Skip", MinWidth = 82, IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(no, DialogChromeStyle, minWidth: 82);
        no.Click += (_, _) => Close(false);

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([yes, no], new Thickness(16, 0, 16, 16));

        Content = new StackPanel { Children = { text, buttons } };
    }

    /// <summary>Show the prompt and return true if the user chose to recover.</summary>
    public static Func<string, bool>? TestResponder { get; set; }

    public static Task<bool> ShowAsync(Window owner, string message) =>
        TestResponder is { } responder
            ? Task.FromResult(responder(message))
            : new RecoveryPromptDialog(message).ShowDialog<bool>(owner);
}
