using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class RendererUtilityOwnershipTests
{
    [Fact]
    public void ScreenClipAdaptersDelegateGeometryAndInsertionMetadataToPresentation()
    {
        var overlay = ReadSource("freew", "FreeW.App.Host", "Editing", "ScreenClipOverlay.cs");
        var capture = ReadSource("freew", "FreeW.App.Host", "Editing", "ScreenshotCapture.cs");

        overlay.Should().Contain("ScreenClipPlanner.BuildPhysicalSelectionFromMappedEndpoints(");
        overlay.Should().NotContain("System.Math.Round(");
        capture.Should().Contain("ScreenClipPlanner.BuildImageInsertionPlan(");
        capture.Should().NotContain("PxPerPoint");
        capture.Should().NotContain("MaxWidthPt");
    }

    [Fact]
    public void StandaloneTabLeaderIsTheOnlyWpfOwner()
    {
        var view = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var leader = ReadSource("freew", "FreeW.App.Host", "Editing", "TabStopLeaderElement.cs");

        view.Should().Contain("new TabStopLeaderElement(plan, brush)");
        view.Should().NotContain("private sealed class TabStopLeaderElement");
        leader.Should().Contain("public sealed class TabStopLeaderElement");
    }

    [Fact]
    public void EligibleWpfAndToolRgbParsingUsesTheSharedCodecBoundary()
    {
        var adapter = ReadSource("freew", "FreeW.App.Host", "Editing", "WpfRgbColorAdapter.cs");
        var hostProject = ReadSource("freew", "FreeW.App.Host", "FreeW.App.Host.csproj");
        var modelProject = ReadSource("freew", "FreeW.Core.Model", "FreeW.Core.Model.csproj");
        var toolProject = ReadSource("freew", "tools", "FreeW.FidelityRender", "FreeW.FidelityRender.csproj");
        var targetSources = new[]
        {
            ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs"),
            ReadSource("freew", "FreeW.App.Host", "Editing", "SmartArtRenderer.cs"),
            ReadSource("freew", "FreeW.App.Host", "Editing", "TableCellBorderChrome.cs"),
            ReadSource("freew", "FreeW.App.Host", "Editing", "TabStopLeaderElement.cs"),
            ReadSource("freew", "FreeW.App.Host", "PrintPreviewWindow.cs"),
            ReadSource("freew", "FreeW.Core.Model", "Shapes.cs"),
            ReadSource("freew", "tools", "FreeW.FidelityRender", "Program.cs"),
        };

        adapter.Should().Contain("DrawingMlRgbColor.TryParseHexRgb(token, out var parsed)");
        hostProject.Should().Contain("Free.Shared.Drawing\\Free.Shared.Drawing.csproj");
        modelProject.Should().Contain("Free.Shared.Drawing\\Free.Shared.Drawing.csproj");
        toolProject.Should().Contain("Free.Shared.Drawing\\Free.Shared.Drawing.csproj");
        adapter.Split("ColorConverter.ConvertFromString(", StringSplitOptions.None)
            .Length.Should().Be(2);
        targetSources.Should().OnlyContain(source => !source.Contains("ColorConverter.ConvertFromString(", StringComparison.Ordinal));
        targetSources[5].Should().Contain("DrawingMlRgbColor.TryParseHexRgb(hex, out var color)");
        targetSources[6].Should().Contain("DrawingMlRgbColor.TryParseHexRgb(hex, out var color)");
    }

    private static string ReadSource(params string[] relativePath)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(relativePath.Aggregate(root, Path.Combine));
    }
}
