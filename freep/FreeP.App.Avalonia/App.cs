using Avalonia;
using Free.Shared.AppServices;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Avalonia.Smoke;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

public sealed class App : Application
{
    public static IReadOnlyList<string> StartupArguments { get; set; } = [];
    internal static LaunchSmokeOptions? LaunchSmokeOptions { get; set; }
    internal static PhysicalValidationOptions? PhysicalValidationOptions { get; set; }
    internal static string? DialogPaneVisualEvidenceOutputRoot { get; set; }
    internal static string? DialogPaneVisualEvidenceScenarioId { get; set; }
    internal static string? WholeWindowVisualEvidenceOutputRoot { get; set; }
    internal static string? WholeWindowVisualEvidenceScenarioId { get; set; }
    internal static Theme ActiveTheme { get; private set; } = BrandThemes.FreeP;

    public override void OnFrameworkInitializationCompleted()
    {
        var theme = string.Equals(
            Environment.GetEnvironmentVariable("FREEP_THEME"),
            "midnight",
            StringComparison.OrdinalIgnoreCase)
            ? BrandThemes.FreeXMidnight
            : BrandThemes.FreeP;
        ActiveTheme = theme;
        Resources.MergedDictionaries.Add(AvaloniaThemeApplier.BuildResources(theme, "FreeP"));
        var options = ApplicationOptionsStore<FreePOptions>.Create().Load();

        SisterAvaloniaAppBootstrap.Initialize(
            this,
            new SisterAvaloniaAppBootstrapSpec<MainWindow>(
                StartupArguments,
                args => new MainWindow(args, loadRecentFilesStore: null, options: options),
                mainWindow =>
                {
                    if (WholeWindowVisualEvidenceOutputRoot is { } wholeWindowOutputRoot)
                    {
                        AvaloniaWholeWindowVisualEvidenceCapture.Start(mainWindow, wholeWindowOutputRoot, WholeWindowVisualEvidenceScenarioId);
                        return;
                    }
                    if (DialogPaneVisualEvidenceOutputRoot is { } outputRoot)
                    {
                        AvaloniaDialogPaneVisualEvidenceCapture.Start(mainWindow, outputRoot, DialogPaneVisualEvidenceScenarioId);
                        return;
                    }
                    if (PhysicalValidationOptions is { } physicalValidationOptions)
                    {
                        PhysicalValidationCoordinator.Start(mainWindow, physicalValidationOptions);
                        return;
                    }
                    if (LaunchSmokeOptions is { } options)
                        LaunchSmokeCoordinator.Start(mainWindow, options);
                }));

        base.OnFrameworkInitializationCompleted();
    }
}
