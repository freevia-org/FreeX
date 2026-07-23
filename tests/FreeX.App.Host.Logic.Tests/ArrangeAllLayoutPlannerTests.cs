using FluentAssertions;
using Free.Shared.Shell;

namespace FreeX.App.Host.Tests;

public sealed class ArrangeAllLayoutPlannerTests
{
    [Fact]
    public void Horizontal_StacksWindowsTopToBottomAcrossTheWorkArea()
    {
        var bounds = ArrangeAllLayoutPlanner.Arrange(
            ShellWindowArrangement.Horizontal,
            workAreaWidth: 900,
            workAreaHeight: 600,
            windowCount: 3);

        bounds.Should().HaveCount(3);
        bounds[0].Should().Be(new ShellRect(0, 0, 900, 200));
        bounds[1].Should().Be(new ShellRect(0, 200, 900, 200));
        bounds[2].Should().Be(new ShellRect(0, 400, 900, 200));
    }

    [Fact]
    public void Vertical_StacksWindowsLeftToRightAcrossTheWorkArea()
    {
        var bounds = ArrangeAllLayoutPlanner.Arrange(
            ShellWindowArrangement.Vertical,
            workAreaWidth: 900,
            workAreaHeight: 600,
            windowCount: 3);

        bounds.Should().HaveCount(3);
        bounds[0].Should().Be(new ShellRect(0, 0, 300, 600));
        bounds[1].Should().Be(new ShellRect(300, 0, 300, 600));
        bounds[2].Should().Be(new ShellRect(600, 0, 300, 600));
    }

    [Fact]
    public void Tiled_BalancesWindowsIntoRowsAndColumnsWithoutLeavingAnEmptyLastRowCell()
    {
        var bounds = ArrangeAllLayoutPlanner.Arrange(
            ShellWindowArrangement.Tiled,
            workAreaWidth: 1200,
            workAreaHeight: 800,
            windowCount: 5);

        bounds.Should().HaveCount(5);
        bounds[0].Should().Be(new ShellRect(0, 0, 400, 400));
        bounds[1].Should().Be(new ShellRect(400, 0, 400, 400));
        bounds[2].Should().Be(new ShellRect(800, 0, 400, 400));
        bounds[3].Should().Be(new ShellRect(0, 400, 600, 400));
        bounds[4].Should().Be(new ShellRect(600, 400, 600, 400));
    }

    [Fact]
    public void RowFirst_UsesFreeWThreeColumnGeometryIncludingItsIncompleteFinalRow()
    {
        var bounds = ArrangeAllLayoutPlanner.ArrangeRowFirst(
            workAreaWidth: 1200,
            workAreaHeight: 800,
            windowCount: 5,
            maxColumns: 3);

        bounds.Should().HaveCount(5);
        bounds[0].Should().Be(new ShellRect(0, 0, 400, 400));
        bounds[1].Should().Be(new ShellRect(400, 0, 400, 400));
        bounds[2].Should().Be(new ShellRect(800, 0, 400, 400));
        bounds[3].Should().Be(new ShellRect(0, 400, 400, 400));
        bounds[4].Should().Be(new ShellRect(400, 400, 400, 400));
    }

    [Fact]
    public void RowFirst_UsesTheSameThreeColumnsForAOneWindowFinalRow()
    {
        var bounds = ArrangeAllLayoutPlanner.ArrangeRowFirst(1000, 600, 4, maxColumns: 3);

        bounds.Should().HaveCount(4);
        bounds[3].Should().Be(new ShellRect(0, 300, 1000d / 3, 300));
    }

    [Fact]
    public void RowFirst_RejectsEmptyWindowsOrAnInvalidColumnPolicy()
    {
        ArrangeAllLayoutPlanner.ArrangeRowFirst(100, 100, 0, maxColumns: 3).Should().BeEmpty();
        ArrangeAllLayoutPlanner.ArrangeRowFirst(100, 100, 2, maxColumns: 0).Should().BeEmpty();
    }

    [Fact]
    public void Cascade_OffsetsWindowsDiagonallyAndKeepsThemInsideTheWorkArea()
    {
        var bounds = ArrangeAllLayoutPlanner.Arrange(
            ShellWindowArrangement.Cascade,
            workAreaWidth: 1200,
            workAreaHeight: 900,
            windowCount: 3);

        bounds.Should().HaveCount(3);
        bounds[0].Should().Be(new ShellRect(0, 0, 900, 675));
        bounds[1].Should().Be(new ShellRect(24, 24, 900, 675));
        bounds[2].Should().Be(new ShellRect(48, 48, 900, 675));
        bounds.Should().OnlyContain(rect => rect.Left + rect.Width <= 1200);
        bounds.Should().OnlyContain(rect => rect.Top + rect.Height <= 900);
    }

    [Fact]
    public void Cascade_ReducesOffsetWhenManyWindowsMustFit()
    {
        var bounds = ArrangeAllLayoutPlanner.Arrange(
            ShellWindowArrangement.Cascade,
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
            ShellWindowArrangement.Vertical,
            workAreaWidth: 0,
            workAreaHeight: -1,
            windowCount: 1);

        bounds.Should().ContainSingle()
            .Which.Should().Be(new ShellRect(
                0,
                0,
                ArrangeAllLayoutPlanner.FallbackWidth,
                ArrangeAllLayoutPlanner.FallbackHeight));
    }

    [Fact]
    public void Arrange_NoWindowsOrInvalidArrangementReturnsNoBounds()
    {
        ArrangeAllLayoutPlanner.Arrange(ShellWindowArrangement.Tiled, 100, 100, 0)
            .Should().BeEmpty();
        ArrangeAllLayoutPlanner.Arrange((ShellWindowArrangement)99, 100, 100, 1)
            .Should().BeEmpty();
    }
}
