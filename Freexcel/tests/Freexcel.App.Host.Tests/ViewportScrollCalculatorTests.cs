using FluentAssertions;
using Freexcel.App.Host;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class ViewportScrollCalculatorTests
{
    [Fact]
    public void CalculateViewportOrigin_DoesNotScrollToFrozenPaneBoundary()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1") { FrozenRows = 1, FrozenCols = 1 };

        ViewportScrollCalculator.CalculateViewportOrigin(sheet, verticalScrollValue: 1, horizontalScrollValue: 1)
            .Should().Be((2u, 2u));
    }

    [Fact]
    public void CalculateScrollbarArrowSmallIncrement_ExpandsAndMovesAtMaximum()
    {
        ViewportScrollCalculator.CalculateScrollbarArrowSmallIncrement(
                currentValue: 40,
                currentMaximum: 40,
                smallChange: 1,
                absoluteLimit: CellAddress.MaxRow)
            .Should().Be((41d, 41d));
    }

    [Fact]
    public void CalculateWheelScroll_ExtendsForwardAtCurrentMaximumWithoutOvershootingViewportOrigin()
    {
        ViewportScrollCalculator.CalculateWheelScroll(
                currentValue: 40,
                currentMaximum: 40,
                wheelNotches: -1,
                stepPerNotch: 3,
                visibleSpan: 40,
                absoluteLimit: CellAddress.MaxRow)
            .Should().Be((43d, 43d));
    }

    [Theory]
    [InlineData(30, 1)]
    [InlineData(-30, -1)]
    [InlineData(240, 2)]
    public void NormalizeWheelNotches_PreservesHighResolutionTouchpadDeltas(int delta, int expected)
    {
        ViewportScrollCalculator.NormalizeWheelNotches(delta).Should().Be(expected);
    }

    [Fact]
    public void CalculateScrollbarMaximumForUsedRange_ReturnsToUsedRangeWhenScrolledBack()
    {
        ViewportScrollCalculator.CalculateScrollbarMaximumForUsedRange(
                usedMax: 20,
                visibleSpan: 40,
                currentScrollValue: 1,
                absoluteLimit: CellAddress.MaxRow)
            .Should().Be(40);
    }
}
