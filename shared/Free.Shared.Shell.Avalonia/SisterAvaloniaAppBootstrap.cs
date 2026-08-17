using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Free.Shared.Shell.Avalonia;

public sealed record SisterAvaloniaAppBootstrapSpec<TWindow>(
    IReadOnlyList<string> StartupArguments,
    Func<IReadOnlyList<string>, TWindow> CreateMainWindow,
    Action<TWindow>? AfterMainWindowCreated = null)
    where TWindow : Window;

public static class SisterAvaloniaAppBootstrap
{
    public static void Initialize<TWindow>(
        Application application,
        SisterAvaloniaAppBootstrapSpec<TWindow> spec)
        where TWindow : Window
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.StartupArguments);
        ArgumentNullException.ThrowIfNull(spec.CreateMainWindow);

        // ThemeVariant.Default (not a hardcoded Light) lets Avalonia's FluentTheme resolve each
        // control's actual variant from the platform's live color-scheme preference (OS-wide
        // dark-mode/high-contrast), instead of forcing every sister app to Light regardless of
        // what the user has configured on Linux/macOS/Windows (r139 avalonia-hardcoded-light-theme).
        application.RequestedThemeVariant = ThemeVariant.Default;
        application.Styles.Add(new FluentTheme());

        if (application.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var mainWindow = spec.CreateMainWindow(spec.StartupArguments);
        desktop.MainWindow = mainWindow;
        spec.AfterMainWindowCreated?.Invoke(mainWindow);
    }
}
