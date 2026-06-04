using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class FormulaAuditingServiceTests
{
    [Fact]
    public void SetFormulaErrorIgnoredCommand_SetsStateAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var cell = Cell.FromFormula("1/0");
        cell.Value = ErrorValue.DivByZero;
        sheet.SetCell(address, cell);
        var ctx = new TestCommandContext(wb);

        var command = new SetFormulaErrorIgnoredCommand(sheet.Id, address, ignored: true);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.GetCell(address)!.IgnoreFormulaError.Should().BeTrue();

        command.Revert(ctx);

        sheet.GetCell(address)!.IgnoreFormulaError.Should().BeFalse();
    }

    [Fact]
    public void SetFormulaErrorIgnoredCommand_IgnoresNumberStoredAsTextIssues()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("42"));
        var ctx = new TestCommandContext(wb);

        var command = new SetFormulaErrorIgnoredCommand(sheet.Id, address, ignored: true);

        command.Apply(ctx).Success.Should().BeTrue();
        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id).Should().BeEmpty();

        command.Revert(ctx);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle()
            .Which.ErrorCode.Should().Be(FormulaAuditingService.NumberStoredAsTextErrorCode);
    }

    [Fact]
    public void SetFormulaErrorIgnoredCommand_IgnoresFormulaRefersToBlankCellsIssues()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(address, Cell.FromFormula("A1+1"));
        var ctx = new TestCommandContext(wb);

        var command = new SetFormulaErrorIgnoredCommand(sheet.Id, address, ignored: true);

        command.Apply(ctx).Success.Should().BeTrue();
        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id).Should().BeEmpty();

        command.Revert(ctx);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle()
            .Which.ErrorCode.Should().Be(FormulaAuditingService.FormulaRefersToBlankCellsErrorCode);
    }

    [Fact]
    public void SetFormulaErrorIgnoredCommand_IgnoresFormulaStoredAsTextIssues()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("=SUM(A1:A2)"));
        var ctx = new TestCommandContext(wb);

        var command = new SetFormulaErrorIgnoredCommand(sheet.Id, address, ignored: true);

        command.Apply(ctx).Success.Should().BeTrue();
        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id).Should().BeEmpty();

        command.Revert(ctx);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle()
            .Which.ErrorCode.Should().Be(FormulaAuditingService.FormulaStoredAsTextErrorCode);
    }

    [Fact]
    public void SetFormulaErrorIgnoredCommand_IgnoresInconsistentCalculatedColumnFormulaIssues()
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
                new CellAddress(sheet.Id, 3, 2)),
            HeaderRowCount = 1
        });
        sheet.StructuredTables[0].Columns.Add(new StructuredTableColumnModel(1, "Sales"));
        sheet.StructuredTables[0].Columns.Add(new StructuredTableColumnModel(2, "Double", CalculatedColumnFormula: "[@Sales]*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromFormula("[@Sales]*2"));
        var address = new CellAddress(sheet.Id, 3, 2);
        sheet.SetCell(address, Cell.FromFormula("[@Sales]*3"));
        var ctx = new TestCommandContext(wb);

        var command = new SetFormulaErrorIgnoredCommand(sheet.Id, address, ignored: true);

        command.Apply(ctx).Success.Should().BeTrue();
        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id).Should().BeEmpty();

        command.Revert(ctx);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle()
            .Which.ErrorCode.Should().Be(FormulaAuditingService.InconsistentCalculatedColumnFormulaErrorCode);
    }

    [Fact]
    public void SetFormulaErrorIgnoredCommand_IgnoresInconsistentFormulaIssues()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromFormula("A2*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromFormula("B2*2"));
        sheet.SetCell(address, Cell.FromFormula("A2*2"));
        var ctx = new TestCommandContext(wb);

        var command = new SetFormulaErrorIgnoredCommand(sheet.Id, address, ignored: true);

        command.Apply(ctx).Success.Should().BeTrue();
        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id).Should().BeEmpty();

        command.Revert(ctx);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle()
            .Which.ErrorCode.Should().Be(FormulaAuditingService.InconsistentFormulaErrorCode);
    }

    [Fact]
    public void SetFormulaErrorIgnoredCommand_IgnoresFormulaOmitsAdjacentCellsIssues()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 4, 1);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        sheet.SetCell(address, Cell.FromFormula("SUM(A1:A2)"));
        var ctx = new TestCommandContext(wb);

        var command = new SetFormulaErrorIgnoredCommand(sheet.Id, address, ignored: true);

        command.Apply(ctx).Success.Should().BeTrue();
        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id).Should().BeEmpty();

        command.Revert(ctx);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle()
            .Which.ErrorCode.Should().Be(FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode);
    }

    [Fact]
    public void SetFormulaErrorIgnoredCommand_IgnoresAggregateFormulaOmitsAdjacentCellsIssues()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 4, 1);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        sheet.SetCell(address, Cell.FromFormula("AVERAGE(A1:A2)"));
        var ctx = new TestCommandContext(wb);

        var command = new SetFormulaErrorIgnoredCommand(sheet.Id, address, ignored: true);

        command.Apply(ctx).Success.Should().BeTrue();
        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id).Should().BeEmpty();

        command.Revert(ctx);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle()
            .Which.ErrorCode.Should().Be(FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode);
    }

    [Fact]
    public void SetFormulaErrorIgnoredCommand_IgnoresUnlockedFormulaCellsIssues()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var unlockedStyleId = wb.RegisterStyle(new CellStyle { Locked = false });
        var address = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        var cell = Cell.FromFormula("A1+1");
        cell.StyleId = unlockedStyleId;
        sheet.SetCell(address, cell);
        var ctx = new TestCommandContext(wb);

        var command = new SetFormulaErrorIgnoredCommand(sheet.Id, address, ignored: true);

        command.Apply(ctx).Success.Should().BeTrue();
        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id).Should().BeEmpty();

        command.Revert(ctx);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle()
            .Which.ErrorCode.Should().Be(FormulaAuditingService.UnlockedFormulaCellsErrorCode);
    }

    [Fact]
    public void SetFormulaErrorIgnoredCommand_IgnoresInvalidDataValidationIssues()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 2);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(address, address),
            Type = DvType.List,
            Formula1 = "Red,Green"
        });
        sheet.SetCell(address, new TextValue("Blue"));
        var ctx = new TestCommandContext(wb);

        var command = new SetFormulaErrorIgnoredCommand(sheet.Id, address, ignored: true);

        command.Apply(ctx).Success.Should().BeTrue();
        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id).Should().BeEmpty();

        command.Revert(ctx);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle()
            .Which.ErrorCode.Should().Be(FormulaAuditingService.DataValidationErrorCode);
    }

    [Fact]
    public void SetFormulaErrorIgnoredCommand_RejectsDisabledCachedFormulaErrorIssues()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var cell = Cell.FromFormula("1/0");
        cell.Value = ErrorValue.DivByZero;
        sheet.SetCell(address, cell);
        wb.DisabledFormulaErrorCodes.Add(ErrorValue.DivByZero.Code);
        var ctx = new TestCommandContext(wb);

        var command = new SetFormulaErrorIgnoredCommand(sheet.Id, address, ignored: true);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("does not currently contain an issue");
        sheet.GetCell(address)!.IgnoreFormulaError.Should().BeFalse();
    }

    [Fact]
    public void SetFormulaErrorIgnoredCommand_RejectsDisabledModeledWarningIssues()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("42"));
        wb.DisabledFormulaErrorCodes.Add(FormulaAuditingService.NumberStoredAsTextErrorCode);
        var ctx = new TestCommandContext(wb);

        var command = new SetFormulaErrorIgnoredCommand(sheet.Id, address, ignored: true);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("does not currently contain an issue");
        sheet.GetCell(address)!.IgnoreFormulaError.Should().BeFalse();
    }
}
