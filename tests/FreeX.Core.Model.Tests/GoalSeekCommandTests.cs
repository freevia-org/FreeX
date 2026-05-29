using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class GoalSeekCommandTests
{
    [Fact]
    public void GoalSeekCommand_AppliesFoundValueAndReportsAffectedCell()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(address, new NumberValue(4));

        var outcome = new GoalSeekCommand(address, 12.5)
            .Apply(new SimpleCtx(workbook));

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().Equal(address);
        sheet.GetValue(address).Should().Be(new NumberValue(12.5));
    }

    [Fact]
    public void GoalSeekCommand_RevertRestoresOriginalCell()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 2);
        var styleId = workbook.RegisterStyle(new CellStyle { Bold = true });
        sheet.SetCell(address, new Cell
        {
            FormulaText = "A1*2",
            Value = new NumberValue(8),
            StyleId = styleId
        });
        var context = new SimpleCtx(workbook);
        var command = new GoalSeekCommand(address, 12);

        command.Apply(context).Success.Should().BeTrue();
        command.Revert(context);

        var restored = sheet.GetCell(address);
        restored.Should().NotBeNull();
        restored!.FormulaText.Should().Be("A1*2");
        restored.Value.Should().Be(new NumberValue(8));
        restored.StyleId.Should().Be(styleId);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void GoalSeekCommand_RejectsNonFiniteResultsWithoutMutatingCell(double value)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(address, new NumberValue(4));

        var outcome = new GoalSeekCommand(address, value)
            .Apply(new SimpleCtx(workbook));

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("finite");
        sheet.GetValue(address).Should().Be(new NumberValue(4));
    }

    private sealed class SimpleCtx(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook => workbook;
        public Sheet GetSheet(SheetId sheetId) => workbook.GetSheet(sheetId)!;
    }
}
