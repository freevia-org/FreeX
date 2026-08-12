namespace FreeW.App.Avalonia;

public sealed partial class App
{
    internal static Action<MainWindow>? ExternalStartupCoordinator { get; set; }

    partial void ConfigureAfterMainWindowCreated(ref Action<MainWindow>? callback) =>
        callback = ExternalStartupCoordinator;
}
