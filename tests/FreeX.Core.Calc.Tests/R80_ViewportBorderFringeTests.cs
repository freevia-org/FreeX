using System.Linq;
using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Calc.Tests;

// R80-render-gridlines-borders-5-3: GridView.Rendering.cs's shared-edge border-precedence lookup
// (borderStyleLookup) is built solely from the currently-scrolled viewport.Cells, so a border
// authored on a cell that scrolls just off the top/bottom/left/right of the rendered window used
// to vanish entirely -- the same document renders two different things (line present vs. absent)
// purely as a function of scroll position, which never happens in real Excel. ViewportService.
// GetViewport now contributes those off-screen authors into ViewportModel.BorderFringe (keyed by
// the still-visible boundary cell whose physical edge they share) so the renderer can resolve the
// seam correctly regardless of scroll position.
public class R80_ViewportBorderFringeTests
{
    [Fact]
    public void GetViewport_BorderedRowScrolledJustAboveTopEdge_ContributesTopBorderFringe()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        var borderedStyleId = workbook.RegisterStyle(
            new CellStyle { BorderBottom = new CellBorder(BorderStyle.Thick, CellColor.Black) });
        var borderedCell = Cell.FromValue(new NumberValue(20));
        borderedCell.StyleId = borderedStyleId;
        sheet.SetCell(new CellAddress(sheet.Id, 20, 2), borderedCell);

        sheet.SetCell(new CellAddress(sheet.Id, 21, 2), Cell.FromValue(new NumberValue(21))); // no border of its own

        var svc = new ViewportService();
        // Scroll so row 20 (the border's author) has just scrolled off the top; row 21 is now the
        // topmost visible row.
        var vp = svc.GetViewport(workbook, sheet.Id, new ViewportRequest(21, 1, 200, 500));

        // Row 20 must never leak into the rendered viewport's own cell list -- it is genuinely
        // off-screen and must not be drawn as a cell of its own.
        vp.Cells.Should().NotContain(c => c.Row == 20);

        vp.BorderFringe.Should().NotBeNull();
        vp.BorderFringe!.Should().ContainKey((21u, 2u));
        var edges = vp.BorderFringe[(21u, 2u)];
        edges.Top.Should().NotBeNull();
        edges.Top!.Value.Style.Should().Be(BorderStyle.Thick,
            "row 20's BorderBottom is still physically on-screen (the top edge of the new topmost visible row) and must not silently vanish once row 20 scrolls out of view");
    }

    [Fact]
    public void GetViewport_UnscrolledViewportWithNoOffscreenBorder_NoRegression()
    {
        // No-regression sibling: when the bordered row is still IN the viewport (no scroll), or
        // simply has no neighbor row above it at all, BorderFringe must stay empty -- the
        // existing in-viewport neighbor resolution already handles this case correctly and must
        // not be second-guessed or duplicated by the fringe mechanism.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        var borderedStyleId = workbook.RegisterStyle(
            new CellStyle { BorderBottom = new CellBorder(BorderStyle.Thick, CellColor.Black) });
        var borderedCell = Cell.FromValue(new NumberValue(20));
        borderedCell.StyleId = borderedStyleId;
        sheet.SetCell(new CellAddress(sheet.Id, 20, 2), borderedCell);
        sheet.SetCell(new CellAddress(sheet.Id, 21, 2), Cell.FromValue(new NumberValue(21)));

        var svc = new ViewportService();
        // No scroll: row 20 is fully in view alongside row 21, so the ordinary in-viewport
        // borderStyleLookup already resolves this seam -- no fringe entry should be produced.
        var vp = svc.GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 500, 500));

        vp.Cells.Should().Contain(c => c.Row == 20);
        if (vp.BorderFringe is not null)
            vp.BorderFringe.Should().NotContainKey((21u, 2u));
    }
}
