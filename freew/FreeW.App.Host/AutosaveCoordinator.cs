using System;
using System.Windows;
using System.Windows.Threading;
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

    public AutosaveCoordinator(DocumentView editor, FileCommands file)
    {
        _file = file;
        _session = new FreeWAutosaveSession(new FreeWAutosavePorts(
            GetOriginalFilePath: () => file.CurrentPath,
            GetDisplayName: () => file.DisplayName,
            GetIsDirty: () => file.IsDirty,
            GetDirtyGeneration: () => file.DirtyGeneration,
            ExecuteWithDocument: writeDocument =>
            {
                editor.CommitToModel();
                writeDocument(editor.Model);
            }));
        _timer = new DispatcherTimer { Interval = FreeWAutosaveSession.DefaultInterval };
        _timer.Tick += (_, _) => _session.Snapshot();
    }

    public void Start() => _timer.Start();

    public void Stop()
    {
        _timer.Stop();
        _session.CompleteCleanExit();
    }

    /// <summary>
    /// Offers the latest prior-session snapshot on startup. Other candidates remain available for
    /// manual recovery; declining also keeps the offered candidate for a later attempt.
    /// </summary>
    public void OfferRecovery(Window owner)
    {
        try
        {
            var recovery = _session.PlanLatestRecovery();
            if (recovery is null)
                return;

            var recover = DialogMessageHelper.AskYesNo(owner,
                $"FreeW found unsaved changes to {recovery.DisplayName} from a previous session. Recover them?",
                "FreeW - Recover");

            _session.CompleteRecovery(recovery, recover, _file.OpenSnapshot);
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
            var recovery = _session.PlanLatestRecovery();
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

            return _session.CompleteRecovery(
                recovery,
                answer == UserMessageResult.Ok,
                _file.RecoverSnapshot);
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
