using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class WorkbookViewportScrollPlannerDedupTests
{
    [Fact]
    public void CalculateViewportOrigin_AllowsHostStartupFallbackWithoutSheet()
    {
        WorkbookViewportScrollPlanner.CalculateViewportOrigin(
                sheet: null,
                verticalScrollValue: 0,
                horizontalScrollValue: 0)
            .Should().Be((1u, 1u));
    }

    [Fact]
    public void CalculateScrollValueToRevealCell_PlansForwardKeyboardRevealWithFrozenRows()
    {
        WorkbookViewportScrollPlanner.CalculateScrollValueToRevealCell(
                targetIndex: 19,
                firstVisibleIndex: 9,
                lastVisibleIndex: 13,
                absoluteLimit: CellAddress.MaxRow,
                visibleSpan: 5)
            .Should().Be(15);
    }

    [Fact]
    public void CalculateWheelScroll_UsesNormalizedTouchpadDeltaInSharedPlanner()
    {
        var notches = WorkbookViewportScrollPlanner.NormalizeWheelNotches(-30);

        WorkbookViewportScrollPlanner.CalculateWheelScroll(
                currentValue: 1,
                currentMaximum: 40,
                wheelNotches: notches,
                stepPerNotch: 3,
                visibleSpan: 40,
                absoluteLimit: CellAddress.MaxRow)
            .Should().Be((40d, 4d));
    }

    [Fact]
    public void ViewportScrollCalculator_IsThinHostAdapter()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ViewportScrollCalculator.cs");

        source.Should().Contain("WorkbookViewportScrollPlanner.PlanCellReveal");
        source.Should().Contain("WorkbookViewportScrollPlanner.CalculateScrollValueToRevealCell");
        source.Should().Contain("WorkbookViewportScrollPlanner.CalculateWheelScroll");
        source.Should().Contain("WorkbookViewportScrollPlanner.CalculateDragAutoScroll");
        source.Should().NotContain("targetIndex - (lastVisibleIndex - firstVisibleIndex)");
        source.Should().NotContain("var desired = currentValue");
    }
}
