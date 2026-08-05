using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;

namespace FreeW.App.Avalonia;

/// <summary>
/// Avalonia-shell autosave + crash-recovery, mirroring <c>FreeW.App.Host.AutosaveCoordinator</c>
/// but using an async <see cref="Task.Delay"/>-based loop instead of a WPF DispatcherTimer.
///
/// <para>
/// Call <see cref="Start"/> after the window opens and <see cref="StopAsync"/> after the
/// dirty-gate has passed (on close). On startup, call <see cref="OfferRecoveryAsync"/> to
/// surface any snapshot from a previous crashed session.
/// </para>
/// </summary>
internal sealed class AutosaveAdapter : IDisposable
{
    // Default autosave interval mirrors the WPF host (30 s).
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly AutosaveSnapshotStore _store;
    private readonly AutosaveSnapshotCoordinator _coordinator;
    private readonly SnapshotSource _source;
    private CancellationTokenSource? _cts;

    public AutosaveAdapter(DocumentView editor, FileCommandWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(workflow);

        _store = AutosaveSnapshotStore.CreateDefault(PlatformApplicationDataPathProvider.LocalInstance);
        _coordinator = new AutosaveSnapshotCoordinator(_store, AutosaveSnapshotStore.LaunchId.ToString("N"));
        _source = new SnapshotSource(editor, workflow);
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

        try { _coordinator.DeleteSnapshot(); } catch { /* best-effort */ }
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
            var recovery = AutosaveRecoveryPlanner.PlanLatest(_store);
            if (recovery is null)
                return;

            var recover = await RecoveryPromptDialog.ShowAsync(
                owner,
                $"FreeW found unsaved changes to \"{recovery.DisplayName}\" from a previous session. Recover them?");

            if (!recover)
            {
                AutosaveRecoveryPlanner.Complete(recovery, accepted: false, recovered: false);
                return; // leave snapshot on disk for later recovery
            }

            var candidate = recovery.Candidate;
            try
            {
                var doc = DocxReader.Read(candidate.SnapshotPath);
                _source.LoadRecoveredSnapshot(doc, candidate.Sidecar.OriginalFilePath);
                AutosaveRecoveryPlanner.Complete(recovery, accepted: true, recovered: true);
            }
            catch
            {
                AutosaveRecoveryPlanner.Complete(recovery, accepted: true, recovered: false);
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
        _coordinator.Dispose();
    }

    // ── Private ──────────────────────────────────────────────────────────────

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(Interval, ct); }
            catch (OperationCanceledException) { break; }

            if (ct.IsCancellationRequested)
                break;

            // Coordinator is best-effort and never throws. Skips when not dirty.
            _coordinator.Snapshot(_source);
        }
    }

    // ── IAutosaveSnapshotSource adapter ─────────────────────────────────────

    private sealed class SnapshotSource : IAutosaveSnapshotSource
    {
        private readonly DocumentView _editor;
        private readonly FileCommandWorkflow _workflow;

        public SnapshotSource(DocumentView editor, FileCommandWorkflow workflow)
        {
            _editor = editor;
            _workflow = workflow;
        }

        public string? OriginalFilePath => _workflow.CurrentPath;
        public string DisplayName => _workflow.DisplayName;
        public bool IsDirty => _workflow.IsDirty;
        public int DirtyGeneration => _workflow.DirtyGeneration;

        public void WriteSnapshot(string snapshotPath)
        {
            // Must read the live document on the UI thread, then write.
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                DocxWriter.Write(_editor.Document, snapshotPath);
            }).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Called by the recovery path (already on the UI thread) to load the recovered
        /// document and mark the workflow dirty so the user is prompted to save or discard.
        /// </summary>
        public void LoadRecoveredSnapshot(FreeW.Core.Model.TextDocument doc, string? originalPath)
        {
            _editor.LoadDocument(doc);
            _workflow.MarkDirtyWithPath(originalPath);
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
