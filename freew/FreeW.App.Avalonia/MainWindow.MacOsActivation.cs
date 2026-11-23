using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Free.Shared.AppServices;

namespace FreeW.App.Avalonia;

public sealed partial class MainWindow
{
    private readonly HashSet<string> _startupFileActivations = new(PlatformPathIdentityComparer.Current);

    internal MainWindow RememberStartupFileActivations(IReadOnlyList<string> startupArguments)
    {
        foreach (var argument in startupArguments)
        {
            if (LocalFilePath.TryNormalize(argument, out var path))
                _startupFileActivations.Add(path);
        }
        // Only suppress the initial launch delivery. In particular, a template opened
        // through argv must remain available for a later Finder gesture to create a new
        // document even when Finder never repeated its initial path. Normal-priority
        // queued activation events run before this startup cleanup turn.
        Opened += (_, _) => Dispatcher.UIThread.Post(
            _startupFileActivations.Clear,
            DispatcherPriority.Background);
        return this;
    }

    internal void ObserveFileActivation(Func<Task> operation) => RunUiTask(operation);

    internal Task ObserveFileActivationAsync(Func<Task> operation) => ObserveUiTaskAsync(operation);

    internal async Task<IReadOnlyList<MainWindow>> OpenActivatedPathsAsync(IReadOnlyList<string> paths)
    {
        var activated = new List<MainWindow>();
        var seen = new HashSet<string>(PlatformPathIdentityComparer.Current);
        foreach (var candidate in paths)
        {
            if (!LocalFilePath.TryNormalize(candidate, out var path) || !seen.Add(path))
                continue;

            var existing = FindActivatedDocumentWindow(path, activated);
            if (existing is not null)
            {
                _startupFileActivations.Remove(path);
                RestoreActivatedWindow(existing);
                activated.Add(existing);
                continue;
            }

            // The initial Finder event can repeat argv, including entries whose startup
            // windows have not been shown yet and templates that opened as untitled documents.
            if (_startupFileActivations.Remove(path))
                continue;

            if (!File.Exists(path) || !_documentPersistence.CanOpenPath(path))
            {
                await _fileWorkflow.ShowFileCommandErrorAsync(
                    "Could not open the document",
                    new InvalidOperationException(
                        $"'{Path.GetFileName(path)}' is missing or is not a supported document format."));
                continue;
            }

            // Use a separate document window so a warm activation never replaces edits or
            // races the primary window's asynchronous startup recovery offer.
            var window = new MainWindow(
                Array.Empty<string>(),
                _options,
                _optionsStore,
                suppressStartupRecoveryOffer: true,
                documentWindowPlanner: _documentWindowPlanner);
            window.Show();
            if (await window._fileCommands.OpenPathAsync(path))
            {
                RestoreActivatedWindow(window);
                activated.Add(window);
            }
            else
            {
                window.Close();
            }
        }
        return activated;
    }

    private MainWindow? FindActivatedDocumentWindow(string path, IReadOnlyList<MainWindow> activated)
    {
        var windows = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.Windows.OfType<MainWindow>()
            : new[] { this };
        return windows.Concat(activated).FirstOrDefault(window =>
            window._fileWorkflow.CurrentPath is { } currentPath
            && PlatformPathIdentityComparer.Current.Equals(currentPath, path));
    }

    private static void RestoreActivatedWindow(MainWindow window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        if (!window.IsVisible)
            window.Show();
        window.Activate();
    }
}
