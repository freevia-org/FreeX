using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage for R65-render-cell-overflow-6-2: ViewportService applied the width-based
/// '#' overflow indicator to a numeric cell's DisplayText BEFORE GridView's font-shrink pass got a
/// chance to run, so a cell with ShrinkToFit in a too-narrow column showed a SHRUNKEN "######"
/// instead of the real number shrinking to fit -- Excel never shows '#'s when ShrinkToFit is on, it
/// always shrinks the actual value. The same too-narrow column WITHOUT ShrinkToFit must still show
/// the normal '#' overflow indicator (no regression to the pre-existing overflow behavior), and a
/// wide-enough column must show the number normally regardless of ShrinkToFit.
/// </summary>
public class R65_ShrinkToFitOverflowIndicatorTests
{
    private const string NineDigitNumber = "123456789";

    [Fact]
    public void GetViewport_NarrowColumn_WithShrinkToFit_ShowsRealNumber_NotHashes()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ColumnWidths[1] = 2; // narrow enough that the 9-digit number cannot fit

        // Format "0" forces plain integer digits (never Excel's General-format scientific
        // fallback), so the overflow decision is purely a function of column width.
        var style = new CellStyle { ShrinkToFit = true, NumberFormat = "0" };
        var styleId = workbook.RegisterStyle(style);

        var cell = Cell.FromValue(new NumberValue(123456789));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var svc = new ViewportService();
        var vp = svc.GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 500, 500));

        vp.Cells.Single(c => c.Row == 1 && c.Col == 1).DisplayText
            .Should().Be(NineDigitNumber);
    }

    /// <summary>Sibling no-regression: the same narrow column WITHOUT ShrinkToFit still shows '######'.</summary>
    [Fact]
    public void GetViewport_NarrowColumn_WithoutShrinkToFit_StillShowsHashes()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ColumnWidths[1] = 2;

        var style = new CellStyle { ShrinkToFit = false, NumberFormat = "0" };
        var styleId = workbook.RegisterStyle(style);

        var cell = Cell.FromValue(new NumberValue(123456789));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var svc = new ViewportService();
        var vp = svc.GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 500, 500));

        var displayText = vp.Cells.Single(c => c.Row == 1 && c.Col == 1).DisplayText;
        displayText.Should().NotBe(NineDigitNumber);
        displayText.Should().MatchRegex("^#+$");
    }

    /// <summary>A wide-enough column shows the number normally, ShrinkToFit or not.</summary>
    [Fact]
    public void GetViewport_WideColumn_WithShrinkToFit_ShowsNumberNormally()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ColumnWidths[1] = 20;

        var style = new CellStyle { ShrinkToFit = true, NumberFormat = "0" };
        var styleId = workbook.RegisterStyle(style);

        var cell = Cell.FromValue(new NumberValue(123456789));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var svc = new ViewportService();
        var vp = svc.GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 500, 500));

        vp.Cells.Single(c => c.Row == 1 && c.Col == 1).DisplayText
            .Should().Be(NineDigitNumber);
    }
}
