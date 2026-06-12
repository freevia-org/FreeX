using FluentAssertions;
using FreeX.App.Host;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class ArrangeAllLayoutPlannerTests
{
    [Fact]
    public void Horizontal_StacksWindowsTopToBottomAcrossTheWorkArea()
    {
        var bounds = ArrangeAllLayoutPlanner.Arrange(
            WorkbookWindowArrangement.Horizontal,
            workAreaWidth: 900,
            workAreaHeight: 600,
            windowCount: 3);

        bounds.Should().HaveCount(3);
        bounds[0].Should().Be(new System.Windows.Rect(0, 0, 900, 200));
        bounds[1].Should().Be(new System.Windows.Rect(0, 200, 900, 200));
        bounds[2].Should().Be(new System.Windows.Rect(0, 400, 900, 200));
    }

    [Fact]
    public void Vertical_StacksWindowsLeftToRightAcrossTheWorkArea()
    {
        var bounds = ArrangeAllLayoutPlanner.Arrange(
            WorkbookWindowArrangement.Vertical,
            workAreaWidth: 900,
            workAreaHeight: 600,
            windowCount: 3);

        bounds.Should().HaveCount(3);
        bounds[0].Should().Be(new System.Windows.Rect(0, 0, 300, 600));
        bounds[1].Should().Be(new System.Windows.Rect(300, 0, 300, 600));
        bounds[2].Should().Be(new System.Windows.Rect(600, 0, 300, 600));
    }

    [Fact]
    public void Tiled_BalancesWindowsIntoRowsAndColumnsWithoutLeavingAnEmptyLastRowCell()
    {
        var bounds = ArrangeAllLayoutPlanner.Arrange(
            WorkbookWindowArrangement.Tiled,
            workAreaWidth: 1200,
            workAreaHeight: 800,
            windowCount: 5);

        bounds.Should().HaveCount(5);
        bounds[0].Should().Be(new System.Windows.Rect(0, 0, 400, 400));
        bounds[1].Should().Be(new System.Windows.Rect(400, 0, 400, 400));
        bounds[2].Should().Be(new System.Windows.Rect(800, 0, 400, 400));
        bounds[3].Should().Be(new System.Windows.Rect(0, 400, 600, 400));
        bounds[4].Should().Be(new System.Windows.Rect(600, 400, 600, 400));
    }

    [Fact]
    public void Cascade_OffsetsWindowsDiagonallyAndKeepsThemInsideTheWorkArea()
    {
        var bounds = ArrangeAllLayoutPlanner.Arrange(
            WorkbookWindowArrangement.Cascade,
            workAreaWidth: 1200,
            workAreaHeight: 900,
            windowCount: 3);

        bounds.Should().HaveCount(3);
        bounds[0].Should().Be(new System.Windows.Rect(0, 0, 900, 675));
        bounds[1].Should().Be(new System.Windows.Rect(24, 24, 900, 675));
        bounds[2].Should().Be(new System.Windows.Rect(48, 48, 900, 675));
        bounds.Should().OnlyContain(rect => rect.Left + rect.Width <= 1200);
        bounds.Should().OnlyContain(rect => rect.Top + rect.Height <= 900);
    }

    [Fact]
    public void Cascade_ReducesOffsetWhenManyWindowsMustFit()
    {
        var bounds = ArrangeAllLayoutPlanner.Arrange(
            WorkbookWindowArrangement.Cascade,
            workAreaWidth: 1200,
            workAreaHeight: 900,
            windowCount: 20);

        bounds.Should().HaveCount(20);
        bounds[^1].Left.Should().BeLessThan(ArrangeAllLayoutPlanner.CascadeOffset * 19);
        bounds.Should().OnlyContain(rect => rect.Left + rect.Width <= 1200);
        bounds.Should().OnlyContain(rect => rect.Top + rect.Height <= 900);
    }

    [Fact]
    public void Arrange_NonPositiveWorkAreaUsesFallbackBounds()
    {
        var bounds = ArrangeAllLayoutPlanner.Arrange(
            WorkbookWindowArrangement.Vertical,
            workAreaWidth: 0,
            workAreaHeight: -1,
            windowCount: 1);

        bounds.Should().ContainSingle()
            .Which.Should().Be(new System.Windows.Rect(
                0,
                0,
                ArrangeAllLayoutPlanner.FallbackWidth,
                ArrangeAllLayoutPlanner.FallbackHeight));
    }

    [Fact]
    public void Arrange_NoWindowsOrInvalidArrangementReturnsNoBounds()
    {
        ArrangeAllLayoutPlanner.Arrange(WorkbookWindowArrangement.Tiled, 100, 100, 0)
            .Should().BeEmpty();
        ArrangeAllLayoutPlanner.Arrange((WorkbookWindowArrangement)99, 100, 100, 1)
            .Should().BeEmpty();
    }
}
