using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

public sealed class AppStoragePathPlannerTests
{
    [Fact]
    public void GetDiagnosticsDirectory_UsesDiagnosticsPathProvider()
    {
        using var temp = new TestTemporaryDirectory();
        var provider = new TestDiagnosticsPathProvider(Path.Combine(temp.Path, "FreeX", "Diagnostics"));

        var path = AppStoragePathPlanner.GetDiagnosticsDirectory(provider);

        path.Should().Be(Path.Combine(temp.Path, "FreeX", "Diagnostics"));
    }

    [Fact]
    public void GetOptionsFilePath_UsesApplicationDataPathProvider()
    {
        using var temp = new TestTemporaryDirectory();
        var provider = new TestApplicationDataPathProvider(temp.Path);

        var path = AppStoragePathPlanner.GetOptionsFilePath(provider);

        path.Should().Be(Path.Combine(temp.Path, "FreeX", "options.json"));
    }

    [Fact]
    public void GetRecentColorsFilePath_UsesApplicationDataPathProvider()
    {
        using var temp = new TestTemporaryDirectory();
        var provider = new TestApplicationDataPathProvider(temp.Path);

        var path = AppStoragePathPlanner.GetRecentColorsFilePath(provider);

        path.Should().Be(Path.Combine(temp.Path, "FreeX", "recent-colors.json"));
    }

    [Fact]
    public void ResolveRecentColorsFilePath_UsesExplicitOverrideWhenProvided()
    {
        using var temp = new TestTemporaryDirectory();
        var provider = new TestApplicationDataPathProvider(Path.Combine(temp.Path, "ignored"));
        var overridePath = Path.Combine(temp.Path, "custom-recent-colors.json");

        var path = AppStoragePathPlanner.ResolveRecentColorsFilePath(provider, overridePath);

        path.Should().Be(overridePath);
    }

    [Fact]
    public void ResolveOptionsFilePath_UsesExplicitOverrideWhenProvided()
    {
        using var temp = new TestTemporaryDirectory();
        var provider = new TestApplicationDataPathProvider(Path.Combine(temp.Path, "ignored"));
        var overridePath = Path.Combine(temp.Path, "custom-options.json");

        var path = AppStoragePathPlanner.ResolveOptionsFilePath(provider, overridePath);

        path.Should().Be(overridePath);
    }

    [Fact]
    public void BuildLocalDiagnosticsNotice_UsesPlannedPathWithoutWindowsEnvironmentVariable()
    {
        using var temp = new TestTemporaryDirectory();
        var diagnosticsDirectory = Path.Combine(temp.Path, "FreeX", "Diagnostics");

        var notice = AppStoragePathPlanner.BuildLocalDiagnosticsNotice(diagnosticsDirectory);

        notice.Should().Contain(diagnosticsDirectory);
        notice.Should().Contain("Crash exception messages and stack traces can occasionally contain sensitive values");
        notice.Should().Contain("FREEX_DIAGNOSTICS=0");
        notice.Should().NotContain("%LOCALAPPDATA%");
    }

    [Fact]
    public void AppDiagnosticsOptions_CreateDefault_UsesPlannedDiagnosticsDirectory()
    {
        using var temp = new TestTemporaryDirectory();
        var provider = new TestDiagnosticsPathProvider(Path.Combine(temp.Path, "FreeX", "Diagnostics"));

        var options = AppDiagnosticsOptions.CreateDefault(provider);

        options.IsEnabled.Should().BeTrue();
        options.DiagnosticsDirectory.Should().Be(Path.Combine(temp.Path, "FreeX", "Diagnostics"));
    }

    [Fact]
    public void PlatformProvider_PlansMacOsDiagnosticsUnderLogsAndOptionsUnderApplicationSupport()
    {
        var home = Path.Combine("Users", "quinn");
        var applicationDataProvider = new PlatformApplicationDataPathProvider(
            isMacOsProvider: () => true,
            userProfilePathProvider: () => home,
            applicationDataPathProvider: () => "ignored");
        var diagnosticsPathProvider = new PlatformAppDiagnosticsPathProvider(
            isMacOsProvider: () => true,
            userProfilePathProvider: () => home,
            localApplicationDataPathProvider: () => "ignored");

        AppStoragePathPlanner.GetDiagnosticsDirectory(diagnosticsPathProvider)
            .Should()
            .Be(Path.Combine(home, "Library", "Logs", "FreeX"));
        AppStoragePathPlanner.GetOptionsFilePath(applicationDataProvider)
            .Should()
            .Be(Path.Combine(home, "Library", "Application Support", "FreeX", "options.json"));
    }

    [Fact]
    public void PlatformDiagnosticsProvider_UsesLocalApplicationDataOutsideMacOs()
    {
        using var temp = new TestTemporaryDirectory();
        var provider = new PlatformAppDiagnosticsPathProvider(
            isMacOsProvider: () => false,
            userProfilePathProvider: () => "ignored",
            localApplicationDataPathProvider: () => temp.Path);

        AppStoragePathPlanner.GetDiagnosticsDirectory(provider)
            .Should()
            .Be(Path.Combine(temp.Path, "FreeX", "Diagnostics"));
    }

    private sealed class TestApplicationDataPathProvider(string path) : IApplicationDataPathProvider
    {
        public string GetApplicationDataDirectory() => path;
    }

    private sealed class TestDiagnosticsPathProvider(string path) : IAppDiagnosticsPathProvider
    {
        public string GetDiagnosticsDirectory() => path;
    }
}
