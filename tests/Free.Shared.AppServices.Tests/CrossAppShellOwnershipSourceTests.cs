namespace Free.Shared.AppServices.Tests;

public sealed class CrossAppShellOwnershipSourceTests
{
    [Theory]
    [InlineData("src", "FreeX.App.Host", "MainWindow.Shell.cs", "SisterAppClientFrameContractPlanner.Plan(")]
    [InlineData("src", "FreeX.App.Avalonia", "MainWindow.cs", "SisterAppClientFrameBuilder.Build(new SisterAppClientFrameSpec(")]
    [InlineData("freew", "FreeW.App.Host", "MainWindow.cs", "SisterAppClientFrameBuilder.Build(")]
    [InlineData("freew", "FreeW.App.Avalonia", "MainWindow.cs", "SisterAppClientFrameBuilder.Build(SisterAppClientFrameSpec.ForWorkArea(")]
    [InlineData("freep", "FreeP.App.Host", "MainWindow.cs", "SisterAppClientFrameBuilder.Build(")]
    [InlineData("freep", "FreeP.App.Avalonia", "MainWindow.cs", "SisterAppClientFrameBuilder.Build(SisterAppClientFrameSpec.ForWorkArea(")]
    public void SisterAppMainWindows_UseSharedClientFrameContracts(
        string appRoot,
        string appProject,
        string fileName,
        string expectedContractToken)
    {
        Read(appRoot, appProject, fileName).Should().Contain(expectedContractToken);
    }

    [Theory]
    [InlineData("src", "FreeX.App.Host", "FreeX.App.Host.csproj", "Free.Shared.Shell.Wpf", "Free.Shared.Shell.Avalonia")]
    [InlineData("src", "FreeX.App.Avalonia", "FreeX.App.Avalonia.csproj", "Free.Shared.Shell.Avalonia", "Free.Shared.Shell.Wpf")]
    [InlineData("freew", "FreeW.App.Host", "FreeW.App.Host.csproj", "Free.Shared.Shell.Wpf", "Free.Shared.Shell.Avalonia")]
    [InlineData("freew", "FreeW.App.Avalonia", "FreeW.App.Avalonia.csproj", "Free.Shared.Shell.Avalonia", "Free.Shared.Shell.Wpf")]
    [InlineData("freep", "FreeP.App.Host", "FreeP.App.Host.csproj", "Free.Shared.Shell.Wpf", "Free.Shared.Shell.Avalonia")]
    [InlineData("freep", "FreeP.App.Avalonia", "FreeP.App.Avalonia.csproj", "Free.Shared.Shell.Avalonia", "Free.Shared.Shell.Wpf")]
    public void RendererProjects_ReferenceOnlyTheirSharedShellAdapter(
        string appRoot,
        string appProject,
        string projectFile,
        string expectedAdapter,
        string otherAdapter)
    {
        var project = Read(appRoot, appProject, projectFile);

        project.Should().Contain(expectedAdapter);
        project.Should().NotContain(otherAdapter);
    }

    [Fact]
    public void PortableContracts_ContainNoToolkitOwnership()
    {
        var source = string.Join(
            Environment.NewLine,
            Read("shared", "Free.Shared.AppServices", "SisterAppClientFrameContract.cs"),
            Read("shared", "Free.Shared.AppServices", "SisterBackstagePaneTextResources.cs"),
            Read("shared", "Free.Shared.AppServices", "FileLifecyclePlanner.cs"));

        source.Should().NotContain("System.Windows");
        source.Should().NotContain("Avalonia.");
    }

    [Fact]
    public void SharedDescriptors_OwnFallbackAndFileSelectionSemantics()
    {
        var descriptor = Read(
            "shared", "Free.Shared.AppServices", "SisterBackstagePaneTextResources.cs");
        var sharedPanePlanner = Read(
            "shared", "Free.Shared.Shell", "SisterBackstagePaneSpecPlanner.cs");
        var freeXValidation = Read(
            "src", "FreeX.App.Presentation", "DefinedNames", "DefinedNameValidationMessages.cs");
        var freeWBackstage = Read(
            "freew", "FreeW.App.Presentation", "Backstage", "BackstagePaneSurfacePlanner.cs");
        var wpfFileDialog = Read(
            "shared", "Free.Shared.Shell.Wpf", "WpfFileDialogService.cs");

        descriptor.Should().Contain("LocalizedFallbackTextResolver.Resolve(");
        sharedPanePlanner.Should().NotContain("private static string Resolve(ResourceTextDescriptor");
        freeXValidation.Should().Contain("return Text.Resolve(textProvider);");
        freeXValidation.Should().NotContain("LocalizedFallbackTextResolver.Resolve(");
        freeWBackstage.Should().Contain("descriptor.Heading.Resolve(getText)");
        freeWBackstage.Should().NotContain("private static string Resolve(ResourceTextDescriptor");
        freeWBackstage.Should().NotContain("LocalizedFallbackTextResolver.Resolve(");
        wpfFileDialog.Should().Contain("public FileDialogSelection Selection => new(FileName);");
        wpfFileDialog.Should().NotContain("Chosen => !string.IsNullOrWhiteSpace(FileName)");
    }

    [Fact]
    public void ProductPresentationLayers_DoNotReimplementFallbackResolution()
    {
        var presentationProjects = new[]
        {
            ProjectDirectory("src", "FreeX.App.Presentation", "FreeX.App.Presentation.csproj"),
            ProjectDirectory("freew", "FreeW.App.Presentation", "FreeW.App.Presentation.csproj"),
            ProjectDirectory("freep", "FreeP.App.Presentation", "FreeP.App.Presentation.csproj"),
        };

        var directResolvers = presentationProjects
            .SelectMany(ProductSourceFiles)
            .Where(path => File.ReadAllText(path).Contains(
                "LocalizedFallbackTextResolver.Resolve(",
                StringComparison.Ordinal))
            .ToArray();

        directResolvers.Should().BeEmpty(
            "ResourceTextDescriptor owns resource-key fallback and mnemonic policy");
    }

    [Fact]
    public void ProductLayers_DoNotRedeclareSharedShellContracts()
    {
        var declarations = new[]
        {
            "enum SisterAppClientFrameSlotRole",
            "record SisterAppClientFrameContract",
            "static class SisterAppStatusBarChromeDefaults",
            "enum FileDialogSelectionStatus",
            "record struct FileDialogSelection",
            "record ResourceTextDescriptor(",
        };
        var productProjects = new[]
        {
            ProjectDirectory("src", "FreeX.App.Presentation", "FreeX.App.Presentation.csproj"),
            ProjectDirectory("src", "FreeX.App.Host", "FreeX.App.Host.csproj"),
            ProjectDirectory("src", "FreeX.App.Avalonia", "FreeX.App.Avalonia.csproj"),
            ProjectDirectory("freew", "FreeW.App.Presentation", "FreeW.App.Presentation.csproj"),
            ProjectDirectory("freew", "FreeW.App.Host", "FreeW.App.Host.csproj"),
            ProjectDirectory("freew", "FreeW.App.Avalonia", "FreeW.App.Avalonia.csproj"),
            ProjectDirectory("freep", "FreeP.App.Presentation", "FreeP.App.Presentation.csproj"),
            ProjectDirectory("freep", "FreeP.App.Host", "FreeP.App.Host.csproj"),
            ProjectDirectory("freep", "FreeP.App.Avalonia", "FreeP.App.Avalonia.csproj"),
        };

        var redeclarations = productProjects
            .SelectMany(ProductSourceFiles)
            .Select(path => (path, source: File.ReadAllText(path)))
            .Where(file => declarations.Any(declaration =>
                file.source.Contains(declaration, StringComparison.Ordinal)))
            .Select(file => file.path)
            .ToArray();

        redeclarations.Should().BeEmpty("portable shell contracts belong in shared projects");
    }

    private static IEnumerable<string> ProductSourceFiles(string projectDirectory) =>
        Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase));

    private static string ProjectDirectory(params string[] projectParts) =>
        Path.GetDirectoryName(TestWorkspaceFileLocator.FindFromWorkspaceRoot(projectParts))!;

    private static string Read(params string[] parts) =>
        File.ReadAllText(TestWorkspaceFileLocator.FindFromWorkspaceRoot(parts));
}
