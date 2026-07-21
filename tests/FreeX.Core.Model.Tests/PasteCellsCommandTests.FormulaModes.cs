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
        var ctx = new TestCommandContext(wb);
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
        var ctx = new TestCommandContext(wb);
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

        // Transpose swaps each relative reference's own (row,col) offset from the copied block's
        // anchor (A1) onto the destination block's anchor (C3), it does NOT translate every
        // reference by the host cell's own delta. C1 is 2 columns right of the copied block's
        // anchor A1 (row offset 0, col offset 2); swapped that becomes row offset 2, col offset 0
        // from the destination anchor C3, i.e. C5 -- not "D4" (which is simply host cell B1->C4's
        // OWN translation delta applied uniformly, the bug fixed by R56-commands-paste-special-5-1).
        var pasted = sheet.GetCell(formulaDestination)!;
        pasted.FormulaText.Should().Be("C5+$D$1");
        pasted.StyleId.Should().Be(destinationStyle);
    }

    [Fact]
    public void PasteCommandFactory_TransposedFormulasModeRebasesReferencesToOtherCopiedCells()
    {
        // R56-commands-paste-special-5-1: a formula referencing OTHER cells in the copied block
        // (not just itself) must have each of those references transposed independently -- swapping
        // its own (row,col) offset from the source anchor, not shifted by the host cell's uniform
        // delta. Source A1:C1 = 10, 20, "=A1+B1"; transposed to anchor E1, real Excel produces
        // E1=10, E2=20, E3="=E1+E2".
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var destinationStart = new CellAddress(sheet.Id, 1, 5); // E1
        var aCell = Cell.FromValue(new NumberValue(10));
        var bCell = Cell.FromValue(new NumberValue(20));
        var cCell = Cell.FromFormula("A1+B1");
        sheet.SetCell(a1, aCell);
        sheet.SetCell(b1, bCell);
        sheet.SetCell(c1, cCell);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(a1, c1),
            [(a1, aCell.Clone()), (b1, bCell.Clone()), (c1, cCell.Clone())],
            destinationStart,
            PasteCellsMode.Formulas,
            new PasteSpecialOptions(Transpose: true));

        command.Apply(ctx).Success.Should().BeTrue();

        var pastedA = sheet.GetCell(new CellAddress(sheet.Id, 1, 5))!; // E1
        var pastedB = sheet.GetCell(new CellAddress(sheet.Id, 2, 5))!; // E2
        var pastedC = sheet.GetCell(new CellAddress(sheet.Id, 3, 5))!; // E3
        pastedA.Value.Should().Be(new NumberValue(10));
        pastedB.Value.Should().Be(new NumberValue(20));
        pastedC.FormulaText.Should().Be("E1+E2");
    }

    [Fact]
    public void PasteCommandFactory_TransposedAllModeCopiesSourceStyleAndRebasesFormula()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
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

        // Same transpose-axis-swap correction as the Formulas-mode test above (R56-commands-paste-
        // special-5-1): C1 is 2 columns right of the copied block's anchor A1, which transposes to
        // 2 rows below the destination anchor C3, i.e. C5.
        var pasted = sheet.GetCell(formulaDestination)!;
        pasted.FormulaText.Should().Be("C5+$D$1");
        pasted.StyleId.Should().Be(sourceStyle);
    }

    [Fact]
    public void PasteCommandFactory_FormulasModePreservesDestinationStyleOnlyAndRebasesFormula()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
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
        var ctx = new TestCommandContext(wb);
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
