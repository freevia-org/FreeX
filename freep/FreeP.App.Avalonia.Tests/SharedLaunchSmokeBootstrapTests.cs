using System.IO;
using Free.Shared.Shell.Avalonia;

namespace FreeP.App.Avalonia.Tests;

public sealed class SharedLaunchSmokeBootstrapTests
{
    private static readonly AvaloniaLaunchSmokeBootstrapTestSpec Spec = new(
        "FreeP.slnx",
        "freep",
        "FreeP",
        "sample.pptx",
        Parse);

    [Fact]
    public void SisterAppLaunchSmokeOptions_preserves_FreeP_startup_arguments() =>
        AvaloniaLaunchSmokeBootstrapTestSupport.AssertLaunchSmokeOptions(Spec);

    [Fact]
    public void App_and_launch_smoke_sources_use_shared_sister_helpers()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var app = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "App.cs"));
        app.Should().Contain("using Free.Shared.Shell.Avalonia;");
        app.Should().Contain("using Free.Shared.Theme.Avalonia;");
        app.Should().Contain("FreePApplicationStartupDescriptor.Theme.Apply(");
        app.Should().Contain("AvaloniaThemeApplier.BuildResources(theme, resourceKeyPrefix)");
        app.Should().Contain("SisterAvaloniaAppBootstrap.Initialize(");
        app.Should().Contain("new SisterAvaloniaAppBootstrapSpec<MainWindow>(");
        app.Should().NotContain("Styles.Add(new FluentTheme())");
        app.Should().NotContain("desktop.MainWindow = mainWindow;");

        var smoke = File.ReadAllText(Path.Combine(
            root, "freep", "FreeP.App.Avalonia", "Smoke", "LaunchSmoke.cs"));
        smoke.Should().Contain(
            "global using LaunchSmokeOptions = Free.Shared.Shell.Avalonia.SisterAppLaunchSmokeOptions;");
        smoke.Should().Contain("SisterAppLaunchSmokeCoordinator.Start(");
        smoke.Should().Contain("new SisterAppLaunchSmokeReport(snapshot.IsPassed, snapshot.ToReport())");
        smoke.Should().NotContain("record LaunchSmokeOptions(");
        smoke.Should().NotContain("SisterAppLaunchSmokeOptions.TryParse(");
        smoke.Should().NotContain("new DispatcherTimer");
        smoke.Should().NotContain("Application.Current?.ApplicationLifetime");
    }

    private static AvaloniaLaunchSmokeParseResult Parse(IReadOnlyList<string> args)
    {
        var result = SisterAppLaunchSmokeOptions.TryParse(
            args,
            out var options,
            out var startupArguments,
            out var error);
        return new(result, options?.ReportPath, options?.DiagnosticsDirectory, startupArguments, error);
    }
}
