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
}
