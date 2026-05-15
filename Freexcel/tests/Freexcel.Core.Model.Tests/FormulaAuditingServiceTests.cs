using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class FormulaAuditingServiceTests
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
    public void FindFormulaErrors_ReturnsFormulaCellsWithCachedErrorsInSheetOrder()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a2 = new CellAddress(sheet.Id, 2, 1);

        var later = Cell.FromFormula("1/0");
        later.Value = ErrorValue.DivByZero;
        sheet.SetCell(a2, later);

        var earlier = Cell.FromFormula("MISSING()");
        earlier.Value = ErrorValue.Name;
        sheet.SetCell(b1, earlier);

        var errors = FormulaAuditingService.FindFormulaErrors(wb, sheet.Id);

        errors.Should().HaveCount(2);
        errors[0].Address.Should().Be(b1);
        errors[0].FormulaText.Should().Be("MISSING()");
        errors[0].Error.Should().Be(ErrorValue.Name);
        errors[1].Address.Should().Be(a2);
        errors[1].FormulaText.Should().Be("1/0");
        errors[1].Error.Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void FindFormulaErrors_CanLimitResultsToRequestedSheet()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");

        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), ErrorValue.Ref);
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), ErrorValue.Value);

        var errors = FormulaAuditingService.FindFormulaErrors(wb, sheet2.Id);

        errors.Should().ContainSingle();
        errors[0].SheetId.Should().Be(sheet2.Id);
        errors[0].SheetName.Should().Be("Sheet2");
        errors[0].Address.Should().Be(new CellAddress(sheet2.Id, 1, 1));
        errors[0].FormulaText.Should().BeNull();
        errors[0].Error.Should().Be(ErrorValue.Value);
    }
}
