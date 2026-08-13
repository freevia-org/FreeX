using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class RendererUtilityOwnershipTests
{
    [Fact]
    public void ScreenClipAdaptersDelegateGeometryAndInsertionMetadataToPresentation()
    {
        var overlay = ReadSource("freew", "FreeW.App.Host", "Editing", "ScreenClipOverlay.cs");
        var capture = ReadSource("freew", "FreeW.App.Host", "Editing", "ScreenshotCapture.cs");
        var factory = ReadSource("freew", "FreeW.App.Presentation", "Dialogs", "ScreenClipImageFactory.cs");

        overlay.Should().Contain("ScreenClipPlanner.BuildPhysicalSelectionFromMappedEndpoints(");
        overlay.Should().NotContain("System.Math.Round(");
        capture.Should().Contain("ScreenClipImageFactory.Create(pngBytes, pixelWidth, pixelHeight)");
        capture.Should().NotContain("ScreenClipPlanner.BuildImageInsertionPlan(");
        capture.Should().NotContain("new InlineImage(");
        factory.Should().Contain("ScreenClipPlanner.BuildImageInsertionPlan(");
        factory.Should().Contain("new InlineImage(");
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
        var mainWindow = ReadSource("freew", "FreeW.App.Host", "MainWindow.cs");
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
            mainWindow,
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
        mainWindow.Should().Contain("WpfRgbColorAdapter.ParseColorToken(colorHex)");
    }

    [Fact]
    public void MergeReconciliationKeepsSharedFreeWPoliciesAtRendererBoundaries()
    {
        var view = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var ruler = ReadSource("freew", "FreeW.App.Host", "Editing", "Ruler.cs");
        var mainWindow = ReadSource("freew", "FreeW.App.Host", "MainWindow.cs");
        var ribbon = ReadSource("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var svg = ReadSource("freew", "FreeW.App.Host", "SvgRasterizerHelper.cs");
        var zoom = ReadSource("freew", "FreeW.App.Host", "ZoomDialog.cs");
        var snapshot = ReadSource(
            "freew",
            "FreeW.App.Presentation",
            "DocumentView",
            "AccessibleDocumentSnapshotPlanner.cs");
        var avaloniaFileWorkflow = ReadSource(
            "shared",
            "Free.Shared.Shell.Avalonia",
            "SisterAvaloniaFileCommandWorkflow.cs");

        ruler.Should().Contain("DocumentRulerInteractionPlanner.");
        snapshot.Should().Contain("HeaderFooterTextSelectionPlanner.Clamp(");

        mainWindow.Should().Contain("new ReviewingPaneSession(");
        mainWindow.Should().Contain("ReviewingPanePresentationPlanner.");
        view.Should().Contain("_editingSession.Review.TryResolveAllRevisions(");
        view.Should().Contain("private readonly OutlineCollapseState _outlineCollapse");
        view.Should().NotContain("private readonly HashSet<int> _collapsedHeadings");

        mainWindow.Should().Contain("_documentWindowPlanner.CreateNext(");
        mainWindow.Should().Contain("newWindow._file.LoadDocumentWindow(plan)");
        mainWindow.Should().Contain("FreeWDocumentWindowPlanner.FormatWindowSuffix(");

        ribbon.Should().Contain("new FreeWPictureImportWorkflow(");
        ribbon.Should().Contain("PictureInsertionPlanner.FitIcon(");
        svg.Should().Contain("PictureInsertionPlanner.BuildVectorRasterSurface(");
        svg.Should().Contain("PictureInsertionPlanner.CreatePngImage(");
        zoom.Should().Contain("ZoomDialogFitFactors fitFactors");

        avaloniaFileWorkflow.Should().Contain("string WindowSuffix = \"\"");
        avaloniaFileWorkflow.Should().Contain("string GroupSuffix = \"\"");
        avaloniaFileWorkflow.Should().Contain("public void ApplyDocumentState(");
        avaloniaFileWorkflow.Should().Contain("windowSuffix: _titleSpec.WindowSuffix");
        avaloniaFileWorkflow.Should().Contain("groupSuffix: _titleSpec.GroupSuffix");
    }

    private static string ReadSource(params string[] relativePath)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(relativePath.Aggregate(root, Path.Combine));
    }
}
