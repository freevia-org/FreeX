namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Covers the SetSlideSizeCommand "Ensure Fit" scaling fix (round 158, finding freep-slide-size
/// F1): changing the slide size must rescale every shape's position/size along with the canvas,
/// or content that fit the old slide ends up cropped/off-slide on the new one.
/// </summary>
public sealed class SetSlideSizeCommandScalingTests
{
    private static Presentation MakePresentation(long cx = 12_192_000L, long cy = 6_858_000L)
    {
        var p = new Presentation { SlideSizeCxEmu = cx, SlideSizeCyEmu = cy };
        p.Slides.Add(new Slide());
        return p;
    }

    private static SlideShape MakeShape(uint id, long offX, long offY, long extCx, long extCy) => new()
    {
        Id = id,
        Name = $"S{id}",
        Kind = SlideShapeKind.AutoShape,
        OffsetXEmu = offX,
        OffsetYEmu = offY,
        ExtentCxEmu = extCx,
        ExtentCyEmu = extCy,
    };

    // ── The defect: shrinking 16:9 -> 4:3 must not leave a shape cropped off the new canvas ──

    [Fact]
    public void Apply_ShrinkingToNarrowerAspect_ScalesShapeSoItStillFitsOnTheNewCanvas()
    {
        var p = MakePresentation(); // 16:9 default: 12,192,000 x 6,858,000
        // A full-bleed background rectangle sized to the original 16:9 canvas.
        var background = MakeShape(1, 0, 0, 12_192_000L, 6_858_000L);
        p.Slides[0].Shapes.Add(background);

        // Standard 4:3 = 9,144,000 x 6,858,000 (the ribbon's quick "Standard (4:3)" button).
        var cmd = new SetSlideSizeCommand(9_144_000L, 6_858_000L);
        cmd.Apply(p);

        p.SlideSizeCxEmu.Should().Be(9_144_000L);

        // The regression: before the fix the shape kept its original 12,192,000-wide extent,
        // so its right edge (12,192,000) sat far beyond the new 9,144,000-wide canvas -- cropped
        // in the editor, Slide Show, and PDF export. After the fix it must be scaled down so it
        // fits entirely within the new slide bounds.
        (background.OffsetXEmu + background.ExtentCxEmu).Should().BeLessThanOrEqualTo(p.SlideSizeCxEmu);
        (background.OffsetYEmu + background.ExtentCyEmu).Should().BeLessThanOrEqualTo(p.SlideSizeCyEmu);

        // Ensure Fit uses the smaller of the two axis ratios (9,144,000/12,192,000 = 0.75 here,
        // since height is unchanged) applied uniformly to both axes.
        background.ExtentCxEmu.Should().Be(9_144_000L);
        background.ExtentCyEmu.Should().Be((long)Math.Round(6_858_000L * 0.75));
    }

    [Fact]
    public void Apply_ShapeNearRightEdge_NoLongerExtendsPastNewNarrowerSlide()
    {
        var p = MakePresentation();
        // A shape positioned near the right edge of the original 16:9 slide.
        var shape = MakeShape(2, 11_000_000L, 1_000_000L, 1_000_000L, 500_000L);
        p.Slides[0].Shapes.Add(shape);

        new SetSlideSizeCommand(9_144_000L, 6_858_000L).Apply(p);

        (shape.OffsetXEmu + shape.ExtentCxEmu).Should().BeLessThanOrEqualTo(p.SlideSizeCxEmu,
            "the shape fit inside the old canvas, so a uniform Ensure-Fit scale must keep it inside the new one");
    }

    // ── Undo must restore the exact original geometry, not merely the old slide size ──

    [Fact]
    public void Revert_RestoresOriginalSlideSizeAndOriginalShapeGeometryExactly()
    {
        var p = MakePresentation();
        var shape = MakeShape(3, 11_000_000L, 1_000_000L, 1_000_000L, 500_000L);
        p.Slides[0].Shapes.Add(shape);

        var cmd = new SetSlideSizeCommand(9_144_000L, 6_858_000L);
        cmd.Apply(p);
        shape.OffsetXEmu.Should().NotBe(11_000_000L); // sanity: it really did move

        cmd.Revert(p);

        p.SlideSizeCxEmu.Should().Be(12_192_000L);
        p.SlideSizeCyEmu.Should().Be(6_858_000L);
        shape.OffsetXEmu.Should().Be(11_000_000L);
        shape.OffsetYEmu.Should().Be(1_000_000L);
        shape.ExtentCxEmu.Should().Be(1_000_000L);
        shape.ExtentCyEmu.Should().Be(500_000L);
    }

    // ── Group children (absolute slide-space coords) must scale together with the group ──

    [Fact]
    public void Apply_GroupShape_ScalesGroupAndDescendantsBySameFactorKeepingThemInSync()
    {
        var p = MakePresentation();
        var child = MakeShape(11, 10_500_000L, 500_000L, 400_000L, 300_000L);
        var group = new SlideShape
        {
            Id = 10,
            Kind = SlideShapeKind.Group,
            OffsetXEmu = 10_000_000L,
            OffsetYEmu = 400_000L,
            ExtentCxEmu = 1_000_000L,
            ExtentCyEmu = 500_000L,
        };
        group.Children.Add(child);
        p.Slides[0].Shapes.Add(group);

        new SetSlideSizeCommand(9_144_000L, 6_858_000L).Apply(p);

        double expectedScale = 9_144_000.0 / 12_192_000.0; // 0.75, the binding (smaller) ratio
        group.OffsetXEmu.Should().Be((long)Math.Round(10_000_000L * expectedScale));
        child.OffsetXEmu.Should().Be((long)Math.Round(10_500_000L * expectedScale));
        child.ExtentCxEmu.Should().Be((long)Math.Round(400_000L * expectedScale));
    }

    // ── Sibling / no-regression: growing the slide, or an unchanged aspect ratio ──

    [Fact]
    public void Apply_SameAspectRatioResize_UniformlyScalesRatherThanDistorting()
    {
        var p = MakePresentation(); // 16:9
        var shape = MakeShape(4, 0, 0, 12_192_000L, 6_858_000L);
        p.Slides[0].Shapes.Add(shape);

        // A different absolute size that keeps the exact same 16:9 ratio.
        new SetSlideSizeCommand(6_096_000L, 3_429_000L).Apply(p);

        shape.ExtentCxEmu.Should().Be(6_096_000L);
        shape.ExtentCyEmu.Should().Be(3_429_000L);
    }

    [Fact]
    public void Apply_NoSizeChange_LeavesShapeGeometryUntouched()
    {
        var p = MakePresentation();
        var shape = MakeShape(5, 123_456L, 654_321L, 200_000L, 100_000L);
        p.Slides[0].Shapes.Add(shape);

        new SetSlideSizeCommand(p.SlideSizeCxEmu, p.SlideSizeCyEmu).Apply(p);

        shape.OffsetXEmu.Should().Be(123_456L);
        shape.OffsetYEmu.Should().Be(654_321L);
        shape.ExtentCxEmu.Should().Be(200_000L);
        shape.ExtentCyEmu.Should().Be(100_000L);
    }
}
