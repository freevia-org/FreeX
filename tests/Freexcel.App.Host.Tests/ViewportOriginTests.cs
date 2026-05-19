using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class ViewportOriginTests
{
    [Fact]
    public void CalculateViewportOrigin_DoesNotScrollToFrozenPaneBoundary()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1") { FrozenRows = 1, FrozenCols = 1 };

        MainWindow.CalculateViewportOrigin(sheet, verticalScrollValue: 1, horizontalScrollValue: 1)
            .Should().Be((2u, 2u));
    }

    [Fact]
    public void CalculateViewportOrigin_ScrollsFrozenPaneBodyImmediately()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1") { FrozenRows = 3, FrozenCols = 2 };

        MainWindow.CalculateViewportOrigin(sheet, verticalScrollValue: 1, horizontalScrollValue: 1)
            .Should().Be((4u, 3u));
        MainWindow.CalculateViewportOrigin(sheet, verticalScrollValue: 2, horizontalScrollValue: 2)
            .Should().Be((5u, 4u));
    }

    [Fact]
    public void CalculateOpenedWorksheetScrollValue_UsesSavedTopLeftInsteadOfFrozenBoundary()
    {
        MainWindow.CalculateOpenedWorksheetScrollValue(
                savedTopLeftIndex: null,
                fallbackIndex: 1,
                absoluteLimit: CellAddress.MaxRow)
            .Should().Be(1);

        MainWindow.CalculateOpenedWorksheetScrollValue(
                savedTopLeftIndex: 42,
                fallbackIndex: 1,
                absoluteLimit: CellAddress.MaxRow)
            .Should().Be(42);
    }

    [Fact]
    public void CalculateOpenedWorksheetScrollValue_ConvertsSavedFrozenBodyOriginToScrollbarValue()
    {
        MainWindow.CalculateOpenedWorksheetScrollValue(
                savedTopLeftIndex: 42,
                fallbackIndex: 1,
                absoluteLimit: CellAddress.MaxRow,
                frozenCount: 3)
            .Should().Be(39);
    }

    [Fact]
    public void WorksheetIndexToScrollbarValue_MapsFrozenCellsToFirstScrollablePosition()
    {
        MainWindow.WorksheetIndexToScrollbarValue(1, frozenCount: 3).Should().Be(1);
        MainWindow.WorksheetIndexToScrollbarValue(4, frozenCount: 3).Should().Be(1);
        MainWindow.WorksheetIndexToScrollbarValue(5, frozenCount: 3).Should().Be(2);
    }

    [Fact]
    public void CalculateScrollableLimit_RemovesFrozenPaneDeadZoneFromScrollbarRange()
    {
        MainWindow.CalculateScrollableLimit(absoluteLimit: 100, frozenCount: 3)
            .Should().Be(97);
    }

    [Fact]
    public void CalculateScrollValueToRevealCell_KeepsKeyboardTargetInsideViewport()
    {
        MainWindow.CalculateScrollValueToRevealCell(targetIndex: 100, firstVisibleIndex: 1, lastVisibleIndex: 40)
            .Should().Be(61);
    }

    [Fact]
    public void CalculateScrollbarMaximumForKeyboardReveal_ExpandsPastUsedRangeWhenNeeded()
    {
        MainWindow.CalculateScrollbarMaximumForKeyboardReveal(currentMaximum: 40, desiredScrollValue: 61)
            .Should().Be(61);
    }

    [Fact]
    public void CalculateScrollbarArrowSmallIncrement_ExpandsAndMovesAtMaximum()
    {
        MainWindow.CalculateScrollbarArrowSmallIncrement(
                currentValue: 40,
                currentMaximum: 40,
                smallChange: 1,
                absoluteLimit: CellAddress.MaxRow)
            .Should().Be((41d, 41d));
    }

    [Fact]
    public void CalculateMaximumViewportOrigin_KeepsLastCellAtViewportEdge()
    {
        MainWindow.CalculateMaximumViewportOrigin(
                absoluteLimit: CellAddress.MaxRow,
                visibleSpan: 40)
            .Should().Be(1_048_537);

        MainWindow.CalculateMaximumViewportOrigin(
                absoluteLimit: CellAddress.MaxCol,
                visibleSpan: 20)
            .Should().Be(16_365);
    }

    [Fact]
    public void ClampViewportOrigin_ConstrainsRawScrollbarValueToGrid()
    {
        MainWindow.ClampViewportOrigin(
                rawValue: 2_000_000,
                absoluteLimit: CellAddress.MaxRow,
                visibleSpan: 40)
            .Should().Be(1_048_537);
    }

    [Fact]
    public void CalculateScrollValueToRevealCell_ClampsToMaximumViewportOrigin()
    {
        MainWindow.CalculateScrollValueToRevealCell(
                targetIndex: CellAddress.MaxRow,
                firstVisibleIndex: 1,
                lastVisibleIndex: 40,
                absoluteLimit: CellAddress.MaxRow,
                visibleSpan: 40)
            .Should().Be(1_048_537);
    }

    [Fact]
    public void CalculateWheelScroll_ExtendsForwardAtCurrentMaximumWithoutOvershootingViewportOrigin()
    {
        MainWindow.CalculateWheelScroll(
                currentValue: 40,
                currentMaximum: 40,
                wheelNotches: -1,
                stepPerNotch: 3,
                visibleSpan: 40,
                absoluteLimit: CellAddress.MaxRow)
            .Should().Be((43d, 43d));
    }

    [Fact]
    public void CalculateViewportAvailableWidth_ReclaimsSpaceWhenRowHeaderShrinks()
    {
        MainWindow.CalculateViewportAvailableWidth(
                gridWidth: 800,
                rowHeaderWidth: 30,
                zoomLevel: 1)
            .Should().Be(770);
    }

    [Theory]
    [InlineData(1_048_576u, 1_048_576u, 1_048_575u)]
    [InlineData(16_384u, 16_384u, 16_383u)]
    public void CalculateScrollValueToRevealCell_ClampsToWorksheetLimit(
        uint target,
        uint absoluteLimit,
        uint expected)
    {
        MainWindow.CalculateScrollValueToRevealCell(
                targetIndex: target,
                firstVisibleIndex: target - 2,
                lastVisibleIndex: target - 1,
                absoluteLimit: absoluteLimit)
            .Should().Be(expected);
    }

    [Fact]
    public void CalculateScrollbarMaximumForKeyboardReveal_NeverExceedsWorksheetLimit()
    {
        MainWindow.CalculateScrollbarMaximumForKeyboardReveal(
                currentMaximum: 1_048_575,
                desiredScrollValue: 1_048_576,
                absoluteLimit: 1_048_576)
            .Should().Be(1_048_576);

        MainWindow.CalculateScrollbarMaximumForKeyboardReveal(
                currentMaximum: 1_048_576,
                desiredScrollValue: 1_048_577,
                absoluteLimit: 1_048_576)
            .Should().Be(1_048_576);
    }

    [Fact]
    public void CalculateScrollbarMaximumForUsedRange_ReturnsToUsedRangeWhenScrolledBack()
    {
        MainWindow.CalculateScrollbarMaximumForUsedRange(
                usedMax: 20,
                visibleSpan: 40,
                currentScrollValue: 1,
                absoluteLimit: 1_048_576)
            .Should().Be(40);
    }
}
