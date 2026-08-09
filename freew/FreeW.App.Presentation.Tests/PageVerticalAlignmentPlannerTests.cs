using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class PageVerticalAlignmentPlannerTests
{
    [Theory]
    [InlineData(PageVerticalAlignment.Top, PageVerticalAlignment.Center)]
    [InlineData(PageVerticalAlignment.Center, PageVerticalAlignment.Bottom)]
    [InlineData(PageVerticalAlignment.Bottom, PageVerticalAlignment.Justified)]
    [InlineData(PageVerticalAlignment.Justified, PageVerticalAlignment.Top)]
    public void Next_CyclesEveryWordVerticalAlignment(
        PageVerticalAlignment current,
        PageVerticalAlignment expected)
    {
        PageVerticalAlignmentPlanner.Next(current).Should().Be(expected);
    }

    [Fact]
    public void OrderBodyStartsByColumn_uses_reading_order_for_wrapped_blocks()
    {
        var starts = PageVerticalAlignmentPlanner.OrderBodyStartsByColumn([
            new PageVerticalAlignmentPlanner.BodyFlowStart(0, 0, 120),
            new PageVerticalAlignmentPlanner.BodyFlowStart(1, 1, 140),
            new PageVerticalAlignmentPlanner.BodyFlowStart(2, 0, 220),
            new PageVerticalAlignmentPlanner.BodyFlowStart(1, 1, 20),
        ]);

        starts.Select(start => start.BlockIndex)
            .Should()
            .Equal(0, 2, 1);
        starts.Select(start => start.PageSpaceY)
            .Should()
            .Equal(120, 220, 20);
    }

    [Fact]
    public void OrderBodyStartsByColumn_deduplicates_continuations_of_the_same_block()
    {
        var starts = PageVerticalAlignmentPlanner.OrderBodyStartsByColumn([
            new PageVerticalAlignmentPlanner.BodyFlowStart(4, 1, 80),
            new PageVerticalAlignmentPlanner.BodyFlowStart(4, 0, 420),
            new PageVerticalAlignmentPlanner.BodyFlowStart(4, 1, 10),
        ]);

        starts.Should().ContainSingle();
        starts[0].Should().Be(new PageVerticalAlignmentPlanner.BodyFlowStart(4, 0, 420));
    }

    [Theory]
    [InlineData(PageVerticalAlignment.Top, 120, 0)]
    [InlineData(PageVerticalAlignment.Center, 120, 60)]
    [InlineData(PageVerticalAlignment.Bottom, 120, 120)]
    [InlineData(PageVerticalAlignment.Justified, 120, 0)]
    [InlineData(PageVerticalAlignment.Center, -20, 0)]
    public void ResolveBodyOffset_UsesWordVerticalPlacement(
        PageVerticalAlignment alignment,
        double freeSpaceDip,
        double expectedOffsetDip)
    {
        PageVerticalAlignmentPlanner.ResolveBodyOffset(alignment, freeSpaceDip)
            .Should().Be(expectedOffsetDip);
    }

    [Theory]
    [InlineData(PageVerticalAlignment.Justified, 120, 2, 60)]
    [InlineData(PageVerticalAlignment.Justified, 120, 1, 120)]
    [InlineData(PageVerticalAlignment.Justified, 120, 0, 0)]
    [InlineData(PageVerticalAlignment.Center, 120, 2, 0)]
    public void ResolveJustifiedParagraphGap_DistributesOnlyAcrossBoundaries(
        PageVerticalAlignment alignment,
        double freeSpaceDip,
        int paragraphGapCount,
        double expectedGapDip)
    {
        PageVerticalAlignmentPlanner.ResolveJustifiedParagraphGap(
                alignment,
                freeSpaceDip,
                paragraphGapCount)
            .Should().Be(expectedGapDip);
    }
}
