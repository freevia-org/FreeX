using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class FreeXLaunchSmokeOwnershipSourceTests
{
    [Fact]
    public void ShippingAvaloniaAssembly_DoesNotOwnLaunchSmokeOrchestration()
    {
        var app = Read("src", "FreeX.App.Avalonia", "App.cs");
        var program = Read("src", "FreeX.App.Avalonia", "Program.cs");
        var project = Read("src", "FreeX.App.Avalonia", "FreeX.App.Avalonia.csproj");

        var sourceDirectory = Path.GetDirectoryName(
            RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "Program.cs"))!;
        File.Exists(Path.Combine(sourceDirectory, "MacOsLaunchSmoke.cs"))
            .Should().BeFalse();
        app.Should().NotContain("MacOsLaunchSmokeOptions");
        app.Should().NotContain("MacOsLaunchSmokeCoordinator");
        program.Should().NotContain("--macos-launch-smoke");
        program.Should().NotContain("MacOsLaunchSmokeOptions.TryParse");
        project.Should().Contain("FREEX_RENDERER_CONTRACTS");
        project.Should().Contain("Link=\"ValidationAccess\\LaunchSmokeObservationContracts.cs\"");
    }

    [Fact]
    public void ValidationHost_OwnsOptionsCoordinationAndPersistence()
    {
        var program = Read("tools", "FreeX.Validation.Avalonia", "Program.cs");
        var smoke = Read("tools", "FreeX.Validation.Avalonia", "MacOsLaunchSmoke.cs");
        var project = Read("tools", "FreeX.Validation.Avalonia", "FreeX.Validation.Avalonia.csproj");

        program.Should().Contain("MacOsLaunchSmokeOptions.TryParse");
        program.Should().Contain("FreeX.App.Avalonia.Program.RunToolHost");
        smoke.Should().Contain("internal static class MacOsLaunchSmokeCoordinator");
        smoke.Should().Contain("File.WriteAllLines(");
        smoke.Should().Contain("File.AppendAllLines(reportPath");
        project.Should().Contain("FREEX_VALIDATION_HOST");
        project.Should().Contain("FreeX.App.Avalonia.csproj");
    }

    [Fact]
    public void ValidationHost_UsesDedicatedRendererAccessAdapter()
    {
        var access = Read("src", "FreeX.App.Avalonia", "MainWindow.LaunchSmokeAccessAdapter.cs");
        var smoke = Read("tools", "FreeX.Validation.Avalonia", "MacOsLaunchSmoke.cs");

        access.Should().Contain("internal sealed class LaunchSmokeAccessAdapter");
        smoke.Should().Contain("MainWindow.LaunchSmokeAccessAdapter access");
        smoke.Should().NotContain("MainWindow mainWindow");
    }

    private static string Read(params string[] path) =>
        File.ReadAllText(RepositoryFileLocator.Find(path));
}
