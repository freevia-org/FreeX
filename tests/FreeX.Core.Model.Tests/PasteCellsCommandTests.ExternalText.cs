using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PasteCellsCommandTests
{
    [Fact]
    public void PasteCommandFactory_ExternalTextBuildsCommandForCurrentDestination()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(
            sheet.Id,
            new CellAddress(sheet.Id, 3, 2),
            [["1", "Name"], ["2.5", "West"]]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(new CellAddress(sheet.Id, 3, 2)).Should().Be(new NumberValue(1));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 3)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 4, 2)).Should().Be(new NumberValue(2.5));
        sheet.GetValue(new CellAddress(sheet.Id, 4, 3)).Should().Be(new TextValue("West"));
    }

    [Fact]
    public void PasteCommandFactory_ExternalTextRejectsRectanglePastWorksheetEdge()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var edge = new CellAddress(sheet.Id, 1, CellAddress.MaxCol);
        sheet.SetCell(edge, Cell.FromValue(new TextValue("keep")));

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(
            sheet.Id,
            edge,
            [["A", "B"]]);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("bounds");
        sheet.GetValue(edge).Should().Be(new TextValue("keep"));
    }

    [Fact]
    public void PasteCommandFactory_ExternalTextCanPreserveNumericLookingFieldsAsText()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(
            sheet.Id,
            new CellAddress(sheet.Id, 3, 2),
            [["00123", "2.5"], ["1E4", "West"]],
            preserveText: true);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(new CellAddress(sheet.Id, 3, 2)).Should().Be(new TextValue("00123"));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 3)).Should().Be(new TextValue("2.5"));
        sheet.GetValue(new CellAddress(sheet.Id, 4, 2)).Should().Be(new TextValue("1E4"));
        sheet.GetValue(new CellAddress(sheet.Id, 4, 3)).Should().Be(new TextValue("West"));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void PasteCommandFactory_ExternalTextKeepsNonFiniteNumericTokensAsText(string text)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 3, 2);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [[text]]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(address).Should().Be(new TextValue(text));
    }
}
