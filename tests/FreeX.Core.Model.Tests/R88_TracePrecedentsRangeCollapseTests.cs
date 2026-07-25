using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R88-app-formula-auditing-5-3: Trace Precedents/Dependents used to draw one arrow per cell for
/// a RANGE precedent (e.g. =SUM(A1:A20) produced 20 individual arrows into B10) instead of
/// Excel's single arrow anchored at the range's box. GetDirectPrecedentRegions/the trace-arrow
/// builders now collapse a contiguous range precedent into one arrow anchored at the range's
/// top-left cell.
/// </summary>
public sealed class R88_TracePrecedentsRangeCollapseTests
{
    [Fact]
    public void GetPrecedentTraceArrows_CollapsesRangePrecedentIntoSingleArrow()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        for (uint row = 1; row <= 20; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        var formulaAddress = new CellAddress(sheet.Id, 10, 2);
        sheet.SetCell(formulaAddress, Cell.FromFormula("SUM(A1:A20)"));

        var arrows = FormulaAuditingService.GetPrecedentTraceArrows(wb, formulaAddress);

        arrows.Should().Equal(
            new FormulaTraceArrow(new CellAddress(sheet.Id, 1, 1), formulaAddress, FormulaTraceArrowKind.Precedent));
    }

    [Fact]
    public void FormulaTraceArrowPlanner_CollapsesRangePrecedentIntoSingleArrow()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        for (uint row = 1; row <= 20; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        var formulaAddress = new CellAddress(sheet.Id, 10, 2);
        sheet.SetCell(formulaAddress, Cell.FromFormula("SUM(A1:A20)"));

        var arrows = FormulaTraceArrowPlanner.GetNextPrecedentTraceArrows(wb, formulaAddress, []);

        arrows.Should().Equal(
            new FormulaTraceArrow(new CellAddress(sheet.Id, 1, 1), formulaAddress, FormulaTraceArrowKind.Precedent));
    }

    // No-regression sibling: GetDirectPrecedents (used by Ctrl+[/Go To Special/etc.) must keep
    // returning the fully flattened per-cell list for a range precedent -- only the trace-ARROW
    // builders collapse to a region; this contract must not change.
    [Fact]
    public void GetDirectPrecedents_StillReturnsFlattenedCellsForRangePrecedent()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));

        var formulaAddress = new CellAddress(sheet.Id, 10, 2);
        sheet.SetCell(formulaAddress, Cell.FromFormula("SUM(A1:A3)"));

        var precedents = FormulaAuditingService.GetDirectPrecedents(wb, formulaAddress);

        precedents.Should().Equal(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 1));
    }

    // No-regression sibling: a simple single-cell precedent chain (no ranges involved) must
    // produce the exact same arrows as before this change.
    [Fact]
    public void GetPrecedentTraceArrows_StillReturnsMultiLevelChainForSingleCellPrecedents()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetCell(b1, Cell.FromFormula("A1+1"));
        sheet.SetCell(c1, Cell.FromFormula("B1*2"));

        var arrows = FormulaAuditingService.GetPrecedentTraceArrows(wb, c1);

        arrows.Should().Equal(
            new FormulaTraceArrow(b1, c1, FormulaTraceArrowKind.Precedent),
            new FormulaTraceArrow(a1, b1, FormulaTraceArrowKind.Precedent));
    }
}
