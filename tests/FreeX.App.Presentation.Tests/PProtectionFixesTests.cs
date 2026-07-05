using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// Regression tests for the P-protection review-5 fix K35: the "Hidden" protection option (Format
/// Cells &gt; Protection &gt; Hidden) must suppress formula-bar text once the sheet is protected,
/// matching Excel's documented behavior. Covers <see cref="SpreadsheetDisplayFormatter.FormatFormulaBarText"/>.
/// </summary>
public sealed class PProtectionFixesTests
{
    private static (Workbook Workbook, Sheet Sheet, CellAddress Address) CreateWorkbookWithFormulaCell(
        string formulaText, ScalarValue computedValue, bool hidden, bool locked = true)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var styleId = workbook.RegisterStyle(new CellStyle { Hidden = hidden, Locked = locked });
        // Simulate what the calc engine would have already stored: the formula text plus its
        // computed value (FormatFormulaBarText renders Value when the formula is suppressed).
        var cell = Cell.FromFormula(formulaText);
        cell.Value = computedValue;
        cell.StyleId = styleId;
        sheet.SetCell(address, cell);
        return (workbook, sheet, address);
    }

    [Fact]
    public void FormatFormulaBarText_SuppressesFormulaWhenCellHiddenAndSheetProtected()
    {
        var (workbook, sheet, addr) = CreateWorkbookWithFormulaCell("1+1", new NumberValue(2), hidden: true);
        sheet.IsProtected = true;
        var cell = sheet.GetCell(addr);

        var text = SpreadsheetDisplayFormatter.FormatFormulaBarText(cell, addr, false, sheet, workbook);

        text.Should().NotStartWith("=");
        text.Should().Be("2");
    }

    [Fact]
    public void FormatFormulaBarText_ShowsFormulaWhenCellHiddenButSheetNotProtected()
    {
        var (workbook, sheet, addr) = CreateWorkbookWithFormulaCell("1+1", new NumberValue(2), hidden: true);
        // Sheet is NOT protected: Excel's Hidden option only takes effect while the sheet is protected.
        var cell = sheet.GetCell(addr);

        var text = SpreadsheetDisplayFormatter.FormatFormulaBarText(cell, addr, false, sheet, workbook);

        text.Should().Be("=1+1");
    }

    [Fact]
    public void FormatFormulaBarText_ShowsFormulaWhenSheetProtectedButCellNotHidden()
    {
        var (workbook, sheet, addr) = CreateWorkbookWithFormulaCell("1+1", new NumberValue(2), hidden: false);
        sheet.IsProtected = true;
        var cell = sheet.GetCell(addr);

        var text = SpreadsheetDisplayFormatter.FormatFormulaBarText(cell, addr, false, sheet, workbook);

        text.Should().Be("=1+1");
    }

    [Fact]
    public void FormatFormulaBarText_SuppressesFormulaForFormulaTextUsingR1C1Style()
    {
        // The suppression check must run before the reference-style translation, regardless of
        // which reference style is active.
        var (workbook, sheet, addr) = CreateWorkbookWithFormulaCell("A1+1", new NumberValue(2), hidden: true);
        sheet.IsProtected = true;
        var cell = sheet.GetCell(addr);

        var text = SpreadsheetDisplayFormatter.FormatFormulaBarText(cell, addr, useR1C1ReferenceStyle: true, sheet, workbook);

        text.Should().Be("2");
    }

    [Fact]
    public void FormatFormulaBarText_ThreeArgOverloadStillShowsFormulaWhenNoSheetContextAvailable()
    {
        // Backward-compatible overload (no sheet/workbook context): cannot enforce Hidden, but must
        // not throw and must keep its prior behavior for callers that have not been wired up yet.
        var (_, sheet, addr) = CreateWorkbookWithFormulaCell("1+1", new NumberValue(2), hidden: true);
        sheet.IsProtected = true;
        var cell = sheet.GetCell(addr);

        var text = SpreadsheetDisplayFormatter.FormatFormulaBarText(cell, addr, false);

        text.Should().Be("=1+1");
    }
}
