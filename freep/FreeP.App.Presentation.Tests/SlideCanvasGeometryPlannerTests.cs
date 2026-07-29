using System.IO;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideCanvasGeometryPlannerTests
{
    private const double EmuPerDip = 9525.0;

    private static long ToEmu(double dip) => (long)Math.Round(dip * EmuPerDip);

    [Fact]
    public void ShapeBoundsToScreen_UsesSharedUniformFitTransform()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        slide.Shapes.Add(new SlideShape
        {
            Id = 7,
            OffsetXEmu = ToEmu(10),
            OffsetYEmu = ToEmu(20),
            ExtentCxEmu = ToEmu(80),
            ExtentCyEmu = ToEmu(40),
        });
        var transform = new SlideTransformCore(2, 100, 50, 960, 540);

        var rect = SlideCanvasGeometryPlanner.ShapeBoundsToScreen(
            slide,
            presentation,
            7,
            transform);

        rect.Should().Be(new SlideScreenRect(120, 90, 160, 80));
    }

    [Fact]
    public void PlanEditorPlacement_ClampsToMinimumOverlaySize()
    {
        var placement = SlideCanvasGeometryPlanner.PlanEditorPlacement(
            new SlideScreenRect(12, 34, 10, 5),
            minimumWidth: 40,
            minimumHeight: 20);

        placement.Should().Be(new InCanvasEditorPlacement(12, 34, 40, 20));
    }

    [Fact]
    public void PlanEditorPlacement_RetainsRotationAndUnexpandedShapeCenter()
    {
        var placement = SlideCanvasGeometryPlanner.PlanEditorPlacement(
            new SlideScreenRect(12, 34, 10, 5),
            minimumWidth: 40,
            minimumHeight: 20,
            rotationDegrees: 30,
            flipHorizontal: true,
            flipVertical: false);

        placement.HasTransform.Should().BeTrue();
        placement.RotationDegrees.Should().Be(30);
        placement.FlipHorizontal.Should().BeTrue();
        placement.FlipVertical.Should().BeFalse();
        placement.EffectiveTransformOriginX.Should().Be(5);
        placement.EffectiveTransformOriginY.Should().Be(2.5);
        placement.Width.Should().Be(40);
        placement.Height.Should().Be(20);
    }

    [Fact]
    public void PlanMove_AppliesGridSnapToPreviewAndCommitDelta()
    {
        var transform = new SlideTransformCore(2, 10, 20, 400, 300);
        var plan = CanvasGesturePlanner.PlanMove(new CanvasMoveRequest(
            StartScreen: new CanvasGesturePoint(0, 0),
            CurrentScreen: new CanvasGesturePoint(15, 0),
            Transform: transform,
            Shapes:
            [
                new CanvasMoveShapeState(
                    ShapeId: 1,
                    OffsetXEmu: 0,
                    OffsetYEmu: 0,
                    ExtentCxEmu: ToEmu(40),
                    ExtentCyEmu: ToEmu(20)),
            ],
            CurrentSlide: new Slide(),
            SnapToGrid: true,
            SnapToShapes: false,
            BypassSnap: false));

        plan.DeltaXEmu.Should().Be(ToEmu(8));
        plan.DeltaYEmu.Should().Be(0);
        plan.PreviewRects.Should().ContainSingle();
        plan.PreviewRects[0].ScreenRect.Should().Be(new SlideScreenRect(26, 20, 80, 40));
        plan.PreviewBounds.Should().Be(new SlideScreenRect(26, 20, 80, 40));
        plan.SnapGuides.Should().Contain(g => !g.IsHorizontal && g.Position == 8);
    }

    [Fact]
    public void ScreenRectBetween_NormalizesDragCorners()
    {
        SlideCanvasGeometryPlanner.ScreenRectBetween(
                new CanvasGesturePoint(50, 80),
                new CanvasGesturePoint(10, 20))
            .Should()
            .Be(new SlideScreenRect(10, 20, 40, 60));
    }

    [Fact]
    public void WpfAndAvaloniaAdapters_DelegateCoordinateMathToSharedPlanners()
    {
        var wpfGesture = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Wpf",
            "CanvasGestureHandler.cs");
        var avaloniaGesture = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "AvaloniaCanvasGestureHandler.cs");

        foreach (var source in new[] { wpfGesture, avaloniaGesture })
        {
            source.Should().Contain("CanvasGesturePlanner.CaptureMoveState");
            source.Should().Contain("CanvasGesturePlanner.PlanMove");
            source.Should().Contain("SlideCanvasGeometryPlanner.EmuBoundsToScreen");
            source.Should().Contain("SlideCanvasGeometryPlanner.ScreenRectBetween");
            source.Should().Contain("SlideCanvasGeometryPlanner.ShapeBoundsToScreen");
            source.Should().NotContain("BoundsToScreenRect");
            source.Should().NotContain("SnapEngine.Snap(");
        }

        var wpfText = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Wpf",
            "InCanvasTextEditor.cs");
        var avaloniaText = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "AvaloniaInCanvasTextEditor.cs");
        var wpfTable = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Wpf",
            "InCanvasTableCellEditor.cs");

        foreach (var source in new[] { wpfText, avaloniaText })
        {
            source.Should().Contain("InCanvasTextEditPlanner.BeginShapeEdit");
            source.Should().NotContain("* xf.Scale + xf.OffsetX");
        }

        wpfTable.Should().NotContain("* xf.Scale + xf.OffsetX");
        wpfTable.Should().Contain("TableCellEditPlanner.BeginEdit");
        wpfTable.Should().Contain("TableCellEditPlanner.PlanSelectedCell");
        wpfTable.Should().Contain("ApplyInitialSelection(_cellTextBox, editStart.InitialSelection)");
        wpfTable.Should().Contain("RichTextBox");
        wpfTable.Should().Contain("ExecuteCellFormattingCommand(EditingCommands.ToggleBold)");
        wpfTable.Should().Contain("ApplyWithPreservedSelection");
        wpfTable.Should().Contain("Selection.ApplyPropertyValue");

        var avaloniaTable = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "AvaloniaTableCellEditAdapter.cs");
        avaloniaTable.Should().Contain("TableCellEditPlanner.BeginEdit");
        avaloniaTable.Should().Contain("TableCellEditPlanner.PlanSelectedCell");
        avaloniaTable.Should().Contain("TableCellEditPlanner.PlanTextFormat");

        var wpfAdorner = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Wpf",
            "SelectionAdorner.cs");
        var avaloniaAdorner = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SelectionAdornerLayer.cs");

        foreach (var source in new[] { wpfAdorner, avaloniaAdorner })
        {
            source.Should().Contain("SlideCanvasGeometryPlanner.SnapGuideToScreenPosition");
            source.Should().NotContain("g.Position * xf.Scale");
        }

        var avaloniaCanvas = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SlideCanvas.cs");
        avaloniaCanvas.Should().Contain("SlideTransformCore.Compute");
        avaloniaCanvas.Should().NotContain("Math.Min(renderW / _slideWidthDip");
    }

    private static string ReadWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var parts = new string[relativeParts.Length + 1];
            parts[0] = directory.FullName;
            relativeParts.CopyTo(parts, 1);

            var candidate = Path.Combine(parts);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate workspace file.",
            Path.Combine(relativeParts));
    }
}
