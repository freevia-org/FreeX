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
        AutosaveSnapshotStore? store = null,
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
        _session = store is null
            ? new FreeWAutosaveSession(ports)
            : new FreeWAutosaveSession(ports, store);
        _recoverInNewWindow = recoverInNewWindow;
        _timer = new DispatcherTimer { Interval = FreeWAutosaveSession.DefaultInterval };
        _timer.Tick += (_, _) => _session.Snapshot();
    }

    internal string SnapshotIdForTests => _session.SnapshotId;
    internal void SnapshotNowForTests() => _session.Snapshot();

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
            var recoveries = _session.PlanRecoveries();
            var anyAccepted = false;
            for (var index = 0; index < recoveries.Count; index++)
            {
                var recovery = recoveries[index];
                var remaining = recoveries.Count - index;
                var prompt = remaining > 1
                    ? $"FreeW found unsaved changes to {recovery.DisplayName} from a previous session ({remaining} unsaved documents found). Recover this one?"
                    : $"FreeW found unsaved changes to {recovery.DisplayName} from a previous session. Recover them?";
                if (!DialogMessageHelper.AskYesNo(owner, prompt, "FreeW - Recover"))
                    continue;

                var firstAccepted = !anyAccepted;
                anyAccepted = true;
                _session.CompleteRecovery(
                    recovery,
                    accepted: true,
                    firstAccepted
                        ? _file.OpenSnapshot
                        : (_, _) => _recoverInNewWindow?.Invoke(recovery.Candidate) ?? false,
                    FreeWRecoveryRestoreExceptionPolicy.QuarantineCandidate);
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
            var recoveries = _session.PlanRecoveries();
            if (recoveries.Count == 0)
            {
                DialogMessageHelper.ShowInfo(owner,
                    "No unsaved documents were found.",
                    "FreeW - Recover");
                return false;
            }

            var anyAccepted = false;
            var anyRecovered = false;
            for (var index = 0; index < recoveries.Count; index++)
            {
                var recovery = recoveries[index];
                var remaining = recoveries.Count - index;
                var prompt = remaining > 1
                    ? $"Recover unsaved changes to {recovery.DisplayName}? ({remaining} unsaved documents found.)"
                    : $"Recover unsaved changes to {recovery.DisplayName}?";
                var answer = DialogMessageHelper.ShowMessage(
                    owner,
                    prompt,
                    "FreeW - Recover",
                    UserMessageButtons.OkCancel,
                    UserMessageIcon.Question);
                if (answer != UserMessageResult.Ok)
                    continue;

                var firstAccepted = !anyAccepted;
                anyAccepted = true;
                var recovered = _session.CompleteRecovery(
                    recovery,
                    accepted: true,
                    firstAccepted
                        ? _file.RecoverSnapshot
                        : (_, _) => _recoverInNewWindow?.Invoke(recovery.Candidate) ?? false);
                anyRecovered |= recovered;
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
}
