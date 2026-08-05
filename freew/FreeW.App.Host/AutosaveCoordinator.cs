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

    private readonly AutosaveSnapshotStore _store =
        AutosaveSnapshotStore.CreateDefault(PlatformApplicationDataPathProvider.LocalInstance);
    private readonly FileCommands _file;
    private readonly DispatcherTimer _timer;
    private readonly AutosaveSnapshotCoordinator _coordinator;
    private readonly DocumentSnapshotSource _source;

    public AutosaveCoordinator(DocumentView editor, FileCommands file)
    {
        _file = file;
        _source = new DocumentSnapshotSource(editor, file);
        _coordinator = new AutosaveSnapshotCoordinator(_store, AutosaveSnapshotStore.LaunchId.ToString("N"));
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += (_, _) => _coordinator.Snapshot(_source);
    }

    public void Start() => _timer.Start();

    public void Stop()
    {
        _timer.Stop();
        try { _coordinator.DeleteSnapshot(); } catch { /* best-effort cleanup */ }
    }

    /// <summary>
    /// If a snapshot survives from a previous session, offer to recover it. Only the single latest
    /// candidate is offered; other candidates are left intact for a later "Recover Unsaved Documents"
    /// invocation. The offered candidate is deleted only after a successful recovery (user accepted
    /// and the file loaded). A declined prompt leaves the candidate on disk so the user can revisit it.
    /// </summary>
    public void OfferRecovery(Window owner)
    {
        try
        {
            var recovery = AutosaveRecoveryPlanner.PlanLatest(_store);
            if (recovery is null)
                return;

            var recover = DialogMessageHelper.AskYesNo(owner,
                $"FreeW found unsaved changes to {recovery.DisplayName} from a previous session. Recover them?",
                "FreeW - Recover");

            if (!recover)
            {
                // Leave the candidate intact for a later manual recovery or startup prompt.
                AutosaveRecoveryPlanner.Complete(recovery, accepted: false, recovered: false);
                return;
            }

            // Open the snapshot; delete it on success. On failure the snapshot is structurally
            // unreadable (e.g. a truncated ZIP from a crashed write) — quarantine it so it is not
            // re-offered on every launch, which otherwise loops the "Could not recover" error.
            var candidate = recovery.Candidate;
            var loaded = _file.OpenSnapshot(candidate.SnapshotPath, candidate.Sidecar.OriginalFilePath);
            AutosaveRecoveryPlanner.Complete(recovery, accepted: true, recovered: loaded);
        }
        catch
        {
            // Recovery is best-effort; never block startup on it.
        }
    }

    public bool RecoverUnsavedDocuments(Window owner)
    {
        try
        {
            var recovery = AutosaveRecoveryPlanner.PlanLatest(_store);
            if (recovery is null)
            {
                DialogMessageHelper.ShowInfo(owner,
                    "No unsaved documents were found.",
                    "FreeW - Recover");
                return false;
            }

            var answer = DialogMessageHelper.ShowMessage(owner,
                $"Recover unsaved changes to {recovery.DisplayName}?",
                "FreeW - Recover",
                UserMessageButtons.OkCancel,
                UserMessageIcon.Question);
            if (answer != UserMessageResult.Ok)
            {
                AutosaveRecoveryPlanner.Complete(recovery, accepted: false, recovered: false);
                return false;
            }

            var candidate = recovery.Candidate;
            var recovered = _file.RecoverSnapshot(candidate.SnapshotPath, candidate.Sidecar.OriginalFilePath);
            AutosaveRecoveryPlanner.Complete(recovery, accepted: true, recovered: recovered);

            return recovered;
        }
        catch (Exception ex)
        {
            DialogMessageHelper.ShowError(owner,
                $"Could not recover the document.\n\n{ex.Message}",
                "FreeW - Recover");
            return false;
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
