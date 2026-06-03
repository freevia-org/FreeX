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
    public void PasteCommandFactory_RejectsPasteRectanglePastWorksheetEdgeWithoutClamping()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
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
        var ctx = new SimpleCtx(wb);
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
        var ctx = new SimpleCtx(wb);
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
