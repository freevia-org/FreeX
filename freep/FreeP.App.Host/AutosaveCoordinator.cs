using System.Windows;
using System.Windows.Threading;
using Free.Shared.AppServices;
using Free.Shared.Shell;
using Free.Shared.Shell.Wpf;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>
/// WPF scheduling and prompt adapter for the renderer-neutral <see cref="FreePAutosaveSession"/>.
/// Mirrors FreeW's <c>AutosaveCoordinator</c>.
/// </summary>
internal sealed partial class AutosaveCoordinator
{
    private readonly PresentationFileCommandSession _file;
    private readonly DispatcherTimer _timer;
    private readonly FreePAutosaveSession _session;
    private readonly Func<AutosaveRecoveryCandidate, bool>? _recoverInNewWindow;

    public AutosaveCoordinator(
        Func<Presentation> getPresentation,
        PresentationFileCommandSession file,
        Func<FreePAutosavePorts, FreePAutosaveSession>? sessionFactory = null,
        Func<AutosaveRecoveryCandidate, bool>? recoverInNewWindow = null)
    {
        ArgumentNullException.ThrowIfNull(getPresentation);
        ArgumentNullException.ThrowIfNull(file);

        _file = file;
        var ports = new FreePAutosavePorts(
            GetOriginalFilePath: () => file.CurrentPath,
            GetDisplayName: () => file.DisplayName,
            GetIsDirty: () => file.IsDirty,
            GetDirtyGeneration: () => file.DirtyGeneration,
            ExecuteWithPresentation: writePresentation => writePresentation(getPresentation()));
        _session = sessionFactory?.Invoke(ports) ?? new FreePAutosaveSession(ports);
        _recoverInNewWindow = recoverInNewWindow;
        _timer = new DispatcherTimer { Interval = FreePAutosaveSession.DefaultInterval };
        _timer.Tick += (_, _) => _session.Snapshot();
    }

    public void Start() => _timer.Start();

    /// <summary>
    /// Best-effort emergency snapshot for the crash handler (see
    /// <see cref="EmergencySnapshotCrashHandler.TryEmergencySnapshotAllWindows"/>). Must never
    /// throw -- delegates to <see cref="FreePAutosaveSession.TryEmergencySnapshot"/>, which is
    /// never-throw by design.
    /// </summary>
    public void TryEmergencySnapshot() => _session.TryEmergencySnapshot();

    public void Stop()
    {
        _timer.Stop();
        _session.CompleteCleanExit();
    }

    /// <summary>
    /// Offers every prior-session snapshot on startup. The first accepted presentation uses this
    /// window; subsequent presentations open through the new-window callback.
    /// </summary>
    public bool OfferRecovery(Window owner)
    {
        var text = AutosaveRecoveryTextCatalog.Resolve(UiText.Get);
        try
        {
            return FreePRecoveryWorkflow.RunAsync(
                    _session.PlanRecoveries(),
                    FreePRecoveryPromptMode.Startup,
                    offer => new ValueTask<bool>(DialogMessageHelper.AskYesNo(
                        owner,
                        offer.Prompt,
                        text.Title)),
                    (recovery, useCurrentWindow) =>
                    {
                        var recovered = _session.CompleteRecovery(
                            recovery,
                            accepted: true,
                            useCurrentWindow
                                ? _file.RestoreAutosaveSnapshot
                                : (_, _) => _recoverInNewWindow?.Invoke(recovery.Candidate) ?? false,
                            FreePRecoveryRestoreExceptionPolicy.QuarantineCandidate);
                        return new ValueTask<bool>(recovered);
                    })
                .GetAwaiter()
                .GetResult()
                .AnyAccepted;
        }
        catch
        {
            // Recovery is best-effort; never block startup on it.
            return false;
        }
    }

    /// <summary>
    /// Manual "Recover Unsaved Presentations" Backstage command. Unlike <see cref="OfferRecovery"/>
    /// (best-effort, silent on failure, used only at startup), this is user-invoked: it must tell the
    /// user when there is nothing to recover, and it must surface failures instead of swallowing
    /// them. Ported from FreeW's <c>AutosaveCoordinator.RecoverUnsavedDocuments</c>.
    /// </summary>
    public bool RecoverUnsavedPresentations(Window owner)
    {
        var text = AutosaveRecoveryTextCatalog.Resolve(UiText.Get);
        try
        {
            var recoveries = _session.PlanRecoveries();
            if (recoveries.Count == 0)
            {
                DialogMessageHelper.ShowInfo(owner,
                    text.NoDocumentsMessage,
                    text.Title);
                return false;
            }

            return FreePRecoveryWorkflow.RunAsync(
                    recoveries,
                    FreePRecoveryPromptMode.Manual,
                    offer => new ValueTask<bool>(DialogMessageHelper.ShowMessage(
                        owner,
                        offer.Prompt,
                        text.Title,
                        UserMessageButtons.OkCancel,
                        UserMessageIcon.Question) == UserMessageResult.Ok),
                    (recovery, useCurrentWindow) => new ValueTask<bool>(_session.CompleteRecovery(
                        recovery,
                        accepted: true,
                        useCurrentWindow
                            ? _file.RestoreAutosaveSnapshot
                            : (_, _) => _recoverInNewWindow?.Invoke(recovery.Candidate) ?? false)))
                .GetAwaiter()
                .GetResult()
                .AnyRecovered;
        }
        catch (Exception ex)
        {
            DialogMessageHelper.ShowError(owner,
                string.Format(System.Globalization.CultureInfo.CurrentCulture, text.FailureMessageFormat, ex.Message),
                text.Title);
            return false;
        }
    }
}
