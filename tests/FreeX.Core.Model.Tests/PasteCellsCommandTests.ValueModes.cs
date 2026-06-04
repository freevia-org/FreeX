using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PasteCellsCommandTests
{
    [Fact]
    public void PasteCommandFactory_ValuesModeBuildsValueOnlyCommand()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var sourceCell = Cell.FromFormula("B1+1");
        sourceCell.Value = new NumberValue(42);
        sheet.SetCell(source, sourceCell);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            new CellAddress(sheet.Id, 2, 1),
            PasteCellsMode.Values,
            default);

        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.GetCell(new CellAddress(sheet.Id, 2, 1))!;
        pasted.FormulaText.Should().BeNull();
        pasted.Value.Should().Be(new NumberValue(42));
    }

    [Fact]
    public void PasteCommandFactory_ValuesModePreservesDestinationStyle()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);
        var sourceStyle = wb.RegisterStyle(new CellStyle { Bold = true });
        var destinationStyle = wb.RegisterStyle(new CellStyle { Italic = true });
        var sourceCell = Cell.FromFormula("B1+1");
        sourceCell.Value = new NumberValue(42);
        sourceCell.StyleId = sourceStyle;
        sheet.SetCell(source, sourceCell);
        var destinationCell = Cell.FromValue(new TextValue("old"));
        destinationCell.StyleId = destinationStyle;
        sheet.SetCell(destination, destinationCell);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.Values,
            default);

        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.GetCell(destination)!;
        pasted.FormulaText.Should().BeNull();
        pasted.Value.Should().Be(new NumberValue(42));
        pasted.StyleId.Should().Be(destinationStyle);
    }

    [Fact]
    public void PasteCommandFactory_ValuesModePreservesDestinationStyleOnly()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);
        var sourceStyle = wb.RegisterStyle(new CellStyle { Bold = true });
        var destinationStyle = wb.RegisterStyle(new CellStyle { Italic = true });
        var sourceCell = Cell.FromValue(new NumberValue(42));
        sourceCell.StyleId = sourceStyle;
        sheet.SetCell(source, sourceCell);
        sheet.SetStyleOnly(destination.Row, destination.Col, destinationStyle);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.Values,
            default);

        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.GetCell(destination)!;
        pasted.Value.Should().Be(new NumberValue(42));
        pasted.StyleId.Should().Be(destinationStyle);
    }

    [Fact]
    public void PasteCommandFactory_TransposedValuesModePreservesDestinationStyles()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var sourceEnd = new CellAddress(sheet.Id, 1, 2);
        var sourceStyle = wb.RegisterStyle(new CellStyle { Bold = true });
        var firstDestinationStyle = wb.RegisterStyle(new CellStyle { Italic = true });
        var secondDestinationStyle = wb.RegisterStyle(new CellStyle { Underline = true });
        var firstSourceCell = Cell.FromValue(new NumberValue(10));
        firstSourceCell.StyleId = sourceStyle;
        var secondSourceCell = Cell.FromFormula("C1+1");
        secondSourceCell.Value = new NumberValue(20);
        secondSourceCell.StyleId = sourceStyle;
        sheet.SetCell(sourceStart, firstSourceCell);
        sheet.SetCell(sourceEnd, secondSourceCell);
        var destinationStart = new CellAddress(sheet.Id, 3, 3);
        var firstDestinationCell = Cell.FromValue(new TextValue("old 1"));
        firstDestinationCell.StyleId = firstDestinationStyle;
        sheet.SetCell(destinationStart, firstDestinationCell);
        var secondDestination = new CellAddress(sheet.Id, 4, 3);
        var secondDestinationCell = Cell.FromValue(new TextValue("old 2"));
        secondDestinationCell.StyleId = secondDestinationStyle;
        sheet.SetCell(secondDestination, secondDestinationCell);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(sourceStart, sourceEnd),
            [(sourceStart, firstSourceCell.Clone()), (sourceEnd, secondSourceCell.Clone())],
            destinationStart,
            PasteCellsMode.Values,
            new PasteSpecialOptions(Transpose: true));

        command.Apply(ctx).Success.Should().BeTrue();

        var firstPasted = sheet.GetCell(destinationStart)!;
        firstPasted.Value.Should().Be(new NumberValue(10));
        firstPasted.FormulaText.Should().BeNull();
        firstPasted.StyleId.Should().Be(firstDestinationStyle);
        var secondPasted = sheet.GetCell(secondDestination)!;
        secondPasted.Value.Should().Be(new NumberValue(20));
        secondPasted.FormulaText.Should().BeNull();
        secondPasted.StyleId.Should().Be(secondDestinationStyle);
    }
}
