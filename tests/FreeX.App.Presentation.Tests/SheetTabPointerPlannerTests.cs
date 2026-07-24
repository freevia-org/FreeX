using FluentAssertions;
using FreeX.App.Presentation.SheetUI;

namespace FreeX.App.Presentation.Tests;

public sealed class SheetTabPointerPlannerTests
{
    [Theory]
    [InlineData(0, 1, false, 0)]
    [InlineData(0, 1, true, 1)]
    [InlineData(3, 1, false, 1)]
    [InlineData(3, 1, true, 2)]
    [InlineData(1, 3, false, 2)]
    [InlineData(1, 3, true, 3)]
    public void CalculateDropIndex_UsesTargetHalfAndAccountsForSourceRemoval(
        int fromIndex,
        int targetIndex,
        bool insertAfterTarget,
        int expected)
    {
        SheetTabPointerPlanner.CalculateDropIndex(fromIndex, targetIndex, insertAfterTarget)
            .Should().Be(expected);
    }

    [Fact]
    public void CalculateDropIndex_RejectsMissingIndexes()
    {
        SheetTabPointerPlanner.CalculateDropIndex(-1, 2, insertAfterTarget: false).Should().Be(-1);
        SheetTabPointerPlanner.CalculateDropIndex(2, -1, insertAfterTarget: false).Should().Be(-1);
    }

    [Fact]
    public void CalculateHorizontalScrollOffset_ClampsToScrollableExtent()
    {
        SheetTabPointerPlanner.CalculateHorizontalScrollOffset(0, 800, 300, -140).Should().Be(0);
        SheetTabPointerPlanner.CalculateHorizontalScrollOffset(0, 800, 300, 140).Should().Be(140);
        SheetTabPointerPlanner.CalculateHorizontalScrollOffset(700, 800, 300, 140).Should().Be(500);
    }

    [Fact]
    public void ScrollAvailability_IsBasedOnViewportOffsetNotActiveSheet()
    {
        SheetTabPointerPlanner.CanScrollLeft(0).Should().BeFalse();
        SheetTabPointerPlanner.CanScrollLeft(1).Should().BeTrue();
        SheetTabPointerPlanner.CanScrollRight(0, 800, 300).Should().BeTrue();
        SheetTabPointerPlanner.CanScrollRight(500, 800, 300).Should().BeFalse();
    }
}
