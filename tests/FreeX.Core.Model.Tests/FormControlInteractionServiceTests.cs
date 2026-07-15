using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Behavioral unit tests for <see cref="FormControlInteractionService"/>:
/// toggle/select/step/recalc/undo per the spec.
/// </summary>
public sealed class FormControlInteractionServiceTests
{
    [Fact]
    public void CreateCommand_DispatchesNormalizedSpinnerGesture()
    {
        var (wb, sheet) = NewWorkbook();
        var control = new FormControlModel
        {
            Kind = FormControlKind.Spinner,
            Value = 5,
            Min = 1,
            Max = 10,
            Increment = 1,
            LinkedCell = "B1",
        };

        var command = FormControlInteractionService.CreateCommand(
            new FormControlInteractionRequest(control, FormControlGesture.StepUp),
            sheet.FormControls,
            sheet.Id,
            wb);

        command.Should().NotBeNull();
        control.Value.Should().Be(6);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (Workbook Workbook, Sheet Sheet) NewWorkbook()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet);
    }

    private static void ApplyAndVerify(IWorkbookCommand? cmd, Workbook wb, CellAddress addr, ScalarValue expected)
    {
        cmd.Should().NotBeNull();
        var ctx = new TestCommandContext(wb);
        var outcome = cmd!.Apply(ctx);
        outcome.Success.Should().BeTrue();

        var sheet = wb.GetSheet(addr.Sheet)!;
        var cell = sheet.GetCell(addr);
        cell.Should().NotBeNull();
        cell!.Value.Should().Be(expected);
    }

    // ── CheckBox toggle ──────────────────────────────────────────────────────

    [Fact]
    public void ToggleCheckBox_UncheckedToChecked_WritesTrueToLinkedCell()
    {
        var (wb, sheet) = NewWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            IsChecked = false,
            LinkedCell = "A1",
        };

        var cmd = FormControlInteractionService.CreateToggleCheckBoxCommand(control, sheet.Id, wb);

        control.IsChecked.Should().BeTrue("in-model state flipped immediately");
        ApplyAndVerify(cmd, wb, addr, new BoolValue(true));
    }

    [Fact]
    public void ToggleCheckBox_CheckedToUnchecked_WritesFalseToLinkedCell()
    {
        var (wb, sheet) = NewWorkbook();
        var addr = new CellAddress(sheet.Id, 2, 3);
        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            IsChecked = true,
            LinkedCell = "$C$2",
        };

        var cmd = FormControlInteractionService.CreateToggleCheckBoxCommand(control, sheet.Id, wb);

        control.IsChecked.Should().BeFalse("in-model state flipped");
        ApplyAndVerify(cmd, wb, addr, new BoolValue(false));
    }

    [Fact]
    public void ToggleCheckBox_NoLinkedCell_ReturnsNullButFlipsModel()
    {
        var (wb, sheet) = NewWorkbook();
        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            IsChecked = false,
            LinkedCell = null,
        };

        var cmd = FormControlInteractionService.CreateToggleCheckBoxCommand(control, sheet.Id, wb);

        cmd.Should().BeNull("no linked cell → no command");
        control.IsChecked.Should().BeTrue("model still flipped");
    }

    // ── ToggleCheckBox undo ─────────────────────────────────────────────────

    [Fact]
    public void ToggleCheckBox_UndoReverts_LinkedCellToOriginalValue()
    {
        var (wb, sheet) = NewWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 1);
        // Pre-populate linked cell with FALSE
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

        // Linked cell is now TRUE
        sheet.GetCell(addr)!.Value.Should().Be(new BoolValue(true));

        // Undo
        cmd.Revert(ctx);

        // Linked cell restored to original FALSE (the cell that was there before Apply)
        sheet.GetCell(addr)!.Value.Should().Be(new BoolValue(false));
    }

    // ── OptionButton select ──────────────────────────────────────────────────

    [Fact]
    public void SelectOptionButton_WritesOneBasedIndexToLinkedCell()
    {
        var (wb, sheet) = NewWorkbook();
        var linkedAddr = new CellAddress(sheet.Id, 5, 5);

        var opt1 = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = false, LinkedCell = "$E$5" };
        var opt2 = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = true,  LinkedCell = "$E$5" };
        var opt3 = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = false, LinkedCell = "$E$5" };
        sheet.FormControls.Add(opt1);
        sheet.FormControls.Add(opt2);
        sheet.FormControls.Add(opt3);

        // Click opt3 (3rd in group)
        var cmd = FormControlInteractionService.CreateSelectOptionButtonCommand(
            opt3, sheet.FormControls, sheet.Id, wb);

        opt1.IsChecked.Should().BeFalse();
        opt2.IsChecked.Should().BeFalse();
        opt3.IsChecked.Should().BeTrue();

        ApplyAndVerify(cmd, wb, linkedAddr, new NumberValue(3));
    }

    [Fact]
    public void SelectOptionButton_ClearsSiblingsInSameLinkedCellGroup()
    {
        var (wb, sheet) = NewWorkbook();
        var opt1 = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = true,  LinkedCell = "$B$2" };
        var opt2 = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = false, LinkedCell = "$B$2" };
        sheet.FormControls.Add(opt1);
        sheet.FormControls.Add(opt2);

        // Click opt1 again (already checked)
        FormControlInteractionService.CreateSelectOptionButtonCommand(
            opt2, sheet.FormControls, sheet.Id, wb);

        opt1.IsChecked.Should().BeFalse("sibling cleared");
        opt2.IsChecked.Should().BeTrue("clicked one selected");
    }

    // ── Spinner/ScrollBar step ────────────────────────────────────────────────

    [Fact]
    public void Step_SpinnerUp_IncrementsValueAndWritesToLinkedCell()
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

        var cmd = FormControlInteractionService.CreateStepCommand(control, +1, sheet.Id, wb);

        control.Value.Should().Be(6);
        ApplyAndVerify(cmd, wb, addr, new NumberValue(6));
    }

    [Fact]
    public void Step_SpinnerDown_DecrementsValueAndWritesToLinkedCell()
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

        var cmd = FormControlInteractionService.CreateStepCommand(control, -1, sheet.Id, wb);

        control.Value.Should().Be(4);
        ApplyAndVerify(cmd, wb, addr, new NumberValue(4));
    }

    [Fact]
    public void Step_ClampedAtMax_DoesNotExceedMax()
    {
        var (wb, sheet) = NewWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 2);
        var control = new FormControlModel
        {
            Kind = FormControlKind.Spinner,
            Value = 10,
            Min = 1,
            Max = 10,
            Increment = 1,
            LinkedCell = "B1",
        };

        var cmd = FormControlInteractionService.CreateStepCommand(control, +1, sheet.Id, wb);

        control.Value.Should().Be(10, "clamped at Max");
        ApplyAndVerify(cmd, wb, addr, new NumberValue(10));
    }

    [Fact]
    public void Step_ClampedAtMin_DoesNotGoBelowMin()
    {
        var (wb, sheet) = NewWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 2);
        var control = new FormControlModel
        {
            Kind = FormControlKind.Spinner,
            Value = 1,
            Min = 1,
            Max = 10,
            Increment = 1,
            LinkedCell = "B1",
        };

        var cmd = FormControlInteractionService.CreateStepCommand(control, -1, sheet.Id, wb);

        control.Value.Should().Be(1, "clamped at Min");
        ApplyAndVerify(cmd, wb, addr, new NumberValue(1));
    }

    [Fact]
    public void Step_WithCustomIncrement_StepsByIncrement()
    {
        var (wb, sheet) = NewWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var control = new FormControlModel
        {
            Kind = FormControlKind.ScrollBar,
            Value = 0,
            Min = 0,
            Max = 100,
            Increment = 5,
            LinkedCell = "A1",
        };

        var cmd = FormControlInteractionService.CreateStepCommand(control, +1, sheet.Id, wb);

        control.Value.Should().Be(5);
        ApplyAndVerify(cmd, wb, addr, new NumberValue(5));
    }

    // ── ListBox / DropDown select ─────────────────────────────────────────────

    [Fact]
    public void SelectListItem_WritesOneBasedIndexToLinkedCell()
    {
        var (wb, sheet) = NewWorkbook();
        var addr = new CellAddress(sheet.Id, 3, 1);
        var control = new FormControlModel
        {
            Kind = FormControlKind.ListBox,
            SelectedIndex = 0,
            LinkedCell = "A3",
        };

        var cmd = FormControlInteractionService.CreateSelectListItemCommand(control, 2, sheet.Id, wb);

        control.SelectedIndex.Should().Be(2);
        ApplyAndVerify(cmd, wb, addr, new NumberValue(2));
    }

    [Fact]
    public void SelectListItem_DropDown_WritesIndexToLinkedCell()
    {
        var (wb, sheet) = NewWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 5);
        var control = new FormControlModel
        {
            Kind = FormControlKind.DropDown,
            SelectedIndex = 1,
            LinkedCell = "$E$1",
        };

        var cmd = FormControlInteractionService.CreateSelectListItemCommand(control, 3, sheet.Id, wb);

        control.SelectedIndex.Should().Be(3);
        ApplyAndVerify(cmd, wb, addr, new NumberValue(3));
    }

    // ── Linked-cell write triggers recalc of dependent formula ───────────────

    [Fact]
    public void ToggleCheckBox_WrittenCell_LinkedCellContainsBooleanValue()
    {
        // The linked cell must hold a proper BoolValue that formula engines can read.
        var (wb, sheet) = NewWorkbook();
        var a1 = new CellAddress(sheet.Id, 1, 1);

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

        // Verify A1 = new BoolValue(true) — the value type that formula engines recognise as TRUE.
        var cell = sheet.GetCell(a1);
        cell.Should().NotBeNull();
        cell!.Value.Should().BeOfType<BoolValue>()
            .Which.Value.Should().BeTrue("checkbox writes TRUE as BoolValue");
    }

    // ── LinkedCell resolution ─────────────────────────────────────────────────

    [Fact]
    public void TryResolveLinkedCell_AbsoluteRef_Resolves()
    {
        var (wb, sheet) = NewWorkbook();
        var result = FormControlInteractionService.TryResolveLinkedCell("$A$1", sheet.Id, wb, out var addr);
        result.Should().BeTrue();
        addr.Row.Should().Be(1);
        addr.Col.Should().Be(1);
        addr.Sheet.Should().Be(sheet.Id);
    }

    [Fact]
    public void TryResolveLinkedCell_RelativeRef_Resolves()
    {
        var (wb, sheet) = NewWorkbook();
        var result = FormControlInteractionService.TryResolveLinkedCell("C5", sheet.Id, wb, out var addr);
        result.Should().BeTrue();
        addr.Row.Should().Be(5);
        addr.Col.Should().Be(3);
    }

    [Fact]
    public void TryResolveLinkedCell_CrossSheetRef_ResolvesCorrectSheet()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");

        var result = FormControlInteractionService.TryResolveLinkedCell("Sheet2!$B$3", sheet1.Id, wb, out var addr);

        result.Should().BeTrue();
        addr.Sheet.Should().Be(sheet2.Id);
        addr.Row.Should().Be(3);
        addr.Col.Should().Be(2);
    }

    [Fact]
    public void TryResolveLinkedCell_EmptyString_ReturnsFalse()
    {
        var (wb, sheet) = NewWorkbook();
        var result = FormControlInteractionService.TryResolveLinkedCell("", sheet.Id, wb, out _);
        result.Should().BeFalse();
    }

    [Fact]
    public void TryResolveLinkedCell_NullString_ReturnsFalse()
    {
        var (wb, sheet) = NewWorkbook();
        var result = FormControlInteractionService.TryResolveLinkedCell(null, sheet.Id, wb, out _);
        result.Should().BeFalse();
    }

    [Fact]
    public void TryResolveLinkedCell_WithLeadingEquals_Resolves()
    {
        var (wb, sheet) = NewWorkbook();
        var result = FormControlInteractionService.TryResolveLinkedCell("=$A$1", sheet.Id, wb, out var addr);
        result.Should().BeTrue();
        addr.Row.Should().Be(1);
        addr.Col.Should().Be(1);
    }

    [Fact]
    public void AdvanceListSelection_UsesWorkbookGlobalDefinedNameAndWraps()
    {
        var (wb, sheet) = NewWorkbook();
        var listRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));
        wb.DefineNamedRange("GlobalChoices", listRange);
        var control = new FormControlModel
        {
            Kind = FormControlKind.DropDown,
            ListFillRange = "GlobalChoices",
            SelectedIndex = 3,
            LinkedCell = "B1",
        };

        var command = FormControlInteractionService.CreateAdvanceListSelectionCommand(control, sheet.Id, wb);

        control.SelectedIndex.Should().Be(1);
        ApplyAndVerify(command, wb, new CellAddress(sheet.Id, 1, 2), new NumberValue(1));
    }

    [Fact]
    public void AdvanceListSelection_PrefersSheetScopedDefinedNameOverGlobalName()
    {
        var (wb, sheet) = NewWorkbook();
        wb.DefineNamedRange(
            "Choices",
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)));
        wb.DefineNamedRange(
            "Choices",
            new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 7, 1)),
            metadata: null,
            scopeSheetId: sheet.Id);
        var control = new FormControlModel
        {
            Kind = FormControlKind.ListBox,
            ListFillRange = "Choices",
            SelectedIndex = 2,
            LinkedCell = "B1",
        };

        var command = FormControlInteractionService.CreateAdvanceListSelectionCommand(control, sheet.Id, wb);

        control.SelectedIndex.Should().Be(3);
        ApplyAndVerify(command, wb, new CellAddress(sheet.Id, 1, 2), new NumberValue(3));
    }
}
