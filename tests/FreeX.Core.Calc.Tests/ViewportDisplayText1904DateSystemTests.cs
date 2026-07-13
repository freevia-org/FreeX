using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage for G12: ordinary grid cell display (not just TEXT()) must render date
/// serials against the workbook's actual date system. Before the fix, ViewportService.GetDisplayText
/// always called the 1900-epoch-only NumberFormatter overload, so any date-formatted cell in a
/// workbook with Uses1904DateSystem=true displayed 1462 days (4 years) earlier than the real date.
/// </summary>
public class ViewportDisplayText1904DateSystemTests
{
    [Fact]
    public void GetViewport_DateFormattedCell_RendersAgainst1904Epoch_WhenWorkbookUses1904DateSystem()
    {
        var workbook = new Workbook("test") { Uses1904DateSystem = true };
        var sheet = workbook.AddSheet("Sheet1");
        var style = new CellStyle { NumberFormat = "yyyy-mm-dd" };
        var styleId = workbook.RegisterStyle(style);

        // Wide enough that "1904-01-01" (10 chars) fits -- this test is about 1904/1900 epoch
        // selection, not the round-41 width-overflow-to-'#' behavior (the sheet's default column
        // width is too narrow to hold a 10-character date and would otherwise show "#" fill).
        sheet.ColumnWidths[1] = 20;

        // Serial 0 under the 1904 date system is 1904-01-01.
        var cell = Cell.FromValue(new DateTimeValue(0));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var svc = new ViewportService();
        var vp = svc.GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 500, 500));

        vp.Cells.Single(c => c.Row == 1 && c.Col == 1).DisplayText
            .Should().Be("1904-01-01");
    }

    [Fact]
    public void GetViewport_DateFormattedCell_RendersAgainst1900Epoch_WhenWorkbookDoesNotUse1904DateSystem()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var style = new CellStyle { NumberFormat = "yyyy-mm-dd" };
        var styleId = workbook.RegisterStyle(style);

        // Wide enough that "1900-01-01" (10 chars) fits -- this test is about 1904/1900 epoch
        // selection, not the round-41 width-overflow-to-'#' behavior (the sheet's default column
        // width is too narrow to hold a 10-character date and would otherwise show "#" fill).
        sheet.ColumnWidths[1] = 20;

        // Serial 1 under the default 1900 date system is 1900-01-01.
        var cell = Cell.FromValue(new DateTimeValue(1));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var svc = new ViewportService();
        var vp = svc.GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 500, 500));

        vp.Cells.Single(c => c.Row == 1 && c.Col == 1).DisplayText
            .Should().Be("1900-01-01");
    }
}
