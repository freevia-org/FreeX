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
internal sealed class AutosaveCoordinator
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

    internal string SnapshotIdForTests => _session.SnapshotId;
    internal void SnapshotNowForTests() => _session.Snapshot();
    internal void SimulateCrashForTests() => _session.Dispose();

    public void Start() => _timer.Start();

    public void Stop()
    {
        _timer.Stop();
        _session.CompleteCleanExit();
    }

    /// <summary>
    /// Offers every prior-session snapshot on startup. The first accepted document uses this window;
    /// subsequent documents open through the new-window callback.
    /// </summary>
    public bool OfferRecovery(Window owner)
    {
        try
        {
            return FreeWRecoveryWorkflow.RunAsync(
                    _session.PlanRecoveries(),
                    FreeWRecoveryPromptMode.Startup,
                    offer => new ValueTask<bool>(DialogMessageHelper.AskYesNo(
                        owner,
                        offer.Prompt,
                        "FreeW - Recover")),
                    (recovery, useCurrentWindow) =>
                    {
                        var recovered = _session.CompleteRecovery(
                            recovery,
                            accepted: true,
                            useCurrentWindow
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
        try
        {
            var recoveries = _session.PlanRecoveries();
            if (recoveries.Count == 0)
            {
                DialogMessageHelper.ShowInfo(owner,
                    "No unsaved documents were found.",
                    "FreeW - Recover");
                return false;
            }

            return FreeWRecoveryWorkflow.RunAsync(
                    recoveries,
                    FreeWRecoveryPromptMode.Manual,
                    offer => new ValueTask<bool>(DialogMessageHelper.ShowMessage(
                        owner,
                        offer.Prompt,
                        "FreeW - Recover",
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
                $"Could not recover the document.\n\n{ex.Message}",
                "FreeW - Recover");
            return false;
        }
    }
}
