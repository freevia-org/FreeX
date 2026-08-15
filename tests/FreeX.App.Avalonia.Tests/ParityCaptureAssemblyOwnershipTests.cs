extern alias ProductionAvalonia;

using System.Reflection;
using System.Runtime.CompilerServices;
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
    public void ShippingAssembly_DoesNotExposeRendererTestAccessOrTestFriendships()
    {
        var assembly = typeof(ProductionAvalonia::FreeX.App.Avalonia.MainWindow).Assembly;
        var mainWindow = typeof(ProductionAvalonia::FreeX.App.Avalonia.MainWindow);
        const BindingFlags allMembers = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static;

        mainWindow.GetMembers(allMembers)
            .Select(member => member.Name)
            .Should().NotContain(name =>
                name.Contains("ForTest", StringComparison.Ordinal) ||
                name.Contains("ForParityCapture", StringComparison.Ordinal) ||
                name.Contains("OverrideForTest", StringComparison.Ordinal));

        mainWindow.GetField("_sheetGridBuildCount", allMembers).Should().BeNull();
        mainWindow.GetField("_sheetTabsBuildCount", allMembers).Should().BeNull();
        mainWindow.GetField("ChromeSurfaceColor", allMembers).Should().BeNull();

        assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName)
            .Should().NotContain(name =>
                name.Contains("Tests", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Capture", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ShippingAssembly_DoesNotOwnDialogInspectionSupport()
    {
        var assembly = typeof(ProductionAvalonia::FreeX.App.Avalonia.MainWindow).Assembly;
        var mainWindow = typeof(ProductionAvalonia::FreeX.App.Avalonia.MainWindow);
        const BindingFlags allMembers = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static;
        string[] inspectionTypes =
        [
            "FindDialogInspection",
            "ReplaceDialogInspection",
            "GoToDialogInspection",
            "GoToSpecialDialogInspection",
            "SortDialogInspection",
            "DataValidationDialogInspection",
            "FormatCellsDialogInspection",
            "ConditionalFormatRuleDialogInspection",
            "ManageConditionalFormatsDialogInspection",
            "PasteSpecialDialogInspection",
        ];

        inspectionTypes.Should().AllSatisfy(name =>
            assembly.GetType($"FreeX.App.Avalonia.MainWindow+{name}").Should().BeNull());
        mainWindow.GetMembers(allMembers)
            .Select(member => member.Name)
            .Should().NotContain([
                "CompleteDialogInspection",
                "ShowHeaderFooterPictureFormatParityDialogAsync",
                "ShowUnhideWindowParityDialogAsync",
                "ShowSelectDataDialogAsync",
            ]);
    }

    [Fact]
    public void ParityRendererHost_OwnsExtractedRendererTestAccess()
    {
        var supportMainWindow = typeof(global::FreeX.App.Avalonia.MainWindow);
        const BindingFlags allMembers = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static;
        var memberNames = supportMainWindow.GetMembers(allMembers)
            .Select(member => member.Name)
            .ToHashSet(StringComparer.Ordinal);

        memberNames.Should().Contain([
            "RebuildSheetGridForTest",
            "RaiseKeyDownForTest",
            "ApplyQuickAnalysisConditionalFormatItemForTestAsync",
            "BackstageCommandActivationOverrideForTest",
            "PasteSpecialWorkflowOverrideForTest",
            "WorkbookSaveAsPickerOverrideForTest",
            "AllowCloseWithoutDirtyPromptForParityCapture",
            "ChromeSurfaceColor",
        ]);

        typeof(global::FreeX.App.Avalonia.MainWindow)
            .GetNestedTypes(allMembers)
            .Select(type => type.Name)
            .Should().Contain([
                "FindDialogInspection",
                "ReplaceDialogInspection",
                "GoToDialogInspection",
                "GoToSpecialDialogInspection",
                "SortDialogInspection",
                "DataValidationDialogInspection",
                "FormatCellsDialogInspection",
                "ConditionalFormatRuleDialogInspection",
                "ManageConditionalFormatsDialogInspection",
                "PasteSpecialDialogInspection",
            ]);
    }

    [Fact]
    public void ParityRendererProject_OwnsExtractedTestSupportSources()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var shippingDirectory = Path.Combine(root, "src", "FreeX.App.Avalonia");
        var supportDirectory = Path.Combine(root, "tools", "FreeX.ParityCapture.Avalonia", "TestSupport");
        var shippingProject = File.ReadAllText(Path.Combine(
            shippingDirectory,
            "FreeX.App.Avalonia.csproj"));
        var parityProject = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "FreeX.ParityCapture.Avalonia",
            "FreeX.ParityCapture.Avalonia.csproj"));

        Directory.GetFiles(supportDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .Should().HaveCountGreaterThan(20);
        parityProject.Should().Contain("<Compile Include=\"TestSupport\\**\\*.cs\" />");
        shippingProject.Should().NotContain("FreeX.App.Avalonia.Tests");
        shippingProject.Should().NotContain("FreeX.App.Avalonia.CaptureTests");

        Directory.GetFiles(shippingDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .SelectMany(File.ReadLines)
            .Should().NotContain(line =>
                line.Contains("internal", StringComparison.Ordinal) &&
                (line.Contains("ForTest", StringComparison.Ordinal) ||
                 line.Contains("ForParityCapture", StringComparison.Ordinal) ||
                 line.Contains("OverrideForTest", StringComparison.Ordinal)));
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
        File.Exists(Path.Combine(
                validationDirectory,
                "RendererHost",
                "MainWindow.DialogInspectionAccess.cs"))
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
        shippingProject.Should().Contain("<GlobalPropertiesToRemove>FreeXValidationHost</GlobalPropertiesToRemove>");
        shippingProject.Should().Contain("..\\..\\tools\\FreeX.Validation.Avalonia\\RendererHost");
        shippingProject.Should().Contain("MainWindow.DialogInspectionAccess.cs");
        validationProject.Should().Contain("Compile Remove=\"RendererHost\\**\\*.cs\"");
        validationProject.Should().Contain("AdditionalProperties=\"FreeXValidationHost=true\"");
        shippingProgram.Should().NotContain("RunValidationToolHost");
        shippingProgram.Should().NotContain("RendererValidationAccess");
    }

    [Fact]
    public void ShippingSources_DoNotOwnDialogInspectionContractsOrParityEntryPoints()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var shippingDirectory = Path.Combine(root, "src", "FreeX.App.Avalonia");
        var supportSource = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "FreeX.Validation.Avalonia",
            "RendererHost",
            "MainWindow.DialogInspectionAccess.cs"));
        var shippingSource = Directory.GetFiles(shippingDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .Aggregate(string.Empty, static (all, source) => all + source);

        shippingSource.Should().NotContain("record FindDialogInspection");
        shippingSource.Should().NotContain("record ReplaceDialogInspection");
        shippingSource.Should().NotContain("record GoToDialogInspection");
        shippingSource.Should().NotContain("record GoToSpecialDialogInspection");
        shippingSource.Should().NotContain("record SortDialogInspection");
        shippingSource.Should().NotContain("record DataValidationDialogInspection");
        shippingSource.Should().NotContain("record FormatCellsDialogInspection");
        shippingSource.Should().NotContain("record ConditionalFormatRuleDialogInspection");
        shippingSource.Should().NotContain("record ManageConditionalFormatsDialogInspection");
        shippingSource.Should().NotContain("record PasteSpecialDialogInspection");
        shippingSource.Should().NotContain("CompleteDialogInspection");
        shippingSource.Should().NotContain("ShowHeaderFooterPictureFormatParityDialogAsync");
        shippingSource.Should().NotContain("ShowUnhideWindowParityDialogAsync");
        shippingSource.Should().NotContain("ShowSelectDataDialogAsync");

        supportSource.Should().Contain("record FindDialogInspection");
        supportSource.Should().Contain("record PasteSpecialDialogInspection");
        supportSource.Should().Contain("CompleteDialogInspection");
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
