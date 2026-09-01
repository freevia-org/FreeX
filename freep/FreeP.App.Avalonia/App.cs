using System.Globalization;
using Avalonia;
using Free.Shared.AppServices;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.App.Localization;

namespace FreeP.App.Avalonia;

public sealed partial class App : Application
{
    internal static Theme ActiveTheme { get; private set; } = BrandThemes.FreeP;

    internal static SisterAvaloniaStandardDesktopProfile<App, MainWindow, FreePOptions> DesktopProfile { get; } =
        new(
            FreePApplicationStartupDescriptor.ProductIdentity,
            new SisterAvaloniaLocalizationStartupDescriptor(
                () => AvaloniaAppLocalizationBootstrap.InstallSharedSeams(
                    UiText.Get,
                    UiText.Format,
                    UiText.CreateAutomationName),
                // r189: this shell offers a UI-language field with a restart notice; without this
                // the restart changed nothing. FreeX had the same bug and fixing it there alone
                // left it here, so the applier is wired through the shared profile for every
                // sister Avalonia app rather than in one App.cs.
                uiLanguage => AvaloniaAppLocalizationBootstrap.ApplyAppLanguage(
                    uiLanguage,
                    name => AppLanguageCatalog.ResolveCulture(name, CultureInfo.CurrentUICulture),
                    CultureInfo.CurrentUICulture)),
            new SisterAvaloniaThemeStartupDescriptor<Theme>(
                FreePApplicationStartupDescriptor.Theme,
                theme => ActiveTheme = theme,
                (application, theme, resourceKeyPrefix) =>
                    application.Resources.MergedDictionaries.Add(
                        AvaloniaThemeApplier.BuildResources(theme, resourceKeyPrefix))),
            new SisterAvaloniaOptionsStartupDescriptor<FreePOptions>(
                () => ApplicationOptionsStore<FreePOptions>.Create()),
            new SisterAvaloniaWindowStartupDescriptor<MainWindow, FreePOptions>(CreateMainWindow),
            onEmergencySnapshot: AutosaveAdapter.TryEmergencySnapshots);

    public override void OnFrameworkInitializationCompleted()
    {
        // Route the shared shell's OK/Cancel button text and generic message-box titles
        // (AvaloniaDialogButtonRowFactory.CreateOkCancel, AvaloniaUserMessageDialog) through
        // FreeP's own localized resource catalog instead of the shared shell's neutral-English
        // ShellStrings.Current default — mirrors the WPF host's
        // AppLocalization.Bootstrap.InstallSharedSeams() (App.xaml.cs). Must run before any
        // window/dialog can be shown, so it goes first.
        SisterAvaloniaStandardDesktopFactory.Initialize(this, DesktopProfile);

        base.OnFrameworkInitializationCompleted();
    }

    private static MainWindow CreateMainWindow(
        IReadOnlyList<string> startupArguments,
        FreePOptions options,
        IApplicationOptionsStore<FreePOptions> optionsStore) =>
        new(
            startupArguments,
            loadRecentFilesStore: null,
            options: options,
            optionsStore: optionsStore);
}
