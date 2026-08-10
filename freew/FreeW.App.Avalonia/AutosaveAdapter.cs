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
    private readonly Func<AutosaveRecoveryCandidate, Task<bool>>? _recoverInNewWindowAsync;
    private CancellationTokenSource? _cts;

    // Unique per MainWindow instance — see FreeW.App.Host.AutosaveCoordinator's matching field for
    // why: the snapshot ID used to be keyed on the per-PROCESS AutosaveSnapshotStore.LaunchId alone,
    // so multiple MainWindow instances in the same process (FreeW.App.Avalonia.MainWindow supports
    // "New Window" / report windows, same as the WPF host) shared one snapshot slot and could
    // overwrite or delete each other's crash-recovery data.
    private readonly Guid _windowId = Guid.NewGuid();

    /// <summary>
    /// The <paramref name="store"/> parameter exists so tests can point two adapters at an
    /// isolated temp directory instead of the real per-user Recovery folder (production always
    /// passes null, which resolves to <see cref="AutosaveSnapshotStore.CreateDefault"/> exactly as
    /// before this parameter was added). <paramref name="recoverInNewWindowAsync"/> is how
    /// <see cref="OfferRecoveryAsync"/> hands off every accepted candidate beyond the first: the
    /// first restores into the window that owns this adapter, every additional one is opened in a
    /// brand-new window via this callback (mirrors <c>MainWindow.OpenNewWindow</c>'s
    /// window-creation pattern) so accepting more than one pending snapshot never overwrites an
    /// already-recovered document. Null (the default, used by tests that don't need it) means extra
    /// candidates are simply left on disk, unrecovered.
    /// </summary>
    public AutosaveAdapter(
        DocumentView editor,
        FileCommandWorkflow workflow,
        AutosaveSnapshotStore? store = null,
        Func<AutosaveRecoveryCandidate, Task<bool>>? recoverInNewWindowAsync = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(workflow);

        _store = store ?? AutosaveSnapshotStore.CreateDefault(PlatformApplicationDataPathProvider.LocalInstance);
        var launchTag = AutosaveSnapshotStore.LaunchId.ToString("N")[..8];
        var windowTag = _windowId.ToString("N")[..8];
        _coordinator = new AutosaveSnapshotCoordinator(
            _store,
            FormattableString.Invariant($"recovery-{Environment.ProcessId}-{launchTag}-{windowTag}"));
        _source = new SnapshotSource(editor, workflow);
        _recoverInNewWindowAsync = recoverInNewWindowAsync;
    }

    /// <summary>Test seam (FreeW.App.Avalonia.Tests has InternalsVisibleTo). Exposes the snapshot ID
    /// this instance resolved to, and lets a test drive a snapshot write without waiting on the loop.</summary>
    internal string SnapshotIdForTests => _coordinator.SnapshotId;
    internal void SnapshotNowForTests() => _coordinator.Snapshot(_source);

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
    /// If snapshots survive from a previous session, offer to recover ALL of them, one prompt at a
    /// time (R133-remediation: previously only the single latest candidate was ever offered, so a
    /// crash with two or more windows open left every window but one orphaned on disk). The first
    /// accepted candidate restores into <paramref name="owner"/>; every additional accepted
    /// candidate opens its own new window via the constructor's <c>recoverInNewWindowAsync</c>
    /// callback, so accepting more than one never overwrites an already-recovered document.
    /// Must be called from the UI thread (it may show an Avalonia dialog).
    /// Errors are swallowed — recovery is best-effort and never blocks startup.
    /// </summary>
    public async Task OfferRecoveryAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        try
        {
            var candidates = AutosaveRecoveryPlanner.SelectAllOrdered(_store.EnumerateCandidates());
            if (candidates.Count == 0)
                return;

            var anyAccepted = false;

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var name = AutosaveRecoveryPlanner.DisplayName(candidate);
                var remaining = candidates.Count - i;
                var prompt = remaining > 1
                    ? $"FreeW found unsaved changes to \"{name}\" from a previous session ({remaining} unsaved documents found). Recover this one?"
                    : $"FreeW found unsaved changes to \"{name}\" from a previous session. Recover them?";

                var recover = await RecoveryPromptDialog.ShowAsync(owner, prompt);
                if (!recover)
                {
                    // Leave the candidate on disk for later recovery; keep asking about any
                    // remaining candidates rather than stopping at the first decline.
                    continue;
                }

                // The first accepted candidate restores into the window that is already open; every
                // later accepted candidate gets its own new window (mirrors the WPF host's
                // AutosaveCoordinator.OfferRecovery, which does the same split).
                var isFirstAccepted = !anyAccepted;
                anyAccepted = true;

                bool recovered;
                if (isFirstAccepted)
                {
                    try
                    {
                        var doc = DocxReader.Read(candidate.SnapshotPath);
                        _source.LoadRecoveredSnapshot(doc, candidate.Sidecar.OriginalFilePath);
                        recovered = true;
                    }
                    catch
                    {
                        recovered = false;
                    }
                }
                else
                {
                    recovered = _recoverInNewWindowAsync is null
                        ? false
                        : await _recoverInNewWindowAsync(candidate);
                }

                ApplyRecoveryDisposition(candidate,
                    AutosaveRecoveryPlanner.ResolveDisposition(accepted: true, recovered: recovered));
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

    private static void ApplyRecoveryDisposition(
        AutosaveRecoveryCandidate candidate,
        AutosaveRecoveryDisposition disposition)
    {
        switch (disposition)
        {
            case AutosaveRecoveryDisposition.Delete:
                AutosaveSnapshotStore.DeleteCandidate(candidate);
                break;
            case AutosaveRecoveryDisposition.Quarantine:
                AutosaveSnapshotStore.QuarantineCandidate(candidate);
                break;
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

    /// <summary>
    /// Test/headless seam mirroring the WPF host's <c>Free.Shared.Shell.HeadlessMessageBox.Handler</c>:
    /// when set, <see cref="ShowAsync"/> returns this answer instead of constructing and showing a
    /// real modal dialog window (driving a real Avalonia dialog to completion is unsupported/flaky
    /// under the headless test platform used by this assembly's tests). Null (the default) shows the
    /// real dialog exactly as before this seam was added.
    /// </summary>
    public static Func<string, bool>? TestResponder { get; set; }

    /// <summary>Show the prompt and return true if the user chose to recover.</summary>
    public static Task<bool> ShowAsync(Window owner, string message) =>
        TestResponder is { } responder
            ? Task.FromResult(responder(message))
            : new RecoveryPromptDialog(message).ShowDialog<bool>(owner);
}
