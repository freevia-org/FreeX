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
        typeof(ProductionAvalonia::FreeX.App.Avalonia.MainWindow)
            .GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Select(method => method.Name)
            .Should().NotContain([
                "PrepareOptionalStartupState",
                "CompleteOptionalStartupState",
                "RecordOptionalNeutralCellSelection",
                "RecordOptionalNameBoxSelection",
                "AttachOptionalTextBoxInlineObservation",
                "RequestOptionalTextBoxInlineLayoutObservation",
                "RecordOptionalTextBoxInlineObservation",
            ]);
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
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var textBoxEditor = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.TextBoxInlineEditing.cs"));
        var options = File.ReadAllText(Path.Combine(root, "src", "FreeX.App.Avalonia", "MainWindow.Options.cs"));
        var captureProject = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "FreeX.ParityCapture.Avalonia",
            "FreeX.ParityCapture.Avalonia.csproj"));
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
        mainWindow.Should().NotContain("FREEX_PARITY_CAPTURE");
        mainWindow.Should().NotContain("InteractionValidationOptions");
        mainWindow.Should().NotContain("SeedNameBoxDropdownPhysicalFixture");
        mainWindow.Should().Contain("PrepareOptionalStartupState(startupArguments);");
        mainWindow.Should().Contain("Content = BuildContent();");
        mainWindow.IndexOf("PrepareOptionalStartupState(startupArguments);", StringComparison.Ordinal)
            .Should().BeLessThan(mainWindow.IndexOf("Content = BuildContent();", StringComparison.Ordinal));
        mainWindow.IndexOf("CompleteOptionalStartupState(startupArguments);", StringComparison.Ordinal)
            .Should().BeGreaterThan(mainWindow.IndexOf("Content = BuildContent();", StringComparison.Ordinal));
        File.Exists(Path.Combine(
            root,
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.TextBoxInlinePhysicalEvidence.cs")).Should().BeFalse();
        textBoxEditor.Should().NotContain("PhysicalEvidence");
        textBoxEditor.Should().NotContain("Environment.GetEnvironmentVariable");
        textBoxEditor.Should().NotContain("System.Text.Json");
        options.Should().NotContain("FREEX_PARITY_CAPTURE");
        options.Should().Contain("ExternalOptionsFixtureFactory?.Invoke()");
        captureProject.Should().NotContain("FREEX_PARITY_CAPTURE");
        captureProgram.Should().Contain("ParityCaptureOptions.TryParse(");
        captureProgram.Should().Contain("App.ExternalStartupCoordinator =");
    }
}
