using System.Diagnostics;
using FluentAssertions;
using FreeX.App.Host;
using FreeX.Core.Model;
using Xunit.Abstractions;

namespace FreeX.App.Host.Tests;

public sealed class WorkbookViewportScrollPlannerTests(ITestOutputHelper output)
{
    [Fact]
    public void CalculateViewportOrigin_DoesNotScrollToFrozenPaneBoundary()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1") { FrozenRows = 1, FrozenCols = 1 };

        WorkbookViewportScrollPlanner.CalculateViewportOrigin(sheet, verticalScrollValue: 1, horizontalScrollValue: 1)
            .Should().Be((2u, 2u));
    }

    [Fact]
    public void CalculateScrollbarArrowSmallIncrement_ExpandsAndMovesAtMaximum()
    {
        WorkbookViewportScrollPlanner.CalculateScrollbarArrowSmallIncrement(
                currentValue: 40,
                currentMaximum: 40,
                smallChange: 1,
                absoluteLimit: CellAddress.MaxRow)
            .Should().Be((41d, 41d));
    }

    [Fact]
    public void CalculateWheelScroll_ExtendsForwardAtCurrentMaximumWithoutOvershootingViewportOrigin()
    {
        WorkbookViewportScrollPlanner.CalculateWheelScroll(
                currentValue: 40,
                currentMaximum: 40,
                wheelNotches: -1,
                stepPerNotch: 3,
                visibleSpan: 40,
                absoluteLimit: CellAddress.MaxRow)
            .Should().Be((43d, 43d));
    }

    [Fact]
    public void CalculateDragAutoScroll_ExtendsForwardAtCurrentMaximumWithoutOvershootingViewportOrigin()
    {
        WorkbookViewportScrollPlanner.CalculateDragAutoScroll(
                currentValue: 40,
                currentMaximum: 40,
                direction: 1,
                step: 2,
                visibleSpan: 40,
                absoluteLimit: CellAddress.MaxCol)
            .Should().Be((42d, 42d));
    }

    [Fact]
    public void CalculateDragAutoScroll_MovesBackwardWithoutChangingMaximum()
    {
        WorkbookViewportScrollPlanner.CalculateDragAutoScroll(
                currentValue: 40,
                currentMaximum: 80,
                direction: -1,
                step: 2,
                visibleSpan: 40,
                absoluteLimit: CellAddress.MaxCol)
            .Should().Be((80d, 38d));
    }

    [Fact]
    public void CalculateWheelScroll_UsesNormalizedTouchpadDeltaForSmallVerticalMovement()
    {
        var notches = WorkbookViewportScrollPlanner.NormalizeWheelNotches(-30);

        WorkbookViewportScrollPlanner.CalculateWheelScroll(
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
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Viewport.cs");

        source.Should().Contain("WorkbookViewportScrollPlanner.NormalizeWheelNotches(e.Delta)");
        source.Should().Contain("WorkbookViewportScrollPlanner.CalculateWheelScroll");
        WorkbookViewportScrollPlanner.NormalizeWheelNotches(240)
            .Should().Be(WorkbookViewportScrollPlanner.NormalizeWheelNotches(240),
                "the WPF route must remain a thin facade over the shared wheel authority");
    }

    [Fact]
    public void MainWindowWheelHandler_RoutesSplitPaneWheelThroughPointerResolvedTarget()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Viewport.cs");
        var wheelHandler = source[
            source.IndexOf("private void SheetGrid_MouseWheel", StringComparison.Ordinal)..
            source.IndexOf("private void OnAutofillEdgeScrollRequested", StringComparison.Ordinal)];

        wheelHandler.Should().Contain("var horizontal = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;");
        wheelHandler.Should().Contain("var wheelPos = e.GetPosition(SheetGrid);");
        wheelHandler.Should().Contain("FreeX.App.UI.GridView.ResolveSplitPaneWheelTarget(");
        wheelHandler.Should().Contain("SheetGrid.ActualWidth");
        wheelHandler.Should().Contain("SheetGrid.ActualHeight");
        wheelHandler.Should().Contain("_activeSplitPaneRegion = wheelTarget.Region;");
        wheelHandler.Should().Contain("horizontal = wheelTarget.Horizontal;");
        wheelHandler.IndexOf("ResolveSplitPaneWheelTarget", StringComparison.Ordinal)
            .Should()
            .BeLessThan(wheelHandler.IndexOf("CanScrollSplitPaneRegion", StringComparison.Ordinal));
    }

    [Fact]
    public void MainWindowEnsureCellVisible_DelegatesCellRevealPlanningToSharedCalculator()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Viewport.cs");
        var ensureCellVisible = source[
            source.IndexOf("private void EnsureCellVisible", StringComparison.Ordinal)..
            source.IndexOf("// \u2500\u2500 Navigation helpers", StringComparison.Ordinal)];

        ensureCellVisible.Should().Contain("WorkbookViewportScrollPlanner.PlanCellReveal(");
        ensureCellVisible.Should().Contain("VerticalScroll.Maximum = plan.Vertical.Maximum;");
        ensureCellVisible.Should().Contain("HorizontalScroll.Maximum = plan.Horizontal.Maximum;");
        ensureCellVisible.Should().NotContain("GetScrollableRowWindow");
        ensureCellVisible.Should().NotContain("GetScrollableColumnWindow");
        ensureCellVisible.Should().NotContain("CalculateScrollValueToRevealCell(");
        ensureCellVisible.Should().NotContain(".Where(");
        ensureCellVisible.Should().NotContain(".ToList()");
        ensureCellVisible.Should().NotContain(".Any(");
    }

    [Theory]
    [InlineData(30, 1)]
    [InlineData(-30, -1)]
    [InlineData(240, 2)]
    public void NormalizeWheelNotches_PreservesHighResolutionTouchpadDeltas(int delta, int expected)
    {
        WorkbookViewportScrollPlanner.NormalizeWheelNotches(delta).Should().Be(expected);
    }

    [Fact]
    public void CalculateScrollbarMaximumForUsedRange_ReturnsToUsedRangeWhenScrolledBack()
    {
        WorkbookViewportScrollPlanner.CalculateScrollbarMaximumForUsedRange(
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
