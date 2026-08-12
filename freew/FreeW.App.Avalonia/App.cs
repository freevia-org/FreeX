using Avalonia;
using Free.Shared.AppServices;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Options;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Avalonia;

public sealed partial class App : Application
{
    public static IReadOnlyList<string> StartupArguments { get; set; } = [];
    internal static Theme ActiveTheme { get; private set; } = FreeWApplicationStartup.Theme.DefaultTheme;

    public override void OnFrameworkInitializationCompleted()
    {
        AvaloniaAppLocalizationBootstrap.InstallSharedSeams(
            UiText.Get,
            UiText.Format,
            UiText.CreateAutomationName);

        FreeWApplicationStartup.Theme.Apply(
            Environment.GetEnvironmentVariable,
            theme => ActiveTheme = theme,
            (theme, resourceKeyPrefix) =>
                Resources.MergedDictionaries.Add(
                    AvaloniaThemeApplier.BuildResources(theme, resourceKeyPrefix)));

        var optionsStore = ApplicationOptionsStore<FreeWOptions>.Create(
            PlatformApplicationDataPathProvider.LocalInstance);
        var loadedOptions = optionsStore.Load();
        Action<MainWindow>? afterMainWindowCreated = null;
        ConfigureAfterMainWindowCreated(ref afterMainWindowCreated);

        SisterAvaloniaAppBootstrap.Initialize(
            this,
            new SisterAvaloniaAppBootstrapSpec<MainWindow>(
                StartupArguments,
                args => new MainWindow(args, loadedOptions, optionsStore),
                afterMainWindowCreated));

        base.OnFrameworkInitializationCompleted();
    }

    partial void ConfigureAfterMainWindowCreated(ref Action<MainWindow>? callback);
}
