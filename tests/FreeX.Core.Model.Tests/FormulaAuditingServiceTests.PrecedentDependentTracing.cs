using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class FormulaAuditingServiceTests
{
    [Fact]
    public void GetDirectPrecedents_ReturnsCellsFromRefsRangesCrossSheetRefsAndNamedRanges()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var formulaAddress = new CellAddress(sheet1.Id, 5, 1);
        var namedStart = new CellAddress(sheet1.Id, 10, 1);
        var namedEnd = new CellAddress(sheet1.Id, 11, 1);
        wb.DefineNamedRange("Rates", new GridRange(namedStart, namedEnd));

        sheet1.SetCell(formulaAddress, Cell.FromFormula("SUM(A1:B2,Sheet2!C3,Rates)"));

        var precedents = FormulaAuditingService.GetDirectPrecedents(wb, formulaAddress);

        precedents.Should().Equal(
            new CellAddress(sheet1.Id, 1, 1),
            new CellAddress(sheet1.Id, 1, 2),
            new CellAddress(sheet1.Id, 2, 1),
            new CellAddress(sheet1.Id, 2, 2),
            namedStart,
            namedEnd,
            new CellAddress(sheet2.Id, 3, 3));
    }

    [Fact]
    public void GetPrecedentTraceArrows_ReturnsMultiLevelFormulaChain()
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
            new FormulaTraceArrow(b1, c1),
            new FormulaTraceArrow(a1, b1));
    }

    [Fact]
    public void GetDirectDependents_ReturnsFormulaCellsThatReferenceAddress()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var target = new CellAddress(sheet1.Id, 2, 1);
        var localDependent = new CellAddress(sheet1.Id, 1, 2);
        var rangeDependent = new CellAddress(sheet1.Id, 4, 1);
        var crossSheetDependent = new CellAddress(sheet2.Id, 1, 1);

        sheet1.SetCell(localDependent, Cell.FromFormula("A2*2"));
        sheet1.SetCell(rangeDependent, Cell.FromFormula("SUM(A1:A3)"));
        sheet2.SetCell(crossSheetDependent, Cell.FromFormula("Sheet1!A2"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), Cell.FromFormula("Sheet1!A3"));

        var dependents = FormulaAuditingService.GetDirectDependents(wb, target);

        dependents.Should().Equal(localDependent, rangeDependent, crossSheetDependent);
    }

    [Fact]
    public void GetDirectDependents_ReturnsFormulaCellsWithAbsoluteLocalReferences()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var target = new CellAddress(sheet.Id, 2, 1);
        var dependent = new CellAddress(sheet.Id, 5, 3);

        sheet.SetCell(dependent, Cell.FromFormula("$A$2*2"));

        FormulaAuditingService.GetDirectDependents(wb, target)
            .Should()
            .Equal(dependent);
    }

    [Fact]
    public void GetDependentTraceArrows_ReturnsMultiLevelFormulaChain()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetCell(b1, Cell.FromFormula("A1+1"));
        sheet.SetCell(c1, Cell.FromFormula("B1*2"));

        var arrows = FormulaAuditingService.GetDependentTraceArrows(wb, a1);

        arrows.Should().Equal(
            new FormulaTraceArrow(a1, b1),
            new FormulaTraceArrow(b1, c1));
    }
}
