using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PasteCellsCommandTests
{
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
    public void PasteCommandFactory_TransposedFormulasModePreservesDestinationStyleAndRebasesFormula()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var formulaSource = new CellAddress(sheet.Id, 1, 2);
        var destinationStart = new CellAddress(sheet.Id, 3, 3);
        var formulaDestination = new CellAddress(sheet.Id, 4, 3);
        var sourceStyle = wb.RegisterStyle(new CellStyle { Bold = true });
        var destinationStyle = wb.RegisterStyle(new CellStyle { Italic = true });
        var valueSourceCell = Cell.FromValue(new NumberValue(10));
        valueSourceCell.StyleId = sourceStyle;
        var formulaSourceCell = Cell.FromFormula("C1+$D$1");
        formulaSourceCell.StyleId = sourceStyle;
        sheet.SetCell(sourceStart, valueSourceCell);
        sheet.SetCell(formulaSource, formulaSourceCell);
        var destinationCell = Cell.FromValue(new TextValue("old"));
        destinationCell.StyleId = destinationStyle;
        sheet.SetCell(formulaDestination, destinationCell);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(sourceStart, formulaSource),
            [(sourceStart, valueSourceCell.Clone()), (formulaSource, formulaSourceCell.Clone())],
            destinationStart,
            PasteCellsMode.Formulas,
            new PasteSpecialOptions(Transpose: true));

        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.GetCell(formulaDestination)!;
        pasted.FormulaText.Should().Be("D4+$D$1");
        pasted.StyleId.Should().Be(destinationStyle);
    }

    [Fact]
    public void PasteCommandFactory_TransposedAllModeCopiesSourceStyleAndRebasesFormula()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var formulaSource = new CellAddress(sheet.Id, 1, 2);
        var destinationStart = new CellAddress(sheet.Id, 3, 3);
        var formulaDestination = new CellAddress(sheet.Id, 4, 3);
        var sourceStyle = wb.RegisterStyle(new CellStyle { Bold = true });
        var destinationStyle = wb.RegisterStyle(new CellStyle { Italic = true });
        var valueSourceCell = Cell.FromValue(new NumberValue(10));
        valueSourceCell.StyleId = sourceStyle;
        var formulaSourceCell = Cell.FromFormula("C1+$D$1");
        formulaSourceCell.StyleId = sourceStyle;
        sheet.SetCell(sourceStart, valueSourceCell);
        sheet.SetCell(formulaSource, formulaSourceCell);
        var destinationCell = Cell.FromValue(new TextValue("old"));
        destinationCell.StyleId = destinationStyle;
        sheet.SetCell(formulaDestination, destinationCell);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(sourceStart, formulaSource),
            [(sourceStart, valueSourceCell.Clone()), (formulaSource, formulaSourceCell.Clone())],
            destinationStart,
            PasteCellsMode.All,
            new PasteSpecialOptions(Transpose: true));

        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.GetCell(formulaDestination)!;
        pasted.FormulaText.Should().Be("D4+$D$1");
        pasted.StyleId.Should().Be(sourceStyle);
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
}
