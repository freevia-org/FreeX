using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// F4: rotating a Group shape used to be a complete visual no-op — <c>TransformGroupChild</c>
/// only translated/scaled a group child's absolute bounds via the off/ext vs. chOff/chExt
/// mapping and never applied the group's own <see cref="SlideShape.RotationDeg"/>. These pin the
/// fix: a rotated group's children must move (and pick up the group's angle), while an
/// unrotated group — including one whose chOff/chExt differ from its off/ext (a group that was
/// resized after its children were authored) — must keep composing exactly as before.
/// </summary>
public sealed class SlideCompositorGroupRotationTests
{
    private const long EmuPerPx = 9525L;

    [Fact]
    public void Compose_RotatedGroup_MovesAndRotatesChildAroundGroupCenter()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];

        var child = new SlideShape
        {
            Id = 501,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 10 * EmuPerPx,
            OffsetYEmu = 10 * EmuPerPx,
            ExtentCxEmu = 20 * EmuPerPx,
            ExtentCyEmu = 20 * EmuPerPx,
        };
        var group = new SlideShape
        {
            Id = 500,
            Kind = SlideShapeKind.Group,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 200 * EmuPerPx,
            ExtentCyEmu = 100 * EmuPerPx,
            RotationDeg = 90,
        };
        group.Children.Add(child);
        slide.Shapes.Add(group);

        var ops = SlideCompositor.Compose(presentation, slide, 0);

        var composedChild = ops.OfType<DrawOp.Shape>().Single(op => op.ShapeId == 501);

        // Group center = (100, 50)px, child center = (20, 20)px before rotation. Rotating that
        // center 90 degrees clockwise around the group center (same clockwise, Y-down matrix as
        // ConnectionSiteHelper.TransformSite) lands it at (130, -30)px, i.e. a top-left of
        // (120, -40)px for the still-20x20 box.
        composedChild.BoundsDip.X.Should().BeApproximately(120, 0.5);
        composedChild.BoundsDip.Y.Should().BeApproximately(-40, 0.5);
        composedChild.BoundsDip.Width.Should().BeApproximately(20, 0.5);
        composedChild.BoundsDip.Height.Should().BeApproximately(20, 0.5);

        // The child's own rotation (0) composes with the group's (90) so the leaf DrawOp itself
        // renders tilted, matching how ShapeTransformPlanner rotates every leaf around its own
        // (now-relocated) bounds center.
        composedChild.RotationDeg.Should().BeApproximately(90, 0.001);
    }

    [Fact]
    public void Compose_UnrotatedResizedGroup_StillUsesPlainScaleTranslateMapping()
    {
        // Adjacent case (rule 10): a group whose chOff/chExt differ from its own off/ext (resized
        // after its children were authored) but carries no rotation must keep the pre-existing
        // linear scale+translate behavior untouched.
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];

        var child = new SlideShape
        {
            Id = 601,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 50 * EmuPerPx,
            ExtentCyEmu = 50 * EmuPerPx,
        };
        var group = new SlideShape
        {
            Id = 600,
            Kind = SlideShapeKind.Group,
            OffsetXEmu = 100 * EmuPerPx,
            OffsetYEmu = 100 * EmuPerPx,
            ExtentCxEmu = 200 * EmuPerPx,   // authored box was resized to double size...
            ExtentCyEmu = 200 * EmuPerPx,
            ChildOffsetXEmu = 0,
            ChildOffsetYEmu = 0,
            ChildExtentCxEmu = 100 * EmuPerPx, // ...from this original child-space extent.
            ChildExtentCyEmu = 100 * EmuPerPx,
            RotationDeg = 0,
        };
        group.Children.Add(child);
        slide.Shapes.Add(group);

        var ops = SlideCompositor.Compose(presentation, slide, 0);
        var composedChild = ops.OfType<DrawOp.Shape>().Single(op => op.ShapeId == 601);

        // scaleX = scaleY = 200/100 = 2. absX = 100 + (0-0)*2 = 100, absCx = 50*2 = 100.
        composedChild.BoundsDip.X.Should().BeApproximately(100, 0.5);
        composedChild.BoundsDip.Y.Should().BeApproximately(100, 0.5);
        composedChild.BoundsDip.Width.Should().BeApproximately(100, 0.5);
        composedChild.BoundsDip.Height.Should().BeApproximately(100, 0.5);
        composedChild.RotationDeg.Should().Be(0);
    }

    [Fact]
    public void Compose_UnrotatedIdentityGroup_ChildKeepsOwnPosition()
    {
        // Adjacent case: the overwhelmingly common no-op path (never-resized, never-rotated
        // group) must still short-circuit to the child's own bounds unchanged.
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];

        var child = new SlideShape
        {
            Id = 701,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 15 * EmuPerPx,
            OffsetYEmu = 25 * EmuPerPx,
            ExtentCxEmu = 30 * EmuPerPx,
            ExtentCyEmu = 40 * EmuPerPx,
        };
        var group = new SlideShape
        {
            Id = 700,
            Kind = SlideShapeKind.Group,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 200 * EmuPerPx,
            ExtentCyEmu = 100 * EmuPerPx,
            RotationDeg = 0,
        };
        group.Children.Add(child);
        slide.Shapes.Add(group);

        var ops = SlideCompositor.Compose(presentation, slide, 0);
        var composedChild = ops.OfType<DrawOp.Shape>().Single(op => op.ShapeId == 701);

        composedChild.BoundsDip.X.Should().BeApproximately(15, 0.5);
        composedChild.BoundsDip.Y.Should().BeApproximately(25, 0.5);
        composedChild.RotationDeg.Should().Be(0);
    }
}
