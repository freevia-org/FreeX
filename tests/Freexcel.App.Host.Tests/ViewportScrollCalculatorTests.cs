using System.Diagnostics;
using System.IO;
using FluentAssertions;
using Freexcel.App.Host;
using Freexcel.Core.Model;
using Xunit.Abstractions;

namespace Freexcel.App.Host.Tests;

public sealed class ViewportScrollCalculatorTests(ITestOutputHelper output)
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

    [Fact]
    public void CalculateWheelScroll_UsesNormalizedTouchpadDeltaForSmallVerticalMovement()
    {
        var notches = ViewportScrollCalculator.NormalizeWheelNotches(-30);

        ViewportScrollCalculator.CalculateWheelScroll(
                currentValue: 1,
                currentMaximum: 40,
                wheelNotches: notches,
                stepPerNotch: 3,
                visibleSpan: 40,
                absoluteLimit: CellAddress.MaxRow)
            .Should()
            .Be((40d, 4d));
    }

    [Fact]
    public void MainWindowWheelHandler_NormalizesRawMouseWheelDeltaBeforeScrolling()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Viewport.cs"));

        source.Should().Contain("ViewportScrollCalculator.NormalizeWheelNotches(e.Delta)");
        source.Should().Contain("ViewportScrollCalculator.CalculateWheelScroll");
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

    [Fact]
    public void CalculateUsedRangeExtents_BoundsSparseSheetWithoutUsedCellDictionaryCopy()
    {
        var empty = new Sheet(SheetId.New(), "Empty");
        MainWindow.CalculateUsedRangeExtents(empty).Should().Be((1u, 1u));

        var sheet = new Sheet(SheetId.New(), "Sparse");
        for (uint i = 1; i <= 10_000; i++)
        {
            sheet.SetCell(
                new CellAddress(sheet.Id, i * 100, (i % 100) + 1),
                new NumberValue(i));
        }
        sheet.SetCell(new CellAddress(sheet.Id, 1_000_000, 16_000), new TextValue("edge"));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        const int repetitions = 100;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        (uint UsedMaxRow, uint UsedMaxCol) extents = default;
        for (var i = 0; i < repetitions; i++)
            extents = MainWindow.CalculateUsedRangeExtents(sheet);
        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        extents.Should().Be((1_000_000u, 16_000u));
        allocated.Should().BeLessThan(100_000);
        output.WriteLine(
            $"CalculateUsedRangeExtents repeated {repetitions}x over {sheet.CellCount:N0} cells: {stopwatch.Elapsed.TotalMilliseconds:F2} ms, {allocated:N0} bytes allocated.");
    }
}
