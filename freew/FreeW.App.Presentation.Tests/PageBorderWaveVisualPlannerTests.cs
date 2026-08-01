using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class PageBorderWaveVisualPlannerTests
{
    [Fact]
    public void BuildFrame_ReproducesMeasuredWordSegmentRegistration()
    {
        var segments = PageBorderWaveVisualPlanner.BuildFrame(816, 1056, 32);

        segments.Should().ContainInOrder(
            new PageBorderWaveSegment(36, 32, 39, 35),
            new PageBorderWaveSegment(36, 1020, 39, 1023),
            new PageBorderWaveSegment(44, 32, 47, 35),
            new PageBorderWaveSegment(44, 1020, 47, 1023));
        segments.Should().Contain(new PageBorderWaveSegment(35, 36, 32, 39));
        segments.Should().Contain(new PageBorderWaveSegment(783, 36, 780, 39));
        PageBorderWaveVisualPlanner.StrokeOpacity.Should().BeApproximately(166.0 / 255.0, 0.0001);
    }

    [Fact]
    public void BuildFrame_RejectsCollapsedFrames()
    {
        PageBorderWaveVisualPlanner.BuildFrame(40, 40, 20).Should().BeEmpty();
    }

    [Theory]
    [InlineData(PageBorderDisplay.AllPages, 0, true)]
    [InlineData(PageBorderDisplay.AllPages, 3, true)]
    [InlineData(PageBorderDisplay.FirstPage, 0, true)]
    [InlineData(PageBorderDisplay.FirstPage, 1, false)]
    [InlineData(PageBorderDisplay.NotFirstPage, 0, false)]
    [InlineData(PageBorderDisplay.NotFirstPage, 1, true)]
    public void VisibilityPlanner_UsesZeroBasedPageSemantics(
        PageBorderDisplay display,
        int pageIndex,
        bool expected)
    {
        PageBorderVisibilityPlanner.ShouldRender(display, pageIndex).Should().Be(expected);
    }
}
