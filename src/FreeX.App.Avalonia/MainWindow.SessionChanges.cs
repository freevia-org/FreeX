using FreeX.App.Presentation.Shell;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private SessionChangesWindow? _sessionChangesWindow;

    private Task ShowSessionChangesWindowAsync()
    {
        var plan = SessionChangesPlanner.Create(
            _session.GetUndoHistory(SessionChangesPlanner.MaxEntries),
            _session.GetRedoHistory(SessionChangesPlanner.MaxEntries));

        if (_sessionChangesWindow is { IsVisible: true } existing)
        {
            existing.Refresh(plan);
            existing.Activate();
            return Task.CompletedTask;
        }

        var window = new SessionChangesWindow(plan);
        _sessionChangesWindow = window;
        ShowOwnedModelessWindow(
            window,
            window.Activate,
            () =>
            {
                if (ReferenceEquals(_sessionChangesWindow, window))
                    _sessionChangesWindow = null;
            });

        return Task.CompletedTask;
    }

    private void CloseSessionChangesWindowIfOpen() => _sessionChangesWindow?.Close();
}
