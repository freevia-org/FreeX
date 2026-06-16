using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Free.Shared.AppServices;
using FreeW.App.Host.Editing;
using FreeW.Core.IO;

namespace FreeW.App.Host;

/// <summary>
/// FreeW autosave + crash recovery, reusing the shared <see cref="AutosaveSnapshotStore"/> (which
/// places snapshots under FreeW's own Recovery folder via AppProduct). Every interval, if the
/// document is dirty, it writes a .docx snapshot + sidecar. On startup it offers to recover any
/// snapshot left over from a previous (crashed) session; on a clean exit it removes its own.
/// </summary>
internal sealed class AutosaveCoordinator
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly AutosaveSnapshotStore _store =
        AutosaveSnapshotStore.CreateDefault(PlatformApplicationDataPathProvider.LocalInstance);
    private readonly DocumentView _editor;
    private readonly FileCommands _file;
    private readonly DispatcherTimer _timer;
    private readonly string _snapshotId = AutosaveSnapshotStore.LaunchId.ToString("N");

    public AutosaveCoordinator(DocumentView editor, FileCommands file)
    {
        _editor = editor;
        _file = file;
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += (_, _) => Snapshot();
    }

    public void Start() => _timer.Start();

    public void Stop()
    {
        _timer.Stop();
        try { _store.DeleteSnapshot(_snapshotId); } catch { /* best-effort cleanup */ }
    }

    /// <summary>If a snapshot survives from a previous session, offer to recover it.</summary>
    public void OfferRecovery(Window owner)
    {
        try
        {
            var candidates = _store.EnumerateCandidates();
            if (candidates.Count == 0)
                return;

            var candidate = candidates[0];
            var name = string.IsNullOrEmpty(candidate.Sidecar.DisplayName) ? "a document" : candidate.Sidecar.DisplayName;
            var answer = MessageBox.Show(owner,
                $"FreeW found unsaved changes to {name} from a previous session. Recover them?",
                "FreeW — Recover", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (answer == MessageBoxResult.Yes)
                _file.OpenSnapshot(candidate.SnapshotPath, candidate.Sidecar.OriginalFilePath);

            foreach (var stale in candidates)
                AutosaveSnapshotStore.DeleteCandidate(stale);
        }
        catch
        {
            // Recovery is best-effort; never block startup on it.
        }
    }

    private void Snapshot()
    {
        if (!_file.IsDirty)
            return;
        try
        {
            _editor.CommitToModel();
            DocxWriter.Write(_editor.Model, _store.GetSnapshotPath(_snapshotId));
            var sidecar = new AutosaveSidecar
            {
                OriginalFilePath = _file.CurrentPath,
                DisplayName = _file.DisplayName,
                TimestampUtc = DateTime.UtcNow.ToString("o"),
                SnapshotId = _snapshotId
            };
            File.WriteAllText(_store.GetSidecarPath(_snapshotId), AutosaveSnapshotStore.SerializeSidecar(sidecar));
        }
        catch
        {
            // Autosave is best-effort; a failed snapshot must never disrupt editing.
        }
    }
}
