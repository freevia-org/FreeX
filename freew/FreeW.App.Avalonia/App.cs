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
    internal static Theme ActiveTheme { get; private set; } = FreeWApplicationStartup.Theme.DefaultTheme;

    internal static SisterAvaloniaStandardDesktopProfile<App, MainWindow, FreeWOptions> DesktopProfile { get; } =
        new(
            FreeWApplicationStartup.ProductIdentity,
            new SisterAvaloniaLocalizationStartupDescriptor(
                () => AvaloniaAppLocalizationBootstrap.InstallSharedSeams(
                    UiText.Get,
                    UiText.Format,
                    UiText.CreateAutomationName)),
            new SisterAvaloniaThemeStartupDescriptor<Theme>(
                FreeWApplicationStartup.Theme,
                theme => ActiveTheme = theme,
                (application, theme, resourceKeyPrefix) =>
                    application.Resources.MergedDictionaries.Add(
                        AvaloniaThemeApplier.BuildResources(theme, resourceKeyPrefix))),
            new SisterAvaloniaOptionsStartupDescriptor<FreeWOptions>(
                () => ApplicationOptionsStore<FreeWOptions>.Create(
                    PlatformApplicationDataPathProvider.LocalInstance)),
            new SisterAvaloniaWindowStartupDescriptor<MainWindow, FreeWOptions>(
                (startupArguments, options, optionsStore) =>
                    new MainWindow(startupArguments, options, optionsStore)),
            onEmergencySnapshot: AutosaveAdapter.TryEmergencySnapshots);

    public override void OnFrameworkInitializationCompleted()
    {
        SisterAvaloniaStandardDesktopFactory.Initialize(this, DesktopProfile);

        base.OnFrameworkInitializationCompleted();
    }
}
