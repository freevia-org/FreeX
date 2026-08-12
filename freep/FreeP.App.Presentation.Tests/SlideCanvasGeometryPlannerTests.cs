using System.IO;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideCanvasGeometryPlannerTests
{
    private const double EmuPerDip = 9525.0;

    private static long ToEmu(double dip) => (long)Math.Round(dip * EmuPerDip);

    [Theory]
    [InlineData(1000, 1000, 1000, 562.5)]
    [InlineData(double.PositiveInfinity, 270, 480, 270)]
    [InlineData(320, double.PositiveInfinity, 320, 180)]
    [InlineData(double.PositiveInfinity, double.PositiveInfinity, 960, 540)]
    [InlineData(0, 100, 1, 1)]
    public void FitAspectRatio_PreservesRendererMeasureGeometry(
        double availableWidth,
        double availableHeight,
        double expectedWidth,
        double expectedHeight)
    {
        SlideCanvasGeometryPlanner.FitAspectRatio(
                960,
                540,
                availableWidth,
                availableHeight)
            .Should().Be(new SlideCanvasSize(expectedWidth, expectedHeight));
    }

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
    public void PlanTableCellEditorPlacement_UsesTransformedTableFrameCenter()
    {
        var table = new ShapeBoundsDip(100, 50, 288, 96);
        var cell = new CellRectDip(196, 50, 96, 48);
        var view = new SlideTransformCore(2, 10, 20, 960, 540);

        var placement = SlideCanvasGeometryPlanner.PlanTableCellEditorPlacement(
            cell,
            table,
            view,
            minimumWidth: 30,
            minimumHeight: 18,
            rotationDegrees: 30,
            flipHorizontal: true,
            flipVertical: false);

        var tableCenter = view.SlideToScreen(244, 98);
        var cellCenter = view.SlideToScreen(244, 74);
        var expectedCenter = ShapeTransformPlanner.TransformPoint(
            tableCenter.X,
            tableCenter.Y,
            cellCenter.X,
            cellCenter.Y,
            rotationDeg: 30,
            flipH: true,
            flipV: false);

        placement.HasTransform.Should().BeTrue();
        placement.RotationDegrees.Should().Be(30);
        placement.FlipHorizontal.Should().BeTrue();
        placement.FlipVertical.Should().BeFalse();
        (placement.Left + placement.Width / 2).Should().BeApproximately(expectedCenter.X, 0.001);
        (placement.Top + placement.Height / 2).Should().BeApproximately(expectedCenter.Y, 0.001);
        placement.TransformOriginX.Should().Be(placement.Width / 2);
        placement.TransformOriginY.Should().Be(placement.Height / 2);
    }

    [Fact]
    public void TableCellHitTester_InvertsRotationAndFlipBeforeGridHitTest()
    {
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.Table,
            OffsetXEmu = ToEmu(100),
            OffsetYEmu = ToEmu(50),
            ExtentCxEmu = ToEmu(288),
            ExtentCyEmu = ToEmu(96),
            RotationDeg = 30,
            FlipH = true,
            FlipV = true,
            Table = MakeTable(),
        };

        var transformedCellCenter = ShapeTransformPlanner.TransformPoint(
            centerX: 244,
            centerY: 98,
            pointX: 244,
            pointY: 74,
            rotationDeg: 30,
            flipH: true,
            flipV: true);

        TableCellHitTester.HitTest(shape, transformedCellCenter.X, transformedCellCenter.Y)
            .Should().Be((0, 1));
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
        var router = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Presentation",
            "CanvasGestureRouter.cs");

        router.Should().Contain("CanvasGestureSession _session");
        router.Should().Contain("_session.PlanMove(");
        router.Should().Contain("SlideCanvasGeometryPlanner.ScreenRectBetween");
        router.Should().Contain("CanvasGesturePreviewProjector");
        router.Should().Contain("SlideCanvasGeometryPlanner.EmuBoundsToScreen");
        router.Should().Contain("SlideCanvasGeometryPlanner.ShapeVisualBoundsToScreen");

        foreach (var source in new[] { wpfGesture, avaloniaGesture })
        {
            source.Should().Contain("CanvasGestureRouter _gestureRouter");
            source.Should().Contain("_gestureRouter.PreviewPointer(");
            source.Should().Contain("ApplyPreviewPlan(");
            source.Should().Contain("CanvasGesturePreviewProjector.Project(");
            source.Should().Contain("ToGesturePoint(");
            source.Should().NotContain("CanvasGesturePlanner.CaptureMoveState");
            source.Should().NotContain("CanvasGesturePlanner.PlanMove");
            source.Should().NotContain("SlideCanvasGeometryPlanner.ScreenRectBetween");
            source.Should().NotContain("SlideCanvasGeometryPlanner.EmuBoundsToScreen");
            source.Should().NotContain("SlideCanvasGeometryPlanner.ShapeVisualBoundsToScreen");
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
        wpfTable.Should().Contain("ApplyPlacementTransform(_cellTextBox, placement)");
        wpfTable.Should().Contain("ApplyPlacementTransform(_cellHighlight, placement)");
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

        avaloniaText.Should().Contain("ApplyPlacementTransform(_cellTextBox, placement)");
        avaloniaText.Should().Contain("ApplyPlacementTransform(_cellHighlight, placement)");

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

        var wpfCanvas = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Wpf",
            "SlideCanvas.cs");
        var avaloniaCanvas = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SlideCanvas.cs");
        foreach (var source in new[] { wpfCanvas, avaloniaCanvas })
        {
            source.Should().Contain("SlideCanvasGeometryPlanner.FitAspectRatio(")
                .And.NotContain("double ratio = _slideWidthDip / _slideHeightDip")
                .And.NotContain("if (w / h > ratio)");
        }

        avaloniaCanvas.Should().Contain("PresentationViewZoomPlanner.PlanStageTransform");
        avaloniaCanvas.Should().NotContain("Math.Min(renderW / _slideWidthDip");
    }

    private static string ReadWorkspaceFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllText(relativeParts);

    private static TableShape MakeTable()
    {
        var table = new TableShape();
        table.ColumnWidthsEmu.Add(ToEmu(96));
        table.ColumnWidthsEmu.Add(ToEmu(96));
        table.ColumnWidthsEmu.Add(ToEmu(96));
        var row = new TableRow { HeightEmu = ToEmu(48) };
        row.Cells.Add(new TableCell());
        row.Cells.Add(new TableCell());
        row.Cells.Add(new TableCell());
        table.Rows.Add(row);
        return table;
    }
}
