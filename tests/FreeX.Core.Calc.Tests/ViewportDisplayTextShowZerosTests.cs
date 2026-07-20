using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage for R51-io-worksheet-sheetview-props-3-1: Sheet.ShowZeros (the sheetView
/// showZeros="0" attribute -- Excel: File &gt; Options &gt; Advanced &gt; "Show a zero in cells that
/// have zero value") was faithfully modeled and round-tripped through XLSX I/O but never consumed
/// by ViewportService.GetDisplayText, so a numeric zero always rendered as "0" in the interactive
/// grid regardless of the sheet setting. Excel blanks a literal-zero cell when this option is off,
/// unless the cell's own number format defines an explicit third (zero) section, in which case that
/// section's own text governs instead of the sheet-level preference.
/// </summary>
public class ViewportDisplayTextShowZerosTests
{
    [Fact]
    public void GetViewport_ZeroValuedCell_RendersBlank_WhenSheetShowZerosIsFalse()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ShowZeros = false;

        var cell = Cell.FromValue(new NumberValue(0));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var svc = new ViewportService();
        var vp = svc.GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 500, 500));

        vp.Cells.Single(c => c.Row == 1 && c.Col == 1).DisplayText
            .Should().Be(string.Empty);
    }

    /// <summary>Sibling no-regression: the default (ShowZeros = true) still renders "0".</summary>
    [Fact]
    public void GetViewport_ZeroValuedCell_RendersZero_WhenSheetShowZerosIsTrue()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ShowZeros.Should().BeTrue(); // default

        var cell = Cell.FromValue(new NumberValue(0));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var svc = new ViewportService();
        var vp = svc.GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 500, 500));

        vp.Cells.Single(c => c.Row == 1 && c.Col == 1).DisplayText
            .Should().Be("0");
    }

    /// <summary>
    /// Non-zero values must never be blanked by ShowZeros=false -- only literal zero is affected.
    /// </summary>
    [Fact]
    public void GetViewport_NonZeroValuedCell_StillRenders_WhenSheetShowZerosIsFalse()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ShowZeros = false;

        var cell = Cell.FromValue(new NumberValue(42));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var svc = new ViewportService();
        var vp = svc.GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 500, 500));

        vp.Cells.Single(c => c.Row == 1 && c.Col == 1).DisplayText
            .Should().Be("42");
    }

    /// <summary>
    /// When the cell's own number format defines an explicit third (zero) section, that section's
    /// rendering governs regardless of the sheet-level ShowZeros preference (Excel parity).
    /// </summary>
    [Fact]
    public void GetViewport_ZeroValuedCell_UsesFormatZeroSection_WhenFormatDefinesOne_EvenIfShowZerosIsFalse()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ShowZeros = false;

        var style = new CellStyle { NumberFormat = "0;-0;\"zero\"" };
        var styleId = workbook.RegisterStyle(style);

        var cell = Cell.FromValue(new NumberValue(0));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var svc = new ViewportService();
        var vp = svc.GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 500, 500));

        vp.Cells.Single(c => c.Row == 1 && c.Col == 1).DisplayText
            .Should().Be("zero");
    }
}
