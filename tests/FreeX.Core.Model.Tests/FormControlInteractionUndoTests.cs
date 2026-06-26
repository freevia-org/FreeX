using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Tests for NN2 (undo restores control state), NN3 (list index clamp), and
/// NN4 (spinner reads linked cell value as step base).
/// </summary>
public sealed class FormControlInteractionUndoTests
{
    private static (Workbook Workbook, Sheet Sheet) NewWorkbook()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet);
    }

    // ── NN2: undo restores IsChecked ──────────────────────────────────────

    [Fact]
    public void ToggleCheckBox_Undo_RestoresIsCheckedAndCell()
    {
        var (wb, sheet) = NewWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 1);
        // Pre-set linked cell to FALSE.
        sheet.SetCell(addr, Cell.FromValue(new BoolValue(false)));

        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            IsChecked = false,
            LinkedCell = "A1",
        };

        var cmd = FormControlInteractionService.CreateToggleCheckBoxCommand(control, sheet.Id, wb);
        cmd.Should().NotBeNull();

        var ctx = new TestCommandContext(wb);
        cmd!.Apply(ctx);

        control.IsChecked.Should().BeTrue("Apply flips IsChecked to true");
        sheet.GetCell(addr)!.Value.Should().Be(new BoolValue(true));

        // Undo
        cmd.Revert(ctx);

        control.IsChecked.Should().BeFalse("undo restores IsChecked to its prior false state");
        sheet.GetCell(addr)!.Value.Should().Be(new BoolValue(false), "undo also restores linked cell");
    }

    [Fact]
    public void ToggleCheckBox_UndoThenRedo_WorksCorrectly()
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

        var cmd = FormControlInteractionService.CreateToggleCheckBoxCommand(control, sheet.Id, wb);
        cmd.Should().NotBeNull();

        var ctx = new TestCommandContext(wb);
        cmd!.Apply(ctx);
        cmd.Revert(ctx);

        // Redo (Apply again)
        cmd.Apply(ctx);

        control.IsChecked.Should().BeTrue("redo re-applies IsChecked = true");
        sheet.GetCell(addr)!.Value.Should().Be(new BoolValue(true));
    }

    // ── NN2: undo restores spinner Value ──────────────────────────────────

    [Fact]
    public void StepSpinner_Undo_RestoresValueAndCell()
    {
        var (wb, sheet) = NewWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(addr, Cell.FromValue(new NumberValue(5)));

        var control = new FormControlModel
        {
            Kind = FormControlKind.Spinner,
            Value = 5,
            Min = 1,
            Max = 10,
            Increment = 1,
            LinkedCell = "B1",
        };

        var cmd = FormControlInteractionService.CreateStepCommand(control, +1, sheet.Id, wb);
        cmd.Should().NotBeNull();

        var ctx = new TestCommandContext(wb);
        cmd!.Apply(ctx);
        control.Value.Should().Be(6);
        sheet.GetCell(addr)!.Value.Should().Be(new NumberValue(6));

        // Undo
        cmd.Revert(ctx);

        control.Value.Should().Be(5, "undo restores Value to 5");
        sheet.GetCell(addr)!.Value.Should().Be(new NumberValue(5), "undo restores linked cell to 5");
    }

    // ── NN2: undo restores listbox SelectedIndex ──────────────────────────

    [Fact]
    public void SelectListItem_Undo_RestoresSelectedIndexAndCell()
    {
        var (wb, sheet) = NewWorkbook();

        // Populate 3 items in B1:B3 (ListFillRange).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new TextValue("A")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new TextValue("B")));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromValue(new TextValue("C")));

        // Linked cell is A5 — separate from the list data.
        var addr = new CellAddress(sheet.Id, 5, 1);
        sheet.SetCell(addr, Cell.FromValue(new NumberValue(1)));

        var control = new FormControlModel
        {
            Kind = FormControlKind.ListBox,
            SelectedIndex = 1,
            LinkedCell = "A5",
            ListFillRange = "B1:B3",
        };

        var cmd = FormControlInteractionService.CreateSelectListItemCommand(control, 2, sheet.Id, wb);
        cmd.Should().NotBeNull();

        var ctx = new TestCommandContext(wb);
        cmd!.Apply(ctx);
        control.SelectedIndex.Should().Be(2);
        sheet.GetCell(addr)!.Value.Should().Be(new NumberValue(2));

        // Undo
        cmd.Revert(ctx);

        control.SelectedIndex.Should().Be(1, "undo restores SelectedIndex to 1");
        sheet.GetCell(addr)!.Value.Should().Be(new NumberValue(1), "undo restores linked cell to 1");
    }

    // ── NN3: list index out-of-range click is ignored ─────────────────────

    [Fact]
    public void SelectListItem_IndexBeyondItemCount_ReturnsNull()
    {
        var (wb, sheet) = NewWorkbook();
        // ListBox with 3 items in A1:A3.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("A")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("B")));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new TextValue("C")));

        var control = new FormControlModel
        {
            Kind = FormControlKind.ListBox,
            SelectedIndex = 1,
            LinkedCell = "$B$1",
            ListFillRange = "$A$1:$A$3",
        };

        // Click in row 6 of an 8-row-tall anchor → would yield index 6 (beyond 3 items).
        var cmd = FormControlInteractionService.CreateSelectListItemCommand(control, 6, sheet.Id, wb);

        cmd.Should().BeNull("clicking below last item (index 6 > 3 items) returns null — no write");
        // SelectedIndex is NOT mutated when the command is null (no-op).
        control.SelectedIndex.Should().Be(1, "model is NOT mutated when click is out-of-range");
    }

    [Fact]
    public void SelectListItem_IndexWithinRange_WritesCorrectly()
    {
        var (wb, sheet) = NewWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("A")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("B")));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new TextValue("C")));

        var control = new FormControlModel
        {
            Kind = FormControlKind.ListBox,
            SelectedIndex = 1,
            LinkedCell = "$B$1",
            ListFillRange = "$A$1:$A$3",
        };

        var cmd = FormControlInteractionService.CreateSelectListItemCommand(control, 3, sheet.Id, wb);

        cmd.Should().NotBeNull("index 3 is within range [1, 3]");
        var ctx = new TestCommandContext(wb);
        var outcome = cmd!.Apply(ctx);
        outcome.Success.Should().BeTrue();
        var addr = new CellAddress(sheet.Id, 1, 2);
        sheet.GetCell(addr)!.Value.Should().Be(new NumberValue(3));
    }

    // ── NN4: spinner reads linked cell as step base ───────────────────────

    [Fact]
    public void StepSpinner_ReadsLinkedCellAsBase_NotStaleModelValue()
    {
        var (wb, sheet) = NewWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 1);

        // Set linked cell to 20 externally (model.Value is stale at 5).
        sheet.SetCell(addr, Cell.FromValue(new NumberValue(20)));

        var control = new FormControlModel
        {
            Kind = FormControlKind.Spinner,
            Value = 5,          // stale — linked cell has 20
            Min = 0,
            Max = 100,
            Increment = 1,
            LinkedCell = "A1",
        };

        var cmd = FormControlInteractionService.CreateStepCommand(control, +1, sheet.Id, wb);
        cmd.Should().NotBeNull();

        var ctx = new TestCommandContext(wb);
        cmd!.Apply(ctx);

        // Should step from 20 (cell value), not from 5 (stale model value).
        control.Value.Should().Be(21, "step base is linked cell value (20), not stale model.Value (5)");
        sheet.GetCell(addr)!.Value.Should().Be(new NumberValue(21));
    }

    [Fact]
    public void StepSpinner_NoLinkedCell_FallsBackToModelValue()
    {
        var (wb, sheet) = NewWorkbook();
        var control = new FormControlModel
        {
            Kind = FormControlKind.Spinner,
            Value = 7,
            Min = 0,
            Max = 100,
            Increment = 1,
            LinkedCell = null,
        };

        // No linked cell → should fall back to model.Value = 7
        var cmd = FormControlInteractionService.CreateStepCommand(control, +1, sheet.Id, wb);

        // No command (no linked cell), but model should have been stepped.
        cmd.Should().BeNull("no linked cell → no command");
        control.Value.Should().Be(8, "model still stepped from 7 to 8 even without linked cell");
    }

    [Fact]
    public void StepSpinner_LinkedCellIsText_FallsBackToModelValue()
    {
        var (wb, sheet) = NewWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 1);

        // Linked cell contains text — non-numeric, so fall back to model.Value.
        sheet.SetCell(addr, Cell.FromValue(new TextValue("hello")));

        var control = new FormControlModel
        {
            Kind = FormControlKind.Spinner,
            Value = 3,
            Min = 0,
            Max = 10,
            Increment = 1,
            LinkedCell = "A1",
        };

        var cmd = FormControlInteractionService.CreateStepCommand(control, +1, sheet.Id, wb);
        cmd.Should().NotBeNull();

        var ctx = new TestCommandContext(wb);
        cmd!.Apply(ctx);

        // Falls back to model.Value = 3, step to 4.
        control.Value.Should().Be(4, "non-numeric cell → fall back to model.Value (3) + 1 = 4");
    }
}
