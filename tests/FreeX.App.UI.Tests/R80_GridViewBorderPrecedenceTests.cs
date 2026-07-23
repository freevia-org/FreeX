using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R80-render-gridlines-borders-5-2 / -5-3: GridView.Rendering.cs's shared-edge border-precedence
/// resolution (borderStyleLookup + ResolveBorderEdgeWinner) previously only ran for the main
/// (BottomRight) pane's own cells:
///  - (finding 1) every OTHER split-pane quadrant (RenderSplitPaneCell, driven by
///    RenderSplitPaneCells) painted each cell's 4 border edges unconditionally, so whichever
///    DisplayCell SplitPaneCellLayoutPlanner.VisitLayouts happened to visit last silently won a
///    conflicting shared edge instead of the heavier style;
///  - (finding 2) the lookup was built solely from the currently-scrolled viewport.Cells, so a
///    border authored on a cell that scrolled just off one edge of the rendered window vanished
///    entirely even though its seam was still physically on-screen.
/// </summary>
public sealed class R80_GridViewBorderPrecedenceTests
{
    private static readonly CellColor Red = new(255, 0, 0);
    private static readonly CellColor Blue = new(0, 0, 255);

    private static RowMetric[] BuildTopRows()
    {
        var rows = new RowMetric[9];
        for (var i = 0; i < 9; i++)
            rows[i] = new RowMetric((uint)(i + 1), 20, i * 20);
        return rows;
    }

    private static ColMetric[] BuildLeftColumns() =>
    [
        new ColMetric(1, 64, 0),
        new ColMetric(2, 64, 64),
    ];

    private static RenderTargetBitmap RenderGridToBitmap(GridView grid)
    {
        var bitmap = new RenderTargetBitmap((int)grid.Width, (int)grid.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        return bitmap;
    }

    private static (byte Blue, byte Green, byte Red, byte Alpha) SamplePixel(BitmapSource bitmap, int x, int y)
    {
        var pixels = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        return (pixels[0], pixels[1], pixels[2], pixels[3]);
    }

    private static bool IsRedDominant((byte Blue, byte Green, byte Red, byte Alpha) p) =>
        p.Alpha > 10 && p.Red > 100 && p.Red - p.Blue > 40 && p.Red - p.Green > 40;

    private static bool IsBlueDominant((byte Blue, byte Green, byte Red, byte Alpha) p) =>
        p.Alpha > 10 && p.Blue > 100 && p.Blue - p.Red > 40 && p.Blue - p.Green > 40;

    /// <summary>
    /// Finding 1: two adjacent split-pane (TopLeft quadrant) cells author conflicting styles on
    /// their shared vertical edge -- col 1's BorderRight is authored FIRST in the DisplayCell list
    /// as heavier (Thick, Red), col 2's BorderLeft SECOND as lighter (Thin, Blue). Unconditional
    /// per-cell painting draws col 1's edge then immediately overwrites it with col 2's, so the
    /// seam renders as the weaker Thin/Blue purely because of visit order. Real Excel (and the
    /// fixed renderer) always resolves the shared edge to the heavier style regardless of which
    /// side authored it or which side is painted last.
    /// </summary>
    [Fact]
    public void SplitPaneQuadrant_ConflictingAdjacentBorders_ResolvesToHeavierStyle_NotLastDrawn()
    {
        WpfTestThread.Run(() =>
        {
            var grid = new GridView
            {
                Width = 320,
                Height = 320,
                ShowHeaders = true,
                ShowGridLines = false,
                Viewport = new ViewportModel(
                    [],
                    BuildMainRowsForSplit(),
                    BuildMainColumnsForSplit(),
                    SplitPanes: new SplitPaneState(
                        10,
                        3,
                        BuildTopRows(),
                        BuildLeftColumns(),
                        [
                            // Visited FIRST by SplitPaneCellLayoutPlanner.VisitLayouts (array order):
                            // authors the HEAVIER style on the shared edge.
                            new DisplayCell(5, 1, null, "", null, default, null,
                                new CellStyle { BorderRight = new CellBorder(BorderStyle.Thick, Red) }),
                            // Visited SECOND: authors the LIGHTER style on the same shared edge.
                            new DisplayCell(5, 2, null, "", null, default, null,
                                new CellStyle { BorderLeft = new CellBorder(BorderStyle.Thin, Blue) }),
                        ])),
            };

            grid.Measure(new Size(320, 320));
            grid.Arrange(new Rect(0, 0, 320, 320));
            grid.UpdateLayout();
            var bitmap = RenderGridToBitmap(grid);

            // Shared edge sits at x = RowHeaderWidth + 64 (end of pinned col 1 / start of col 2),
            // y within row 5's band (TopOffset 80, height 20) -> sample mid-row.
            var x = (int)GridView.RowHeaderWidth + 64;
            var y = (int)GridView.ColHeaderHeight + 90;

            var pixel = SamplePixel(bitmap, x, y);
            IsRedDominant(pixel).Should().BeTrue(
                "the shared edge must resolve to the heavier Thick/Red border authored by col 1, matching Excel's deterministic heaviest-wins rule, regardless of draw order");
            IsBlueDominant(pixel).Should().BeFalse(
                "the lighter Thin/Blue border authored by col 2 must never silently win just because it was visited last");
        });
    }

    /// <summary>No-regression sibling: a split-pane cell with a border and NO conflicting neighbor
    /// must still render its own border normally (unaffected by the new precedence resolution).</summary>
    [Fact]
    public void SplitPaneQuadrant_SingleBorderedCellWithNoNeighborConflict_StillRenders_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var grid = new GridView
            {
                Width = 320,
                Height = 320,
                ShowHeaders = true,
                ShowGridLines = false,
                Viewport = new ViewportModel(
                    [],
                    BuildMainRowsForSplit(),
                    BuildMainColumnsForSplit(),
                    SplitPanes: new SplitPaneState(
                        10,
                        3,
                        BuildTopRows(),
                        BuildLeftColumns(),
                        [
                            new DisplayCell(5, 1, null, "", null, default, null,
                                new CellStyle { BorderRight = new CellBorder(BorderStyle.Thick, Red) }),
                        ])),
            };

            grid.Measure(new Size(320, 320));
            grid.Arrange(new Rect(0, 0, 320, 320));
            grid.UpdateLayout();
            var bitmap = RenderGridToBitmap(grid);

            var x = (int)GridView.RowHeaderWidth + 64;
            var y = (int)GridView.ColHeaderHeight + 90;

            IsRedDominant(SamplePixel(bitmap, x, y)).Should().BeTrue(
                "a lone border with no conflicting neighbor must still render exactly as authored");
        });
    }

    private static RowMetric[] BuildMainRowsForSplit()
    {
        var rows = new RowMetric[20];
        for (var i = 0; i < 20; i++)
            rows[i] = new RowMetric((uint)(10 + i), 20, i * 20);
        return rows;
    }

    private static ColMetric[] BuildMainColumnsForSplit()
    {
        var cols = new ColMetric[6];
        for (var i = 0; i < 6; i++)
            cols[i] = new ColMetric((uint)(3 + i), 64, i * 64);
        return cols;
    }

    /// <summary>
    /// Finding 2: a cell's border has scrolled just off the top edge of the (non-split) rendered
    /// viewport window. ViewportService contributes it via ViewportModel.BorderFringe (constructed
    /// directly here to isolate the render-side consumption, matching how ViewportService's own
    /// R80_ViewportBorderFringeTests cover the production side); the renderer must still paint the
    /// still-on-screen top edge of the new topmost visible row.
    /// </summary>
    [Fact]
    public void BorderFringe_TopEdgeOfScrolledViewport_StillRenders()
    {
        WpfTestThread.Run(() =>
        {
            var grid = new GridView
            {
                Width = 200,
                Height = 100,
                ShowHeaders = true,
                ShowGridLines = false,
                Viewport = new ViewportModel(
                    [], // row 21 itself carries no border of its own.
                    [new RowMetric(21, 20, 0)],
                    [new ColMetric(2, 64, 0)],
                    BorderFringe: new Dictionary<(uint Row, uint Col), BorderFringeEdges>
                    {
                        [(21u, 2u)] = new BorderFringeEdges(Top: new CellBorder(BorderStyle.Thick, Red)),
                    }),
            };

            grid.Measure(new Size(200, 100));
            grid.Arrange(new Rect(0, 0, 200, 100));
            grid.UpdateLayout();
            var bitmap = RenderGridToBitmap(grid);

            var x = (int)GridView.RowHeaderWidth + 30;
            // 1px below the exact top edge coordinate: a centered pen straddles the line
            // (half above, half below), and the half above y=0 would be clipped by the viewport's
            // own render-clip geometry, so sample just inside the visible half of the stroke.
            var y = (int)GridView.ColHeaderHeight + 1;

            IsRedDominant(SamplePixel(bitmap, x, y)).Should().BeTrue(
                "row 20's BorderBottom scrolled off-screen but its seam is still physically the top edge of the new topmost visible row, and must render identically regardless of scroll position");
        });
    }

    /// <summary>No-regression sibling: without a BorderFringe entry, nothing spurious is painted at
    /// the viewport's boundary edge.</summary>
    [Fact]
    public void BorderFringe_Absent_NoSpuriousTopEdgeLine_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var grid = new GridView
            {
                Width = 200,
                Height = 100,
                ShowHeaders = true,
                ShowGridLines = false,
                Viewport = new ViewportModel(
                    [],
                    [new RowMetric(21, 20, 0)],
                    [new ColMetric(2, 64, 0)]),
            };

            grid.Measure(new Size(200, 100));
            grid.Arrange(new Rect(0, 0, 200, 100));
            grid.UpdateLayout();
            var bitmap = RenderGridToBitmap(grid);

            var x = (int)GridView.RowHeaderWidth + 30;
            var y = (int)GridView.ColHeaderHeight + 1;

            IsRedDominant(SamplePixel(bitmap, x, y)).Should().BeFalse(
                "with no BorderFringe contributed, no border line should be painted at the viewport's top boundary");
        });
    }
}
