using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R17-freeze-largegrid-1: GetUsedRange() must include style-only (formatting-only, empty) cells
/// so that Ctrl+End / scroll / print extent reach a formatted-but-empty far cell, matching Excel's
/// used-range semantics.
/// </summary>
public partial class SheetTests
{
    [Fact]
    public void GetUsedRange_IncludesFarStyleOnlyCell_BeyondValueCells()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(2));

        var fill = wb.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 199, 206) });
        // Z100: row 100, col 26 (A=1 ... Z=26).
        sheet.SetStyleOnly(100, 26, fill);

        sheet.GetUsedRange().Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 100, 26)));
    }

    [Fact]
    public void GetUsedRange_SheetWithOnlyStyleOnlyCell_IsNotReportedEmpty()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var fill = wb.RegisterStyle(new CellStyle { FillColor = new CellColor(198, 239, 206) });

        sheet.SetStyleOnly(5, 3, fill);

        sheet.GetUsedRange().Should().Be(new GridRange(
            new CellAddress(sheet.Id, 5, 3),
            new CellAddress(sheet.Id, 5, 3)));
    }

    [Fact]
    public void GetUsedRange_WithNoValuesOrStyles_IsStillEmpty()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        sheet.GetUsedRange().Should().BeNull();
    }

    [Fact]
    public void GetUsedRange_IncludesStyleOnlyRunBounds()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        var fill = wb.RegisterStyle(new CellStyle { FillColor = new CellColor(189, 215, 238) });
        sheet.SetStyleOnlyRuns([new StyleOnlyRun(50, 10, 20, fill)]);

        sheet.GetUsedRange().Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 50, 20)));
    }
}
