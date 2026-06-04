using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class FormulaAuditingServiceTests
{
    [Fact]
    public void FindFormulaErrorIssues_SkipsDisabledInconsistentCalculatedColumnFormulaRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sales");
        wb.DisabledFormulaErrorCodes.Add(FormulaAuditingService.InconsistentCalculatedColumnFormulaErrorCode);
        wb.DisabledFormulaErrorCodes.Add(FormulaAuditingService.InconsistentFormulaErrorCode);
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2)),
            HeaderRowCount = 1
        });
        sheet.StructuredTables[0].Columns.Add(new StructuredTableColumnModel(1, "Sales"));
        sheet.StructuredTables[0].Columns.Add(new StructuredTableColumnModel(2, "Double", CalculatedColumnFormula: "[@Sales]*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromFormula("[@Sales]*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromFormula("[@Sales]*3"));

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().BeEmpty();
    }

    [Fact]
    public void FindFormulaErrorIssues_SkipsDisabledFormulaRefersToBlankCellsRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromFormula("A1+1"));
        wb.DisabledFormulaErrorCodes.Add(FormulaAuditingService.FormulaRefersToBlankCellsErrorCode);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().BeEmpty();
    }

    [Fact]
    public void FindFormulaErrors_SkipsDisabledErrorCheckingRules()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var div0Address = new CellAddress(sheet.Id, 1, 1);
        var nameAddress = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(div0Address, new Cell { FormulaText = "1/0", Value = ErrorValue.DivByZero });
        sheet.SetCell(nameAddress, new Cell { FormulaText = "MISSING()", Value = ErrorValue.Name });
        wb.DisabledFormulaErrorCodes.Add(ErrorValue.DivByZero.Code);

        FormulaAuditingService.FindFormulaErrors(wb, sheet.Id)
            .Should().ContainSingle()
            .Which.Address.Should().Be(nameAddress);
    }

    [Fact]
    public void FindFormulaErrorIssues_SkipsDisabledNumbersStoredAsTextRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("42"));
        wb.DisabledFormulaErrorCodes.Add(FormulaAuditingService.NumberStoredAsTextErrorCode);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().BeEmpty();
    }

    [Fact]
    public void FindFormulaErrorIssues_SkipsDisabledTwoDigitYearTextDateRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("1/2/24"));
        wb.DisabledFormulaErrorCodes.Add(FormulaAuditingService.TwoDigitYearTextDateErrorCode);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().BeEmpty();
    }

    [Fact]
    public void FindFormulaErrorIssues_SkipsDisabledFormulaStoredAsTextRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("=SUM(A1:A2)"));
        wb.DisabledFormulaErrorCodes.Add(FormulaAuditingService.FormulaStoredAsTextErrorCode);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().BeEmpty();
    }

    [Fact]
    public void FindFormulaErrorIssues_SkipsDisabledInconsistentFormulaRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromFormula("A2*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromFormula("B2*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), Cell.FromFormula("A2*2"));
        wb.DisabledFormulaErrorCodes.Add(FormulaAuditingService.InconsistentFormulaErrorCode);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().BeEmpty();
    }

    [Fact]
    public void FindFormulaErrorIssues_SkipsDisabledFormulaOmitsAdjacentCellsRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), Cell.FromFormula("SUM(A1:A2)"));
        wb.DisabledFormulaErrorCodes.Add(FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().BeEmpty();
    }

    [Fact]
    public void FindFormulaErrorIssues_SkipsDisabledAggregateFormulaOmitsAdjacentCellsRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), Cell.FromFormula("AVERAGE(A1:A2)"));
        wb.DisabledFormulaErrorCodes.Add(FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().BeEmpty();
    }

    [Fact]
    public void FindFormulaErrorIssues_SkipsDisabledUnlockedFormulaCellsRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var unlockedStyleId = wb.RegisterStyle(new CellStyle { Locked = false });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        var cell = Cell.FromFormula("A1+1");
        cell.StyleId = unlockedStyleId;
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), cell);
        wb.DisabledFormulaErrorCodes.Add(FormulaAuditingService.UnlockedFormulaCellsErrorCode);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().BeEmpty();
    }

    [Fact]
    public void FindFormulaErrorIssues_SkipsDisabledDataValidationRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(address, address),
            Type = DvType.List,
            Formula1 = "Red,Green"
        });
        sheet.SetCell(address, new TextValue("Blue"));
        wb.DisabledFormulaErrorCodes.Add(FormulaAuditingService.DataValidationErrorCode);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().BeEmpty();
    }

    [Fact]
    public void SetFormulaErrorCheckingRuleCommand_TogglesRuleAndUndoRestores()
    {
        var wb = new Workbook("test");
        var ctx = new TestCommandContext(wb);

        var command = new SetFormulaErrorCheckingRuleCommand(ErrorValue.DivByZero.Code, enabled: false);

        command.Apply(ctx).Success.Should().BeTrue();
        wb.DisabledFormulaErrorCodes.Should().Contain(ErrorValue.DivByZero.Code);

        command.Revert(ctx);

        wb.DisabledFormulaErrorCodes.Should().BeEmpty();
    }
}
