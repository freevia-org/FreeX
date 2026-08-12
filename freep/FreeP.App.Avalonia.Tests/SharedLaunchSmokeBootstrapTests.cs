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
    public void Validation_host_owns_launch_smoke_and_uses_shared_sister_helpers()
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
        app.Should().NotContain("LaunchSmokeOptions");
        app.Should().NotContain("LaunchSmokeCoordinator");

        var smoke = File.ReadAllText(Path.Combine(
            root, "freep", "TestSupport", "Validation.Avalonia", "LaunchSmokeValidation.cs"));
        smoke.Should().Contain("SisterAppLaunchSmokeCoordinator.Start(");
        smoke.Should().Contain("new SisterAppLaunchSmokeReport(snapshot.IsPassed, snapshot.ToReport())");
        smoke.Should().NotContain("record LaunchSmokeOptions(");
        smoke.Should().NotContain("SisterAppLaunchSmokeOptions.TryParse(");
        smoke.Should().NotContain("new DispatcherTimer");
        smoke.Should().NotContain("Application.Current?.ApplicationLifetime");

        File.Exists(Path.Combine(root, "freep", "FreeP.App.Avalonia", "Smoke", "LaunchSmoke.cs"))
            .Should().BeFalse("shipping FreeP must not compile launch-smoke ownership");
        File.Exists(Path.Combine(root, "freep", "FreeP.App.Avalonia", "Smoke", "PackagingSmoke.cs"))
            .Should().BeFalse("shipping FreeP must not compile packaging-smoke ownership");
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
