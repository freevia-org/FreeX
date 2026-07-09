using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

// R16-undo-redo-composite-1: AddSheetCommand.Apply used to call Workbook.AddSheet
// unconditionally, which mints a brand-new SheetId on EVERY Apply — including redo. Only
// _addedSheetId (used by Revert) tracked the id, so after undo+redo the re-created sheet got a
// DIFFERENT id than the one produced by the first Apply. Any later command already sitting in
// the redo stack that captured the ORIGINAL id (e.g. a rename targeting that sheet) would then
// fail to find its sheet on its own redo. Fixed by re-creating the sheet with the SAME id
// (captured on the first Apply) on every subsequent Apply.
public class R16_sheet_redo_id_Tests
{
    [Fact]
    public void AddSheetCommand_ApplyTwice_ReCreatesSheetWithSameCapturedId()
    {
        var wb = new Workbook("test");
        wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var command = new AddSheetCommand("Sheet2");

        command.Apply(ctx).Success.Should().BeTrue();
        var firstId = wb.Sheets[1].Id;

        // Simulate undo then redo (Apply called again on the SAME command instance, which is
        // exactly what CommandBus.Redo does).
        command.Revert(ctx);
        wb.Sheets.Should().HaveCount(1);

        command.Apply(ctx).Success.Should().BeTrue();
        wb.Sheets.Should().HaveCount(2);
        wb.Sheets[1].Id.Should().Be(firstId, "redo must re-mint the sheet with the SAME id captured on the first Apply");
    }

    [Fact]
    public void AddSheetCommand_UndoTwiceRedoTwice_RecreatedSheetKeepsIdAndLaterRedoStillTargetsIt()
    {
        // Add a sheet, capture its id; do an edit on it (rename); undo twice then redo twice ->
        // the re-created sheet has the SAME SheetId, and the redo of the edit that targets it
        // (by that captured id) succeeds.
        var wb = new Workbook("test");
        wb.AddSheet("Sheet1");
        var bus = new CommandBus(_ => new TestCommandContext(wb));

        bus.Execute(wb.Id, new AddSheetCommand("Sheet2")).Success.Should().BeTrue();
        var createdSheet = wb.Sheets[1];
        var originalId = createdSheet.Id;

        bus.Execute(wb.Id, new RenameSheetCommand(originalId, "Renamed")).Success.Should().BeTrue();
        wb.Sheets[1].Name.Should().Be("Renamed");

        // Undo the rename, then undo the add.
        bus.Undo(wb.Id).Success.Should().BeTrue();
        bus.Undo(wb.Id).Success.Should().BeTrue();
        wb.Sheets.Should().HaveCount(1);

        // Redo the add.
        var redoAddOutcome = bus.Redo(wb.Id);
        redoAddOutcome.Success.Should().BeTrue();
        wb.Sheets.Should().HaveCount(2);
        wb.Sheets[1].Id.Should().Be(originalId, "redo must re-create the sheet with the SAME id captured on the first Apply");

        // Redo the rename — this command captured the ORIGINAL sheet id at construction time,
        // so it only succeeds if the re-created sheet from the previous redo kept that same id.
        var redoRenameOutcome = bus.Redo(wb.Id);
        redoRenameOutcome.Success.Should().BeTrue("the rename command's redo must still find the sheet by its original captured id");
        wb.Sheets[1].Name.Should().Be("Renamed");
    }
}
