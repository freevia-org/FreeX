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
    public void AvaloniaCaptureTool_RendersTheShippingBackstageOverlayForLivePanes()
    {
        var capture = File.ReadAllText(RepoFile(
            "tools",
            "FreeX.ParityCapture.Avalonia",
            "Capture",
            "MainWindow.ParityCapture.cs"));

        // Home, Info, Print and Export come from the real overlay; only Account (a native dialog in the
        // shipping shell) keeps a capture-only pane.
        capture.Should().Contain("ShowBackstageOverlay();");
        capture.Should().Contain("TryActivateBackstagePane(pane);");
        capture.Should().Contain("capture.UsesCaptureOnlyAccountPane");
        capture.Should().NotContain("CreateParityCapturedBackstageInfoPane");
        capture.Should().NotContain("CreateParityCapturedBackstageHomePane");
    }

    private static string RepoFile(params string[] segments) =>
        TestWorkspaceFileLocator.FindFileFromBaseDirectory(segments);
}
