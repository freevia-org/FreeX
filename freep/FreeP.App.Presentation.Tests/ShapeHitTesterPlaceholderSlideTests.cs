using FreeP.App.Compositor;
using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Regression coverage for the slide-aware placeholder/geometry resolution family:
/// <see cref="ShapeHitTester.GetShapeBoundsDip(SlideShape, Slide, FreeP.Core.Model.Presentation)"/>,
/// <see cref="ShapeHitTester.HitTest"/>, <see cref="ShapeHitTester.MarqueeHitTest"/>, and
/// <see cref="PlaceholderResolver.ResolveAnchor(SlideShape, Slide, PresentationModel)"/>.
///
/// A prior defect resolved a placeholder shape's inherited geometry by scanning EVERY layout in
/// the presentation for the first idx/type-compatible match, ignoring which layout the slide is
/// actually linked to (<see cref="Slide.LayoutId"/>). When two layouts declare a compatible
/// placeholder at the same idx (e.g. a "Title Slide" ctrTitle and a "Title and Content" title,
/// both idx=0 -- compatible under PlaceholderResolver's title-group matching) at different
/// on-slide positions, the wrong layout's geometry could be picked, so a click directly on the
/// visibly-rendered placeholder missed entirely.
/// </summary>
public sealed class ShapeHitTesterPlaceholderSlideTests
{
    // Two layouts share a compatible idx=0 title placeholder at very different positions.
    // Layout A is listed FIRST (so a list-order scan finds it first) but the slide is actually
    // linked to Layout B. The correct, slide-aware resolution must use Layout B's geometry.
    private static (PresentationModel Presentation, Slide Slide, SlideShape Shape) MakeTwoLayoutScenario()
    {
        var p = new PresentationModel();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layoutA = new SlideLayout { Id = "layoutA", MasterId = "m1" };
        layoutA.Placeholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.CenteredTitle, Idx = 0 },
            OffsetXEmu = 95_250,     // 10 dip
            OffsetYEmu = 95_250,     // 10 dip
            ExtentCxEmu = 952_500,   // 100 dip
            ExtentCyEmu = 952_500,   // 100 dip
        });
        p.Layouts.Add(layoutA);

        var layoutB = new SlideLayout { Id = "layoutB", MasterId = "m1" };
        layoutB.Placeholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = 4_762_500,  // 500 dip -- far away from layout A's position
            OffsetYEmu = 4_762_500,  // 500 dip
            ExtentCxEmu = 952_500,   // 100 dip
            ExtentCyEmu = 952_500,   // 100 dip
        });
        p.Layouts.Add(layoutB);

        var slide = new Slide { LayoutId = "layoutB" };
        var shape = new SlideShape
        {
            Id = 42,
            Name = "Title",
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            // No own geometry (ExtentCx/Cy = 0) -> must inherit from the slide's LINKED layout.
        };
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        return (p, slide, shape);
    }

    [Fact]
    public void GetShapeBoundsDip_TwoCompatibleLayouts_UsesSlidesLinkedLayout_NotFirstListMatch()
    {
        var (p, slide, shape) = MakeTwoLayoutScenario();

        var bounds = ShapeHitTester.GetShapeBoundsDip(shape, slide, p);

        // Must resolve from layoutB (the slide's actual layout), not layoutA (first in list).
        bounds.Left.Should().BeApproximately(500.0, 1e-6);
        bounds.Top.Should().BeApproximately(500.0, 1e-6);
    }

    [Fact]
    public void HitTest_ClickOnLinkedLayoutPosition_HitsShape_NotTheOtherLayoutsPosition()
    {
        var (p, slide, shape) = MakeTwoLayoutScenario();

        // Click inside layoutB's bounds (500..600 dip) -- where the placeholder actually renders
        // because the slide is linked to layoutB.
        var hitAtCorrectPosition = ShapeHitTester.HitTest(slide, p, 550, 550);
        hitAtCorrectPosition.Should().Be(shape.Id);

        // A click at layoutA's position (10..110 dip) must NOT hit this shape -- that geometry
        // belongs to an unrelated layout the slide isn't linked to.
        var hitAtWrongLayoutPosition = ShapeHitTester.HitTest(slide, p, 50, 50);
        hitAtWrongLayoutPosition.Should().BeNull();
    }

    [Fact]
    public void MarqueeHitTest_MarqueeOverLinkedLayoutPosition_IncludesShape()
    {
        var (p, slide, shape) = MakeTwoLayoutScenario();

        // Marquee around layoutB's position (where the shape actually renders).
        var hitsAtCorrectPosition = ShapeHitTester.MarqueeHitTest(slide, p, 480, 480, 620, 620);
        hitsAtCorrectPosition.Should().Contain(shape.Id);

        // Marquee around layoutA's position must NOT pick up the shape.
        var hitsAtWrongLayoutPosition = ShapeHitTester.MarqueeHitTest(slide, p, 0, 0, 120, 120);
        hitsAtWrongLayoutPosition.Should().NotContain(shape.Id);
    }

    // ── Sibling / no-regression coverage ────────────────────────────────────────────

    /// <summary>
    /// Single-layout scenario (the common case): proves the slide-aware fix did not break
    /// ordinary placeholder inheritance when there is only one candidate layout to match.
    /// </summary>
    [Fact]
    public void GetShapeBoundsDip_SingleLayout_StillInheritsCorrectly()
    {
        var p = new PresentationModel();
        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        layout.Placeholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = 457_200,
            OffsetYEmu = 274_320,
            ExtentCxEmu = 8_229_600,
            ExtentCyEmu = 1_143_000,
        });
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var shape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
        };
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var bounds = ShapeHitTester.GetShapeBoundsDip(shape, slide, p);

        bounds.Left.Should().BeApproximately(48.0, 1e-6);   // 457200 / 9525
        bounds.Top.Should().BeApproximately(28.8, 1e-3);    // 274320 / 9525
    }

    /// <summary>
    /// A shape with its own explicit geometry must keep winning over any layout, even in the
    /// two-competing-layouts scenario -- proves the fix doesn't over-correct into always
    /// preferring inherited geometry.
    /// </summary>
    [Fact]
    public void GetShapeBoundsDip_ShapeHasOwnGeometry_LayoutsAreIgnoredEntirely()
    {
        var (p, slide, _) = MakeTwoLayoutScenario();

        var shapeWithOwnGeometry = new SlideShape
        {
            Id = 99,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = 1_905_000,   // 200 dip
            OffsetYEmu = 1_905_000,   // 200 dip
            ExtentCxEmu = 476_250,    // 50 dip
            ExtentCyEmu = 476_250,    // 50 dip
        };
        slide.Shapes.Add(shapeWithOwnGeometry);

        var bounds = ShapeHitTester.GetShapeBoundsDip(shapeWithOwnGeometry, slide, p);

        bounds.Left.Should().BeApproximately(200.0, 1e-6);
        bounds.Top.Should().BeApproximately(200.0, 1e-6);
    }
}
