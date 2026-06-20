using System;
using System.Windows;
using System.Windows.Threading;
using Free.Shared.AppServices;
using FreeW.App.Host.Editing;
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
