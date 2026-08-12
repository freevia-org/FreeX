extern alias ProductionAvalonia;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class ParityCaptureAssemblyOwnershipTests
{
    [Fact]
    public void ShippingAssembly_DoesNotOwnParityCaptureOrInteractionValidation()
    {
        var assembly = typeof(ProductionAvalonia::FreeX.App.Avalonia.MainWindow).Assembly;

        assembly.GetType("FreeX.App.Avalonia.ParityCaptureCoordinator").Should().BeNull();
        assembly.GetType("FreeX.App.Avalonia.ParityCaptureOptions").Should().BeNull();
        assembly.GetType("FreeX.App.Avalonia.InteractionValidationCoordinator").Should().BeNull();
        assembly.GetType("FreeX.App.Avalonia.GridCaptureCoordinator").Should().BeNull();
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Should().NotContain("FreeX.ParityCapture.Support");
    }

    [Fact]
    public void ShippingStartup_DoesNotOwnCaptureParsingOrCoordination()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var program = File.ReadAllText(Path.Combine(root, "src", "FreeX.App.Avalonia", "Program.cs"));
        var app = File.ReadAllText(Path.Combine(root, "src", "FreeX.App.Avalonia", "App.cs"));
        var options = File.ReadAllText(Path.Combine(root, "src", "FreeX.App.Avalonia", "MainWindow.Options.cs"));
        var captureProgram = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "FreeX.ParityCapture.Avalonia",
            "Capture",
            "Program.cs"));

        program.Should().NotContain("FREEX_PARITY_CAPTURE");
        program.Should().NotContain("ParityCaptureOptions");
        app.Should().NotContain("FREEX_PARITY_CAPTURE");
        app.Should().NotContain("ParityCaptureCoordinator");
        app.Should().Contain("ExternalStartupCoordinator");
        options.Should().NotContain("FREEX_PARITY_CAPTURE");
        options.Should().Contain("ExternalOptionsFixtureFactory?.Invoke()");
        captureProgram.Should().Contain("ParityCaptureOptions.TryParse(");
        captureProgram.Should().Contain("App.ExternalStartupCoordinator =");
    }
}
