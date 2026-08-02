using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R115-manual-break-title-exclusion: RenderManualPageBreaks (GridView.Overlays.cs, called
/// unconditionally from RenderWorksheetViewOverlay in Normal, Page Layout, and Page Break Preview
/// view modes) used to draw a solid blue manual-page-break line for every row/column present in
/// RowPageBreaks/ColumnPageBreaks that was visible in the viewport, with no check against the
/// sheet's print-title range. But the real pagination engine
/// (PrintLayoutPlanner.BuildAxisPlans -&gt; BuildManualBreakSet) only keeps a manual break when
/// <c>manualBreak &gt; firstBodyValue</c> (the first row/column after the print-title range) -- a
/// break registered at or before that row/column is silently dropped and has zero effect on the
/// real printed/exported page layout. This meant the grid could show a page-break indicator that
/// corresponds to nothing in the actual print/PDF/export output. These tests render real composited
/// pixels through GridView's full OnRender pipeline to prove the indicator is now suppressed for a
/// break the pagination engine ignores, while a break beyond the title range still paints.
/// </summary>
public sealed class R115_ManualPageBreakTitleExclusionTests
{
    private const byte PageBreakBlueRed = 0; // MakePageBreakPen: SolidColorBrush(0, 103, 192)

    private static Color GetPixel(RenderTargetBitmap bitmap, int x, int y)
    {
        var pixels = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        // Pbgra32: B, G, R, A
        return Color.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);
    }

    private static bool IsPageBreakBlue(Color c) =>
        c.A > 10 && c.R < 40 && c.G is > 80 and < 130 && c.B > 160;

    private static RenderTargetBitmap RenderGrid(GridView grid)
    {
        var bitmap = new RenderTargetBitmap(
            (int)grid.Width, (int)grid.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        return bitmap;
    }

    private static GridView CreateGridWithRowBreaks(
        ViewportModel viewport,
        WorksheetRepeatRange? printTitleRows,
        IReadOnlyCollection<uint> rowPageBreaks)
    {
        var grid = new GridView
        {
            Width = 100,
            Height = 140,
            ShowHeaders = false,
            ShowGridLines = false,
            Viewport = viewport,
            WorksheetViewMode = WorksheetViewMode.Normal,
            PrintTitleRows = printTitleRows,
            RowPageBreaks = rowPageBreaks,
        };

        grid.Measure(new Size(grid.Width, grid.Height));
        grid.Arrange(new Rect(0, 0, grid.Width, grid.Height));
        grid.UpdateLayout();
        return grid;
    }

    private static GridView CreateGridWithColumnBreaks(
        ViewportModel viewport,
        WorksheetRepeatRange? printTitleColumns,
        IReadOnlyCollection<uint> columnPageBreaks)
    {
        var grid = new GridView
        {
            Width = 140,
            Height = 100,
            ShowHeaders = false,
            ShowGridLines = false,
            Viewport = viewport,
            WorksheetViewMode = WorksheetViewMode.Normal,
            PrintTitleColumns = printTitleColumns,
            ColumnPageBreaks = columnPageBreaks,
        };

        grid.Measure(new Size(grid.Width, grid.Height));
        grid.Arrange(new Rect(0, 0, grid.Width, grid.Height));
        grid.UpdateLayout();
        return grid;
    }

    // Primary case: mirrors the existing PrintLayoutPlannerTests fixture (repeat range (1,1), manual
    // breaks [2, 6]) -- pagination drops the break at row 2 (the first body row right after the
    // 1-row title band) because 2 is not strictly greater than firstBodyValue (2). Before the fix,
    // RenderManualPageBreaks drew a line at row 2's top edge regardless; after the fix it must not.
    [Fact]
    public void ManualRowBreak_AtFirstBodyRowAfterTitleRange_IsNotDrawn()
    {
        WpfTestThread.Run(() =>
        {
            var viewport = new ViewportModel(
                [],
                [
                    new RowMetric(1, 20, 0),
                    new RowMetric(2, 20, 20),
                    new RowMetric(3, 20, 40),
                    new RowMetric(4, 20, 60),
                    new RowMetric(5, 20, 80),
                    new RowMetric(6, 20, 100),
                ],
                [new ColMetric(1, 100, 0)]);

            var grid = CreateGridWithRowBreaks(
                viewport,
                printTitleRows: new WorksheetRepeatRange(1, 1),
                rowPageBreaks: [2, 6]);
            var bitmap = RenderGrid(grid);

            // Row 2's top edge sits at y=20 (the line spans the full width there).
            var row2Line = GetPixel(bitmap, 50, 20);
            IsPageBreakBlue(row2Line).Should().BeFalse(
                "a manual break at row 2 -- the first body row right after the 1-row print-title " +
                "range -- is silently dropped by PrintLayoutPlanner.BuildManualBreakSet and has no " +
                "effect on the real printed/exported page layout, so the on-screen indicator must " +
                "not show it either");
        });
    }

    // No-regression sibling: the same fixture's OTHER manual break (row 6, well past the title
    // range) is kept by BuildManualBreakSet and must keep drawing exactly as before.
    [Fact]
    public void ManualRowBreak_PastTitleRange_StillDrawn_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var viewport = new ViewportModel(
                [],
                [
                    new RowMetric(1, 20, 0),
                    new RowMetric(2, 20, 20),
                    new RowMetric(3, 20, 40),
                    new RowMetric(4, 20, 60),
                    new RowMetric(5, 20, 80),
                    new RowMetric(6, 20, 100),
                ],
                [new ColMetric(1, 100, 0)]);

            var grid = CreateGridWithRowBreaks(
                viewport,
                printTitleRows: new WorksheetRepeatRange(1, 1),
                rowPageBreaks: [2, 6]);
            var bitmap = RenderGrid(grid);

            // Row 6's top edge sits at y=100.
            var row6Line = GetPixel(bitmap, 50, 100);
            IsPageBreakBlue(row6Line).Should().BeTrue(
                "row 6 is a normal body break well past the title range -- BuildManualBreakSet keeps " +
                "it, so the indicator must keep drawing it exactly as before this fix");
        });
    }

    // No print-title range configured at all: every break the pagination engine would keep (i.e.
    // every break > 0, since firstBodyValue collapses to the print range's own start) must still be
    // drawn -- this is the overwhelmingly common case (no repeated rows/columns) and must not
    // regress just because the exclusion logic was added.
    [Fact]
    public void ManualRowBreak_NoTitleRangeConfigured_StillDrawn_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var viewport = new ViewportModel(
                [],
                [
                    new RowMetric(1, 20, 0),
                    new RowMetric(2, 20, 20),
                    new RowMetric(3, 20, 40),
                ],
                [new ColMetric(1, 100, 0)]);

            var grid = CreateGridWithRowBreaks(
                viewport,
                printTitleRows: null,
                rowPageBreaks: [2]);
            var bitmap = RenderGrid(grid);

            var row2Line = GetPixel(bitmap, 50, 20);
            IsPageBreakBlue(row2Line).Should().BeTrue(
                "with no print-title range configured, a manual break at row 2 is a perfectly " +
                "ordinary body break and must still be drawn");
        });
    }

    // Column-axis sibling: the identical exclusion must apply to ColumnPageBreaks against
    // PrintTitleColumns -- BuildColumnPlans routes through the exact same BuildManualBreakSet.
    [Fact]
    public void ManualColumnBreak_AtFirstBodyColumnAfterTitleRange_IsNotDrawn()
    {
        WpfTestThread.Run(() =>
        {
            var viewport = new ViewportModel(
                [],
                [new RowMetric(1, 100, 0)],
                [
                    new ColMetric(1, 20, 0),
                    new ColMetric(2, 20, 20),
                    new ColMetric(3, 20, 40),
                    new ColMetric(4, 20, 60),
                    new ColMetric(5, 20, 80),
                    new ColMetric(6, 20, 100),
                ]);

            var grid = CreateGridWithColumnBreaks(
                viewport,
                printTitleColumns: new WorksheetRepeatRange(1, 1),
                columnPageBreaks: [2, 6]);
            var bitmap = RenderGrid(grid);

            var col2Line = GetPixel(bitmap, 20, 50);
            IsPageBreakBlue(col2Line).Should().BeFalse(
                "a manual break at column 2 -- the first body column right after the 1-column " +
                "print-title range -- is silently dropped by pagination and must not be drawn");

            var col6Line = GetPixel(bitmap, 100, 50);
            IsPageBreakBlue(col6Line).Should().BeTrue(
                "column 6 is a normal body break well past the title range and must keep drawing");
        });
    }
}
