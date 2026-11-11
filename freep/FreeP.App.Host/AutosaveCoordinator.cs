using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.Shell.Wpf;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>
/// FreeP's binding of the shared <see cref="WpfAutosaveHost{TDocument}"/> (dispatcher timer,
/// clean-exit stop, emergency snapshot, startup/manual recovery). Mirrors FreeW's
/// <c>AutosaveCoordinator</c>; only the document ports, the restore delegate, and the
/// new-window / dirty-gate callbacks are FreeP-specific.
/// </summary>
internal sealed partial class AutosaveCoordinator
{
    private readonly WpfAutosaveHost<Presentation> _host;

    public AutosaveCoordinator(
        Func<Presentation> getPresentation,
        PresentationFileCommandSession file,
        Func<AutosaveDocumentPorts<Presentation>, FreePAutosaveSession>? sessionFactory = null,
        Func<AutosaveRecoveryCandidate, bool>? recoverInNewWindow = null)
    {
        ArgumentNullException.ThrowIfNull(getPresentation);
        ArgumentNullException.ThrowIfNull(file);

        var ports = new AutosaveDocumentPorts<Presentation>(
            GetOriginalFilePath: () => file.CurrentPath,
            GetDisplayName: () => file.DisplayName,
            GetIsDirty: () => file.IsDirty,
            GetDirtyGeneration: () => file.DirtyGeneration,
            ExecuteWithDocument: writePresentation => writePresentation(getPresentation()));

        _host = new WpfAutosaveHost<Presentation>(
            FreePAutosaveProfile.Session,
            ports,
            new WpfAutosaveHostCallbacks<Presentation>(
                // startup-fileopen F2 (WPF host): MainWindow opens a command-line/file-association
                // document into this window synchronously before the startup offer runs, so it is
                // not dirty and must not be silently replaced by the first accepted candidate.
                CurrentWindowHasExplicitDocument: () => file.CurrentPath is not null,
                RestoreIntoCurrentWindow: AutosaveRecoveryRestore<Presentation>.FromSnapshotPath(
                    file.RestoreAutosaveSnapshot),
                // The manual command may target a window with unrelated unsaved edits. Run the
                // product's asynchronous dirty gate before accepting the recovery candidate.
                ConfirmRecoveryIntoCurrentWindow: () =>
                    file.ConfirmCloseAllowedAsync("recovering an unsaved presentation")
                        .GetAwaiter()
                        .GetResult(),
                RecoverInNewWindow: recoverInNewWindow),
            UiText.Get,
            sessionFactory: sessionPorts => sessionFactory?.Invoke(sessionPorts) ?? new FreePAutosaveSession(sessionPorts));
    }

    public void Start() => _host.Start();

    /// <summary>
    /// Best-effort emergency snapshot for the crash handler (see
    /// <see cref="EmergencySnapshotCrashHandler.TryEmergencySnapshotAllWindows"/>). Must never throw.
    /// </summary>
    public void TryEmergencySnapshot() => _host.TryEmergencySnapshot();

    public void Stop() => _host.Stop();

    /// <summary>
    /// Offers every prior-session snapshot on startup. The first accepted presentation uses this
    /// window; subsequent presentations open through the new-window callback.
    /// </summary>
    public bool OfferRecovery(Window owner) => _host.OfferRecovery(owner);

    /// <summary>
    /// Manual "Recover Unsaved Presentations" Backstage command: user-invoked, so it tells the user
    /// when there is nothing to recover and surfaces failures instead of swallowing them.
    /// </summary>
    public bool RecoverUnsavedPresentations(Window owner) => _host.RecoverManually(owner);
}
