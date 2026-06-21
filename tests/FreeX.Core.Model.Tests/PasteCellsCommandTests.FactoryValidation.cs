using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PasteCellsCommandTests
{
    [Fact]
    public void PasteCommandFactory_AllModeBuildsCommandForCurrentDestinationAndAdjustsRelativeFormulas()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
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
    public void PasteCommandFactory_AllModeTilesCopiedBlockAcrossLargerDestinationRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var sourceEnd = new CellAddress(sheet.Id, 2, 2);
        var sourceCells = new List<(CellAddress Source, Cell Cell)>();
        for (uint row = sourceStart.Row; row <= sourceEnd.Row; row++)
        {
            for (uint col = sourceStart.Col; col <= sourceEnd.Col; col++)
            {
                var address = new CellAddress(sheet.Id, row, col);
                var value = $"{row},{col}";
                var cell = Cell.FromValue(new TextValue(value));
                sheet.SetCell(address, cell);
                sourceCells.Add((address, cell.Clone()));
            }
        }

        var destinationStart = new CellAddress(sheet.Id, 4, 4);
        var destinationEnd = new CellAddress(sheet.Id, 7, 6);
        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(sourceStart, sourceEnd),
            sourceCells,
            new GridRange(destinationStart, destinationEnd),
            PasteCellsMode.All,
            default);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(4, 4).Should().Be(new TextValue("1,1"));
        sheet.GetValue(4, 5).Should().Be(new TextValue("1,2"));
        sheet.GetValue(4, 6).Should().Be(new TextValue("1,1"));
        sheet.GetValue(5, 4).Should().Be(new TextValue("2,1"));
        sheet.GetValue(5, 5).Should().Be(new TextValue("2,2"));
        sheet.GetValue(5, 6).Should().Be(new TextValue("2,1"));
        sheet.GetValue(6, 4).Should().Be(new TextValue("1,1"));
        sheet.GetValue(7, 6).Should().Be(new TextValue("2,1"));
    }

    [Fact]
    public void PasteCommandFactory_TiledInternalPasteRebasesFormulasForEachDestinationCell()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destinationStart = new CellAddress(sheet.Id, 4, 4);
        var destinationEnd = new CellAddress(sheet.Id, 5, 5);
        sheet.SetFormula(source, "B1+$C$1");

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sheet.GetCell(source)!.Clone())],
            new GridRange(destinationStart, destinationEnd),
            PasteCellsMode.All,
            default);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(destinationStart)!.FormulaText.Should().Be("E4+$C$1");
        sheet.GetCell(new CellAddress(sheet.Id, 4, 5))!.FormulaText.Should().Be("F4+$C$1");
        sheet.GetCell(new CellAddress(sheet.Id, 5, 4))!.FormulaText.Should().Be("E5+$C$1");
        sheet.GetCell(destinationEnd)!.FormulaText.Should().Be("F5+$C$1");
    }

    [Fact]
    public void PasteCommandFactory_TiledInternalPasteHonorsTransposeAcrossLargerDestinationRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var sourceEnd = new CellAddress(sheet.Id, 1, 2);
        var first = Cell.FromValue(new TextValue("left"));
        var second = Cell.FromValue(new TextValue("right"));
        sheet.SetCell(sourceStart, first);
        sheet.SetCell(sourceEnd, second);
        var destinationStart = new CellAddress(sheet.Id, 4, 4);
        var destinationEnd = new CellAddress(sheet.Id, 6, 5);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(sourceStart, sourceEnd),
            [(sourceStart, first.Clone()), (sourceEnd, second.Clone())],
            new GridRange(destinationStart, destinationEnd),
            PasteCellsMode.All,
            new PasteSpecialOptions(Transpose: true));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(4, 4).Should().Be(new TextValue("left"));
        sheet.GetValue(5, 4).Should().Be(new TextValue("right"));
        sheet.GetValue(6, 4).Should().Be(new TextValue("left"));
        sheet.GetValue(4, 5).Should().Be(new TextValue("left"));
        sheet.GetValue(5, 5).Should().Be(new TextValue("right"));
        sheet.GetValue(6, 5).Should().Be(new TextValue("left"));
    }

    [Fact]
    public void PasteCommandFactory_RejectsPasteRectanglePastWorksheetEdgeWithoutClamping()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var sourceEnd = new CellAddress(sheet.Id, 1, 2);
        var edge = new CellAddress(sheet.Id, 5, CellAddress.MaxCol);
        sheet.SetCell(sourceStart, Cell.FromValue(new TextValue("left")));
        sheet.SetCell(sourceEnd, Cell.FromValue(new TextValue("right")));
        sheet.SetCell(edge, Cell.FromValue(new TextValue("keep")));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(sourceStart, sourceEnd),
            [(sourceStart, sheet.GetCell(sourceStart)!.Clone()), (sourceEnd, sheet.GetCell(sourceEnd)!.Clone())],
            edge,
            PasteCellsMode.All,
            default);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("bounds");
        sheet.GetValue(edge).Should().Be(new TextValue("keep"));
    }

    [Fact]
    public void PasteCommandFactory_ExactEdgePasteAppliesAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var sourceEnd = new CellAddress(sheet.Id, 1, 2);
        var destinationStart = new CellAddress(sheet.Id, 5, CellAddress.MaxCol - 1);
        var destinationEnd = new CellAddress(sheet.Id, 5, CellAddress.MaxCol);
        sheet.SetCell(sourceStart, Cell.FromValue(new TextValue("left")));
        sheet.SetCell(sourceEnd, Cell.FromValue(new TextValue("right")));
        sheet.SetCell(destinationEnd, Cell.FromValue(new TextValue("old")));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(sourceStart, sourceEnd),
            [(sourceStart, sheet.GetCell(sourceStart)!.Clone()), (sourceEnd, sheet.GetCell(sourceEnd)!.Clone())],
            destinationStart,
            PasteCellsMode.All,
            default);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(destinationStart).Should().Be(new TextValue("left"));
        sheet.GetValue(destinationEnd).Should().Be(new TextValue("right"));

        command.Revert(ctx);

        sheet.GetCell(destinationStart).Should().BeNull();
        sheet.GetValue(destinationEnd).Should().Be(new TextValue("old"));
    }

    [Fact]
    public void PasteCommandFactory_RejectsDuplicateSourceCellsBeforeApplying()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(source, Cell.FromValue(new TextValue("copy")));
        sheet.SetCell(destination, Cell.FromValue(new TextValue("keep")));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sheet.GetCell(source)!.Clone()), (source, sheet.GetCell(source)!.Clone())],
            destination,
            PasteCellsMode.All,
            default);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("duplicate");
        sheet.GetValue(destination).Should().Be(new TextValue("keep"));
    }
}
