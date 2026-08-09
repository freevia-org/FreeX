using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for round-126 finding "Form-control click pre-check omits the array-split
/// guard": <see cref="FormControlInteractionService"/>'s private CanWriteLinkedCell pre-flight gate
/// only mirrored <see cref="EditCellsCommand.Apply"/>'s protection guard
/// (<see cref="CommandGuards.CanEditCell"/>), never its second, unconditional guard --
/// <see cref="CommandGuards.RejectIfSplitsArray"/> (Commands.cs line 96) -- which rejects a write
/// that lands on a legacy Ctrl+Shift+Enter (CSE) array member/anchor whose full declared range is
/// not included in the edit, independent of sheet protection.
///
/// A form control's LinkedCell (the Format Control dialog's "Cell link" field) can validly be set to
/// any cell reference, including a cell inside an existing legacy CSE array's footprint. Before the
/// fix: CanWriteLinkedCell returned true for such a cell (array membership was never checked), so the
/// control's IsChecked/Value/SelectedIndex was mutated immediately and unconditionally by
/// CreateToggleCheckBoxCommand/CreateSelectOptionButtonCommand/CreateStepCommand/
/// CreateSelectListItemCommand -- even though the returned command's own Apply would then reject the
/// write with "You cannot change part of an array.", leaving the control's in-model visible state
/// permanently desynced from the (correctly unwritten) cell (WPF additionally never resynced on this
/// failure path -- see the sibling fix in src/FreeX.App.Host/MainWindow.FormControls.cs). Matching
/// Excel: a rejected write must never change the control's appearance, exactly as
/// HProtection2FixesTests already covers for the sheet-protection half of this same contract.
/// </summary>
public sealed class R126_FormControlLinkedCellArraySplitGuardTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    /// <summary>
    /// Sets up a legacy CSE array anchored at A1, spilling into A1:A3 (3 rows x 1 col), on a fresh
    /// unprotected sheet -- mirrors R123_DynamicSpillMemberContentWriteTests's
    /// R123_LegacyCseArrayMember_IsStillBlocked_NoRegression fixture, the same construction the
    /// product's own EditCellsCommand.Apply test suite uses to exercise this exact guard.
    /// </summary>
    private static (Workbook Workbook, Sheet Sheet, CellAddress Anchor, CellAddress Member) MakeLegacyCseArraySheet()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var anchor = Addr(sheet, "A1");
        var legacyCell = Cell.FromFormula("A10:A11+A20:A21");
        legacyCell.LegacyArrayRows = 3;
        legacyCell.LegacyArrayCols = 1;
        sheet.SetCell(anchor, legacyCell);
        var cells = new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) },
        };
        sheet.SetSpillRange(anchor, new RangeValue(cells)); // spills to A1:A3

        var member = Addr(sheet, "A2"); // covered, non-anchor
        return (wb, sheet, anchor, member);
    }

    [Fact]
    public void ToggleCheckBox_LinkedCellOnLegacyArrayMember_ReturnsNullAndLeavesIsCheckedUntouched()
    {
        var (wb, sheet, _, member) = MakeLegacyCseArraySheet();

        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            IsChecked = false,
            LinkedCell = "A2",
        };

        var cmd = FormControlInteractionService.CreateToggleCheckBoxCommand(control, sheet.Id, wb);

        cmd.Should().BeNull("A2 is a non-anchor member of an existing legacy CSE array, and " +
            "EditCellsCommand.Apply would reject a single-cell write to it independent of protection");
        control.IsChecked.Should().BeFalse("a rejected write must never flip the checkbox's visible state");
        sheet.GetValue(member).Should().Be(new NumberValue(2), "the linked cell's array-spill value must be untouched");
    }

    [Fact]
    public void SelectOptionButton_LinkedCellOnLegacyArrayMember_ReturnsNullAndLeavesGroupUntouched()
    {
        var (wb, sheet, _, _) = MakeLegacyCseArraySheet();

        var first = new FormControlModel
        {
            Kind = FormControlKind.OptionButton,
            IsChecked = true,
            LinkedCell = "A2",
        };
        var second = new FormControlModel
        {
            Kind = FormControlKind.OptionButton,
            IsChecked = false,
            LinkedCell = "A2",
        };
        var all = new List<FormControlModel> { first, second };

        var cmd = FormControlInteractionService.CreateSelectOptionButtonCommand(second, all, sheet.Id, wb);

        cmd.Should().BeNull("the shared linked cell is a non-anchor legacy CSE array member");
        first.IsChecked.Should().BeTrue("the previously-selected sibling must stay selected");
        second.IsChecked.Should().BeFalse("the clicked button must not be marked selected on a rejected write");
    }

    [Fact]
    public void StepSpinner_LinkedCellOnLegacyArrayMember_ReturnsNullAndLeavesValueUntouched()
    {
        var (wb, sheet, _, _) = MakeLegacyCseArraySheet();

        var control = new FormControlModel
        {
            Kind = FormControlKind.Spinner,
            Value = 5,
            Min = 1,
            Max = 10,
            Increment = 1,
            LinkedCell = "A2",
        };

        var cmd = FormControlInteractionService.CreateStepCommand(control, delta: 1, sheet.Id, wb);

        cmd.Should().BeNull("the linked cell is a non-anchor legacy CSE array member");
        control.Value.Should().Be(5, "a rejected write must never change the spinner's displayed value");
    }

    [Fact]
    public void SelectListItem_LinkedCellOnLegacyArrayMember_ReturnsNullAndLeavesSelectedIndexUntouched()
    {
        var (wb, sheet, _, _) = MakeLegacyCseArraySheet();

        var control = new FormControlModel
        {
            Kind = FormControlKind.ListBox,
            SelectedIndex = 1,
            LinkedCell = "A2",
        };

        var cmd = FormControlInteractionService.CreateSelectListItemCommand(control, oneBasedIndex: 2, sheet.Id, wb);

        cmd.Should().BeNull("the linked cell is a non-anchor legacy CSE array member");
        control.SelectedIndex.Should().Be(1, "a rejected write must never change the list's visible selection");
    }

    // ── No-regression sibling: a live DYNAMIC array's spill member must NOT be blocked ──────────
    // R123-dynamic-spill-member-write: only a legacy CSE array keeps the whole-range restriction;
    // a modern dynamic array's spill member is a normal, individually-writable cell in real Excel.
    // CanWriteLinkedCell must call RejectIfSplitsArray with allowDynamicSpillMemberWrite: true
    // (matching EditCellsCommand.Apply's own call) so this case is NOT regressed by the fix above.

    [Fact]
    public void ToggleCheckBox_LinkedCellOnDynamicSpillMember_StillSucceeds_NoRegression()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var anchor = Addr(sheet, "A1");
        sheet.SetCell(anchor, Cell.FromFormula("SEQUENCE(3)"));
        var spillValues = new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) },
        };
        sheet.SetSpillRange(anchor, new RangeValue(spillValues)); // spills to A1:A3, no LegacyArrayRows

        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            IsChecked = false,
            LinkedCell = "A2", // non-anchor member of the live DYNAMIC spill
        };

        var cmd = FormControlInteractionService.CreateToggleCheckBoxCommand(control, sheet.Id, wb);

        cmd.Should().NotBeNull("a dynamic array's spill member may be written directly, unlike a legacy CSE array's");
        control.IsChecked.Should().BeTrue("model flips once the write is confirmed to be allowed");

        var ctx = new TestCommandContext(wb);
        var outcome = cmd!.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(Addr(sheet, "A2")).Should().Be(new BoolValue(true));
    }
}
