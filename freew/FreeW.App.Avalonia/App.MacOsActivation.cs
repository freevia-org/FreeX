using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace FreeW.App.Avalonia;

public sealed partial class App
{
    private readonly Queue<IReadOnlyList<string>> _pendingFileActivations = new();
    private bool _processingFileActivations;

    private void SubscribeToFileActivations()
    {
        // Subscribe before constructing the desktop window: Finder can deliver its initial
        // document event during startup, rather than putting document paths in argv.
        if (this.TryGetFeature<IActivatableLifetime>() is { } lifetime)
            lifetime.Activated += OnFileActivated;
    }

    private void OnFileActivated(object? sender, ActivatedEventArgs args)
    {
        if (args is not FileActivatedEventArgs { Kind: ActivationKind.File } files)
            return;

        var paths = files.Files.Select(file => file.Path.AbsoluteUri).ToArray();
        Dispatcher.UIThread.Post(() =>
        {
            _pendingFileActivations.Enqueue(paths);
            DispatchPendingFileActivations();
        });
    }

    private void CompleteFileActivationStartup()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is MainWindow window)
        {
            window.Opened += (_, _) => DispatchPendingFileActivations();
            DispatchPendingFileActivations();
        }
    }

    private void DispatchPendingFileActivations() =>
        ResolveFileActivationWindow()?.ObserveFileActivation(ProcessPendingFileActivationsAsync);

    private MainWindow? ResolveFileActivationWindow() =>
        ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? SelectFileActivationWindow(desktop.Windows.OfType<MainWindow>())
            : null;

    internal static MainWindow? SelectFileActivationWindow(IEnumerable<MainWindow> windows)
    {
        // The original main window can be closed while other document windows survive.
        // Resolve a live owner each time instead of retaining that first window forever.
        var visible = windows.Where(window => window.IsVisible).ToArray();
        return visible.FirstOrDefault(window => window.IsActive) ?? visible.FirstOrDefault();
    }

    private async Task ProcessPendingFileActivationsAsync()
    {
        if (_processingFileActivations)
            return;

        _processingFileActivations = true;
        try
        {
            // Serializing events also prevents two Finder events from independently opening
            // the same document while the first password/error dialog is still awaiting input.
            while (_pendingFileActivations.Count > 0 && ResolveFileActivationWindow() is { } window)
            {
                var paths = _pendingFileActivations.Dequeue();
                await window.ObserveFileActivationAsync(async () =>
                {
                    await window.OpenActivatedPathsAsync(paths);
                });
            }
        }
        finally
        {
            _processingFileActivations = false;
        }
    }
}
