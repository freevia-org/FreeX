using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R139-workbook-protection (freex-workbook-protection lens, finding F1): the command layer
/// previously only checked <see cref="Workbook.IsStructureProtected"/> before renaming, deleting,
/// moving, hiding, or duplicating a sheet's own tab -- an individually-protected sheet (Review >
/// Protect Sheet, no workbook structure protection) could still be silently renamed/deleted/moved/
/// hidden/duplicated. These tests exercise the same command-layer entry points a real user reaches
/// (RenameSheetCommand/RemoveSheetCommand/MoveSheetCommand/MoveSheetsCommand/SetSheetHiddenCommand/
/// DuplicateSheetCommand.Apply -- the same commands WorkbookSession's RenameActiveSheet/
/// DeleteSelectedSheets/MoveActiveSheetTo/HideActiveSheet/DuplicateSelectedSheets funnel into from
/// both the WPF and Avalonia shells' ribbon, tab context menu, and drag-to-reorder entry points).
///
/// Each "Rejects" test fails before the SheetCommands.cs/DuplicateSheetCommand.cs/
/// MoveSheetsCommand.cs fix (the target commands returned Success=true and mutated the protected
/// sheet) and passes after. Each "StillWorks"/"Sibling" test proves the fix did not collaterally
/// block legitimate operations on an unprotected sheet in the same workbook, or (for Unhide)
/// deliberately did not over-broaden the guard.
/// </summary>
public class R139_SheetOwnProtectionBlocksTabOperationsTests
{
    private static (Workbook wb, Sheet protectedSheet, Sheet otherSheet) MakeWorkbookWithOneProtectedSheet()
    {
        var wb = new Workbook("test");
        var protectedSheet = wb.AddSheet("Sheet1");
        var otherSheet = wb.AddSheet("Sheet2");
        protectedSheet.IsProtected = true;
        return (wb, protectedSheet, otherSheet);
    }

    // ---------------------------------------------------------------------------
    // RenameSheetCommand
    // ---------------------------------------------------------------------------

    [Fact]
    public void RenameSheetCommand_RejectsIndividuallyProtectedSheet()
    {
        var (wb, protectedSheet, _) = MakeWorkbookWithOneProtectedSheet();
        var originalName = protectedSheet.Name;
        var command = new RenameSheetCommand(protectedSheet.Id, "Renamed");

        var outcome = command.Apply(new TestCommandContext(wb));

        outcome.Success.Should().BeFalse(because: "an individually-protected sheet must refuse Rename of its own tab");
        outcome.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        protectedSheet.Name.Should().Be(originalName, because: "a rejected rename must not mutate the sheet");
    }

    [Fact]
    public void RenameSheetCommand_StillWorksOnUnprotectedSheetInSameWorkbook()
    {
        var (wb, _, otherSheet) = MakeWorkbookWithOneProtectedSheet();
        var command = new RenameSheetCommand(otherSheet.Id, "Renamed");

        var outcome = command.Apply(new TestCommandContext(wb));

        outcome.Success.Should().BeTrue(because: "renaming an unprotected sheet must still work even when another sheet in the workbook is protected");
        otherSheet.Name.Should().Be("Renamed");
    }

    // ---------------------------------------------------------------------------
    // RemoveSheetCommand
    // ---------------------------------------------------------------------------

    [Fact]
    public void RemoveSheetCommand_RejectsIndividuallyProtectedSheet()
    {
        var (wb, protectedSheet, _) = MakeWorkbookWithOneProtectedSheet();
        var command = new RemoveSheetCommand(protectedSheet.Id);

        var outcome = command.Apply(new TestCommandContext(wb));

        outcome.Success.Should().BeFalse(because: "an individually-protected sheet must refuse Delete of its own tab");
        outcome.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        wb.Sheets.Should().Contain(s => s.Id == protectedSheet.Id, because: "a rejected delete must not remove the sheet");
    }

    [Fact]
    public void RemoveSheetCommand_StillWorksOnUnprotectedSheetInSameWorkbook()
    {
        var (wb, _, otherSheet) = MakeWorkbookWithOneProtectedSheet();
        var command = new RemoveSheetCommand(otherSheet.Id);

        var outcome = command.Apply(new TestCommandContext(wb));

        outcome.Success.Should().BeTrue(because: "deleting an unprotected sheet must still work even when another sheet in the workbook is protected");
        wb.Sheets.Should().NotContain(s => s.Id == otherSheet.Id);
    }

    // ---------------------------------------------------------------------------
    // MoveSheetCommand (single-sheet move, drag-to-reorder + Move-or-Copy path)
    // ---------------------------------------------------------------------------

    [Fact]
    public void MoveSheetCommand_RejectsIndividuallyProtectedSheet()
    {
        var (wb, protectedSheet, _) = MakeWorkbookWithOneProtectedSheet();
        protectedSheet.Should().BeSameAs(wb.Sheets[0]);
        var command = new MoveSheetCommand(fromIndex: 0, toIndex: 1);

        var outcome = command.Apply(new TestCommandContext(wb));

        outcome.Success.Should().BeFalse(because: "an individually-protected sheet must refuse Move of its own tab");
        outcome.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        wb.Sheets[0].Id.Should().Be(protectedSheet.Id, because: "a rejected move must not reorder the sheets");
    }

    [Fact]
    public void MoveSheetCommand_StillWorksOnUnprotectedSheetInSameWorkbook()
    {
        var (wb, _, otherSheet) = MakeWorkbookWithOneProtectedSheet();
        otherSheet.Should().BeSameAs(wb.Sheets[1]);
        var command = new MoveSheetCommand(fromIndex: 1, toIndex: 0);

        var outcome = command.Apply(new TestCommandContext(wb));

        outcome.Success.Should().BeTrue(because: "moving an unprotected sheet must still work even when another sheet in the workbook is protected");
        wb.Sheets[0].Id.Should().Be(otherSheet.Id);
    }

    [Fact]
    public void MoveSheetCommand_SamePositionDragIsNoOpEvenWhenProtected()
    {
        // A no-op drag (dropping a tab back on its own position) must not be rejected just because
        // the tab happens to be protected -- nothing would actually change.
        var (wb, protectedSheet, _) = MakeWorkbookWithOneProtectedSheet();
        var command = new MoveSheetCommand(fromIndex: 0, toIndex: 0);

        var outcome = command.Apply(new TestCommandContext(wb));

        outcome.Success.Should().BeTrue();
        outcome.IsNoOp.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // MoveSheetsCommand (grouped multi-sheet move -- does NOT delegate to MoveSheetCommand)
    // ---------------------------------------------------------------------------

    [Fact]
    public void MoveSheetsCommand_RejectsWhenAnySelectedSheetIsProtected()
    {
        var (wb, protectedSheet, otherSheet) = MakeWorkbookWithOneProtectedSheet();
        var command = new MoveSheetsCommand([protectedSheet.Id, otherSheet.Id], insertBeforeIndex: 0);

        var outcome = command.Apply(new TestCommandContext(wb));

        outcome.Success.Should().BeFalse(because: "a grouped move that includes an individually-protected sheet must be refused entirely");
        outcome.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void MoveSheetsCommand_StillWorksWhenOnlyUnprotectedSheetsAreSelected()
    {
        var (wb, _, otherSheet) = MakeWorkbookWithOneProtectedSheet();
        var thirdSheet = wb.AddSheet("Sheet3");
        var command = new MoveSheetsCommand([otherSheet.Id, thirdSheet.Id], insertBeforeIndex: 0);

        var outcome = command.Apply(new TestCommandContext(wb));

        outcome.Success.Should().BeTrue(because: "moving a group of unprotected sheets must still work even when another sheet in the workbook is protected");
    }

    // ---------------------------------------------------------------------------
    // SetSheetHiddenCommand
    // ---------------------------------------------------------------------------

    [Fact]
    public void SetSheetHiddenCommand_Hide_RejectsIndividuallyProtectedSheet()
    {
        var (wb, protectedSheet, _) = MakeWorkbookWithOneProtectedSheet();
        var command = new SetSheetHiddenCommand(protectedSheet.Id, hidden: true);

        var outcome = command.Apply(new TestCommandContext(wb));

        outcome.Success.Should().BeFalse(because: "an individually-protected sheet must refuse Hide of its own tab");
        outcome.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        protectedSheet.IsHidden.Should().BeFalse(because: "a rejected hide must not mutate the sheet");
    }

    [Fact]
    public void SetSheetHiddenCommand_Hide_StillWorksOnUnprotectedSheetInSameWorkbook()
    {
        var (wb, _, otherSheet) = MakeWorkbookWithOneProtectedSheet();
        var command = new SetSheetHiddenCommand(otherSheet.Id, hidden: true);

        var outcome = command.Apply(new TestCommandContext(wb));

        outcome.Success.Should().BeTrue(because: "hiding an unprotected sheet must still work even when another sheet in the workbook is protected");
        otherSheet.IsHidden.Should().BeTrue();
    }

    [Fact]
    public void SetSheetHiddenCommand_Unhide_IsNotBlockedByTheTargetSheetsOwnProtection()
    {
        // Deliberately narrow: Unhide reveals an already-hidden sheet without altering its
        // protection state, and real Excel's Unhide dialog operates at the workbook level, not
        // gated by the individual sheet's own protection -- unlike Hide, this must remain allowed.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        wb.AddSheet("Sheet2"); // second visible sheet so Hide below doesn't hit "only visible sheet"
        new SetSheetHiddenCommand(sheet.Id, hidden: true).Apply(new TestCommandContext(wb));
        sheet.IsProtected = true;
        var command = new SetSheetHiddenCommand(sheet.Id, hidden: false);

        var outcome = command.Apply(new TestCommandContext(wb));

        outcome.Success.Should().BeTrue(because: "Unhide must not be blocked by the target sheet's own protection");
        sheet.IsHidden.Should().BeFalse();
    }

    // ---------------------------------------------------------------------------
    // DuplicateSheetCommand
    // ---------------------------------------------------------------------------

    [Fact]
    public void DuplicateSheetCommand_RejectsIndividuallyProtectedSheet()
    {
        var (wb, protectedSheet, _) = MakeWorkbookWithOneProtectedSheet();
        var sheetCountBefore = wb.Sheets.Count;
        var command = new DuplicateSheetCommand(protectedSheet.Id, "Copy");

        var outcome = command.Apply(new TestCommandContext(wb));

        outcome.Success.Should().BeFalse(because: "an individually-protected sheet must refuse Duplicate of its own tab");
        outcome.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        wb.Sheets.Count.Should().Be(sheetCountBefore, because: "a rejected duplicate must not add a copy");
    }

    [Fact]
    public void DuplicateSheetCommand_StillWorksOnUnprotectedSheetInSameWorkbook()
    {
        var (wb, _, otherSheet) = MakeWorkbookWithOneProtectedSheet();
        var sheetCountBefore = wb.Sheets.Count;
        var command = new DuplicateSheetCommand(otherSheet.Id, "Copy");

        var outcome = command.Apply(new TestCommandContext(wb));

        outcome.Success.Should().BeTrue(because: "duplicating an unprotected sheet must still work even when another sheet in the workbook is protected");
        wb.Sheets.Count.Should().Be(sheetCountBefore + 1);
    }
}
