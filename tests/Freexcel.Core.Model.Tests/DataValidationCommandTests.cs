using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using Xunit;

namespace Freexcel.Core.Model.Tests;

public sealed class DataValidationCommandTests
{
    [Fact]
    public void SetDataValidationCommand_SetsRuleAndUndoRemovesIt()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var rule = NewRule(sheet.Id);

        var command = new SetDataValidationCommand(sheet.Id, rule);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.DataValidations.Should().ContainSingle().Which.Should().BeSameAs(rule);

        command.Revert(ctx);

        sheet.DataValidations.Should().BeEmpty();
    }

    [Fact]
    public void SetDataValidationCommand_RejectsInvalidRuleChoices()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var original = NewRule(sheet.Id);
        sheet.DataValidations.Add(original);

        var invalidType = NewRule(sheet.Id);
        invalidType.Type = (DvType)99;
        var invalidOperator = NewRule(sheet.Id);
        invalidOperator.Operator = (DvOperator)99;
        var invalidAlertStyle = NewRule(sheet.Id);
        invalidAlertStyle.AlertStyle = (DvAlertStyle)99;

        foreach (var invalidRule in new[] { invalidType, invalidOperator, invalidAlertStyle })
        {
            var outcome = new SetDataValidationCommand(sheet.Id, invalidRule).Apply(ctx);

            outcome.Success.Should().BeFalse();
            sheet.DataValidations.Should().ContainSingle().Which.Should().BeSameAs(original);
        }
    }

    [Fact]
    public void SetDataValidationCommand_RejectsRuleRangeOnAnotherSheet()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var other = wb.AddSheet("Other");
        var ctx = new SimpleCtx(wb);
        var rule = NewRule(other.Id);

        var outcome = new SetDataValidationCommand(sheet.Id, rule).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.DataValidations.Should().BeEmpty();
    }

    [Fact]
    public void SetDataValidationCommand_RejectsProtectedSheet()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.IsProtected = true;

        var outcome = new SetDataValidationCommand(sheet.Id, NewRule(sheet.Id)).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.DataValidations.Should().BeEmpty();
    }

    [Fact]
    public void ClearDataValidationCommand_RejectsProtectedSheet()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var rule = NewRule(sheet.Id);
        sheet.DataValidations.Add(rule);
        sheet.IsProtected = true;

        var outcome = new ClearDataValidationCommand(sheet.Id, rule.AppliesTo).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.DataValidations.Should().ContainSingle().Which.Should().BeSameAs(rule);
    }

    private static DataValidation NewRule(SheetId sheetId) =>
        new()
        {
            AppliesTo = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 5, 1)),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AlertStyle = DvAlertStyle.Stop
        };

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
