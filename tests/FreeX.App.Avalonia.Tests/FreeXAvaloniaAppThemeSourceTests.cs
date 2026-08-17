using System.IO;
using System.Text.RegularExpressions;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// r139 avalonia-hardcoded-light-theme: FreeX.App.Avalonia's own <c>App.cs</c> duplicates the
/// shared bootstrap's theme-variant assignment (it does not route through
/// <c>SisterAvaloniaAppBootstrap.Initialize</c> for this particular line), so it needs the same
/// fix independently -- <c>RequestedThemeVariant</c> must be <c>ThemeVariant.Default</c>, not a
/// hardcoded <c>ThemeVariant.Light</c>, so the app honors the OS-wide dark-mode/high-contrast
/// preference on Linux/macOS/Windows instead of always rendering light.
/// </summary>
public sealed class FreeXAvaloniaAppThemeSourceTests
{
    [Fact]
    public void AppCs_SetsRequestedThemeVariantToDefault_NotAHardcodedLight()
    {
        var source = File.ReadAllText(TestWorkspaceFileLocator.Find("src", "FreeX.App.Avalonia", "App.cs"));

        source.Should().Contain("RequestedThemeVariant = ThemeVariant.Default;");
        source.Should().NotContain("ThemeVariant.Light");
    }

    /// <summary>
    /// Sibling coverage: the theme-variant fix must not disturb the rest of startup -- FluentTheme
    /// is still installed right after the theme-variant line, exactly as before.
    /// </summary>
    [Fact]
    public void AppCs_StillInstallsFluentThemeImmediatelyAfterTheThemeVariantAssignment()
    {
        var source = File.ReadAllText(TestWorkspaceFileLocator.Find("src", "FreeX.App.Avalonia", "App.cs"));

        Regex.IsMatch(
                source,
                @"RequestedThemeVariant = ThemeVariant\.Default;\r?\n\s*Styles\.Add\(new FluentTheme\(\)\);")
            .Should().BeTrue("FluentTheme must still be installed immediately after the theme-variant assignment");
    }
}
