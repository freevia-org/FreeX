using FluentAssertions;
using FreeX.App.Presentation.SheetUI;

namespace FreeX.App.Presentation.Tests.SheetUI;

public sealed class SheetTabViewportScrollPlannerTests
{
    [Fact]
    public void CalculateOffsetForSelectedTab_KeepsCurrentOffsetWhenTabIsFullyVisible()
    {
        var offset = SheetTabViewportScrollPlanner.CalculateOffsetForSelectedTab(
            currentOffset: 240,
            selectedTabViewportLeft: 72,
            selectedTabViewportRight: 168,
            visibleViewportRight: 300,
            scrollableWidth: 900);

        offset.Should().Be(240);
    }

    [Fact]
    public void CalculateOffsetForSelectedTab_ScrollsLeftOnlyEnoughForClippedLeftEdge()
    {
        var offset = SheetTabViewportScrollPlanner.CalculateOffsetForSelectedTab(
            currentOffset: 240,
            selectedTabViewportLeft: -18,
            selectedTabViewportRight: 86,
            visibleViewportRight: 300,
            scrollableWidth: 900);

        offset.Should().Be(222);
    }

    [Fact]
    public void CalculateOffsetForSelectedTab_ScrollsRightOnlyEnoughForClippedRightEdge()
    {
        var offset = SheetTabViewportScrollPlanner.CalculateOffsetForSelectedTab(
            currentOffset: 240,
            selectedTabViewportLeft: 220,
            selectedTabViewportRight: 332,
            visibleViewportRight: 300,
            scrollableWidth: 900);

        offset.Should().Be(272);
    }

    [Theory]
    [InlineData(-18.0, 92.0, 3.0, 0.0)]
    [InlineData(260.0, 332.0, 890.0, 900.0)]
    public void CalculateOffsetForSelectedTab_ClampsToScrollableRange(
        double selectedTabViewportLeft,
        double selectedTabViewportRight,
        double currentOffset,
        double expectedOffset)
    {
        var offset = SheetTabViewportScrollPlanner.CalculateOffsetForSelectedTab(
            currentOffset,
            selectedTabViewportLeft,
            selectedTabViewportRight,
            visibleViewportRight: 300,
            scrollableWidth: 900);

        offset.Should().Be(expectedOffset);
    }

    [Fact]
    public void CalculateOffsetForSelectedTab_UsesEpsilonSoSubpixelEdgesDoNotMoveTabs()
    {
        var offset = SheetTabViewportScrollPlanner.CalculateOffsetForSelectedTab(
            currentOffset: 240,
            selectedTabViewportLeft: -0.25,
            selectedTabViewportRight: 300.25,
            visibleViewportRight: 300,
            scrollableWidth: 900);

        offset.Should().Be(240);
    }
}
