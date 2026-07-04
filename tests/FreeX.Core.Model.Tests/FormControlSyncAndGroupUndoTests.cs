using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for the I-form-controls review group:
/// <list type="bullet">
///   <item>H14 — editing a linked cell directly (no control click) must still update the control's
///       IsChecked/Value/SelectedIndex the next time <see cref="FormControlInteractionService.SyncControlsFromLinkedCells"/>
///       runs (the shared cell-to-control refresh hook both shells call on render).</item>
///   <item>H15 — undoing an option-button group selection must restore the WHOLE group's IsChecked
///       state, not just the clicked button's own prior value.</item>
///   <item>H48 — unlinked option-button groups (no LinkedCell) must clear only within their own
///       GroupBox, or the sheet-level default group when the clicked button is anchored in no
///       GroupBox at all; independent GroupBox'd groups must never cross-clear each other.</item>
/// </list>
/// </summary>
public sealed class FormControlSyncAndGroupUndoTests
{
    private static (Workbook Workbook, Sheet Sheet) NewWorkbook()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet);
    }

    private static GridRange Anchor(SheetId sheetId, uint fromRow, uint fromCol, uint toRow, uint toCol) =>
        new(new CellAddress(sheetId, fromRow, fromCol), new CellAddress(sheetId, toRow, toCol));

    // ── H14: cell edit -> control sync ────────────────────────────────────────

    [Fact]
    public void SyncControlsFromLinkedCells_CheckBox_ReflectsDirectCellEdit()
    {
        var (wb, sheet) = NewWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, Cell.FromValue(new BoolValue(false)));

        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            IsChecked = false,
            LinkedCell = "A1",
        };
        sheet.FormControls.Add(control);

        // User types TRUE directly into A1 — never clicks the checkbox.
        sheet.SetCell(addr, Cell.FromValue(new BoolValue(true)));

        FormControlInteractionService.SyncControlsFromLinkedCells(sheet, wb);

        control.IsChecked.Should().BeTrue("the checkbox must mirror its linked cell's live value, matching Excel");
    }

    [Fact]
    public void SyncControlsFromLinkedCells_CheckBox_RevertsWhenCellEditedBack()
    {
        var (wb, sheet) = NewWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            IsChecked = true,
            LinkedCell = "A1",
        };
        sheet.FormControls.Add(control);
        sheet.SetCell(addr, Cell.FromValue(new BoolValue(false)));

        FormControlInteractionService.SyncControlsFromLinkedCells(sheet, wb);

        control.IsChecked.Should().BeFalse("cell now reads FALSE, so the checkbox must uncheck");
    }

    [Fact]
    public void SyncControlsFromLinkedCells_Spinner_ReflectsDirectCellEdit()
    {
        var (wb, sheet) = NewWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 2);
        var control = new FormControlModel
        {
            Kind = FormControlKind.Spinner,
            Value = 5,
            Min = 1,
            Max = 10,
            Increment = 1,
            LinkedCell = "B1",
        };
        sheet.FormControls.Add(control);

        // Formula/direct edit sets B1 to 8 without ever touching the spinner.
        sheet.SetCell(addr, Cell.FromValue(new NumberValue(8)));

        FormControlInteractionService.SyncControlsFromLinkedCells(sheet, wb);

        control.Value.Should().Be(8, "the spinner must mirror its linked cell's live value");
    }

    [Fact]
    public void SyncControlsFromLinkedCells_DropDown_ReflectsDirectCellEdit()
    {
        var (wb, sheet) = NewWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 5);
        var control = new FormControlModel
        {
            Kind = FormControlKind.DropDown,
            SelectedIndex = 1,
            LinkedCell = "$E$1",
        };
        sheet.FormControls.Add(control);
        sheet.SetCell(addr, Cell.FromValue(new NumberValue(3)));

        FormControlInteractionService.SyncControlsFromLinkedCells(sheet, wb);

        control.SelectedIndex.Should().Be(3, "the dropdown must mirror its linked cell's live selection index");
    }

    [Fact]
    public void SyncControlsFromLinkedCells_OptionButtonGroup_ReflectsDirectCellEdit()
    {
        var (wb, sheet) = NewWorkbook();
        var linkedAddr = new CellAddress(sheet.Id, 5, 5);

        var opt1 = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = true, LinkedCell = "$E$5" };
        var opt2 = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = false, LinkedCell = "$E$5" };
        var opt3 = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = false, LinkedCell = "$E$5" };
        sheet.FormControls.Add(opt1);
        sheet.FormControls.Add(opt2);
        sheet.FormControls.Add(opt3);

        // A formula recalculates E5 to 3 (selecting opt3) without any button ever being clicked.
        sheet.SetCell(linkedAddr, Cell.FromValue(new NumberValue(3)));

        FormControlInteractionService.SyncControlsFromLinkedCells(sheet, wb);

        opt1.IsChecked.Should().BeFalse();
        opt2.IsChecked.Should().BeFalse();
        opt3.IsChecked.Should().BeTrue("linked cell now selects the 3rd group member");
    }

    // ── H15: undo restores the WHOLE option-button group ──────────────────────

    [Fact]
    public void SelectOptionButton_Undo_RestoresWholeGroupNotJustClickedButton()
    {
        var (wb, sheet) = NewWorkbook();
        var linkedAddr = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(linkedAddr, Cell.FromValue(new NumberValue(1)));

        var opt1 = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = true, LinkedCell = "$B$2" };
        var opt2 = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = false, LinkedCell = "$B$2" };
        sheet.FormControls.Add(opt1);
        sheet.FormControls.Add(opt2);

        var cmd = FormControlInteractionService.CreateSelectOptionButtonCommand(
            opt2, sheet.FormControls, sheet.Id, wb);
        cmd.Should().NotBeNull();

        var ctx = new TestCommandContext(wb);
        var outcome = cmd!.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        opt1.IsChecked.Should().BeFalse("sibling cleared by Apply");
        opt2.IsChecked.Should().BeTrue("clicked button selected");
        sheet.GetCell(linkedAddr)!.Value.Should().Be(new NumberValue(2));

        // Undo.
        cmd.Revert(ctx);

        opt1.IsChecked.Should().BeTrue("undo must restore the sibling that was checked before the click");
        opt2.IsChecked.Should().BeFalse("undo must restore the clicked button to its own prior (unchecked) state");
        sheet.GetCell(linkedAddr)!.Value.Should().Be(new NumberValue(1), "undo also restores the linked cell");
    }

    [Fact]
    public void SelectOptionButton_UndoThenRedo_RestoresGroupBothWays()
    {
        var (wb, sheet) = NewWorkbook();
        var linkedAddr = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(linkedAddr, Cell.FromValue(new NumberValue(1)));

        var opt1 = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = true, LinkedCell = "$B$2" };
        var opt2 = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = false, LinkedCell = "$B$2" };
        sheet.FormControls.Add(opt1);
        sheet.FormControls.Add(opt2);

        var cmd = FormControlInteractionService.CreateSelectOptionButtonCommand(
            opt2, sheet.FormControls, sheet.Id, wb);
        cmd.Should().NotBeNull();

        var ctx = new TestCommandContext(wb);
        cmd!.Apply(ctx);
        cmd.Revert(ctx);

        // Redo.
        cmd.Apply(ctx);

        opt1.IsChecked.Should().BeFalse("redo re-clears the sibling");
        opt2.IsChecked.Should().BeTrue("redo re-selects the clicked button");
        sheet.GetCell(linkedAddr)!.Value.Should().Be(new NumberValue(2));
    }

    [Fact]
    public void SelectOptionButton_Undo_ThreeMemberGroup_RestoresEveryMember()
    {
        var (wb, sheet) = NewWorkbook();
        var linkedAddr = new CellAddress(sheet.Id, 5, 5);
        sheet.SetCell(linkedAddr, Cell.FromValue(new NumberValue(2)));

        var opt1 = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = false, LinkedCell = "$E$5" };
        var opt2 = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = true, LinkedCell = "$E$5" };
        var opt3 = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = false, LinkedCell = "$E$5" };
        sheet.FormControls.Add(opt1);
        sheet.FormControls.Add(opt2);
        sheet.FormControls.Add(opt3);

        var cmd = FormControlInteractionService.CreateSelectOptionButtonCommand(
            opt3, sheet.FormControls, sheet.Id, wb);
        var ctx = new TestCommandContext(wb);
        cmd!.Apply(ctx);

        cmd.Revert(ctx);

        opt1.IsChecked.Should().BeFalse();
        opt2.IsChecked.Should().BeTrue("opt2 was checked before the click and must come back on undo");
        opt3.IsChecked.Should().BeFalse();
    }

    // ── H48: unlinked option-button groups scoped per GroupBox ────────────────

    [Fact]
    public void SelectOptionButton_Unlinked_TwoIndependentGroupBoxes_DoNotCrossClear()
    {
        var (wb, sheet) = NewWorkbook();

        var groupBox1 = new FormControlModel { Kind = FormControlKind.GroupBox, Anchor = Anchor(sheet.Id, 1, 1, 5, 3) };
        var groupBox2 = new FormControlModel { Kind = FormControlKind.GroupBox, Anchor = Anchor(sheet.Id, 1, 5, 5, 7) };

        var optA1 = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = true, Anchor = Anchor(sheet.Id, 2, 1, 2, 2) };
        var optA2 = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = false, Anchor = Anchor(sheet.Id, 3, 1, 3, 2) };
        var optB1 = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = true, Anchor = Anchor(sheet.Id, 2, 5, 2, 6) };
        var optB2 = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = false, Anchor = Anchor(sheet.Id, 3, 5, 3, 6) };

        sheet.FormControls.Add(groupBox1);
        sheet.FormControls.Add(groupBox2);
        sheet.FormControls.Add(optA1);
        sheet.FormControls.Add(optA2);
        sheet.FormControls.Add(optB1);
        sheet.FormControls.Add(optB2);

        // Click optA2 — must only affect GroupBox1's members.
        var cmd = FormControlInteractionService.CreateSelectOptionButtonCommand(
            optA2, sheet.FormControls, sheet.Id, wb);

        cmd.Should().BeNull("no linked cell → no undoable command, matching the existing contract");
        optA1.IsChecked.Should().BeFalse("sibling within the SAME GroupBox is cleared");
        optA2.IsChecked.Should().BeTrue("clicked button selected");
        optB1.IsChecked.Should().BeTrue("an unrelated GroupBox's selection must be untouched");
        optB2.IsChecked.Should().BeFalse();
    }

    [Fact]
    public void SelectOptionButton_Unlinked_NoGroupBoxAtAll_FallsBackToSheetWideDefaultGroup()
    {
        var (wb, sheet) = NewWorkbook();

        // No GroupBox controls at all — Excel's sheet-level default group applies.
        var opt1 = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = true };
        var opt2 = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = false };
        sheet.FormControls.Add(opt1);
        sheet.FormControls.Add(opt2);

        FormControlInteractionService.CreateSelectOptionButtonCommand(
            opt2, sheet.FormControls, sheet.Id, wb);

        opt1.IsChecked.Should().BeFalse("sheet-wide default group clears the previously-checked sibling");
        opt2.IsChecked.Should().BeTrue();
    }

    [Fact]
    public void SelectOptionButton_Unlinked_ButtonOutsideAnyGroupBox_DoesNotClearButtonInsideGroupBox()
    {
        var (wb, sheet) = NewWorkbook();

        var groupBox = new FormControlModel { Kind = FormControlKind.GroupBox, Anchor = Anchor(sheet.Id, 1, 1, 5, 3) };
        var insideBox = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = true, Anchor = Anchor(sheet.Id, 2, 1, 2, 2) };
        var outsideBox = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = false, Anchor = Anchor(sheet.Id, 10, 1, 10, 2) };

        sheet.FormControls.Add(groupBox);
        sheet.FormControls.Add(insideBox);
        sheet.FormControls.Add(outsideBox);

        // Click the button that lives outside any GroupBox — the sheet-level default group applies
        // to it, which must not include (and must not clear) the button anchored inside the GroupBox.
        FormControlInteractionService.CreateSelectOptionButtonCommand(
            outsideBox, sheet.FormControls, sheet.Id, wb);

        insideBox.IsChecked.Should().BeTrue("a button inside an unrelated GroupBox must be untouched");
        outsideBox.IsChecked.Should().BeTrue("clicked button selected");
    }
}
