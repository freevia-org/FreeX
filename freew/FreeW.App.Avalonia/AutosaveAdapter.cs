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
    private CancellationTokenSource? _cts;

    public AutosaveAdapter(DocumentView editor, FileCommandWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(workflow);

        _editor = editor;
        _workflow = workflow;
        _session = new FreeWAutosaveSession(new FreeWAutosavePorts(
            GetOriginalFilePath: () => workflow.CurrentPath,
            GetDisplayName: () => workflow.DisplayName,
            GetIsDirty: () => workflow.IsDirty,
            GetDirtyGeneration: () => workflow.DirtyGeneration,
            ExecuteWithDocument: writeDocument => Dispatcher.UIThread.InvokeAsync(() =>
                writeDocument(editor.Document))
                .GetAwaiter()
                .GetResult()));
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
        if (_cts is null)
            return;

        await _cts.CancelAsync();
        _cts.Dispose();
        _cts = null;

        _session.CompleteCleanExit();
    }

    /// <summary>
    /// Check for a recovery candidate from a previous session and offer to restore it.
    /// Must be called from the UI thread (it may show an Avalonia dialog).
    /// Errors are swallowed — recovery is best-effort and never blocks startup.
    /// </summary>
    public async Task OfferRecoveryAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        try
        {
            var recovery = _session.PlanLatestRecovery();
            if (recovery is null)
                return;

            var recover = await RecoveryPromptDialog.ShowAsync(
                owner,
                $"FreeW found unsaved changes to \"{recovery.DisplayName}\" from a previous session. Recover them?");

            _session.CompleteDocumentRecovery(recovery, recover, (document, originalPath) =>
            {
                _editor.LoadDocument(document);
                _workflow.MarkDirtyWithPath(originalPath);
            }, FreeWRecoveryRestoreExceptionPolicy.QuarantineCandidate);
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
    public static Task<bool> ShowAsync(Window owner, string message) =>
        new RecoveryPromptDialog(message).ShowDialog<bool>(owner);
}
