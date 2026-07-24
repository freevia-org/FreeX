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
