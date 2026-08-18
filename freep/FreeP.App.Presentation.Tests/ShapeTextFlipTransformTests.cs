namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r144 finding freep-shape-geometry/F2: flipping a shape must mirror its outline/fill but
/// keep its text upright, matching PowerPoint. Covers both the low-level
/// <see cref="ShapeTransformPlanner"/> transform and the render-plan wiring in
/// <see cref="ShapeAutoFitRenderPlanner"/> that both the WPF and Avalonia SlideCanvas
/// renderers consume for a shape's text overlay.
/// </summary>
public sealed class ShapeTextFlipTransformTests
{
    private static DrawOp.Shape MakeShape(LayoutRect bounds, double rotationDeg, bool flipH, bool flipV) => new()
    {
        ShapeId = 1,
        BoundsDip = bounds,
        Geometry = ShapeGeometryBuilder.Build(DrawingShapeKind.Rectangle, bounds),
        Fill = ResolvedFill.None.Instance,
        Outline = ResolvedOutline.None.Instance,
        RotationDeg = rotationDeg,
        FlipH = flipH,
        FlipV = flipV,
        Text = new ResolvedTextLayout(),
    };

    [Fact]
    public void PlanShapeTextRenderTransform_FlipHorizontal_DoesNotMirrorText()
    {
        var bounds = new LayoutRect(10, 20, 100, 50);
        var shape = MakeShape(bounds, rotationDeg: 0, flipH: true, flipV: false);

        // The geometry/outline transform DOES mirror (M11 == -1): this is what makes the
        // shape's outline/fill flip, and must stay true.
        var geometryTransform = ShapeTransformPlanner.PlanShapeRenderTransform(shape);
        geometryTransform.M11.Should().Be(-1, "flipping a shape must still mirror its outline/fill");

        // The text transform must NOT mirror -- PowerPoint keeps flipped-shape text upright.
        var textTransform = ShapeTransformPlanner.PlanShapeTextRenderTransform(shape);
        textTransform.Should().Be(ShapeAffineTransform.Identity,
            "flipping a shape horizontally must not flip its text (no rotation, no flip => identity)");
    }

    [Fact]
    public void PlanShapeTextRenderTransform_FlipVertical_DoesNotMirrorText()
    {
        var bounds = new LayoutRect(0, 0, 200, 80);
        var shape = MakeShape(bounds, rotationDeg: 0, flipH: false, flipV: true);

        var geometryTransform = ShapeTransformPlanner.PlanShapeRenderTransform(shape);
        geometryTransform.M22.Should().Be(-1, "flipping a shape must still mirror its outline/fill");

        var textTransform = ShapeTransformPlanner.PlanShapeTextRenderTransform(shape);
        textTransform.Should().Be(ShapeAffineTransform.Identity,
            "flipping a shape vertically must not flip its text");
    }

    [Fact]
    public void PlanShapeTextRenderTransform_RotationWithFlip_KeepsRotationButDropsFlip()
    {
        // Sibling coverage for the guidance's explicit rotation check: a shape that is BOTH
        // flipped and rotated must still rotate its text (matching PowerPoint) while dropping
        // only the mirror component.
        var bounds = new LayoutRect(10, 10, 100, 100);
        var shape = MakeShape(bounds, rotationDeg: 30, flipH: true, flipV: false);

        var textTransform = ShapeTransformPlanner.PlanShapeTextRenderTransform(shape);
        var rotationOnly = ShapeTransformPlanner.PlanShapeTransform(bounds, rotationDeg: 30, flipH: false, flipV: false);

        textTransform.Should().Be(rotationOnly,
            "text must still rotate with the shape; only the flip mirror is dropped");
        textTransform.M11.Should().NotBe(-1);
    }

    [Fact]
    public void PlanShapeTextRenderTransform_RotationOnly_MatchesGeometryTransform()
    {
        // Sibling/neighbouring-behaviour check: when there is no flip at all, rotating a shape
        // must still rotate its text exactly like the geometry -- this must NOT regress.
        var bounds = new LayoutRect(5, 5, 40, 40);
        var shape = MakeShape(bounds, rotationDeg: 45, flipH: false, flipV: false);

        var geometryTransform = ShapeTransformPlanner.PlanShapeRenderTransform(shape);
        var textTransform = ShapeTransformPlanner.PlanShapeTextRenderTransform(shape);

        textTransform.Should().Be(geometryTransform,
            "with no flip, the text transform must equal the full render transform (rotation still applies to text)");
    }

    [Fact]
    public void ShapeAutoFitRenderPlanner_PlanRender_FlippedShape_TextRenderTransformExcludesMirror()
    {
        var bounds = new LayoutRect(0, 0, 120, 60);
        var shape = MakeShape(bounds, rotationDeg: 0, flipH: true, flipV: true);

        var plan = ShapeAutoFitRenderPlanner.PlanRender(shape, _ => 0);

        // RenderTransform (consumed for the shape's geometry) must still carry the flip.
        plan.RenderTransform.M11.Should().Be(-1);
        plan.RenderTransform.M22.Should().Be(-1);

        // TextRenderTransform (consumed for the shape's text overlay by both SlideCanvas
        // renderers) must be the identity -- no mirroring at all for an unrotated flipped shape.
        plan.TextRenderTransform.Should().Be(ShapeAffineTransform.Identity);
    }

    [Fact]
    public void ShapeAutoFitRenderPlanner_PlanRender_UnflippedRotatedShape_TextRenderTransformStillRotates()
    {
        // Sibling test proving normal (non-flipped) rotated shapes keep behaving exactly as
        // before this fix: text render transform still equals the full render transform.
        var bounds = new LayoutRect(0, 0, 120, 60);
        var shape = MakeShape(bounds, rotationDeg: 15, flipH: false, flipV: false);

        var plan = ShapeAutoFitRenderPlanner.PlanRender(shape, _ => 0);

        plan.TextRenderTransform.Should().Be(plan.RenderTransform);
        plan.TextRenderTransform.IsIdentity.Should().BeFalse();
    }
}
