using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using Xunit;

namespace Freexcel.Core.Model.Tests;

public sealed class PasteCellsCommandTests
{
    [Fact]
    public void PasteCellsCommand_ReplacesValueAndStyleAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var addr = new CellAddress(sheet.Id, 1, 1);

        var oldStyle = wb.RegisterStyle(new CellStyle { Bold = true });
        var newStyle = wb.RegisterStyle(new CellStyle { Italic = true });
        var oldCell = Cell.FromValue(new TextValue("old"));
        oldCell.StyleId = oldStyle;
        sheet.SetCell(addr, oldCell);

        var pastedCell = Cell.FromValue(new TextValue("new"));
        pastedCell.StyleId = newStyle;

        var command = new PasteCellsCommand(sheet.Id, [(addr, pastedCell)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(addr).Should().Be(new TextValue("new"));
        sheet.GetCell(addr)!.StyleId.Should().Be(newStyle);

        command.Revert(ctx);

        sheet.GetValue(addr).Should().Be(new TextValue("old"));
        sheet.GetCell(addr)!.StyleId.Should().Be(oldStyle);
    }

    [Fact]
    public void PasteCommandFactory_AllModeBuildsCommandForCurrentDestinationAndAdjustsRelativeFormulas()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, Cell.FromFormula("B1+$C$1"));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sheet.GetCell(source)!.Clone())],
            new CellAddress(sheet.Id, 3, 2),
            PasteCellsMode.All,
            default);

        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.GetCell(new CellAddress(sheet.Id, 3, 2))!;
        pasted.FormulaText.Should().Be("C3+$C$1");
    }

    [Fact]
    public void PasteCommandFactory_ValuesModeBuildsValueOnlyCommand()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
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
        var ctx = new SimpleCtx(wb);
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
        var ctx = new SimpleCtx(wb);
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
    public void PasteCommandFactory_FormulasModePreservesDestinationStyleAndRebasesFormula()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 3, 2);
        var sourceStyle = wb.RegisterStyle(new CellStyle { Bold = true });
        var destinationStyle = wb.RegisterStyle(new CellStyle { Italic = true });
        var sourceCell = Cell.FromFormula("B1+$C$1");
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
            PasteCellsMode.Formulas,
            default);

        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.GetCell(destination)!;
        pasted.FormulaText.Should().Be("C3+$C$1");
        pasted.StyleId.Should().Be(destinationStyle);
    }

    [Fact]
    public void PasteCommandFactory_FormulasModePreservesDestinationStyleOnlyAndRebasesFormula()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 3, 2);
        var sourceStyle = wb.RegisterStyle(new CellStyle { Bold = true });
        var destinationStyle = wb.RegisterStyle(new CellStyle { Italic = true });
        var sourceCell = Cell.FromFormula("B1+$C$1");
        sourceCell.StyleId = sourceStyle;
        sheet.SetCell(source, sourceCell);
        sheet.SetStyleOnly(destination.Row, destination.Col, destinationStyle);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.Formulas,
            default);

        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.GetCell(destination)!;
        pasted.FormulaText.Should().Be("C3+$C$1");
        pasted.StyleId.Should().Be(destinationStyle);
    }

    [Fact]
    public void PasteCommandFactory_FormulasModeWithNonFormulaSourcePreservesDestinationStyle()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);
        var sourceStyle = wb.RegisterStyle(new CellStyle { Bold = true });
        var destinationStyle = wb.RegisterStyle(new CellStyle { Italic = true });
        var sourceCell = Cell.FromValue(new NumberValue(42));
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
            PasteCellsMode.Formulas,
            default);

        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.GetCell(destination)!;
        pasted.FormulaText.Should().BeNull();
        pasted.Value.Should().Be(new NumberValue(42));
        pasted.StyleId.Should().Be(destinationStyle);
    }

    [Fact]
    public void PasteCommandFactory_AllModeCopiesSourceStyle()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);
        var sourceStyle = wb.RegisterStyle(new CellStyle { Bold = true });
        var destinationStyle = wb.RegisterStyle(new CellStyle { Italic = true });
        var sourceCell = Cell.FromValue(new TextValue("new"));
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
            PasteCellsMode.All,
            default);

        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.GetCell(destination)!;
        pasted.Value.Should().Be(new TextValue("new"));
        pasted.StyleId.Should().Be(sourceStyle);
    }

    [Fact]
    public void PasteCommandFactory_ExternalTextBuildsCommandForCurrentDestination()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);

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

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
