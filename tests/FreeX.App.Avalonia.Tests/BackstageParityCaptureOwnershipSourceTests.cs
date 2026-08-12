using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class BackstageParityCaptureOwnershipSourceTests
{
    [Fact]
    public void ProductionPresentation_HasNoCaptureSurfaceOrDetailCatalog()
    {
        var catalog = File.ReadAllText(RepoFile(
            "src",
            "FreeX.App.Presentation",
            "Backstage",
            "FreeXBackstagePaneCatalog.cs"));

        catalog.Should().NotContain("ParityCapture");
        catalog.Should().NotContain("ParityInfoDetails");
    }

    [Fact]
    public void AvaloniaCaptureTool_DerivesProjectionFromProductionWpfPlan()
    {
        var projection = File.ReadAllText(RepoFile(
            "tools",
            "FreeX.ParityCapture.Avalonia",
            "Capture",
            "BackstageInfoParityProjection.cs"));
        var capture = File.ReadAllText(RepoFile(
            "tools",
            "FreeX.ParityCapture.Avalonia",
            "Capture",
            "MainWindow.ParityCapture.cs"));

        projection.Should().Contain("FreeXBackstageInfoSurface.WpfInfoPane");
        projection.Should().Contain("wpfPlan with { Details = capturedDetails }");
        capture.Should().Contain("BackstageInfoParityProjection.Build(");
    }

    private static string RepoFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;

        directory.Should().NotBeNull();
        return Path.Combine([directory!.FullName, .. segments]);
    }
}
