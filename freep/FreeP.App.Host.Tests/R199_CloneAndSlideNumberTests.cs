using System.Linq;
using Free.Shared.Pdf;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// r199, from two re-sweeps that were supposed to be spent.
/// </summary>
public class R199_CloneAndSlideNumberTests
{
    // ── "a copier that handles a subset of its model type" ────────────────────────────────────
    // SlideCloner.CloneShape copied every SlideShape member except the four that describe a group's
    // CHILD coordinate space (a:chOff/a:chExt). Dropping them to null makes
    // SlideCompositor.TransformGroupChild take its identity-bounds path, which treats each child's
    // stored chOff-space position as an absolute slide position -- so Duplicate Slide, or copy/paste
    // of a group, displaced everything inside it.

    private static SlideShape GroupWithDistinctChildSpace()
    {
        var child = new SlideShape
        {
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 2_000_000,
            OffsetYEmu = 2_000_000,
            ExtentCxEmu = 400_000,
            ExtentCyEmu = 400_000,
        };

        var group = new SlideShape
        {
            Kind = SlideShapeKind.Group,
            OffsetXEmu = 500_000,
            OffsetYEmu = 500_000,
            ExtentCxEmu = 1_000_000,
            ExtentCyEmu = 1_000_000,
            // The child space is offset from, and twice the size of, the group's own bounds -- the
            // ordinary state of any group resized in PowerPoint after its children were authored.
            ChildOffsetXEmu = 1_800_000,
            ChildOffsetYEmu = 1_800_000,
            ChildExtentCxEmu = 2_000_000,
            ChildExtentCyEmu = 2_000_000,
        };
        group.Children.Add(child);
        return group;
    }

    [Fact]
    public void CloneShape_KeepsAGroupsChildCoordinateSpace()
    {
        var clone = SlideCloner.CloneShape(GroupWithDistinctChildSpace());

        clone.ChildOffsetXEmu.Should().Be(1_800_000);
        clone.ChildOffsetYEmu.Should().Be(1_800_000);
        clone.ChildExtentCxEmu.Should().Be(2_000_000);
        clone.ChildExtentCyEmu.Should().Be(2_000_000);
    }

    [Fact]
    public void CloneShape_LeavesAGroupWithNoChildSpaceAlone()
    {
        // The control: a group that never carried a:chOff/a:chExt must still clone to null.
        var group = new SlideShape
        {
            Kind = SlideShapeKind.Group,
            OffsetXEmu = 500_000,
            OffsetYEmu = 500_000,
            ExtentCxEmu = 1_000_000,
            ExtentCyEmu = 1_000_000,
        };

        var clone = SlideCloner.CloneShape(group);

        clone.ChildOffsetXEmu.Should().BeNull();
        clone.ChildExtentCxEmu.Should().BeNull();
    }

    [Fact]
    public void ADuplicatedGroupsChild_LandsWhereTheOriginalsDoes()
    {
        // The consequence, measured through the renderer rather than asserted: compose a slide
        // holding the group, and the same slide holding a clone of it, and compare where the child
        // is actually drawn.
        var presentation = new Presentation();
        var original = new Slide();
        original.Shapes.Add(GroupWithDistinctChildSpace());
        var duplicated = new Slide();
        duplicated.Shapes.Add(SlideCloner.CloneShape(GroupWithDistinctChildSpace()));
        presentation.Slides.Add(original);
        presentation.Slides.Add(duplicated);

        static (double X, double Y) FirstRectOrigin(Slide slide) =>
            PresentationPdfExporter.BuildSlidePage(slide).Ops
                .OfType<PdfStrokeRect>()
                .Select(op => (op.X, op.Y))
                .First();

        FirstRectOrigin(duplicated).Should().Be(FirstRectOrigin(original));
    }
}
