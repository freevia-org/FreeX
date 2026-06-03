using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class FormulaAuditingServiceTests
{
    [Fact]
    public void FindFormulaErrorIssues_ReturnsFormulaRefersToBlankCellsForDirectRefsRangesAndCrossSheetRefs()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var formulaAddress = new CellAddress(sheet1.Id, 4, 4);

        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 2), BlankValue.Instance);
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 2), BlankValue.Instance);
        sheet1.SetCell(formulaAddress, Cell.FromFormula("SUM(A1:B1,Sheet2!B2,C1)"));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet1.Id)
            .Should().ContainSingle().Subject;

        issue.SheetName.Should().Be("Sheet1");
        issue.Cell.Should().Be("D4");
        issue.ErrorCode.Should().Be(FormulaAuditingService.FormulaRefersToBlankCellsErrorCode);
        issue.FormulaText.Should().Be("=SUM(A1:B1,Sheet2!B2,C1)");
        issue.Description.Should().Contain("blank cells");
    }

    [Fact]
    public void FindFormulaErrorIssues_ReturnsInconsistentCalculatedColumnFormulaInTable()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sales");
        wb.DisabledFormulaErrorCodes.Add(FormulaAuditingService.InconsistentFormulaErrorCode);
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 3)),
            HeaderRowCount = 1
        });
        sheet.StructuredTables[0].Columns.Add(new StructuredTableColumnModel(1, "Region"));
        sheet.StructuredTables[0].Columns.Add(new StructuredTableColumnModel(2, "Sales"));
        sheet.StructuredTables[0].Columns.Add(new StructuredTableColumnModel(3, "Double", CalculatedColumnFormula: "[@Sales]*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), Cell.FromFormula("[@Sales]*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), Cell.FromFormula("[@Sales]*3"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), Cell.FromFormula("[@Sales]*2"));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle(i => i.ErrorCode == FormulaAuditingService.InconsistentCalculatedColumnFormulaErrorCode).Subject;

        issue.SheetName.Should().Be("Sales");
        issue.Cell.Should().Be("C3");
        issue.FormulaText.Should().Be("=[@Sales]*3");
        issue.Description.Should().Contain("calculated column formula");
    }

    [Fact]
    public void FindFormulaErrorIssues_ReturnsInconsistentFormulaInRow()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromFormula("A2*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromFormula("B2*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), Cell.FromFormula("A2*2"));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle(i => i.ErrorCode == FormulaAuditingService.InconsistentFormulaErrorCode).Subject;

        issue.Cell.Should().Be("C3");
        issue.FormulaText.Should().Be("=A2*2");
        issue.Description.Should().Contain("inconsistent with nearby formulas");
    }

    [Fact]
    public void FindFormulaErrorIssues_ReturnsInconsistentFormulaInColumn()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromFormula("A1*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromFormula("A2*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromFormula("A1*2"));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle(i => i.ErrorCode == FormulaAuditingService.InconsistentFormulaErrorCode).Subject;

        issue.Cell.Should().Be("B3");
        issue.FormulaText.Should().Be("=A1*2");
    }

    [Fact]
    public void FindFormulaErrorIssues_ReturnsUnlockedFormulaCells()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var unlockedStyleId = wb.RegisterStyle(new CellStyle { Locked = false });
        var address = new CellAddress(sheet.Id, 3, 2);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        var cell = Cell.FromFormula("B2*2");
        cell.StyleId = unlockedStyleId;
        sheet.SetCell(address, cell);

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle(i => i.ErrorCode == FormulaAuditingService.UnlockedFormulaCellsErrorCode).Subject;

        issue.Cell.Should().Be("B3");
        issue.FormulaText.Should().Be("=B2*2");
        issue.Description.Should().Contain("unlocked");
    }

    [Fact]
    public void FindFormulaErrorIssues_ReturnsInvalidDataValidationEntries()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 2, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(address, address),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            ErrorMessage = "Enter a whole number from 1 to 10."
        });
        sheet.SetCell(address, new NumberValue(99));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle(i => i.ErrorCode == FormulaAuditingService.DataValidationErrorCode).Subject;

        issue.SheetName.Should().Be("Sheet1");
        issue.Cell.Should().Be("A2");
        issue.FormulaText.Should().BeNull();
        issue.Description.Should().Contain("data validation rule");
        issue.Description.Should().Contain("whole number from 1 to 10");
    }
}
