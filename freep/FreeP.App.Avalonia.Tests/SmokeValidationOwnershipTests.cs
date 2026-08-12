using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class SmokeValidationOwnershipTests
{
    [Fact]
    public void Shipping_sources_retain_only_compile_time_erased_observation_hooks()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var shipping = Path.Combine(root, "freep", "FreeP.App.Avalonia");
        var program = File.ReadAllText(Path.Combine(shipping, "Program.cs"));
        var app = File.ReadAllText(Path.Combine(shipping, "App.cs"));
        var mainWindow = File.ReadAllText(Path.Combine(shipping, "MainWindow.cs"));

        program.Should().NotContain("RunToolHost(");
        program.Should().NotContain("PackagingSmoke");
        program.Should().NotContain("LaunchSmokeOptions");
        app.Should().NotContain("LaunchSmoke");
        app.Should().NotContain("ExternalStartupCoordinator");
        app.Should().NotContain("EnableStartupDirtyTrace");
        mainWindow.Should().Contain("partial void RecordStartupObservation(string stage);");
        mainWindow.Should().NotContain("StartupDirtyTrace");
        File.Exists(Path.Combine(shipping, "MainWindow.ValidationAccessAdapter.cs")).Should().BeFalse();
        File.Exists(Path.Combine(shipping, "StartupDirtyTrace.cs")).Should().BeFalse();

        foreach (var source in Directory.EnumerateFiles(shipping, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(source);
            text.Should().NotContain("freep_packaging_smoke=");
            text.Should().NotContain("freep_launch_smoke=");
            text.Should().NotContain("SisterAppPackagingSmoke");
            text.Should().NotContain("SisterAppLaunchSmokeCoordinator");
        }
    }

    [Fact]
    public void Validation_host_owns_smoke_parsing_fixtures_and_reports()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var support = Path.Combine(root, "freep", "TestSupport", "Validation.Avalonia");
        var program = File.ReadAllText(Path.Combine(support, "Program.cs"));
        var packaging = File.ReadAllText(Path.Combine(support, "PackagingSmokeValidation.cs"));
        var launch = File.ReadAllText(Path.Combine(support, "LaunchSmokeValidation.cs"));
        var adapter = File.ReadAllText(Path.Combine(support, "MainWindow.ValidationAccessAdapter.cs"));

        program.Should().Contain("PackagingSmokeCommand.TryRun(");
        program.Should().Contain("SisterAppLaunchSmokeOptions.TryParse(");
        packaging.Should().Contain("Presentation.CreateEmpty()");
        packaging.Should().Contain("PptxPackageWriter.Write(");
        packaging.Should().Contain("SisterAppPackagingSmoke.WriteReport(");
        launch.Should().Contain("SisterAppLaunchSmokeCoordinator.Start(");
        launch.Should().Contain("freep_launch_smoke=");
        adapter.Should().Contain("internal bool HasToolbar");
        adapter.Should().Contain("internal int CurrentSlideIndex");
    }
}
