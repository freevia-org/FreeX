using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Free.Shared.Shell.Avalonia.Tests;

/// <summary>
/// r139 avalonia-hardcoded-light-theme: <see cref="SisterAvaloniaAppBootstrap.Initialize{TWindow}"/>
/// used to hardcode <c>ThemeVariant.Light</c>, so every sister app (FreeX/FreeW/FreeP on Linux/macOS
/// via this shared bootstrap) ignored the OS-wide dark-mode/high-contrast preference. It must instead
/// set <see cref="ThemeVariant.Default"/>, which makes Avalonia's FluentTheme resolve each control's
/// actual variant from <c>IPlatformSettings</c> (the live OS preference) instead of always light.
/// </summary>
public sealed class SisterAvaloniaAppBootstrapThemeTests
{
    [Fact]
    public void Initialize_SetsRequestedThemeVariantToDefault_SoAvaloniaFollowsTheOSDarkModePreference()
    {
        var application = new TestApplication();

        var spec = new SisterAvaloniaAppBootstrapSpec<TestWindow>(
            [],
            _ => new TestWindow());

        SisterAvaloniaAppBootstrap.Initialize(application, spec);

        application.RequestedThemeVariant.Should().Be(ThemeVariant.Default);
    }

    /// <summary>
    /// Sibling coverage: the theme-variant fix must not disturb the rest of Initialize's contract --
    /// FluentTheme is still installed, and (since a bare <see cref="Application"/> has no
    /// <see cref="Application.ApplicationLifetime"/> attached, so it is not an
    /// <see cref="Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime"/>)
    /// Initialize still takes its early-return path without ever invoking CreateMainWindow.
    /// </summary>
    [Fact]
    public void Initialize_StillInstallsFluentThemeAndTakesEarlyReturnPath_WhenLifetimeIsNotClassicDesktop()
    {
        var application = new TestApplication();

        TestWindow? created = null;
        var spec = new SisterAvaloniaAppBootstrapSpec<TestWindow>(
            ["arg"],
            _ =>
            {
                created = new TestWindow();
                return created;
            });

        SisterAvaloniaAppBootstrap.Initialize(application, spec);

        // A bare Application (no ApplicationLifetime attached, exactly like this test's TestApplication)
        // is not an IClassicDesktopStyleApplicationLifetime, so Initialize takes its early-return path:
        // FluentTheme is still installed, but CreateMainWindow is never invoked.
        application.Styles.Should().ContainSingle().Which.Should().BeOfType<FluentTheme>();
        created.Should().BeNull();
    }

    private sealed class TestApplication : Application;

    private sealed class TestWindow : Window;
}
