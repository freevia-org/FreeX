using FluentAssertions;
using Freexcel.Core.Calc;
using Freexcel.Core.Commands;
using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.Integration.Tests;

public class UndoRedoTests
{
    private static (Workbook Wb, Sheet Sheet, CommandBus Bus)
        CreateHarness()
    {
        var wb    = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var bus   = new CommandBus(_ => new SimpleContext(wb));
        return (wb, sheet, bus);
    }

    [Fact]
    public void Undo_EditCell_RestoresPreviousValue()
    {
        var (wb, sheet, bus) = CreateHarness();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new NumberValue(10));

        bus.Execute(wb.Id, new EditCellsCommand(sheet.Id, addr, new NumberValue(99)));
        sheet.GetValue(addr).Should().Be(new NumberValue(99));

        bus.Undo(wb.Id);
        sheet.GetValue(addr).Should().Be(new NumberValue(10), "undo must restore the original value");
    }

    [Fact]
    public void Redo_AfterUndo_ReappliesEdit()
    {
        var (wb, sheet, bus) = CreateHarness();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new NumberValue(10));

        bus.Execute(wb.Id, new EditCellsCommand(sheet.Id, addr, new NumberValue(99)));
        bus.Undo(wb.Id);
        bus.Redo(wb.Id);

        sheet.GetValue(addr).Should().Be(new NumberValue(99), "redo must re-apply the edit");
    }

    [Fact]
    public void UndoRedo_EditCell_ReturnsAffectedCellForIncrementalRecalculation()
    {
        var (wb, sheet, bus) = CreateHarness();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new NumberValue(10));

        bus.Execute(wb.Id, new EditCellsCommand(sheet.Id, addr, new NumberValue(99)));

        bus.Undo(wb.Id).AffectedCells.Should().Equal(addr);
        bus.Redo(wb.Id).AffectedCells.Should().Equal(addr);
    }

    [Fact]
    public void Undo_StyleCommand_RemovesAppliedStyle()
    {
        var (wb, sheet, bus) = CreateHarness();
        var addr  = new CellAddress(sheet.Id, 1, 1);
        var range = new GridRange(addr, addr);

        bus.Execute(wb.Id, new ApplyStyleCommand(sheet.Id, range, new StyleDiff(Bold: true)));
        // After styling an empty cell, it should have a style-only entry
        sheet.GetStyleOnly(addr.Row, addr.Col).Should().NotBeNull();

        bus.Undo(wb.Id);
        sheet.GetStyleOnly(addr.Row, addr.Col).Should().BeNull(
            "undo of a style-only cell must remove the style-only entry");
    }

    [Fact]
    public void Undo_MultipleEdits_RestoresInReverseOrder()
    {
        var (wb, sheet, bus) = CreateHarness();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new NumberValue(1));

        bus.Execute(wb.Id, new EditCellsCommand(sheet.Id, addr, new NumberValue(2)));
        bus.Execute(wb.Id, new EditCellsCommand(sheet.Id, addr, new NumberValue(3)));

        sheet.GetValue(addr).Should().Be(new NumberValue(3));

        bus.Undo(wb.Id);
        sheet.GetValue(addr).Should().Be(new NumberValue(2), "first undo should restore value from second edit");

        bus.Undo(wb.Id);
        sheet.GetValue(addr).Should().Be(new NumberValue(1), "second undo should restore the original value");
    }

    [Fact]
    public void CanUndo_ReturnsFalse_WhenNothingExecuted()
    {
        var (wb, _, bus) = CreateHarness();
        bus.CanUndo(wb.Id).Should().BeFalse();
    }

    [Fact]
    public void CanRedo_ReturnsFalse_AfterNewCommandClearsStack()
    {
        var (wb, sheet, bus) = CreateHarness();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new NumberValue(10));

        bus.Execute(wb.Id, new EditCellsCommand(sheet.Id, addr, new NumberValue(20)));
        bus.Undo(wb.Id);

        // A new command should clear the redo stack
        bus.Execute(wb.Id, new EditCellsCommand(sheet.Id, addr, new NumberValue(30)));
        bus.CanRedo(wb.Id).Should().BeFalse("a new command must clear the redo stack");
    }

    private sealed class SimpleContext(Workbook wb) : ICommandContext
    {
        public Workbook Workbook => wb;
        public Sheet GetSheet(SheetId id) => wb.GetSheet(id) ?? throw new InvalidOperationException($"Sheet {id} not found");
    }
}
