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
    /// <remarks>
    /// startup-fileopen F2 (WPF host): mirrors the fix already applied to FreeP's Avalonia
    /// <c>AutosaveAdapter.OfferRecoveryAsync</c>. <c>MainWindow</c> opens a command-line/file-
    /// association document into this window synchronously before this offer runs, so routing the
    /// first accepted candidate into "the current window" unconditionally would silently replace
    /// that just-opened, not-yet-dirty presentation -- the dirty-based save/discard gate never fires
    /// because the document isn't dirty. We snapshot whether the window already holds an explicitly
    /// opened document (<see cref="_file"/>.<c>CurrentPath</c> non-null) BEFORE any candidate is
    /// applied, and if so force every accepted candidate through the new-window path instead, same
    /// as every candidate beyond the first. A genuinely fresh window (no startup file) keeps the
    /// prior unconditional behaviour.
    /// </remarks>
    public bool OfferRecovery(Window owner)
    {
        var text = AutosaveRecoveryTextCatalog.Resolve(UiText.Get);
        try
        {
            var currentWindowHasExplicitDocument = _file.CurrentPath is not null;

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
                            useCurrentWindow && !currentWindowHasExplicitDocument
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
                    (recovery, useCurrentWindow) =>
                    {
                        // r146: the manual command can be invoked at any time, not just on a fresh
                        // startup window -- unlike OfferRecovery, the "current window" it targets may
                        // already hold unsaved edits. Route the destructive replace through the same
                        // dirty gate every other destructive file command uses (New/Open/Close) so the
                        // user is asked to save/discard/cancel BEFORE their own unsaved work is
                        // overwritten by the recovered snapshot. Mirrors FreeW's
                        // AutosaveCoordinator.RecoverUnsavedDocuments -> FileCommands.RecoverSnapshot,
                        // which wraps the same restore through FileCommandWorkflow.Open's
                        // ConfirmDiscardOrSave gate. A no-op when the current window isn't dirty.
                        if (useCurrentWindow &&
                            !_file.ConfirmCloseAllowedAsync("recovering an unsaved presentation")
                                .GetAwaiter()
                                .GetResult())
                        {
                            // Declined: leave the candidate on disk (accepted:false -> Keep
                            // disposition) so the user can revisit it later, same as declining the
                            // initial "Recover unsaved changes to X?" offer above.
                            return new ValueTask<bool>(_session.CompleteRecovery(
                                recovery,
                                accepted: false,
                                _file.RestoreAutosaveSnapshot));
                        }

                        return new ValueTask<bool>(_session.CompleteRecovery(
                            recovery,
                            accepted: true,
                            useCurrentWindow
                                ? _file.RestoreAutosaveSnapshot
                                : (_, _) => _recoverInNewWindow?.Invoke(recovery.Candidate) ?? false));
                    })
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
