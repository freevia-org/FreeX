using System;
using System.Windows;
using System.Windows.Threading;
using Free.Shared.AppServices;
using Free.Shared.Shell;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;

namespace FreeW.App.Host;

/// <summary>
/// FreeW autosave + crash recovery, reusing the shared <see cref="AutosaveSnapshotStore"/> (which
/// places snapshots under FreeW's own Recovery folder via AppProduct) and the shared
/// <see cref="AutosaveSnapshotCoordinator"/> for the neutral snapshot/sidecar/delete orchestration.
/// Every interval, if the document is dirty, it writes a .docx snapshot + sidecar. On startup it
/// offers to recover any snapshot left over from a previous (crashed) session; on a clean exit it
/// removes its own.
/// </summary>
internal sealed class AutosaveCoordinator
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly AutosaveSnapshotStore _store;
    private readonly FileCommands _file;
    private readonly DispatcherTimer _timer;
    private readonly AutosaveSnapshotCoordinator _coordinator;
    private readonly DocumentSnapshotSource _source;
    private readonly Func<AutosaveRecoveryCandidate, bool>? _recoverInNewWindow;

    // Unique per MainWindow instance (Feature 5's "New Window" — see MainWindow.OpenNewWindow —
    // and mail-merge report windows can all be open in the SAME process at once). The snapshot ID
    // used to be keyed on AutosaveSnapshotStore.LaunchId alone, which is a per-PROCESS static Guid:
    // every window in the same process therefore wrote to the exact same snapshot file, silently
    // overwriting each other's crash-recovery data, and one window's clean-close Stop() would
    // delete the snapshot the OTHER window was still relying on for recovery. Folding in a fresh
    // per-instance Guid (mirrors FreeX's MainWindow._autosaveWindowId in
    // src/FreeX.App.Host/MainWindow.Autosave.cs) gives each window its own snapshot slot, so
    // writes and cleanup are scoped to this instance only.
    private readonly Guid _windowId = Guid.NewGuid();

    /// <summary>
    /// The <paramref name="store"/> parameter exists so tests can point two coordinators at an
    /// isolated temp directory instead of the real per-user Recovery folder (production always
    /// passes null, which resolves to <see cref="AutosaveSnapshotStore.CreateDefault"/> exactly as
    /// before this parameter was added). <paramref name="recoverInNewWindow"/> is how
    /// <see cref="OfferRecovery"/>/<see cref="RecoverUnsavedDocuments"/> hand off every accepted
    /// candidate beyond the first: the first restores into the window that owns this coordinator,
    /// every additional one is opened in a brand-new window via this callback (mirrors
    /// <c>MainWindow.OpenNewWindow</c>'s window-creation pattern) so accepting more than one
    /// pending snapshot never overwrites an already-recovered document. Null (the default, used by
    /// tests that don't need it) means extra candidates are simply left on disk, unrecovered.
    /// </summary>
    public AutosaveCoordinator(
        DocumentView editor,
        FileCommands file,
        AutosaveSnapshotStore? store = null,
        Func<AutosaveRecoveryCandidate, bool>? recoverInNewWindow = null)
    {
        _file = file;
        _store = store ?? AutosaveSnapshotStore.CreateDefault(PlatformApplicationDataPathProvider.LocalInstance);
        _source = new DocumentSnapshotSource(editor, file);
        _recoverInNewWindow = recoverInNewWindow;
        var launchTag = AutosaveSnapshotStore.LaunchId.ToString("N")[..8];
        var windowTag = _windowId.ToString("N")[..8];
        _coordinator = new AutosaveSnapshotCoordinator(
            _store,
            FormattableString.Invariant($"recovery-{Environment.ProcessId}-{launchTag}-{windowTag}"));
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += (_, _) => _coordinator.Snapshot(_source);
    }

    /// <summary>Test seam (FreeW.App.Host.Tests has InternalsVisibleTo). Exposes the snapshot ID this
    /// instance resolved to, and lets a test drive/inspect a snapshot write without waiting on the timer.</summary>
    internal string SnapshotIdForTests => _coordinator.SnapshotId;
    internal void SnapshotNowForTests() => _coordinator.Snapshot(_source);

    /// <summary>Test seam: simulates this window's process having crashed — releases the
    /// Round134-remediation liveness lock (exactly what the OS does automatically on a real
    /// process exit) while deliberately leaving the already-written snapshot + sidecar files on
    /// disk, so a test can build an orphaned-but-real recovery candidate without an actual crash.
    /// Does NOT call <see cref="Stop"/>/DeleteSnapshot — those would delete the very files a real
    /// crash leaves behind.</summary>
    internal void SimulateCrashForTests() => _coordinator.Dispose();

    public void Start() => _timer.Start();

    public void Stop()
    {
        _timer.Stop();
        try { _coordinator.DeleteSnapshot(); } catch { /* best-effort cleanup */ }
        // Releases this window's liveness lock (Round134-remediation) deterministically on close,
        // rather than leaving it to whenever the GC finalizes the underlying handle — see
        // AutosaveSnapshotCoordinator.Dispose / ReleaseOwnershipLock.
        try { _coordinator.Dispose(); } catch { /* best-effort cleanup */ }
    }

    /// <summary>
    /// If snapshots survive from a previous session, offer to recover ALL of them, one prompt at a
    /// time (R133-remediation: previously only the single latest candidate was ever offered, so a
    /// crash with two or more windows open left every window but one orphaned on disk). The first
    /// accepted candidate restores into <paramref name="owner"/>; every additional accepted
    /// candidate opens its own new window via the constructor's <c>recoverInNewWindow</c> callback,
    /// so accepting more than one never overwrites an already-recovered document. Each candidate is
    /// deleted only after a successful recovery (accepted and the file loaded); a declined prompt
    /// leaves that candidate on disk so the user can revisit it later.
    /// </summary>
    public bool OfferRecovery(Window owner)
    {
        try
        {
            // Round134-remediation: a snapshot still owned by a currently-open window (this
            // process or another) must never be listed here — offering it risks the user
            // "recovering" a stale copy while the live window keeps editing, and accepting it
            // would delete the live window's own snapshot out from under it. See
            // AutosaveSnapshotStore.ExcludeLiveOwned.
            var candidates = AutosaveRecoveryPlanner.SelectAllOrdered(
                _store.ExcludeLiveOwned(_store.EnumerateCandidates()));
            if (candidates.Count == 0)
                return false;

            var anyAccepted = false;

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var name = AutosaveRecoveryPlanner.DisplayName(candidate);
                var remaining = candidates.Count - i;
                var prompt = remaining > 1
                    ? $"FreeW found unsaved changes to {name} from a previous session ({remaining} unsaved documents found). Recover this one?"
                    : $"FreeW found unsaved changes to {name} from a previous session. Recover them?";

                var recover = DialogMessageHelper.AskYesNo(owner, prompt, "FreeW - Recover");
                if (!recover)
                {
                    // On decline ("No"): leave the candidate intact so it remains available for
                    // "Recover Unsaved Documents" or the next startup prompt. Keep asking about
                    // any remaining candidates rather than stopping at the first decline.
                    continue;
                }

                // The first accepted candidate restores into the window that is already open; every
                // later accepted candidate gets its own new window (mirrors FreeX's WPF host —
                // App.xaml.cs's OfferStartupRecovery — which does the same "first into the existing
                // window, rest into new windows" split).
                var isFirstAccepted = !anyAccepted;
                anyAccepted = true;

                // Open the snapshot; delete it on success. On failure the snapshot is structurally
                // unreadable (e.g. a truncated ZIP from a crashed write) — quarantine it so it is not
                // re-offered on every launch, which otherwise loops the "Could not recover" error.
                var loaded = isFirstAccepted
                    ? _file.OpenSnapshot(candidate.SnapshotPath, candidate.Sidecar.OriginalFilePath)
                    : _recoverInNewWindow?.Invoke(candidate) ?? false;

                ApplyRecoveryDisposition(candidate,
                    AutosaveRecoveryPlanner.ResolveDisposition(accepted: true, recovered: loaded));
            }

            return anyAccepted;
        }
        catch
        {
            // Recovery is best-effort; never block startup on it.
            return false;
        }
    }

    public bool RecoverUnsavedDocuments(Window owner)
    {
        try
        {
            // Round134-remediation: same live-ownership exclusion as OfferRecovery above — the
            // menu-driven "Recover Unsaved Documents" command must never list or delete another
            // still-open window's snapshot either.
            var candidates = AutosaveRecoveryPlanner.SelectAllOrdered(
                _store.ExcludeLiveOwned(_store.EnumerateCandidates()));
            if (candidates.Count == 0)
            {
                DialogMessageHelper.ShowInfo(owner,
                    "No unsaved documents were found.",
                    "FreeW - Recover");
                return false;
            }

            var anyAccepted = false;
            var anyRecovered = false;

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var name = AutosaveRecoveryPlanner.DisplayName(candidate);
                var remaining = candidates.Count - i;
                var prompt = remaining > 1
                    ? $"Recover unsaved changes to {name}? ({remaining} unsaved documents found.)"
                    : $"Recover unsaved changes to {name}?";

                var answer = DialogMessageHelper.ShowMessage(owner, prompt, "FreeW - Recover",
                    UserMessageButtons.OkCancel, UserMessageIcon.Question);
                if (answer != UserMessageResult.Ok)
                    continue; // leave this candidate intact; keep offering the rest

                try
                {
                    // The first accepted candidate restores into the window the command was invoked
                    // from (via the save-before-replace gated RecoverSnapshot); every later accepted
                    // candidate opens its own new window, same split as OfferRecovery above.
                    var isFirstAccepted = !anyAccepted;
                    anyAccepted = true;
                    var recovered = isFirstAccepted
                        ? _file.RecoverSnapshot(candidate.SnapshotPath, candidate.Sidecar.OriginalFilePath)
                        : _recoverInNewWindow?.Invoke(candidate) ?? false;

                    if (recovered)
                        anyRecovered = true;

                    ApplyRecoveryDisposition(candidate,
                        AutosaveRecoveryPlanner.ResolveDisposition(accepted: true, recovered: recovered));
                }
                catch (Exception ex)
                {
                    // A failure recovering one candidate must not lose the ability to recover the
                    // others still pending in this same invocation.
                    DialogMessageHelper.ShowError(owner,
                        $"Could not recover the document.\n\n{ex.Message}",
                        "FreeW - Recover");
                }
            }

            return anyRecovered;
        }
        catch (Exception ex)
        {
            DialogMessageHelper.ShowError(owner,
                $"Could not recover the document.\n\n{ex.Message}",
                "FreeW - Recover");
            return false;
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

    /// <summary>
    /// Adapts the FreeW editor/document state to the neutral <see cref="IAutosaveSnapshotSource"/>,
    /// serializing the live document to a .docx via <see cref="DocxWriter"/>. The dirty generation
    /// comes from the shared document state, so the engine re-snapshots whenever a new edit lands.
    /// </summary>
    private sealed class DocumentSnapshotSource : IAutosaveSnapshotSource
    {
        private readonly DocumentView _editor;
        private readonly FileCommands _file;

        public DocumentSnapshotSource(DocumentView editor, FileCommands file)
        {
            _editor = editor;
            _file = file;
        }

        public string? OriginalFilePath => _file.CurrentPath;
        public string DisplayName => _file.DisplayName;
        public bool IsDirty => _file.IsDirty;
        public int DirtyGeneration => _file.DirtyGeneration;

        public void WriteSnapshot(string snapshotPath)
        {
            _editor.CommitToModel();
            DocxWriter.Write(_editor.Model, snapshotPath);
        }
    }
}
