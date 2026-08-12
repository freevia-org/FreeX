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
        var shippingSource = string.Join(
            Environment.NewLine,
            Directory.GetFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        File.Exists(Path.Combine(sourceDirectory, "MacOsLaunchSmoke.cs"))
            .Should().BeFalse();
        app.Should().NotContain("MacOsLaunchSmokeOptions");
        app.Should().NotContain("MacOsLaunchSmokeCoordinator");
        program.Should().NotContain("--macos-launch-smoke");
        program.Should().NotContain("MacOsLaunchSmokeOptions.TryParse");
        shippingSource.Should().NotContain("LaunchSmoke");
        shippingSource.Should().NotContain("launchSmoke");
        shippingSource.Should().NotContain("MacOsLaunchSmokeSnapshot");
        project.Should().NotContain("FREEX_RENDERER_CONTRACTS");
        project.Should().NotContain("MacOsLaunchSmoke.cs");
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
        smoke.Should().Contain("internal sealed record MacOsLaunchSmokeSnapshot(");
        smoke.Should().Contain("private static MacOsLaunchSmokeSnapshot CaptureSnapshot(");
        smoke.Should().Contain("private static async Task<MacOsLaunchSmokeDialogSnapshot> CaptureDialogEvidenceAsync(");
        smoke.Should().Contain("RecordCommandObservation(");
        smoke.Should().Contain("File.WriteAllLines(");
        smoke.Should().Contain("File.AppendAllLines(reportPath");
        project.Should().Contain("FREEX_VALIDATION_HOST");
        project.Should().Contain("FreeX.App.Avalonia.csproj");
    }

    [Fact]
    public void ValidationHost_UsesDedicatedRendererAccessAdapter()
    {
        var access = Read("src", "FreeX.App.Avalonia", "MainWindow.RendererValidationAccess.cs");
        var smoke = Read("tools", "FreeX.Validation.Avalonia", "MacOsLaunchSmoke.cs");

        access.Should().Contain("internal sealed class RendererValidationAccess");
        access.Should().Contain("internal RendererShellObservation ObserveShell()");
        access.Should().Contain("internal T GetControl<T>(string fieldName)");
        access.Should().Contain("internal NativeMenuItem GetNativeMenuItem(string fieldName)");
        access.Should().Contain("internal RendererFormattingState BeginCommandObservation(");
        access.Should().NotContain("LaunchSmoke");
        smoke.Should().Contain("MainWindow.RendererValidationAccess access");
        smoke.Should().NotContain("MainWindow mainWindow");
    }

    private static string Read(params string[] path) =>
        File.ReadAllText(RepositoryFileLocator.Find(path));
}
