using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class PageVerticalAlignmentPlannerTests
{
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
}
