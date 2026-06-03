using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class FormulaAuditingServiceTests
{
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

    [Fact]
    public void FindFormulaErrorIssues_ReturnsUserFacingIssueMessages()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 2, 3);
        var cell = Cell.FromFormula("1/0");
        cell.Value = ErrorValue.DivByZero;
        sheet.SetCell(address, cell);

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle().Subject;

        issue.SheetName.Should().Be("Sheet1");
        issue.Cell.Should().Be("C2");
        issue.ErrorCode.Should().Be("#DIV/0!");
        issue.FormulaText.Should().Be("=1/0");
        issue.Description.Should().Contain("division by zero");
    }

    [Fact]
    public void FindFormulaErrors_SkipsIgnoredFormulaErrors()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ignoredAddress = new CellAddress(sheet.Id, 1, 1);
        var visibleAddress = new CellAddress(sheet.Id, 2, 1);
        var ignored = Cell.FromFormula("1/0");
        ignored.Value = ErrorValue.DivByZero;
        ignored.IgnoreFormulaError = true;
        var visible = Cell.FromFormula("MISSING()");
        visible.Value = ErrorValue.Name;
        sheet.SetCell(ignoredAddress, ignored);
        sheet.SetCell(visibleAddress, visible);

        FormulaAuditingService.FindFormulaErrors(wb, sheet.Id)
            .Should().ContainSingle()
            .Which.Address.Should().Be(visibleAddress);
    }
}
