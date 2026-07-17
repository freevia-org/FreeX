using System.Linq;
using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Calc.Tests;

// R48-render-borders-precedence-3-3: Excel does not erase a border set on a hidden row/column --
// hiding zeroes its height/width, so the border visually fuses onto the boundary between whichever
// visible neighbors it now sits directly between (the "collapsed seam"). ViewportService excludes
// a hidden row/col's own cells from the viewport entirely (by design -- their value/style must not
// leak in as an ordinary visible cell), but a border they carried must still migrate onto the
// adjacent visible neighbors' own facing edge instead of silently vanishing.
public class R48_ViewportHiddenRowBorderMigrationTests
{
    [Fact]
    public void GetViewport_HiddenMiddleRowBorderBottom_MigratesToVisibleSeam()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.HiddenRows.Add(2);

        var borderedStyleId = workbook.RegisterStyle(
            new CellStyle { BorderBottom = new CellBorder(BorderStyle.Thick, CellColor.Black) });
        var hiddenCell = Cell.FromValue(new NumberValue(999));
        hiddenCell.StyleId = borderedStyleId;
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), hiddenCell); // hidden row 2, col B

        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(1))); // visible row 1
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromValue(new NumberValue(3))); // visible row 3

        var svc = new ViewportService();
        var vp = svc.GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 500, 500));

        // The hidden row's own cell must never leak into the viewport.
        vp.Cells.Should().NotContain(c => c.Row == 2);

        var row1 = vp.Cells.Single(c => c.Row == 1 && c.Col == 2);
        var row3 = vp.Cells.Single(c => c.Row == 3 && c.Col == 2);

        // The hidden row's BorderBottom must migrate onto the collapsed seam: row 1's own bottom
        // edge and row 3's own top edge (whichever side the renderer ends up reading from).
        row1.Style!.BorderBottom.Style.Should().Be(BorderStyle.Thick);
        row3.Style!.BorderTop.Style.Should().Be(BorderStyle.Thick);
    }

    [Fact]
    public void GetViewport_HiddenMiddleRowWithNoBorder_DoesNotInjectSpuriousBorder()
    {
        // No-regression sibling: a hidden row that carries no border at all must not cause its
        // visible neighbors to sprout a border out of nowhere.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.HiddenRows.Add(2);

        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new NumberValue(999))); // hidden, no style
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromValue(new NumberValue(3)));

        var svc = new ViewportService();
        var vp = svc.GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 500, 500));

        var row1 = vp.Cells.Single(c => c.Row == 1 && c.Col == 2);
        var row3 = vp.Cells.Single(c => c.Row == 3 && c.Col == 2);

        row1.Style!.BorderBottom.Style.Should().Be(BorderStyle.None);
        row3.Style!.BorderTop.Style.Should().Be(BorderStyle.None);
    }
}
