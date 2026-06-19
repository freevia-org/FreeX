using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class WorksheetViewCommandTests
{
    [Fact]
    public void SetWorksheetOutlineSymbolsCommand_SetsValueAndUndoRestoresNullDefault()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var command = new SetWorksheetOutlineSymbolsCommand(sheet.Id, showOutlineSymbols: false);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.ShowOutlineSymbols.Should().BeFalse();

        command.Revert(ctx);

        sheet.ShowOutlineSymbols.Should().BeNull();
    }

    [Fact]
    public void SetWorksheetOutlineSymbolsCommand_UndoRestoresExplicitPreviousValue()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.ShowOutlineSymbols = false;

        var command = new SetWorksheetOutlineSymbolsCommand(sheet.Id, showOutlineSymbols: true);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.ShowOutlineSymbols.Should().BeTrue();

        command.Revert(ctx);

        sheet.ShowOutlineSymbols.Should().BeFalse();
    }

    [Fact]
    public void SetWorksheetOutlineSettingsCommand_SetsAllValuesAndUndoRestoresNullDefaults()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var command = new SetWorksheetOutlineSettingsCommand(
            sheet.Id,
            summaryBelow: false,
            summaryRight: false,
            applyStyles: true);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.OutlineSummaryBelow.Should().BeFalse();
        sheet.OutlineSummaryRight.Should().BeFalse();
        sheet.ApplyOutlineStyles.Should().BeTrue();

        command.Revert(ctx);

        sheet.OutlineSummaryBelow.Should().BeNull();
        sheet.OutlineSummaryRight.Should().BeNull();
        sheet.ApplyOutlineStyles.Should().BeNull();
    }

    [Fact]
    public void SetWorksheetOutlineSettingsCommand_UndoRestoresExplicitPreviousValues()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.OutlineSummaryBelow = true;
        sheet.OutlineSummaryRight = true;
        sheet.ApplyOutlineStyles = false;

        var command = new SetWorksheetOutlineSettingsCommand(
            sheet.Id,
            summaryBelow: false,
            summaryRight: false,
            applyStyles: true);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.OutlineSummaryBelow.Should().BeFalse();
        sheet.OutlineSummaryRight.Should().BeFalse();
        sheet.ApplyOutlineStyles.Should().BeTrue();

        command.Revert(ctx);

        sheet.OutlineSummaryBelow.Should().BeTrue();
        sheet.OutlineSummaryRight.Should().BeTrue();
        sheet.ApplyOutlineStyles.Should().BeFalse();
    }
}
