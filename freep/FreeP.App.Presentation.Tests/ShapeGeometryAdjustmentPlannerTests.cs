using FluentAssertions;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ShapeGeometryAdjustmentPlannerTests
{
    private static readonly LayoutRect Bounds = new(10, 20, 200, 100);

    [Fact]
    public void Build_Ribbon_ExposesFoldDepthAndWidthHandles()
    {
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Ribbon,
        };

        var plan = ShapeGeometryAdjustmentPlanner.Build(shape, Bounds);

        plan.CanEdit.Should().BeTrue();
        plan.Handles.Select(handle => handle.Name).Should().Equal("adj1", "adj2");
        plan.Handles[0].Label.Should().Be("Ribbon fold depth");
        plan.Handles[0].Value.Should().Be(16667);
        plan.Handles[0].PositionDip.Y.Should().BeApproximately(36.667, 0.001);
        plan.Handles[1].Label.Should().Be("Ribbon fold width");
        plan.Handles[1].Value.Should().Be(50000);
        plan.Handles[1].PositionDip.Should().Be(new LayoutPoint(60, 20));
    }

    // The renderer (ShapeGeometryBuilder.Ribbon) floors the top-band edge at 4% of the shape
    // height regardless of how small the authored adj1 fold-depth guide is, so the "Ribbon fold
    // depth" handle must track that same clamp instead of the raw adj1 value -- otherwise the
    // handle drifts away from the fold line the shape actually renders for any adj1 under the
    // 4% floor (e.g. the legal value adj1=0).
    [Fact]
    public void Build_Ribbon_FoldDepthHandleMatchesRenderedBandTopWhenAdj1IsBelowTheFloor()
    {
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Ribbon,
        };
        shape.PresetGeometryAdjustments["adj1"] = 0;
        shape.PresetGeometryAdjustments["adj2"] = 50000;

        var rendered = ShapeGeometryBuilder.Build(
            DrawingShapeKind.Ribbon, Bounds, shape.PresetGeometryAdjustments);

        var plan = ShapeGeometryAdjustmentPlanner.Build(shape, Bounds);

        plan.Handles[0].Value.Should().Be(0);
        plan.Handles[0].PositionDip.Y.Should().BeApproximately(rendered.Contours[0].Start.Y, 0.001);
        // Proves the clamp genuinely engaged: the rendered fold line sits below boundsDip.Top,
        // so a handle that just used the raw adj1=0 value would be detached from it.
        plan.Handles[0].PositionDip.Y.Should().BeGreaterThan(Bounds.Top);
    }

    // Sibling no-regression guard: once adj1 sits above the renderer's 4% floor, the handle must
    // still track the raw, unclamped fold value -- confirming the fix didn't over-clamp values
    // that never needed it.
    [Fact]
    public void Build_Ribbon_FoldDepthHandleMatchesRawAdjustmentWhenAboveTheFloor()
    {
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Ribbon,
        };
        shape.PresetGeometryAdjustments["adj1"] = 25000;
        shape.PresetGeometryAdjustments["adj2"] = 50000;

        var rendered = ShapeGeometryBuilder.Build(
            DrawingShapeKind.Ribbon, Bounds, shape.PresetGeometryAdjustments);

        var plan = ShapeGeometryAdjustmentPlanner.Build(shape, Bounds);

        plan.Handles[0].Value.Should().Be(25000);
        plan.Handles[0].PositionDip.Y.Should().BeApproximately(rendered.Contours[0].Start.Y, 0.001);
        plan.Handles[0].PositionDip.Y.Should().BeApproximately(Bounds.Top + Bounds.Height * 0.25, 0.001);
    }

    [Fact]
    public void BuildMutationPlan_Ribbon_MapsFoldHandlesToDrawingMlCoordinateUnits()
    {
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Ribbon,
        };

        var fold = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
            shape, Bounds, "adj1", new LayoutPoint(110, 45));
        var width = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
            shape, Bounds, "adj2", new LayoutPoint(60, 20));

        fold.ShouldApply.Should().BeTrue();
        fold.Value.Should().BeApproximately(25000, 0.001);
        width.ShouldApply.Should().BeTrue();
        width.Value.Should().BeApproximately(50000, 0.001);
    }

    [Fact]
    public void Build_Wave_ExposesAmplitudeAndPhaseHandles()
    {
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Wave,
        };

        var plan = ShapeGeometryAdjustmentPlanner.Build(shape, Bounds);

        plan.CanEdit.Should().BeTrue();
        plan.Handles.Select(handle => handle.Name).Should().Equal("adj1", "adj2");
        plan.Handles[0].Label.Should().Be("Wave amplitude");
        plan.Handles[0].Value.Should().Be(12500);
        plan.Handles[0].PositionDip.Should().Be(new LayoutPoint(10, 32.5));
        plan.Handles[1].Label.Should().Be("Wave phase");
        plan.Handles[1].Value.Should().Be(0);
        plan.Handles[1].PositionDip.Should().Be(new LayoutPoint(110, 120));
    }

    [Fact]
    public void BuildMutationPlan_Wave_MapsAmplitudeAndPhaseToDrawingMlCoordinateUnits()
    {
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Wave,
        };

        var amplitude = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
            shape, Bounds, "adj1", new LayoutPoint(10, 40));
        var phase = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
            shape, Bounds, "adj2", new LayoutPoint(120, 120));

        amplitude.ShouldApply.Should().BeTrue();
        amplitude.Value.Should().BeApproximately(20000, 0.001);
        phase.ShouldApply.Should().BeTrue();
        phase.Value.Should().BeApproximately(-10000, 0.001);
    }

    [Fact]
    public void ShapeGeometryBuilder_RibbonAndWave_ConsumeAuthoredAdjustments()
    {
        var ribbonDefault = ShapeGeometryBuilder.Build(
            DrawingShapeKind.Ribbon,
            Bounds,
            new Dictionary<string, double> { ["adj1"] = 16667, ["adj2"] = 50000 });
        var ribbonAdjusted = ShapeGeometryBuilder.Build(
            DrawingShapeKind.Ribbon,
            Bounds,
            new Dictionary<string, double> { ["adj1"] = 30000, ["adj2"] = 70000 });
        var waveDefault = ShapeGeometryBuilder.Build(
            DrawingShapeKind.Wave,
            Bounds,
            new Dictionary<string, double> { ["adj1"] = 12500, ["adj2"] = 0 });
        var waveAdjusted = ShapeGeometryBuilder.Build(
            DrawingShapeKind.Wave,
            Bounds,
            new Dictionary<string, double> { ["adj1"] = 5000, ["adj2"] = -7500 });

        ribbonAdjusted.Contours[0].Segments.Should().NotEqual(ribbonDefault.Contours[0].Segments);
        waveAdjusted.Contours[0].Segments.Should().NotEqual(waveDefault.Contours[0].Segments);
    }

    [Fact]
    public void Build_Chord_ExposesAngleHandlesAtRenderedArcEndpoints()
    {
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Chord,
        };

        var plan = ShapeGeometryAdjustmentPlanner.Build(shape, Bounds);

        plan.CanEdit.Should().BeTrue();
        plan.Handles.Should().HaveCount(2);
        plan.Handles[0].Name.Should().Be("adj1");
        plan.Handles[0].PositionDip.X.Should().BeApproximately(210, 0.001);
        plan.Handles[0].PositionDip.Y.Should().BeApproximately(70, 0.001);
        plan.Handles[1].Name.Should().Be("adj2");
        plan.Handles[1].PositionDip.X.Should().BeApproximately(10, 0.001);
        plan.Handles[1].PositionDip.Y.Should().BeApproximately(70, 0.001);
    }

    [Fact]
    public void BuildMutationPlan_MapsPointerToEllipseAngleInDrawingMlCoordinateUnits()
    {
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Chord,
        };

        var plan = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
            shape,
            Bounds,
            "adj2",
            new LayoutPoint(10, 70));

        plan.ShouldApply.Should().BeTrue();
        plan.Name.Should().Be("adj2");
        plan.Value.Should().BeApproximately(180 * 60000, 0.001);
        plan.DisabledReason.Should().BeNull();
    }

    [Fact]
    public void Build_RoundedRectangle_ExposesCornerRadiusHandle()
    {
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.RoundedRectangle,
        };

        var plan = ShapeGeometryAdjustmentPlanner.Build(shape, Bounds);

        plan.CanEdit.Should().BeTrue();
        plan.Handles.Should().ContainSingle();
        plan.Handles[0].Name.Should().Be("adj");
        plan.Handles[0].Value.Should().Be(18000);
        plan.Handles[0].PositionDip.Should().Be(new LayoutPoint(28, 20));
        plan.Handles[0].Minimum.Should().Be(0);
        plan.Handles[0].Maximum.Should().Be(50000);
    }

    // Wave 137 finding C: the handle used to place itself at 18% of the shorter side
    // unconditionally, but ShapeGeometryBuilder's RoundedRectangle clamps the *unauthored*
    // default corner to a fixed 2-18 DIP band -- once the shorter side exceeds ~100 DIP the
    // rendered corner stops growing while the old handle kept scaling with it. Assert against
    // the actual rendered outline (the contour's start vertex), not a hard-coded number, across
    // several sizes so a reintroduced unclamped formula is caught regardless of the exact bounds.
    [Theory]
    [InlineData(400d, 200d)]
    [InlineData(200d, 400d)]
    [InlineData(1000d, 1000d)]
    public void Build_RoundedRectangle_DefaultCornerHandleMatchesRenderedOutlineOnLargeShapes(double width, double height)
    {
        var bounds = new LayoutRect(10, 20, width, height);
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.RoundedRectangle,
        };

        var rendered = ShapeGeometryBuilder.Build(DrawingShapeKind.RoundedRectangle, bounds);
        var renderedRadius = rendered.Contours[0].Start.X - bounds.Left;

        var plan = ShapeGeometryAdjustmentPlanner.Build(shape, bounds);

        plan.Handles.Should().ContainSingle();
        plan.Handles[0].PositionDip.X.Should().BeApproximately(bounds.Left + renderedRadius, 0.001);
        plan.Handles[0].PositionDip.Y.Should().Be(bounds.Top);
    }

    // Sibling no-regression guard: once an "adj" guide is authored, the renderer scales freely
    // with the shorter side (no 2-18 DIP clamp) -- confirm the handle still tracks that
    // unclamped formula and wasn't accidentally capped too.
    [Fact]
    public void Build_RoundedRectangle_AuthoredCornerHandleMatchesRenderedOutlineOnLargeShapes()
    {
        var bounds = new LayoutRect(10, 20, 400, 200);
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.RoundedRectangle,
        };
        shape.PresetGeometryAdjustments["adj"] = 30000;

        var rendered = ShapeGeometryBuilder.Build(
            DrawingShapeKind.RoundedRectangle, bounds, shape.PresetGeometryAdjustments);
        var renderedRadius = rendered.Contours[0].Start.X - bounds.Left;

        var plan = ShapeGeometryAdjustmentPlanner.Build(shape, bounds);

        plan.Handles[0].PositionDip.X.Should().BeApproximately(bounds.Left + renderedRadius, 0.001);
        renderedRadius.Should().BeGreaterThan(18); // proves the clamp genuinely doesn't apply here
    }

    [Theory]
    [InlineData(DrawingShapeKind.Cross)]
    [InlineData(DrawingShapeKind.PlusSign)]
    public void Build_CrossFamily_ExposesBarInsetHandle(DrawingShapeKind kind)
    {
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = kind,
        };

        var plan = ShapeGeometryAdjustmentPlanner.Build(shape, Bounds);

        plan.CanEdit.Should().BeTrue();
        plan.Handles.Should().ContainSingle();
        plan.Handles[0].Name.Should().Be("adj");
        plan.Handles[0].Label.Should().Be("Bar inset");
        plan.Handles[0].Value.Should().Be(35000);
        plan.Handles[0].PositionDip.Should().Be(new LayoutPoint(80, Bounds.Top));
        plan.Handles[0].Maximum.Should().Be(50000);
    }

    [Fact]
    public void BuildMutationPlan_CrossFamily_MapsTopEdgePointerToInset()
    {
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Cross,
        };

        var plan = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
            shape,
            Bounds,
            "adj",
            new LayoutPoint(110, Bounds.Top));

        plan.ShouldApply.Should().BeTrue();
        plan.Name.Should().Be("adj");
        plan.Value.Should().BeApproximately(50000, 0.001);
        plan.DisabledReason.Should().BeNull();
    }

    [Fact]
    public void BuildMutationPlan_RoundedRectangle_MapsTopEdgePointerToAdjustment()
    {
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.RoundedRectangle,
        };

        var plan = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
            shape,
            Bounds,
            "adj",
            new LayoutPoint(40, Bounds.Top));

        plan.ShouldApply.Should().BeTrue();
        plan.Name.Should().Be("adj");
        plan.Value.Should().BeApproximately(30000, 0.001);
        plan.DisabledReason.Should().BeNull();
    }

    [Fact]
    public void Build_CustomGeometry_ExposesMoveAndLineVertices()
    {
        var shape = MakeCustomTriangle();

        var plan = ShapeGeometryAdjustmentPlanner.Build(shape, Bounds);

        plan.CanEdit.Should().BeTrue();
        plan.Handles.Should().HaveCount(3);
        plan.Handles.Select(handle => handle.Name)
            .Should().Equal("custom:0:0", "custom:0:1", "custom:0:2");
        plan.Handles[0].PositionDip.Should().Be(new LayoutPoint(30, 30));
        plan.Handles[1].PositionDip.Should().Be(new LayoutPoint(190, 30));
        plan.Handles[2].PositionDip.Should().Be(new LayoutPoint(110, 120));
    }

    [Fact]
    public void BuildMutationPlan_CustomGeometry_MapsPointerToPathUnits()
    {
        var shape = MakeCustomTriangle();

        var plan = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
            shape,
            Bounds,
            "custom:0:1",
            new LayoutPoint(150, 70));

        plan.ShouldApply.Should().BeTrue();
        plan.CustomPoint.Should().NotBeNull();
        plan.CustomPoint!.PathIndex.Should().Be(0);
        plan.CustomPoint.SegmentIndex.Should().Be(1);
        plan.CustomPoint.X.Should().BeApproximately(70, 0.001);
        plan.CustomPoint.Y.Should().BeApproximately(50, 0.001);
        plan.Value.Should().BeNull();
    }

    [Fact]
    public void Build_CustomGeometry_ExposesCubicAndQuadraticControlHandles()
    {
        var path = new CustomGeometryPath { PathW = 100, PathH = 100 };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, X: 0, Y: 50));
        path.Segments.Add(new CustomSegment(
            CustomSegmentKind.CubicBezTo,
            X: 20, Y: 0, X1: 80, Y1: 0, X2: 100, Y2: 50));
        path.Segments.Add(new CustomSegment(
            CustomSegmentKind.QuadBezTo,
            X: 50, Y: 100, X1: 0, Y1: 50));
        var shape = MakeCustomShape(path);

        var plan = ShapeGeometryAdjustmentPlanner.Build(shape, Bounds);

        plan.Handles.Select(handle => handle.Name).Should().Equal(
            "custom:0:0",
            "custom:0:1:c1", "custom:0:1:c2", "custom:0:1:end",
            "custom:0:2:c1", "custom:0:2:end");
        plan.Handles[1].PositionDip.Should().Be(new LayoutPoint(50, 20));
        plan.Handles[2].PositionDip.Should().Be(new LayoutPoint(170, 20));
        plan.Handles[3].PositionDip.Should().Be(new LayoutPoint(210, 70));
        plan.Handles[4].Label.Should().Be("Curve control");
    }

    [Fact]
    public void BuildMutationPlan_CustomGeometry_MapsCurveControlSlot()
    {
        var path = new CustomGeometryPath { PathW = 100, PathH = 100 };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, X: 0, Y: 50));
        path.Segments.Add(new CustomSegment(
            CustomSegmentKind.CubicBezTo,
            X: 20, Y: 0, X1: 80, Y1: 0, X2: 100, Y2: 50));
        var shape = MakeCustomShape(path);

        var plan = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
            shape, Bounds, "custom:0:1:c2", new LayoutPoint(150, 70));

        plan.ShouldApply.Should().BeTrue();
        plan.CustomPoint!.Slot.Should().Be(CustomGeometryPointSlot.Control2);
        plan.CustomPoint.X.Should().BeApproximately(70, 0.001);
        plan.CustomPoint.Y.Should().BeApproximately(50, 0.001);
    }

    [Fact]
    public void Build_CustomGeometry_ExposesArcAngleAndRadiusHandles()
    {
        var path = new CustomGeometryPath { PathW = 100, PathH = 100 };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, X: 90, Y: 0));
        path.Segments.Add(new CustomSegment(
            CustomSegmentKind.ArcTo, WR: 40, HR: 30, StAng: 0, SwAng: 90));
        var shape = MakeCustomShape(path);

        var plan = ShapeGeometryAdjustmentPlanner.Build(shape, Bounds);

        plan.Handles.Select(handle => handle.Name).Should().Equal(
            "custom:0:0",
            "arc:0:1:start", "arc:0:1:end", "arc:0:1:radius-x", "arc:0:1:radius-y");
        plan.Handles[2].Label.Should().Be("Arc end");
        plan.Handles[2].PositionDip.Should().Be(new LayoutPoint(110, 50));
        plan.Handles[3].Value.Should().Be(40);
        plan.Handles[4].Value.Should().Be(30);
    }

    [Fact]
    public void BuildMutationPlan_CustomArc_MapsEndAngleAndRadiusToAuthoredValues()
    {
        var path = new CustomGeometryPath { PathW = 100, PathH = 100 };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, X: 90, Y: 0));
        path.Segments.Add(new CustomSegment(
            CustomSegmentKind.ArcTo, WR: 40, HR: 30, StAng: 0, SwAng: 90));
        var shape = MakeCustomShape(path);

        var end = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
            shape, Bounds, "arc:0:1:end", new LayoutPoint(30, 20));
        end.ShouldApply.Should().BeTrue();
        end.ArcPoint!.Slot.Should().Be(CustomGeometryArcPointSlot.EndAngle);
        end.ArcPoint.Value.Should().BeApproximately(180, 0.001);

        var radius = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
            shape, Bounds, "arc:0:1:radius-x", new LayoutPoint(170, 20));
        radius.ArcPoint!.Slot.Should().Be(CustomGeometryArcPointSlot.RadiusX);
        radius.ArcPoint.Value.Should().BeApproximately(30, 0.001);
    }

    // Wave 137 finding B: CustomGeometryBuilder.BuildCustom never reads an ArcTo segment's own
    // StAng to place its rendered start point -- the start is always wherever the pen already
    // sits (the predecessor segment's endpoint). Dragging "Arc start" used to write StAng, which
    // only relocates the centre/end while the rendered start stayed fixed. The fix must move the
    // predecessor's own coordinate instead, and the render (reused via a fresh Build call, the
    // same math a real redraw would use) must reflect that.
    [Fact]
    public void BuildMutationPlan_CustomArc_DraggingStartMovesThePredecessorPointNotTheAngle()
    {
        var path = new CustomGeometryPath { PathW = 100, PathH = 100 };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, X: 90, Y: 0));
        path.Segments.Add(new CustomSegment(
            CustomSegmentKind.ArcTo, WR: 40, HR: 30, StAng: 0, SwAng: 90));
        var shape = MakeCustomShape(path);

        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Title = "S1" });
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);

        // Drag "Arc start" to path-space (20, 30): DIP = (Bounds.Left + 40, Bounds.Top + 30).
        var target = new LayoutPoint(Bounds.Left + 40, Bounds.Top + 30);
        var mutation = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(shape, Bounds, "arc:0:1:start", target);

        mutation.ShouldApply.Should().BeTrue();
        mutation.ArcPoint.Should().BeNull(
            "the rendered start point is the predecessor's own coordinate, not this segment's StAng");
        mutation.CustomPoint.Should().NotBeNull();
        mutation.CustomPoint!.PathIndex.Should().Be(0);
        mutation.CustomPoint.SegmentIndex.Should().Be(0); // the MoveTo that actually renders as the start
        mutation.CustomPoint.Slot.Should().Be(CustomGeometryPointSlot.Endpoint);
        mutation.CustomPoint.X.Should().BeApproximately(20, 0.001);
        mutation.CustomPoint.Y.Should().BeApproximately(30, 0.001);

        // Dispatch exactly as EditingSession.SetCustomGeometryPoint -> CanvasGestureSession does
        // on a real drag.
        bus.Execute(new SetCustomGeometryPointCommand(
            0, shape.Id, mutation.CustomPoint.PathIndex, mutation.CustomPoint.SegmentIndex,
            mutation.CustomPoint.X, mutation.CustomPoint.Y, mutation.CustomPoint.Slot));

        // The renderer's own outline math must now place the arc's rendered start exactly where
        // the user dragged. Before the fix, StAng was written instead and this stayed at the
        // original (90,0)-derived DIP spot regardless of where the pointer went.
        var rebuilt = ShapeGeometryAdjustmentPlanner.Build(shape, Bounds);
        var startHandle = rebuilt.Handles.Single(h => h.Name == "arc:0:1:start");
        startHandle.PositionDip.X.Should().BeApproximately(target.X, 0.001);
        startHandle.PositionDip.Y.Should().BeApproximately(target.Y, 0.001);
    }

    // Sibling no-regression guard: an ArcTo with no usable predecessor point (e.g. it is the
    // path's first segment) must not offer a "start" handle at all -- there is nothing for it to
    // move, so showing a draggable-but-inert handle would just resurrect the same class of bug.
    [Fact]
    public void Build_CustomGeometry_OmitsArcStartHandleWhenNoPredecessorPointExists()
    {
        var path = new CustomGeometryPath { PathW = 100, PathH = 100 };
        path.Segments.Add(new CustomSegment(
            CustomSegmentKind.ArcTo, WR: 40, HR: 30, StAng: 0, SwAng: 90));
        var shape = MakeCustomShape(path);

        var plan = ShapeGeometryAdjustmentPlanner.Build(shape, Bounds);

        plan.Handles.Select(handle => handle.Name).Should().Equal(
            "arc:0:0:end", "arc:0:0:radius-x", "arc:0:0:radius-y");
    }

    [Fact]
    public void CustomGeometryVertexCommands_ResolveInsertionMidpointAndDeleteGuard()
    {
        var shape = MakeCustomTriangle();

        ShapeGeometryAdjustmentPlanner.TryBuildCustomVertexInsertion(
                shape, "custom:0:1", out var pathIndex, out var segmentIndex, out var x, out var y)
            .Should().BeTrue();
        (pathIndex, segmentIndex).Should().Be((0, 1));
        x.Should().BeApproximately(70, 0.001);
        y.Should().BeApproximately(55, 0.001);
        ShapeGeometryAdjustmentPlanner.CanDeleteCustomVertex(shape, "custom:0:1")
            .Should().BeTrue();
        ShapeGeometryAdjustmentPlanner.CanDeleteCustomVertex(shape, "custom:0:0")
            .Should().BeFalse();
    }

    [Fact]
    public void Build_Triangle_ExposesApexHandle()
    {
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Triangle,
        };

        var plan = ShapeGeometryAdjustmentPlanner.Build(shape, Bounds);

        plan.CanEdit.Should().BeTrue();
        plan.Handles.Should().ContainSingle();
        plan.Handles[0].Name.Should().Be("adj");
        plan.Handles[0].Label.Should().Be("Apex position");
        plan.Handles[0].PositionDip.Should().Be(new LayoutPoint(110, Bounds.Top));
        plan.Handles[0].Value.Should().Be(50000);
        plan.Handles[0].Minimum.Should().Be(0);
        plan.Handles[0].Maximum.Should().Be(100000);
    }

    [Fact]
    public void BuildMutationPlan_Triangle_MapsPointerToApexGuide()
    {
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Triangle,
        };

        var plan = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
            shape,
            Bounds,
            "adj",
            new LayoutPoint(Bounds.Left + Bounds.Width * .75, Bounds.Top));

        plan.ShouldApply.Should().BeTrue();
        plan.Name.Should().Be("adj");
        plan.Value.Should().BeApproximately(75000, 0.001);
        plan.DisabledReason.Should().BeNull();
    }

    [Fact]
    public void Build_Star5_ExposesPointDepthGuide()
    {
        var shape = new SlideShape
        {
            Id = 8,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Star5,
        };

        var plan = ShapeGeometryAdjustmentPlanner.Build(shape, Bounds);

        plan.CanEdit.Should().BeTrue();
        plan.Handles.Should().ContainSingle();
        plan.Handles[0].Name.Should().Be("adj");
        plan.Handles[0].Label.Should().Be("Star point depth");
        plan.Handles[0].Value.Should().Be(42000);
        plan.Handles[0].Minimum.Should().Be(0);
        plan.Handles[0].Maximum.Should().Be(100000);
    }

    [Fact]
    public void BuildMutationPlan_Star5_MapsInnerPointToDepthGuide()
    {
        var shape = new SlideShape
        {
            Id = 8,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Star5,
        };

        var angle = -Math.PI / 2 + Math.PI / 5;
        var radial = 0.5 * 0.72;
        var plan = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
            shape,
            Bounds,
            "adj",
            new LayoutPoint(
                Bounds.Left + Bounds.Width * (0.5 + Math.Cos(angle) * radial),
                Bounds.Top + Bounds.Height * (0.5 + Math.Sin(angle) * radial)));

        plan.ShouldApply.Should().BeTrue();
        plan.Name.Should().Be("adj");
        plan.Value.Should().BeApproximately(72000, 0.001);
    }

    [Fact]
    public void Build_Star8_ExposesPointDepthGuide()
    {
        var shape = new SlideShape
        {
            Id = 9,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Star8,
        };

        var plan = ShapeGeometryAdjustmentPlanner.Build(shape, Bounds);

        plan.CanEdit.Should().BeTrue();
        plan.Handles.Should().ContainSingle();
        plan.Handles[0].Name.Should().Be("adj");
        plan.Handles[0].Label.Should().Be("Star point depth");
        plan.Handles[0].Value.Should().Be(46000);
        plan.Handles[0].Maximum.Should().Be(100000);
    }

    [Fact]
    public void BuildMutationPlan_Star8_MapsInnerPointToDepthGuide()
    {
        var shape = new SlideShape
        {
            Id = 9,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Star8,
        };

        var angle = -Math.PI / 2 + Math.PI / 8;
        var radial = 0.5 * 0.72;
        var plan = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
            shape,
            Bounds,
            "adj",
            new LayoutPoint(
                Bounds.Left + Bounds.Width * (0.5 + Math.Cos(angle) * radial),
                Bounds.Top + Bounds.Height * (0.5 + Math.Sin(angle) * radial)));

        plan.ShouldApply.Should().BeTrue();
        plan.Name.Should().Be("adj");
        plan.Value.Should().BeApproximately(72000, 0.001);
        plan.DisabledReason.Should().BeNull();
    }

    [Fact]
    public void Build_Explosion_ExposesSpikeDepthGuide()
    {
        var shape = new SlideShape
        {
            Id = 10,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Explosion,
        };

        var plan = ShapeGeometryAdjustmentPlanner.Build(shape, Bounds);

        plan.CanEdit.Should().BeTrue();
        plan.Handles.Should().ContainSingle();
        plan.Handles[0].Name.Should().Be("adj");
        plan.Handles[0].Label.Should().Be("Explosion spike depth");
        plan.Handles[0].Value.Should().Be(62000);
        plan.Handles[0].Maximum.Should().Be(100000);
    }

    [Fact]
    public void BuildMutationPlan_Explosion_MapsInnerPointToDepthGuide()
    {
        var shape = new SlideShape
        {
            Id = 10,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Explosion,
        };

        var angle = -Math.PI / 2 + 0.08 + Math.PI / 12;
        var radial = 0.5 * 0.74;
        var plan = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
            shape,
            Bounds,
            "adj",
            new LayoutPoint(
                Bounds.Left + Bounds.Width * (0.5 + Math.Cos(angle) * radial),
                Bounds.Top + Bounds.Height * (0.5 + Math.Sin(angle) * radial)));

        plan.ShouldApply.Should().BeTrue();
        plan.Name.Should().Be("adj");
        plan.Value.Should().BeApproximately(74000, 0.001);
        plan.DisabledReason.Should().BeNull();
    }

    [Fact]
    public void Build_RightArrow_ExposesShaftAndHeadGuides()
    {
        var shape = new SlideShape
        {
            Id = 19,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.RightArrow,
        };
        shape.PresetGeometryAdjustments["adj1"] = 25000;
        shape.PresetGeometryAdjustments["adj2"] = 70000;

        var plan = ShapeGeometryAdjustmentPlanner.Build(shape, Bounds);

        plan.CanEdit.Should().BeTrue();
        plan.Handles.Should().HaveCount(2);
        plan.Handles[0].Name.Should().Be("adj1");
        plan.Handles[0].Label.Should().Be("Shaft thickness");
        plan.Handles[0].Value.Should().Be(25000);
        plan.Handles[1].Name.Should().Be("adj2");
        plan.Handles[1].Label.Should().Be("Head length");
        plan.Handles[1].Value.Should().Be(70000);
    }

    [Fact]
    public void BuildMutationPlan_RightArrow_MapsShaftAndHeadPointers()
    {
        var shape = new SlideShape
        {
            Id = 20,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.RightArrow,
        };

        var shaftPlan = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
            shape, Bounds, "adj1", new LayoutPoint(Bounds.Left, Bounds.Top + 25));
        var headPlan = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
            shape, Bounds, "adj2", new LayoutPoint(Bounds.Left + 60, Bounds.Top));

        shaftPlan.ShouldApply.Should().BeTrue();
        shaftPlan.Name.Should().Be("adj1");
        shaftPlan.Value.Should().Be(50000);
        headPlan.ShouldApply.Should().BeTrue();
        headPlan.Name.Should().Be("adj2");
        headPlan.Value.Should().Be(70000);
    }

    [Fact]
    public void Build_DirectionalArrows_ExposeNativeShaftAndHeadGuides()
    {
        foreach (var kind in new[]
        {
            DrawingShapeKind.LeftArrow,
            DrawingShapeKind.UpArrow,
            DrawingShapeKind.DownArrow,
        })
        {
            var shape = new SlideShape
            {
                Id = 21,
                Kind = SlideShapeKind.AutoShape,
                AutoShapeKind = kind,
            };

            var plan = ShapeGeometryAdjustmentPlanner.Build(shape, Bounds);

            plan.CanEdit.Should().BeTrue(kind.ToString());
            plan.Handles.Select(handle => handle.Name).Should().Equal("adj1", "adj2");
            plan.Handles.Should().OnlyContain(handle =>
                (handle.Label == "Shaft thickness" || handle.Label == "Head length") &&
                handle.Minimum == 0 && handle.Maximum == 100000);
        }
    }

    [Fact]
    public void Build_ChevronAndHomePlate_ExposePointDepthGuide()
    {
        foreach (var kind in new[] { DrawingShapeKind.Chevron, DrawingShapeKind.HomePlate })
        {
            var shape = new SlideShape
            {
                Id = 22,
                Kind = SlideShapeKind.AutoShape,
                AutoShapeKind = kind,
            };

            var plan = ShapeGeometryAdjustmentPlanner.Build(shape, Bounds);

            plan.CanEdit.Should().BeTrue(kind.ToString());
            plan.Handles.Should().ContainSingle(kind.ToString());
            plan.Handles[0].Name.Should().Be("adj");
            plan.Handles[0].Label.Should().Be(kind == DrawingShapeKind.Chevron ? "Chevron depth" : "Point depth");
            plan.Handles[0].PositionDip.Should().Be(new LayoutPoint(160, Bounds.Top));
            plan.Handles[0].Value.Should().Be(50000);
            plan.Handles[0].Maximum.Should().Be(200000);

            var mutation = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
                shape, Bounds, "adj", new LayoutPoint(160, Bounds.Top));
            mutation.ShouldApply.Should().BeTrue();
            mutation.Value.Should().Be(50000);
        }
    }

    [Fact]
    public void Build_TrapezoidAndParallelogram_ExposeSlantGuides()
    {
        foreach (var kind in new[] { DrawingShapeKind.Trapezoid, DrawingShapeKind.Parallelogram })
        {
            var shape = new SlideShape
            {
                Id = 25,
                Kind = SlideShapeKind.AutoShape,
                AutoShapeKind = kind,
            };

            var plan = ShapeGeometryAdjustmentPlanner.Build(shape, Bounds);

            plan.CanEdit.Should().BeTrue(kind.ToString());
            plan.Handles.Should().ContainSingle();
            plan.Handles[0].Name.Should().Be("adj");
            plan.Handles[0].Label.Should().Be(kind == DrawingShapeKind.Trapezoid
                ? "Trapezoid depth"
                : "Parallelogram slant");
            plan.Handles[0].PositionDip.Should().Be(new LayoutPoint(50, Bounds.Top));
            plan.Handles[0].Value.Should().Be(40000);
            plan.Handles[0].Maximum.Should().Be(200000);
        }
    }

    [Fact]
    public void BuildMutationPlan_TrapezoidAndParallelogram_MapSlantPointer()
    {
        foreach (var kind in new[] { DrawingShapeKind.Trapezoid, DrawingShapeKind.Parallelogram })
        {
            var shape = new SlideShape
            {
                Id = 26,
                Kind = SlideShapeKind.AutoShape,
                AutoShapeKind = kind,
            };

            var plan = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
                shape, Bounds, "adj", new LayoutPoint(60, Bounds.Top));

            plan.ShouldApply.Should().BeTrue(kind.ToString());
            plan.Name.Should().Be("adj");
            plan.Value.Should().Be(50000);
            plan.DisabledReason.Should().BeNull();
        }
    }

    [Fact]
    public void Build_Cylinder_ExposesAuthoredCapHeightGuide()
    {
        var shape = new SlideShape
        {
            Id = 27,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Cylinder,
        };
        shape.PresetGeometryAdjustments["adj"] = 38485;

        var plan = ShapeGeometryAdjustmentPlanner.Build(shape, Bounds);

        plan.CanEdit.Should().BeTrue();
        plan.Handles.Should().ContainSingle();
        plan.Handles[0].Name.Should().Be("adj");
        plan.Handles[0].Label.Should().Be("Cylinder cap height");
        plan.Handles[0].PositionDip.Should().Be(
            new LayoutPoint(Bounds.Left + Bounds.Width / 2, Bounds.Top + Bounds.Height * .38485));
        plan.Handles[0].Value.Should().Be(38485);
        plan.Handles[0].Minimum.Should().Be(0);
        plan.Handles[0].Maximum.Should().Be(50000);
    }

    [Fact]
    public void BuildMutationPlan_Cylinder_MapsCapHeightPointer()
    {
        var shape = new SlideShape
        {
            Id = 28,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Cylinder,
        };

        var plan = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
            shape,
            Bounds,
            "adj",
            new LayoutPoint(Bounds.Left + Bounds.Width / 2, Bounds.Top + Bounds.Height * .4));

        plan.ShouldApply.Should().BeTrue();
        plan.Name.Should().Be("adj");
        plan.Value.Should().BeApproximately(40000, .001);
        plan.DisabledReason.Should().BeNull();
    }

    [Fact]
    public void Build_CompoundArrows_ExposeShaftAndSymmetricHeadGuides()
    {
        var horizontal = new SlideShape
        {
            Id = 23,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.LeftRightArrow,
        };
        var vertical = new SlideShape
        {
            Id = 24,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.UpDownArrow,
        };

        var horizontalPlan = ShapeGeometryAdjustmentPlanner.Build(horizontal, Bounds);
        horizontalPlan.Handles.Select(handle => handle.Name).Should().Equal("adj1", "adj2");
        horizontalPlan.Handles[0].PositionDip.Should().Be(new LayoutPoint(Bounds.Left, 45));
        horizontalPlan.Handles[1].PositionDip.Should().Be(new LayoutPoint(60, Bounds.Top));
        horizontalPlan.Handles[1].Maximum.Should().Be(200000);

        var verticalPlan = ShapeGeometryAdjustmentPlanner.Build(vertical, Bounds);
        verticalPlan.Handles.Select(handle => handle.Name).Should().Equal("adj1", "adj2");
        verticalPlan.Handles[0].PositionDip.Should().Be(new LayoutPoint(135, Bounds.Top));
        verticalPlan.Handles[1].PositionDip.Should().Be(new LayoutPoint(Bounds.Left, 70));
        verticalPlan.Handles[1].Maximum.Should().Be(100000);

        var horizontalMutation = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
            horizontal, Bounds, "adj2", new LayoutPoint(60, Bounds.Top));
        horizontalMutation.ShouldApply.Should().BeTrue();
        horizontalMutation.Value.Should().Be(50000);

        var verticalMutation = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
            vertical, Bounds, "adj1", new LayoutPoint(135, Bounds.Top));
        verticalMutation.ShouldApply.Should().BeTrue();
        verticalMutation.Value.Should().Be(50000);
    }

    private static SlideShape MakeCustomTriangle()
    {
        var path = new CustomGeometryPath { PathW = 100, PathH = 100 };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, X: 10, Y: 10));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, X: 90, Y: 10));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, X: 50, Y: 100));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.Close));

        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Triangle,
        };
        shape.CustomGeometry.Add(path);
        return shape;
    }

    private static SlideShape MakeCustomShape(CustomGeometryPath path)
    {
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
        };
        shape.CustomGeometry.Add(path);
        return shape;
    }
}
