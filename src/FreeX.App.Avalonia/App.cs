using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using FreeX.App.Services;
using FreeX.App.Services.Updates;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;

namespace FreeX.App.Avalonia;

public sealed class App : Application
{
    private const string ApplicationTitle = "FreeX";

    public static IReadOnlyList<string> StartupArguments { get; set; } = [];

    internal static MacOsLaunchSmokeOptions? LaunchSmokeOptions { get; set; }

    internal static ParityCaptureOptions? ParityCaptureOptions { get; set; }

    internal static AvaloniaAppDiagnostics? Diagnostics { get; set; }

    /// <summary>
    /// The active brand theme selected at startup (default: <see cref="BrandThemes.FreeX"/>).
    /// Stored so that tests and diagnostics can verify the selected palette.
    /// </summary>
    internal static Theme ActiveTheme { get; private set; } = BrandThemes.FreeX;

    public override void OnFrameworkInitializationCompleted()
    {
        Name = ApplicationTitle;
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new FluentTheme());

        // Select the active brand theme and register token brushes into Application.Resources
        // so that chrome surfaces can look them up by key.  FREEX_THEME=midnight swaps in the
        // alternate palette; otherwise the default FreeX palette applies (values are identical
        // to the existing inline colors so the default appearance is unchanged).
        var theme = string.Equals(
            System.Environment.GetEnvironmentVariable("FREEX_THEME"),
            "midnight",
            StringComparison.OrdinalIgnoreCase)
            ? BrandThemes.FreeXMidnight
            : BrandThemes.FreeX;
        ActiveTheme = theme;
        var themeResources = AvaloniaThemeApplier.BuildResources(theme, "FreeX");
        Resources.MergedDictionaries.Add(themeResources);

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
