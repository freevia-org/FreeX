using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SelectionAdornerGeometryTests
{
    private const double EmuPerDip = 9525.0;

    [Fact]
    public void GetHandleCenters_ReturnsResizeHandlesInGestureOrder()
    {
        var rect = new SelectionAdornerRect(10, 20, 100, 40);

        SelectionAdornerGeometry.GetHandleCenters(rect).Should().Equal(
            new CanvasGesturePoint(60, 20),
            new CanvasGesturePoint(110, 20),
            new CanvasGesturePoint(110, 40),
            new CanvasGesturePoint(110, 60),
            new CanvasGesturePoint(60, 60),
            new CanvasGesturePoint(10, 60),
            new CanvasGesturePoint(10, 40),
            new CanvasGesturePoint(10, 20));
    }

    [Fact]
    public void GetRotateHandleCenter_UsesSharedOffsetAboveTopCenter()
    {
        var rect = new SelectionAdornerRect(10, 20, 100, 40);

        SelectionAdornerGeometry.GetRotateHandleCenter(rect)
            .Should().Be(new CanvasGesturePoint(60, 2));
    }

    [Theory]
    [InlineData(60, 2, CanvasGestureHandleKind.Rotate)]
    [InlineData(10, 20, CanvasGestureHandleKind.ResizeNW)]
    [InlineData(110, 20, CanvasGestureHandleKind.ResizeNE)]
    [InlineData(110, 60, CanvasGestureHandleKind.ResizeSE)]
    [InlineData(10, 60, CanvasGestureHandleKind.ResizeSW)]
    [InlineData(60, 40, CanvasGestureHandleKind.Body)]
    [InlineData(0, 0, CanvasGestureHandleKind.None)]
    public void HitTestHandle_ReturnsExpectedHandle(
        double x,
        double y,
        CanvasGestureHandleKind expected)
    {
        var rect = new SelectionAdornerRect(10, 20, 100, 40);

        SelectionAdornerGeometry.HitTestHandle(rect, new CanvasGesturePoint(x, y))
            .Should().Be(expected);
    }

    [Fact]
    public void BuildProjection_ProjectsPictureCropHandlesWithScaleAndOffsets()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        slide.Shapes.Add(new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.Picture,
            OffsetXEmu = ToEmu(10),
            OffsetYEmu = ToEmu(20),
            ExtentCxEmu = ToEmu(100),
            ExtentCyEmu = ToEmu(80),
            Picture = new ImagePart { Bytes = [1] },
            PictureFormat = new PictureFormat
            {
                CropLeft = 0.1,
                CropTop = 0.2,
                CropRight = 0.3,
                CropBottom = 0.05,
            },
        });

        var projection = SelectionAdornerGeometry.BuildProjection(
            slide,
            presentation,
            [7],
            new SlideTransformCore(2, 100, 50, 960, 540),
            editPointsEnabled: true);

        projection.Selections.Should().Equal(
            new SelectionAdornerSelectionPlan(7, new SelectionAdornerRect(120, 90, 200, 160)));
        projection.SelectionBounds.Should().Be(new SelectionAdornerRect(120, 90, 200, 160));
        projection.GeometryHandles.Should().Equal(
            new SelectionAdornerGeometryHandlePlan(
                PictureCropAuthoringPlanner.LeftHandleName,
                new CanvasGesturePoint(140, 170)),
            new SelectionAdornerGeometryHandlePlan(
                PictureCropAuthoringPlanner.TopHandleName,
                new CanvasGesturePoint(220, 122)),
            new SelectionAdornerGeometryHandlePlan(
                PictureCropAuthoringPlanner.RightHandleName,
                new CanvasGesturePoint(260, 170)),
            new SelectionAdornerGeometryHandlePlan(
                PictureCropAuthoringPlanner.BottomHandleName,
                new CanvasGesturePoint(220, 242)));
    }

    [Fact]
    public void BuildProjection_DispatchesNonPicturesToPresetGeometryHandles()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        slide.Shapes.Add(new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Triangle,
            OffsetXEmu = ToEmu(10),
            OffsetYEmu = ToEmu(20),
            ExtentCxEmu = ToEmu(200),
            ExtentCyEmu = ToEmu(100),
        });

        var projection = SelectionAdornerGeometry.BuildProjection(
            slide,
            presentation,
            [7],
            new SlideTransformCore(1.5, 5, -10, 960, 540),
            editPointsEnabled: true);

        projection.GeometryHandles.Should().Equal(
            new SelectionAdornerGeometryHandlePlan("adj", new CanvasGesturePoint(170, 20)));
        SelectionAdornerGeometry.BuildProjection(
                slide,
                presentation,
                [7],
                new SlideTransformCore(1.5, 5, -10, 960, 540),
                editPointsEnabled: false)
            .GeometryHandles.Should().BeEmpty();
    }

    [Fact]
    public void BuildProjection_PreservesRotatedFramesAndMissingSelectionFallback()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        slide.Shapes.Add(new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = ToEmu(10),
            OffsetYEmu = ToEmu(20),
            ExtentCxEmu = ToEmu(100),
            ExtentCyEmu = ToEmu(40),
            RotationDeg = 90,
        });

        var projection = SelectionAdornerGeometry.BuildProjection(
            slide,
            presentation,
            [7, 999],
            new SlideTransformCore(2, 100, 50, 960, 540),
            editPointsEnabled: true);

        var frame = projection.Selections.Should().ContainSingle().Subject.ScreenRect;
        frame.Left.Should().BeApproximately(180, 0.0001);
        frame.Top.Should().BeApproximately(30, 0.0001);
        frame.Width.Should().BeApproximately(80, 0.0001);
        frame.Height.Should().BeApproximately(200, 0.0001);
        projection.GeometryHandles.Should().BeEmpty();
    }

    [Fact]
    public void BuildProjection_KeepsNestedSelectionFrameWithoutTopLevelEditHandles()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var group = new SlideShape { Id = 1, Kind = SlideShapeKind.Group };
        group.Children.Add(new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Triangle,
            OffsetXEmu = ToEmu(10),
            OffsetYEmu = ToEmu(20),
            ExtentCxEmu = ToEmu(100),
            ExtentCyEmu = ToEmu(40),
        });
        slide.Shapes.Add(group);

        var projection = SelectionAdornerGeometry.BuildProjection(
            slide,
            presentation,
            [7],
            SlideTransformCore.Identity,
            editPointsEnabled: true);

        projection.Selections.Should().ContainSingle(selection => selection.ShapeId == 7);
        projection.GeometryHandles.Should().BeEmpty();
    }

    [Fact]
    public void BuildProjection_EmptySelectionAndUnsupportedShapeUseEmptyFallbacks()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        slide.Shapes.Add(new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.Table,
            OffsetXEmu = ToEmu(10),
            OffsetYEmu = ToEmu(20),
            ExtentCxEmu = ToEmu(100),
            ExtentCyEmu = ToEmu(40),
        });

        var empty = SelectionAdornerGeometry.BuildProjection(
            slide,
            presentation,
            Array.Empty<uint>(),
            SlideTransformCore.Identity,
            editPointsEnabled: true);
        var unsupported = SelectionAdornerGeometry.BuildProjection(
            slide,
            presentation,
            [7],
            SlideTransformCore.Identity,
            editPointsEnabled: true);

        empty.Selections.Should().BeEmpty();
        empty.GeometryHandles.Should().BeEmpty();
        empty.SelectionBounds.Should().BeNull();
        unsupported.Selections.Should().ContainSingle();
        unsupported.GeometryHandles.Should().BeEmpty();
    }

    [Fact]
    public void GetSelectionBounds_UnionsFramesAndReturnsNullForEmptySelection()
    {
        SelectionAdornerGeometry.GetSelectionBounds(Array.Empty<SelectionAdornerRect>())
            .Should().BeNull();

        SelectionAdornerGeometry.GetSelectionBounds(
            [
                new SelectionAdornerRect(10, 20, 100, 40),
                new SelectionAdornerRect(-5, 30, 20, 70),
                new SelectionAdornerRect(100, -10, 50, 15),
            ])
            .Should().Be(new SelectionAdornerRect(-5, -10, 155, 110));
        SelectionAdornerProjectionPlan.Empty.SelectionBounds.Should().BeNull();
    }

    [Fact]
    public void HitTestGeometryHandle_UsesInclusiveRadiusAndFirstMatch()
    {
        var handles = new[]
        {
            new SelectionAdornerGeometryHandlePlan("first", new CanvasGesturePoint(10, 20)),
            new SelectionAdornerGeometryHandlePlan("second", new CanvasGesturePoint(20, 20)),
        };

        SelectionAdornerGeometry.HitTestGeometryHandle(
                handles,
                new CanvasGesturePoint(19, 20))
            .Should().Be("first");
        SelectionAdornerGeometry.HitTestGeometryHandle(
                handles,
                new CanvasGesturePoint(10, 29.001))
            .Should().BeNull();
    }

    [Fact]
    public void StateOwnsSelectionPreviewAndGeometryResetPolicy()
    {
        var state = new SelectionAdornerState();
        state.UpdateSelection(
        [
            new SelectionAdornerSelectionPlan(1, new SelectionAdornerRect(10, 20, 30, 40)),
            new SelectionAdornerSelectionPlan(2, new SelectionAdornerRect(50, 5, 20, 10)),
        ]);
        state.UpdateGeometryHandles(
        [
            new SelectionAdornerGeometryHandlePlan("adj", new CanvasGesturePoint(12, 18)),
        ]);
        state.UpdateGeometryPreview("adj", new CanvasGesturePoint(14, 19));
        state.UpdatePreview(new SelectionAdornerRect(1, 2, 3, 4), 15);

        state.SelectionBounds.Should().Be(new SelectionAdornerRect(10, 5, 60, 55));
        state.PreviewRect.Should().Be(new SelectionAdornerRect(1, 2, 3, 4));
        state.PreviewRotationDeg.Should().Be(15);
        state.GeometryPreview.Should().Be(
            new SelectionAdornerGeometryHandlePlan("adj", new CanvasGesturePoint(14, 19)));
        state.HasTransientInteractionVisuals.Should().BeTrue();

        state.UpdateTransformPreview(new CanvasMultiTransformPlan(
            [],
            [new CanvasShapeTransformPreview(1, new SlideScreenRect(3, 4, 5, 6), 25)],
            new SlideScreenRect(7, 8, 9, 10),
            35));

        state.TransformPreview.Should().ContainSingle();
        state.PreviewRect.Should().Be(new SelectionAdornerRect(7, 8, 9, 10));
        state.PreviewRotationDeg.Should().Be(35);

        state.UpdateSelection([]);
        state.Selections.Should().BeEmpty();
        state.SelectionBounds.Should().BeNull();
        state.PreviewRect.Should().BeNull();
        state.TransformPreview.Should().BeEmpty();

        state.UpdateGeometryHandles([]);
        state.GeometryHandles.Should().BeEmpty();
        state.GeometryPreview.Should().BeNull();
        state.HasTransientInteractionVisuals.Should().BeFalse();
    }

    [Fact]
    public void StateOwnsMarqueeSnapGuideAndTransientVisualPolicy()
    {
        var state = new SelectionAdornerState();
        var transform = new SlideTransformCore(2, 3, 4, 960, 540);
        var guides = new[] { new SnapGuideLine { IsHorizontal = true, Position = 20 } };

        state.UpdateMarquee(new SelectionAdornerRect(1, 2, 30, 40));
        state.UpdateSnapGuides(guides, transform);

        state.MarqueeRect.Should().Be(new SelectionAdornerRect(1, 2, 30, 40));
        state.SnapGuides.Should().BeSameAs(guides);
        state.SnapTransform.Should().BeSameAs(transform);
        state.HasTransientInteractionVisuals.Should().BeTrue();

        state.UpdateMarquee(null);
        state.UpdateSnapGuides(null, SlideTransformCore.Identity);
        state.HasTransientInteractionVisuals.Should().BeFalse();
    }

    [Fact]
    public void WpfAndAvaloniaAdorners_DelegateGeometryPolicyToSharedPlanner()
    {
        var sharedSurface = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Presentation",
            "SelectionAdornerSurface.cs");
        var wpf = ReadWorkspaceFile("freep", "FreeP.App.Rendering.Wpf", "SelectionAdorner.cs");
        var avalonia = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "SelectionAdornerLayer.cs");

        wpf.Should().Contain("SelectionAdornerGeometry.GetHandleCenters");
        wpf.Should().Contain("SelectionAdornerGeometry.GetRotateHandleCenter");
        wpf.Should().Contain("SelectionAdornerGeometry.HitTestHandle");
        wpf.Should().Contain("SelectionAdornerGeometry.HitTestGeometryHandle");
        wpf.Should().Contain("State.SelectionBounds");
        wpf.Should().Contain("public CanvasGestureHandleKind HitTestHandle");
        wpf.Should().NotContain("public enum HandleKind");
        wpf.Should().NotContain("ToHandleKind");
        wpf.Should().NotContain("Math.Sqrt");
        wpf.Should().NotContain("HandleHitRadius");
        wpf.Should().NotContain("hitRadius = 9.0");
        wpf.Should().NotContain("_selectionRects.Min");

        avalonia.Should().Contain("SelectionAdornerGeometry.GetHandleCenters");
        avalonia.Should().Contain("SelectionAdornerGeometry.GetRotateHandleCenter");
        avalonia.Should().Contain("SelectionAdornerGeometry.HitTestHandle");
        avalonia.Should().Contain("SelectionAdornerGeometry.HitTestGeometryHandle");
        avalonia.Should().Contain("State.SelectionBounds");
        avalonia.Should().Contain("public CanvasGestureHandleKind HitTestHandle");
        avalonia.Should().NotContain("public enum HandleKind");
        avalonia.Should().NotContain("ToHandleKind");
        avalonia.Should().NotContain("Math.Sqrt");
        avalonia.Should().NotContain("HandleHitRadius");
        avalonia.Should().NotContain("hitRadius = 9.0");
        avalonia.Should().NotContain("_selectionRects.Min");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ISelectionAdornerSurface<Rect, Point>")
                .And.Contain("SelectionAdornerController<Rect, Point> _controller")
                .And.Contain("ISelectionAdornerSurface<Rect, Point>.Controller => _controller;")
                .And.Contain("private SelectionAdornerState State => _controller.State;")
                .And.NotContain("public void UpdateSelection(")
                .And.NotContain("public void UpdateGeometryHandles(")
                .And.NotContain("public void UpdateGeometryPreview(")
                .And.NotContain("public void UpdatePreview(")
                .And.NotContain("public void UpdateTransformPreview(")
                .And.NotContain("public void UpdateMarquee(")
                .And.NotContain("public void UpdateSnapGuides(")
                .And.NotContain("private readonly List<(uint id, Rect screenRect)> _selectionRects")
                .And.NotContain("private Rect? _previewRect")
                .And.NotContain("private Rect? _marqueeRect")
                .And.NotContain("private IReadOnlyList<SnapGuideLine>? _snapGuides")
                .And.NotContain("private readonly List<(string Name, Point Position)> _geometryHandles");
        }

        sharedSurface.Should().Contain("public static class SelectionAdornerSurfaceExtensions")
            .And.Contain("surface.Controller.UpdateSelection(selections)")
            .And.Contain("surface.Controller.UpdateProjection(projection)")
            .And.Contain("surface.Controller.UpdateGeometryHandles(handles)")
            .And.Contain("surface.Controller.UpdateGeometryPreview(name, position)")
            .And.Contain("surface.Controller.UpdatePreview(screenRect, rotationDeg)")
            .And.Contain("surface.Controller.UpdateTransformPreview(plan)")
            .And.Contain("surface.Controller.UpdateMarquee(screenRect)")
            .And.Contain("surface.Controller.UpdateSnapGuides(guides, transform)")
            .And.NotContain("System.Windows")
            .And.NotContain("Avalonia");
    }

    [Fact]
    public void RendererHandlers_ConsumePortableProjectionAndKeepOnlyNativeConversions()
    {
        var portable = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Presentation",
            "SelectionAdornerGeometry.cs");
        var wpf = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Wpf",
            "CanvasGestureHandler.cs");
        var avalonia = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Avalonia",
            "AvaloniaCanvasGestureHandler.cs");

        portable.Should().Contain("public sealed record SelectionAdornerProjectionPlan")
            .And.Contain("public static SelectionAdornerProjectionPlan BuildProjection(")
            .And.Contain("PictureCropAuthoringPlanner.Build(shape, bounds)")
            .And.Contain("ShapeGeometryAdjustmentPlanner.Build(shape, bounds)")
            .And.NotContain("System.Windows")
            .And.NotContain("Avalonia");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("SelectionAdornerGeometry.BuildProjection(")
                .And.Contain("SelectionAdornerProjectionPlan.Empty")
                .And.NotContain("PictureCropAuthoringPlanner.Build")
                .And.NotContain("ShapeGeometryAdjustmentPlanner.Build")
                .And.NotContain("shape.Kind == SlideShapeKind.Picture")
                .And.NotContain("var rects = new List<(uint, Rect)>");
        }
    }

    private static long ToEmu(double dip) => (long)Math.Round(dip * EmuPerDip);

    private static string ReadWorkspaceFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllText(relativeParts);
}
