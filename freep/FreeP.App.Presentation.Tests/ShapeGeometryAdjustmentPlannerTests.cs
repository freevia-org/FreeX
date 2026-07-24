using FluentAssertions;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ShapeGeometryAdjustmentPlannerTests
{
    private static readonly LayoutRect Bounds = new(10, 20, 200, 100);

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
    public void BuildMutationPlan_MapsPointerToEllipseAngleInDrawingMlUnits()
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
    public void Build_NonChordPreset_ReportsUnsupportedWithoutInventingHandles()
    {
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Triangle,
        };

        var plan = ShapeGeometryAdjustmentPlanner.Build(shape, Bounds);

        plan.CanEdit.Should().BeFalse();
        plan.Handles.Should().BeEmpty();
        plan.DisabledReason.Should().Be(ShapeGeometryAdjustmentPlanner.UnsupportedShapeMessage);
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
