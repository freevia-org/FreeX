using System;
using System.Windows;
using System.Windows.Threading;
using Free.Shared.AppServices;
using Free.Shared.Shell;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Host;

/// <summary>
/// WPF scheduling and prompt adapter for the renderer-neutral <see cref="FreeWAutosaveSession"/>.
/// </summary>
internal sealed partial class AutosaveCoordinator
{
    private readonly FileCommands _file;
    private readonly DispatcherTimer _timer;
    private readonly FreeWAutosaveSession _session;
    private readonly Func<AutosaveRecoveryCandidate, bool>? _recoverInNewWindow;

    public AutosaveCoordinator(
        DocumentView editor,
        FileCommands file,
        Func<FreeWAutosavePorts, FreeWAutosaveSession>? sessionFactory = null,
        Func<AutosaveRecoveryCandidate, bool>? recoverInNewWindow = null)
    {
        _file = file;
        var ports = new FreeWAutosavePorts(
            GetOriginalFilePath: () => file.CurrentPath,
            GetDisplayName: () => file.DisplayName,
            GetIsDirty: () => file.IsDirty,
            GetDirtyGeneration: () => file.DirtyGeneration,
            ExecuteWithDocument: writeDocument =>
            {
                editor.CommitToModel();
                writeDocument(editor.Model);
            });
        _session = sessionFactory?.Invoke(ports) ?? new FreeWAutosaveSession(ports);
        _recoverInNewWindow = recoverInNewWindow;
        _timer = new DispatcherTimer { Interval = FreeWAutosaveSession.DefaultInterval };
        _timer.Tick += (_, _) => _session.Snapshot();
    }

    public void Start() => _timer.Start();

    /// <summary>
    /// Best-effort emergency snapshot for the crash handler (see Program.cs's
    /// TryEmergencySnapshotAllWindows). Must never throw -- delegates to
    /// <see cref="FreeWAutosaveSession.TryEmergencySnapshot"/>, which is never-throw by design.
    /// </summary>
    public void TryEmergencySnapshot() => _session.TryEmergencySnapshot();

    public void Stop()
    {
        _timer.Stop();
        _session.CompleteCleanExit();
    }

    /// <summary>
    /// Offers every prior-session snapshot on startup. The first accepted document uses this window;
    /// subsequent documents open through the new-window callback.
    /// </summary>
    /// <remarks>
    /// startup-fileopen F2 (WPF host): mirrors the fix already applied to FreeP's Avalonia
    /// <c>AutosaveAdapter.OfferRecoveryAsync</c>. A command-line/file-association document may
    /// already be loaded into this window before this offer runs, not yet dirty, so routing the
    /// first accepted candidate into "the current window" unconditionally would silently replace it.
    /// We snapshot whether the window already holds an explicitly opened document (<see
    /// cref="_file"/>.<c>CurrentPath</c> non-null) BEFORE any candidate is applied, and if so force
    /// every accepted candidate through the new-window path instead, same as every candidate beyond
    /// the first. A genuinely fresh window (no startup file) keeps the prior unconditional behaviour.
    /// </remarks>
    public bool OfferRecovery(Window owner)
    {
        var text = AutosaveRecoveryTextCatalog.Resolve(UiText.Get);
        try
        {
            var currentWindowHasExplicitDocument = _file.CurrentPath is not null;

            return FreeWRecoveryWorkflow.RunAsync(
                    _session.PlanRecoveries(),
                    FreeWRecoveryPromptMode.Startup,
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
                                ? _file.OpenSnapshot
                                : (_, _) => _recoverInNewWindow?.Invoke(recovery.Candidate) ?? false,
                            FreeWRecoveryRestoreExceptionPolicy.QuarantineCandidate);
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

    public bool RecoverUnsavedDocuments(Window owner)
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

            return FreeWRecoveryWorkflow.RunAsync(
                    recoveries,
                    FreeWRecoveryPromptMode.Manual,
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
                            ? _file.RecoverSnapshot
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
