using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using FreeX.App.Services;
using FreeX.App.Services.Updates;

namespace FreeX.App.Avalonia;

public sealed class App : Application
{
    private const string ApplicationTitle = "FreeX";

    public static IReadOnlyList<string> StartupArguments { get; set; } = [];

    internal static MacOsLaunchSmokeOptions? LaunchSmokeOptions { get; set; }

    internal static ParityCaptureOptions? ParityCaptureOptions { get; set; }

    internal static AvaloniaAppDiagnostics? Diagnostics { get; set; }

    public override void OnFrameworkInitializationCompleted()
    {
        Name = ApplicationTitle;
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new FluentTheme());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow(StartupArguments);
            desktop.MainWindow = mainWindow;
            Diagnostics?.RecordEvent("app_ready", new Dictionary<string, string?>
            {
                ["source"] = "avalonia",
                ["scope"] = "app",
                ["status"] = "ready"
            });

            // Self-update is best-effort: when the app is not Velopack-installed the service
            // degrades to Unavailable and the indicator simply never appears.
            var updateService = VelopackUpdateService.CreateForGitHub(
                repoUrl: UpdateFeed.GitHubRepoUrl,
                prerelease: UpdateFeed.AllowPrereleases(AppHelpInfo.ReleaseChannel),
                releasesPageUrl: AppHelpInfo.LatestReleaseUrl);
            mainWindow.AttachUpdateService(updateService);

            if (this.TryGetFeature<IActivatableLifetime>() is { } activatableLifetime)
                activatableLifetime.Activated += (_, args) => _ = OnActivatedAsync(mainWindow, args);

            if (LaunchSmokeOptions is { } launchSmokeOptions)
                MacOsLaunchSmokeCoordinator.Start(mainWindow, launchSmokeOptions, Diagnostics);

            if (ParityCaptureOptions is { } parityCaptureOptions)
                ParityCaptureCoordinator.Start(mainWindow, parityCaptureOptions, Diagnostics);
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Wraps the fire-and-forget activation handler so an exception cannot escape an async-void
    // event handler and tear the process down; failures are routed to diagnostics instead.
    private static async Task OnActivatedAsync(MainWindow mainWindow, ActivatedEventArgs args)
    {
        try
        {
            await MainWindow_ActivatedAsync(mainWindow, args);
        }
        catch (Exception ex)
        {
            Diagnostics?.RecordEvent("activation_failed", new Dictionary<string, string?>
            {
                ["source"] = "avalonia",
                ["scope"] = "activation",
                ["status"] = "error",
                ["reason"] = ex.GetType().Name
            });
        }
    }

    private static async Task MainWindow_ActivatedAsync(MainWindow mainWindow, ActivatedEventArgs args)
    {
        if (args is not FileActivatedEventArgs fileArgs ||
            fileArgs.Kind != ActivationKind.File)
        {
            return;
        }

        mainWindow.Show();
        mainWindow.Activate();
        await mainWindow.OpenActivatedFilesAsync(fileArgs.Files);
    }
}
