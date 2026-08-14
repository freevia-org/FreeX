namespace FreeP.App.Compositor.Tests;

public sealed class PresentationAssetPickerProfileOwnershipTests
{
    [Fact]
    public void CatalogPreservesNativePickerCapabilitiesPerImportRoute()
    {
        var picture = PresentationAssetPickerProfileCatalog.For(PresentationAssetImportKind.Picture);
        picture.Wpf.Patterns.Should().Equal(
            "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.svg", "*.wmf", "*.emf");
        picture.Avalonia.Patterns.Should().Equal(picture.Wpf.Patterns);
        picture.UseUnownedWpfDialog.Should().BeTrue();

        var audio = PresentationAssetPickerProfileCatalog.For(PresentationAssetImportKind.Audio);
        audio.Wpf.Patterns.Should().Equal("*.mp3", "*.m4a", "*.wav", "*.wma");
        audio.Avalonia.Patterns.Should().Equal(PresentationMediaFileTypeCatalog.AudioFilePatterns);
        audio.UseUnownedWpfDialog.Should().BeTrue();

        var transitionSound = PresentationAssetPickerProfileCatalog.For(
            PresentationAssetImportKind.TransitionSound);
        transitionSound.Wpf.Patterns.Should().Equal(PresentationMediaFileTypeCatalog.AudioFilePatterns);
        transitionSound.Avalonia.Patterns.Should().Equal(PresentationMediaFileTypeCatalog.AudioFilePatterns);
        transitionSound.UseUnownedWpfDialog.Should().BeFalse();

        var zoomCover = PresentationAssetPickerProfileCatalog.For(
            PresentationAssetImportKind.ZoomCoverImage);
        zoomCover.Wpf.Patterns.Should().Contain("*.webp");
        zoomCover.Avalonia.Patterns.Should().Contain("*.wmf").And.Contain("*.emf");
    }

    [Fact]
    public void RendererPickerPortsTranslatePortableProfilesWithoutImportKindPolicy()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var wpf = Read(root, "freep", "FreeP.App.Host", "MainWindow.AssetImports.cs");
        var avalonia = Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.AssetImports.cs");
        var avaloniaWindow = Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs");

        wpf.Should().Contain("request.PickerProfile")
            .And.Contain("pickerProfile.Wpf.BuildWpfFilter()")
            .And.NotContain("BuildFilter(PresentationAssetImportKind")
            .And.NotContain("UsesUnownedDialog(PresentationAssetImportKind")
            .And.NotContain("*.png;*.jpg;*.jpeg");
        avalonia.Should().Contain("request.PickerProfile.Avalonia")
            .And.NotContain("kind switch")
            .And.NotContain("PresentationAssetImportKind.Video =>")
            .And.NotContain("PictureFileType");
        avaloniaWindow.Should().NotContain("private static readonly FilePickerFileType PictureFileType")
            .And.NotContain("private static readonly FilePickerFileType VideoFileType")
            .And.NotContain("private static readonly FilePickerFileType AudioFileType")
            .And.NotContain("private static readonly FilePickerFileType EmbeddedObjectFileType");
    }

    [Fact]
    public void RendererExecutionPortsRefreshNativeSurfacesAfterEmbeddedObjectInsertion()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var wpf = Read(root, "freep", "FreeP.App.Host", "MainWindow.AssetImports.cs");
        var avalonia = Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.AssetImports.cs");

        wpf.Should().Contain("EmbeddedObjectInserted: () =>")
            .And.Contain("RefreshCanvas();")
            .And.Contain("UpdateSlideCount();");
        avalonia.Should().Contain("EmbeddedObjectInserted: () =>")
            .And.Contain("RefreshCanvas();")
            .And.Contain("UpdateStatus();");
    }

    [Fact]
    public void PortableCatalogHasNoNativeFrameworkDependencies()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = Read(
            root,
            "freep",
            "FreeP.App.Presentation",
            "PresentationAssetPickerProfileCatalog.cs");

        source.Should().Contain("public static class PresentationAssetPickerProfileCatalog")
            .And.Contain("public static PresentationAssetPickerProfile For(")
            .And.NotContain("System.Windows")
            .And.NotContain("Avalonia.Controls")
            .And.NotContain("Avalonia.Platform");
    }

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));
}
