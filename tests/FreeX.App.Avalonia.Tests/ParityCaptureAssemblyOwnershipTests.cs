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
        assembly.GetType("FreeX.App.Avalonia.MainWindow+PrintPreviewCapturePage").Should().BeNull();
        assembly.GetType("FreeX.App.Avalonia.MainWindow+PrintPreviewCaptureTextRun").Should().BeNull();
        assembly.GetType("FreeX.App.Avalonia.MainWindow+SubtotalDialogCaptureState").Should().BeNull();
        assembly.GetType("FreeX.App.Avalonia.MainWindow+RendererValidationAccess").Should().BeNull();
        assembly.GetType("FreeX.App.Avalonia.RendererShellObservation").Should().BeNull();
        assembly.GetType("FreeX.App.Avalonia.RendererCommandObservation").Should().BeNull();
        assembly.GetType("FreeX.App.Avalonia.RendererFormattingState").Should().BeNull();
        assembly.GetType("FreeX.App.Avalonia.Program")!
            .GetMethod(
                "RunValidationToolHost",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Should().BeNull();
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
    public void ValidationProject_OwnsConditionallyCompiledRendererAccess()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var shippingDirectory = Path.Combine(root, "src", "FreeX.App.Avalonia");
        var validationDirectory = Path.Combine(root, "tools", "FreeX.Validation.Avalonia");

        File.Exists(Path.Combine(shippingDirectory, "MainWindow.RendererValidationAccess.cs"))
            .Should().BeFalse();
        File.Exists(Path.Combine(
                validationDirectory,
                "RendererHost",
                "MainWindow.RendererValidationAccess.cs"))
            .Should().BeTrue();
        File.Exists(Path.Combine(validationDirectory, "RendererHost", "Program.ValidationHost.cs"))
            .Should().BeTrue();

        var shippingProject = File.ReadAllText(Path.Combine(
            shippingDirectory,
            "FreeX.App.Avalonia.csproj"));
        var validationProject = File.ReadAllText(Path.Combine(
            validationDirectory,
            "FreeX.Validation.Avalonia.csproj"));
        var shippingProgram = File.ReadAllText(Path.Combine(shippingDirectory, "Program.cs"));

        shippingProject.Should().Contain("Condition=\"'$(FreeXValidationHost)' == 'true'\"");
        shippingProject.Should().Contain("..\\..\\tools\\FreeX.Validation.Avalonia\\RendererHost");
        validationProject.Should().Contain("Compile Remove=\"RendererHost\\**\\*.cs\"");
        validationProject.Should().Contain("AdditionalProperties=\"FreeXValidationHost=true\"");
        shippingProgram.Should().NotContain("RunValidationToolHost");
        shippingProgram.Should().NotContain("RendererValidationAccess");
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
        var rendererAccess = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.RendererAccess.cs"));
        var printPreview = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.PrintPreview.cs"));
        var conditionalFormat = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.ConditionalFormat.cs"));
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
        rendererAccess.Should().NotContain("PrintPreviewCapture");
        rendererAccess.Should().NotContain("SubtotalDialogCaptureState");
        rendererAccess.Should().NotContain("ParityDialogWidth");
        printPreview.Should().NotContain("SeedPrintPreviewParityReport");
        printPreview.Should().NotContain("PrintPreviewCapturePage");
        printPreview.Should().NotContain("BuildPreviewParityPageView");
        conditionalFormat.Should().NotContain("parityCapture");
        captureProject.Should().NotContain("FREEX_PARITY_CAPTURE");
        captureProgram.Should().Contain("ParityCaptureOptions.TryParse(");
        captureProgram.Should().Contain("App.ExternalStartupCoordinator =");
    }
}
