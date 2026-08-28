using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Round 165 (freep-slide-layout-apply F1): SetSlideLayoutCommand.Apply's existing-shape geometry
/// loop only updated a shape from the new layout's matching placeholder when that placeholder
/// carried its OWN explicit xfrm. Per ECMA-376 19.3.1.53, a layout placeholder that omits xfrm
/// legitimately means "inherit from master" -- the newly-added-placeholder path a few lines below
/// already understands this (ApplyInheritedMasterGeometry), but the existing-shape loop just left
/// the shape untouched, so it kept the PREVIOUS layout's explicit, non-zero geometry forever.
/// Because PlaceholderResolver.ResolveAnchor never falls through to layout/master inheritance once
/// a shape has its own non-zero extent, that stale geometry becomes the shape's permanent position
/// and gets baked into the saved file as an explicit xfrm. These tests go through the real user
/// path -- EditingSession.SetCurrentSlideLayout, the exact call the Layout Picker in both shells
/// invokes.
/// </summary>
public sealed class SetSlideLayoutStaleGeometryRegressionTests
{
    [Fact]
    public void SwitchingLayout_ToPlaceholderWithNoOwnXfrm_AdoptsMasterInheritedGeometry()
    {
        var editor = MakeEditor(out var titleShapeId);

        // Sanity: before the switch the Title shape sits at the OLD layout's explicit geometry.
        var before = editor.Presentation.Slides[0].Shapes.Single(s => s.Id == titleShapeId);
        before.OffsetXEmu.Should().Be(100_000);
        before.OffsetYEmu.Should().Be(100_000);
        before.ExtentCxEmu.Should().Be(2_000_000);
        before.ExtentCyEmu.Should().Be(300_000);

        editor.SetCurrentSlideLayout("target-layout").Should().BeTrue();

        var after = editor.Presentation.Slides[0].Shapes.Single(s => s.Id == titleShapeId);

        // The bug: this stayed at (100_000, 100_000)/(2_000_000 x 300_000) -- the OLD layout's
        // geometry -- instead of adopting the master's geometry, because the target layout's
        // Title placeholder has no xfrm of its own.
        after.OffsetXEmu.Should().Be(500_000);
        after.OffsetYEmu.Should().Be(500_000);
        after.ExtentCxEmu.Should().Be(8_000_000);
        after.ExtentCyEmu.Should().Be(1_000_000);
    }

    [Fact]
    public void SwitchingLayout_ToPlaceholderWithOwnXfrm_StillUsesLayoutGeometryNotMaster()
    {
        // Sibling/no-regression case: when the new layout's matching placeholder DOES carry its
        // own explicit xfrm, the existing shape must keep adopting the layout's own geometry, not
        // fall through to the master (the already-correct case this fix must not disturb).
        var editor = MakeEditor(out var titleShapeId);

        var layoutWithOwnGeometry = new SlideLayout
        {
            Id = "layout-with-own-geometry",
            Name = "Layout With Own Geometry",
            LayoutType = SlideLayoutType.Custom,
            MasterId = editor.Presentation.Masters[0].Id,
        };
        layoutWithOwnGeometry.Placeholders.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = 2_000_000,
            OffsetYEmu = 2_000_000,
            ExtentCxEmu = 4_500_000,
            ExtentCyEmu = 950_000,
        });
        editor.Presentation.Layouts.Add(layoutWithOwnGeometry);

        editor.SetCurrentSlideLayout("layout-with-own-geometry").Should().BeTrue();

        var after = editor.Presentation.Slides[0].Shapes.Single(s => s.Id == titleShapeId);
        after.OffsetXEmu.Should().Be(2_000_000);
        after.OffsetYEmu.Should().Be(2_000_000);
        after.ExtentCxEmu.Should().Be(4_500_000);
        after.ExtentCyEmu.Should().Be(950_000);
    }

    private static EditingSession MakeEditor(out uint titleShapeId)
    {
        var presentation = Presentation.CreateEmpty();
        var master = presentation.Masters[0];

        // Master-level Title placeholder geometry: real PowerPoint files commonly define
        // placeholder position once on the master and never repeat it per-layout.
        master.Placeholders.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = 500_000,
            OffsetYEmu = 500_000,
            ExtentCxEmu = 8_000_000,
            ExtentCyEmu = 1_000_000,
        });

        // The OLD layout: explicit Title geometry of its own (what the slide currently sits at).
        var oldLayout = presentation.Layouts[0];
        oldLayout.Id = "old-layout";
        oldLayout.Placeholders.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = 100_000,
            OffsetYEmu = 100_000,
            ExtentCxEmu = 2_000_000,
            ExtentCyEmu = 300_000,
        });

        // The TARGET layout: a matching Title placeholder that omits xfrm entirely, per ECMA-376
        // 19.3.1.53 meaning "inherit from master".
        var targetLayout = new SlideLayout
        {
            Id = "target-layout",
            Name = "Target Layout",
            LayoutType = SlideLayoutType.Custom,
            MasterId = master.Id,
        };
        targetLayout.Placeholders.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
        });
        presentation.Layouts.Add(targetLayout);

        var slide = presentation.Slides[0];
        slide.LayoutId = "old-layout";
        var titleShape = slide.Shapes.Single(s => s.Placeholder?.Type == PlaceholderType.Title);
        titleShape.OffsetXEmu = 100_000;
        titleShape.OffsetYEmu = 100_000;
        titleShape.ExtentCxEmu = 2_000_000;
        titleShape.ExtentCyEmu = 300_000;
        titleShapeId = titleShape.Id;

        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }
}
