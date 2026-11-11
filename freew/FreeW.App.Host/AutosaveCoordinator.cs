using System;
using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.Shell.Wpf;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Shell;
using TextDocument = FreeW.Core.Model.TextDocument;

namespace FreeW.App.Host;

/// <summary>
/// FreeW's binding of the shared <see cref="WpfAutosaveHost{TDocument}"/> (dispatcher timer,
/// clean-exit stop, emergency snapshot, startup/manual recovery). Only the document ports, the
/// guarded restore, and the new-window / dirty-gate callbacks are FreeW-specific.
/// </summary>
internal sealed partial class AutosaveCoordinator
{
    private readonly WpfAutosaveHost<TextDocument> _host;

    public AutosaveCoordinator(
        DocumentView editor,
        FileCommands file,
        Func<AutosaveDocumentPorts<TextDocument>, FreeWAutosaveSession>? sessionFactory = null,
        Func<AutosaveRecoveryCandidate, bool>? recoverInNewWindow = null)
    {
        var ports = new AutosaveDocumentPorts<TextDocument>(
            GetOriginalFilePath: () => file.CurrentPath,
            GetDisplayName: () => file.DisplayName,
            GetIsDirty: () => file.IsDirty,
            GetDirtyGeneration: () => file.DirtyGeneration,
            ExecuteWithDocument: writeDocument =>
            {
                editor.CommitToModel();
                // Same guard as FileCommands' GetDocument port: while Mailings > Preview Results is active,
                // editor.Model is the merged, single-record preview document, not the template. Autosave
                // snapshots (and the crash-recovery snapshot that reuses this same port via
                // AutosaveCoordinator.TryEmergencySnapshot) must persist the real template so an
                // untimely autosave/crash doesn't leave the recovered document with its merge fields baked
                // away.
                writeDocument(editor.MailMergeSession?.Template ?? editor.Model);
            });

        _host = new WpfAutosaveHost<TextDocument>(
            FreeWAutosaveProfile.Session,
            ports,
            new WpfAutosaveHostCallbacks<TextDocument>(
                // startup-fileopen F2 (WPF host): a command-line/file-association document opened into
                // this window before the startup offer runs is not dirty, so it must not be replaced.
                CurrentWindowHasExplicitDocument: () => file.CurrentPath is not null,
                RestoreIntoCurrentWindow: AutosaveRecoveryRestore<TextDocument>.FromSnapshotPath(
                    file.OpenSnapshot),
                // The manual command may target a window with unrelated unsaved edits. Run the
                // product's synchronous dirty gate before accepting the recovery candidate.
                ConfirmRecoveryIntoCurrentWindow: () =>
                    file.ConfirmCloseAllowed("recovering an unsaved document"),
                RecoverInNewWindow: recoverInNewWindow),
            UiText.Get,
            sessionFactory: sessionPorts => sessionFactory?.Invoke(sessionPorts) ?? new FreeWAutosaveSession(sessionPorts));
    }

    public void Start() => _host.Start();

    /// <summary>
    /// Best-effort emergency snapshot for the crash handler (see Program.cs's
    /// TryEmergencySnapshotAllWindows). Must never throw.
    /// </summary>
    public void TryEmergencySnapshot() => _host.TryEmergencySnapshot();

    public void Stop() => _host.Stop();

    /// <summary>
    /// Offers every prior-session snapshot on startup. The first accepted document uses this window;
    /// subsequent documents open through the new-window callback.
    /// </summary>
    public bool OfferRecovery(Window owner) => _host.OfferRecovery(owner);

    public bool RecoverUnsavedDocuments(Window owner) => _host.RecoverManually(owner);
}
