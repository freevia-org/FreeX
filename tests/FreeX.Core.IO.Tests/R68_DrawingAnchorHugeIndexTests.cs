using System;
using System.Threading.Tasks;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R68-io-drawing-anchor-6-1: a twoCellAnchor whose 'to' marker is correctly ordered (comes strictly after
/// 'from' on that axis, so the R47 inverted-span clamp does not fire) but is a schema-valid, absurdly huge
/// index -- e.g. a hand-edited/corrupted file with col 3,000,000,000 -- made the
/// `for (offset=0u; offset&lt;count; offset++)` span loop in SumColumnPixels/SumRowPixels iterate on the
/// order of billions of times, hanging file load. Excel's writer side already caps at 16,384 columns /
/// 1,048,576 rows (see XlsxSourceDrawingGeometryRewriter/XlsxWorksheetChartWriter's MaxColumnIndex/
/// MaxRowIndex), so this clamps the from/to indices to those same real ceilings before the walk in both
/// GetAnchorSize (shared by Picture/Shape/TextBox) and ApplyToChart.
/// </summary>
public sealed class R68_DrawingAnchorHugeIndexTests
{
    private const uint MaxColumnIndexZeroBased = 16383;
    private const uint MaxRowIndexZeroBased = 1048575;

    private static Sheet BuildSheet()
    {
        var workbook = new Workbook("DrawingAnchorHugeIndex");
        return workbook.AddSheet("Sheet1");
    }

    private static Task<T> RunOnDedicatedThread<T>(Func<T> work) =>
        Task.Factory.StartNew(
            work,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private static Task RunOnDedicatedThread(Action work) =>
        Task.Factory.StartNew(
            work,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private static XlsxDrawingAnchor BuildTwoCellAnchor(uint fromCol, uint toCol, uint fromRow, uint toRow) =>
        new(
            Kind: ChartDrawingAnchorKind.TwoCell,
            FromRowZeroBased: fromRow,
            FromColumnZeroBased: fromCol,
            FromRowOffset: 0,
            FromColumnOffset: 0,
            AbsoluteLeft: null,
            AbsoluteTop: null,
            ToRowZeroBased: toRow,
            ToColumnZeroBased: toCol,
            ToRowOffset: 0,
            ToColumnOffset: 0,
            Width: null,
            Height: null);

    [Fact]
    public async Task GetAnchorSize_HugeButCorrectlyOrderedToColumn_ClampsSpan_InsteadOfHanging()
    {
        var sheet = BuildSheet();

        // Correctly ordered (toCol > fromCol), so the R47 inverted-span guard does not clamp this to zero;
        // only the huge-index ceiling clamp added here prevents the multi-billion-iteration loop.
        var anchor = BuildTwoCellAnchor(fromCol: 5, toCol: 3_000_000_000u, fromRow: 0, toRow: 3);

        var work = RunOnDedicatedThread(() => XlsxDrawingAnchorApplier.GetAnchorSize(anchor, sheet));
        var completed = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(10))) == work;

        completed.Should().BeTrue(
            "a huge but correctly-ordered to-column index must be capped to Excel's real column ceiling before " +
            "the span walk, not left to iterate billions of times and hang file load");

        var (width, _) = await work;
        var expectedSpan = MaxColumnIndexZeroBased - 5u; // clamped toCol (16383) - fromCol (5)
        var expectedWidth = sheet.DefaultColumnWidth * 8 * expectedSpan;
        width.Should().BeApproximately(expectedWidth, 1e-6,
            "the column span must clamp to Excel's real ceiling (16384 columns, zero-based 16383), matching Excel " +
            "effectively ignoring/truncating an out-of-range column index");
    }

    [Fact]
    public async Task GetAnchorSize_HugeButCorrectlyOrderedToRow_ClampsSpan_InsteadOfHanging()
    {
        var sheet = BuildSheet();

        var anchor = BuildTwoCellAnchor(fromCol: 0, toCol: 3, fromRow: 5, toRow: 2_000_000_000u);

        var work = RunOnDedicatedThread(() => XlsxDrawingAnchorApplier.GetAnchorSize(anchor, sheet));
        var completed = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(10))) == work;

        completed.Should().BeTrue(
            "a huge but correctly-ordered to-row index must be capped to Excel's real row ceiling before the span " +
            "walk, not left to iterate billions of times and hang file load");

        var (_, height) = await work;
        var expectedSpan = MaxRowIndexZeroBased - 5u; // clamped toRow (1048575) - fromRow (5)
        var expectedHeight = sheet.DefaultRowHeight * expectedSpan;
        height.Should().Be(expectedHeight,
            "the row span must clamp to Excel's real ceiling (1,048,576 rows, zero-based 1,048,575)");
    }

    [Fact]
    public async Task ApplyToChart_HugeButCorrectlyOrderedToColumn_ClampsSpan_InsteadOfHanging()
    {
        var sheet = BuildSheet();
        var anchor = BuildTwoCellAnchor(fromCol: 5, toCol: 3_000_000_000u, fromRow: 0, toRow: 3);
        var chart = new ChartModel();

        var work = RunOnDedicatedThread(() => XlsxDrawingAnchorApplier.ApplyToChart(chart, anchor, sheet));
        var completed = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(10))) == work;

        completed.Should().BeTrue(
            "ApplyToChart must also cap the huge to-column index before its inline span walk, not hang file load");

        var expectedSpan = MaxColumnIndexZeroBased - 5u;
        var expectedWidth = sheet.DefaultColumnWidth * 8 * expectedSpan;
        chart.Width.Should().BeApproximately(expectedWidth, 1e-6);
    }

    [Fact]
    public void GetAnchorSize_OrdinaryValidTwoCellAnchor_StillComputesCorrectNonZeroSpan()
    {
        // Sibling no-regression case: an ordinary, small, correctly-ordered twoCellAnchor (the overwhelming
        // common case) must keep computing its real pixel span unaffected by the new huge-index clamp.
        var sheet = BuildSheet();
        var anchor = BuildTwoCellAnchor(fromCol: 1, toCol: 4, fromRow: 0, toRow: 2);

        var (width, height) = XlsxDrawingAnchorApplier.GetAnchorSize(anchor, sheet);

        var expectedWidth = sheet.DefaultColumnWidth * 8 * 3; // columns 2,3,4 (zero-based 1,2,3) => 3 columns
        var expectedHeight = sheet.DefaultRowHeight * 2; // rows 1,2 (zero-based 0,1) => 2 rows

        width.Should().Be(expectedWidth);
        height.Should().Be(expectedHeight);
    }
}
