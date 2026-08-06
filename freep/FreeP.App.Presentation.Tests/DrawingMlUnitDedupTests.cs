using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class DrawingMlUnitDedupTests
{
    [Fact]
    public void DrawingMlUnitConsumers_PreserveExistingConversionBehavior()
    {
        SlideTransformCore.DipToEmu(2).Should().Be(DrawingMlCoordinateUnits.EmuPerPixel * 2);
        SlideTransformCore.DipToEmu(-2).Should().Be(-DrawingMlCoordinateUnits.EmuPerPixel * 2);
        SlideTransformCore.EmuToDip(DrawingMlCoordinateUnits.EmuPerPixel * 3).Should().Be(3);

        SlideShowHostPlanner.EmusPerDip.Should().Be(DrawingMlCoordinateUnits.EmuPerPixel);
        CanvasGesturePlanner.MinimumShapeSizeEmu.Should().Be(DrawingMlCoordinateUnits.EmuPerInch / 10);
        SlideSizeDialogPlanner.EmuPerInch.Should().Be(DrawingMlCoordinateUnits.EmuPerInch);
        SlideSizeDialogPlanner.MinimumSlideSizeEmu.Should().Be(DrawingMlCoordinateUnits.EmuPerInch / 2);
        SlideSizeDialogPlanner.Standard43Emu.Should().Be(
            (DrawingMlCoordinateUnits.EmuPerInch * 10, DrawingMlCoordinateUnits.EmuPerInch * 15 / 2));
        SlideSizeDialogPlanner.Widescreen169Emu.Should().Be(
            (DrawingMlCoordinateUnits.EmuPerInch * 40 / 3, DrawingMlCoordinateUnits.EmuPerInch * 15 / 2));
    }

    [Fact]
    public void FreePPresentationUnitConsumers_UseSharedDrawingMlUnits()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var appFiles = new[]
        {
            Read(root, "freep", "FreeP.App.Presentation", "CanvasGesturePlanner.cs"),
            Read(root, "freep", "FreeP.App.Presentation", "EditingSession.cs"),
            Read(root, "freep", "FreeP.App.Presentation", "SlideCompositor.cs"),
            Read(root, "freep", "FreeP.App.Presentation", "SlideShowHostPlanner.cs"),
            Read(root, "freep", "FreeP.App.Presentation", "SlideSizeDialogPlanner.cs"),
            Read(root, "freep", "FreeP.App.Presentation", "SlideTransformCore.cs"),
            Read(root, "freep", "FreeP.App.Presentation", "SmartArtLayoutEngine.cs"),
            Read(root, "freep", "FreeP.App.Presentation", "SnapEngine.cs"),
            Read(root, "freep", "FreeP.App.Presentation", "TableCellHitTester.cs")
        };

        string.Join(Environment.NewLine, appFiles)
            .Should()
            .Contain("DrawingMlCoordinateUnits.EmuPerPixel")
            .And.Contain("DrawingMlCoordinateUnits.EmuPerInch")
            .And.NotContain("9525")
            .And.NotContain("914400")
            .And.NotContain("914_400")
            .And.NotContain("9_144_000")
            .And.NotContain("6_858_000")
            .And.NotContain("12_192_000")
            .And.NotContain("182880");

        Read(root, "freep", "FreeP.Core.IO", "PptxPackageWriter.cs")
            .Should()
            .Contain("DrawingMlUnits.EmuPerInch * 15 / 2")
            .And.Contain("DrawingMlUnits.EmuPerInch * 10")
            .And.NotContain("6858000")
            .And.NotContain("9144000");
    }

    [Fact]
    public void SlideShowAnimationPlanner_OwnsSharedDrawingMlRgbParsing()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var planner = Read(
            root,
            "freep",
            "FreeP.App.Presentation",
            "SlideShowAnimationRendererSession.cs");
        var renderers = new[]
        {
            Read(root, "freep", "FreeP.App.Host", "SlideShowWindow.cs"),
            Read(root, "freep", "FreeP.App.Avalonia", "SlideShowWindow.cs"),
        };

        planner.Should().Contain("DrawingMlRgbColor.TryParseHexRgb");
        foreach (var renderer in renderers)
        {
            renderer.Should().NotContain("DrawingMlRgbColor.TryParseHexRgb")
                .And.NotContain("NumberStyles.HexNumber");
        }
    }

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));

}
