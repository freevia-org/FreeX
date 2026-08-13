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

    private static string RepoFile(params string[] segments) =>
        TestWorkspaceFileLocator.FindFileFromBaseDirectory(segments);
}
