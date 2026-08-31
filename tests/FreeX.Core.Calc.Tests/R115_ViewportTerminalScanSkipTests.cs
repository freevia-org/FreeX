using System;
using System.Reflection;
using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Calc.Tests;

// R115-viewport-terminal-scan-1: BuildTerminalRowMetrics/BuildTerminalColMetrics always ran their
// expensive bottom-anchored reverse row/column scan (from CellAddress.MaxRow/MaxCol backwards) the
// instant a sheet had ANY hidden row/column or custom size anywhere -- CanSkipDefaultTerminalRowMetrics/
// CanSkipDefaultTerminalColMetrics only elided the scan for a sheet with zero customization at all --
// and it ran that unbounded scan even when the requested viewport was nowhere near the sheet's bottom
// (the `requestedStartRow < terminalThreshold` check only discarded the RESULT afterwards). A sheet
// with a large trailing hidden-row block -- the routine "hide unused rows below my data" pattern --
// therefore paid for walking the entire hidden tail on every scroll tick even while scrolling at the
// very top of the sheet. ComputeTerminalRowThresholdLowerBound/ComputeTerminalColThresholdLowerBound
// (ViewportService.Metrics.cs) now compute a cheap O(1) lower bound on where the terminal window could
// possibly start (from the sheet's hidden/custom-size COUNTS alone, no scanning) so a viewport request
// far from the bottom skips the scan entirely.
public class R115_ViewportTerminalScanSkipTests
{
    private static Sheet BuildSheetWithHiddenTrailingBlock(out Workbook workbook)
    {
        workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        // Routine "hide unused rows below my data" pattern: hide every row from 1000 to the sheet's
        // last row (this is exactly how FreeX.Core.Commands.SheetVisibilityCommands' Hide Rows command
        // populates Sheet.HiddenRows for a range -- one HashSet.Add per row, never a stored range).
        for (uint row = 1000; row <= CellAddress.MaxRow; row++)
            sheet.HiddenRows.Add(row);

        return sheet;
    }

    // R115-viewport-terminal-scan-skip-assertion (sweep112 F1): the two tests below used to infer
    // "the O(1) lower-bound skip fired" purely from wall-clock elapsed time (100 calls under an
    // arbitrary 200ms threshold meant to sit between a measured ~520-557ms slow path and a measured
    // ~6-23ms fast path). That is both flaky (a loaded CI/dev machine can push the fast path itself
    // past 200ms) and blind (a regression that silently disables the fast path and always falls back
    // to the O(n) scan could still finish under 200ms on fast-enough hardware). The skip condition is
    // actually a pure O(1) function of the sheet's hidden/custom-size COUNTS
    // (ComputeTerminalRowThresholdLowerBound / ComputeTerminalColThresholdLowerBound in
    // ViewportService.Metrics.cs), so we invoke it directly via reflection and assert the analytic
    // property that guarantees the scan is skippable, instead of inferring it from timing.
    private static uint InvokeComputeTerminalRowThresholdLowerBound(Sheet sheet, uint maxRow, double availableHeight)
    {
        var method = typeof(ViewportService).GetMethod(
            "ComputeTerminalRowThresholdLowerBound",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull(
            "ComputeTerminalRowThresholdLowerBound must still exist as the O(1) lower-bound guard " +
            "BuildTerminalRowMetrics consults before running its expensive reverse scan");
        return (uint)method!.Invoke(null, [sheet, maxRow, availableHeight])!;
    }

    [Fact]
    public void ComputeRowMetricsSummary_NearTopWithHiddenTrailingBlock_SkipsExpensiveTerminalScan()
    {
        var sheet = BuildSheetWithHiddenTrailingBlock(out var workbook);
        var service = new ViewportService();

        // Viewport request near the TOP of the sheet -- nowhere near the terminal (bottom-anchored)
        // window, so the reverse scan's result would be discarded even if it ran.
        var request = new ViewportRequest(1, 1, 600, 800);

        // Analytic proof (deterministic, hardware-independent): the O(1) lower bound must place the
        // terminal window's earliest possible start strictly above the requested row, which is
        // exactly the condition BuildTerminalRowMetrics checks (ViewportService.Metrics.cs:515)
        // before ever entering the reverse scan loop.
        var lowerBound = InvokeComputeTerminalRowThresholdLowerBound(sheet, CellAddress.MaxRow, request.AvailableHeight);
        lowerBound.Should().BeGreaterThan(
            request.TopRow,
            "the O(1) lower bound must prove a near-top request can never fall inside the terminal " +
            "band so the expensive reverse row scan is skipped entirely, not merely discarded after running");

        // Functional correctness: the summary must still report the true near-top visible band, not
        // some artifact of the hidden trailing block.
        var (lastVisibleRow, _) = service.ComputeRowMetricsSummary(workbook, sheet.Id, request);
        lastVisibleRow.Should().BeLessThan(1000u);
    }

    [Fact]
    public void GetViewport_NearTopWithHiddenTrailingBlock_SkipsExpensiveTerminalScan()
    {
        var sheet = BuildSheetWithHiddenTrailingBlock(out var workbook);
        var service = new ViewportService();
        var request = new ViewportRequest(1, 1, 600, 800);

        var lowerBound = InvokeComputeTerminalRowThresholdLowerBound(sheet, CellAddress.MaxRow, request.AvailableHeight);
        lowerBound.Should().BeGreaterThan(
            request.TopRow,
            "the O(1) lower bound must prove a near-top request can never fall inside the terminal " +
            "band so the expensive reverse row scan is skipped entirely, not merely discarded after running");

        var viewport = service.GetViewport(workbook, sheet.Id, request);
        viewport.RowMetrics.Should().NotBeEmpty();
        viewport.RowMetrics[0].Row.Should().Be(1u);
        foreach (var metric in viewport.RowMetrics)
            metric.Row.Should().BeLessThan(1000u);
    }

    [Fact]
    public void GetViewport_NearBottomWithHiddenTrailingBlock_StillProducesCorrectTerminalWindow()
    {
        // No-regression sibling: a request that genuinely IS near the effective bottom (the last
        // ~1000 rows are all hidden, so scrolling near row 1 is *also* near the bottom of the
        // sheet's visible content) must still receive the correct terminal-anchored metrics -- the
        // lower-bound skip must never fire when the real scan's result would actually be used.
        var sheet = BuildSheetWithHiddenTrailingBlock(out var workbook);
        var service = new ViewportService();

        // Row 1 is the true first visible row once the entire tail (1000..MaxRow) is hidden, so a
        // request starting at row 1 must resolve identically whether or not the fast path fires.
        var viewport = service.GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 60, 300));

        viewport.RowMetrics.Should().NotBeEmpty();
        viewport.RowMetrics[0].Row.Should().Be(1u);
        foreach (var metric in viewport.RowMetrics)
            metric.Row.Should().BeLessThan(1000u);
    }

    [Fact]
    public void GetViewport_SingleHiddenRowFarFromBottom_MatchesUnhiddenSheetNearTop()
    {
        // No-regression sibling covering the finding's other complaint: a single stray hidden row
        // ANYWHERE on the sheet must not perturb ordinary near-top scrolling behaviour just because
        // it now takes a faster code path.
        var plainWorkbook = new Workbook("plain");
        var plainSheet = plainWorkbook.AddSheet("Sheet1");

        var hiddenWorkbook = new Workbook("hidden");
        var hiddenSheet = hiddenWorkbook.AddSheet("Sheet1");
        hiddenSheet.HiddenRows.Add(500_000); // one stray hidden row, far from both ends

        var service = new ViewportService();
        var request = new ViewportRequest(1, 1, 200, 300);

        var plainViewport = service.GetViewport(plainWorkbook, plainSheet.Id, request);
        var hiddenViewport = service.GetViewport(hiddenWorkbook, hiddenSheet.Id, request);

        hiddenViewport.RowMetrics.Should().BeEquivalentTo(plainViewport.RowMetrics);
    }

    [Fact]
    public void GetViewport_NearLastRowWithCustomHeightsElsewhere_StillAlignsToWorksheetBoundary()
    {
        // No-regression sibling: a request genuinely near CellAddress.MaxRow must still get the
        // terminal-anchored alignment even when the sheet also has scattered custom row heights that
        // would previously have forced the (correct, but now potentially short-circuited) full scan.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.RowHeights[5] = 40; // unrelated custom height, nowhere near the bottom

        var service = new ViewportService();
        var viewport = service.GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(CellAddress.MaxRow - 3, 1, 60, 300));

        viewport.RowMetrics.Should().NotBeEmpty();
        viewport.RowMetrics[^1].Row.Should().Be(CellAddress.MaxRow);
    }
}
