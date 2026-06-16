using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Verifies that on Linux (and any non-macOS Unix), FreeX stores options and
/// diagnostics under the XDG-aligned base directories that .NET maps to
/// <see cref="Environment.SpecialFolder.ApplicationData"/> (~/.config) and
/// <see cref="Environment.SpecialFolder.LocalApplicationData"/> (~/.local/share),
/// rather than the macOS ~/Library locations.
/// </summary>
public sealed class LinuxPlatformPathTests
{
    private const string XdgConfigHome = "/home/tester/.config";
    private const string XdgDataHome = "/home/tester/.local/share";
    private const string UserProfile = "/home/tester";

    [Fact]
    public void ApplicationDataDirectory_OnLinux_UsesXdgConfigBase()
    {
        var provider = new PlatformApplicationDataPathProvider(
            isMacOsProvider: () => false,
            userProfilePathProvider: () => UserProfile,
            applicationDataPathProvider: () => XdgConfigHome);

        provider.GetApplicationDataDirectory().Should().Be(XdgConfigHome);
    }

    [Fact]
    public void OptionsFilePath_OnLinux_LivesUnderXdgConfigProductDirectory()
    {
        var provider = new PlatformApplicationDataPathProvider(
            isMacOsProvider: () => false,
            userProfilePathProvider: () => UserProfile,
            applicationDataPathProvider: () => XdgConfigHome);

        var optionsPath = AppStoragePathPlanner.GetOptionsFilePath(provider);

        optionsPath.Should().Be(Path.Combine(
            XdgConfigHome,
            AppStoragePathPlanner.ProductDirectoryName,
            AppStoragePathPlanner.OptionsFileName));
        optionsPath.Should().NotContain("Library");
    }

    [Fact]
    public void DiagnosticsDirectory_OnLinux_LivesUnderXdgDataProductDirectory()
    {
        var provider = new PlatformAppDiagnosticsPathProvider(
            isMacOsProvider: () => false,
            userProfilePathProvider: () => UserProfile,
            localApplicationDataPathProvider: () => XdgDataHome);

        var diagnosticsDirectory = provider.GetDiagnosticsDirectory();

        diagnosticsDirectory.Should().Be(Path.Combine(
            XdgDataHome,
            AppStoragePathPlanner.ProductDirectoryName,
            AppStoragePathPlanner.DiagnosticsDirectoryName));
        diagnosticsDirectory.Should().NotContain("Library");
    }
}
