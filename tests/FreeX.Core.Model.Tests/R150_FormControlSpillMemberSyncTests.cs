using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for round-150 finding "spill-overlay-root F13": form controls linked to a
/// non-anchor spill member never synced their displayed state. <see cref="FormControlInteractionService.SyncControlsFromLinkedCells"/>
/// read the linked cell via <c>Sheet.GetCell(...)?.Value</c>, but <see cref="Sheet.GetCell(CellAddress)"/>
/// only returns a PHYSICAL cell entry -- a non-anchor member of a live dynamic-array spill (e.g. a
/// cell inside a "=SEQUENCE(3,1)" result) has no entry of its own in that dictionary, so GetCell
/// returned null there even though the cell has a real, visible value (served by the sheet's separate
/// spill-value overlay, the same one <see cref="Sheet.GetValue(CellAddress)"/> already falls back to).
/// The fix swaps the four affected reads (CheckBox, Spinner/ScrollBar, ListBox/DropDown, and the
/// OptionButton group) in FormControlInteractionService.cs to <c>Sheet.GetValue</c>, which is
/// overlay-aware.
/// </summary>
public sealed class R150_FormControlSpillMemberSyncTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    /// <summary>
    /// Sets up a live dynamic-array spill anchored at A1, spilling numeric values into A1:A3 (3
    /// rows x 1 col) via <see cref="Sheet.SetSpillRange"/> -- the same construction
    /// R126_FormControlLinkedCellArraySplitGuardTests uses for a "live DYNAMIC spill" (no
    /// LegacyArrayRows/Cols, so it behaves as a modern spilling formula, not a legacy CSE array).
    /// A2 is then a non-anchor spill member: it has a value (visible on the grid, and returned by
    /// Sheet.GetValue), but no entry in Sheet's physical cell dictionary, so Sheet.GetCell(A2) is
    /// null.
    /// </summary>
    private static (Workbook Workbook, Sheet Sheet, CellAddress Member) MakeDynamicSpillSheet(
        double memberValue)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var anchor = Addr(sheet, "A1");
        sheet.SetCell(anchor, Cell.FromFormula("SEQUENCE(3,1)"));
        var cells = new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(memberValue) },
            { new NumberValue(3) },
        };
        sheet.SetSpillRange(anchor, new RangeValue(cells)); // spills to A1:A3, live dynamic array

        var member = Addr(sheet, "A2"); // covered, non-anchor
        sheet.GetCell(member).Should().BeNull(
            "sanity check: a non-anchor spill member has no physical cell entry");
        return (wb, sheet, member);
    }

    [Fact]
    public void SyncControlsFromLinkedCells_CheckBox_ReflectsSpillMemberValue()
    {
        // TRUE-ish (non-zero) spill member value.
        var (wb, sheet, _) = MakeDynamicSpillSheet(memberValue: 1);
        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            IsChecked = false,
            LinkedCell = "A2", // non-anchor member of the live spill
        };
        sheet.FormControls.Add(control);

        FormControlInteractionService.SyncControlsFromLinkedCells(sheet, wb);

        control.IsChecked.Should().BeTrue(
            "the checkbox must mirror its linked cell's live spill-member value, matching Excel");
    }

    [Fact]
    public void SyncControlsFromLinkedCells_Spinner_ReflectsSpillMemberValue()
    {
        var (wb, sheet, _) = MakeDynamicSpillSheet(memberValue: 7);
        var control = new FormControlModel
        {
            Kind = FormControlKind.Spinner,
            Value = 1,
            Min = 0,
            Max = 30,
            Increment = 1,
            LinkedCell = "A2",
        };
        sheet.FormControls.Add(control);

        FormControlInteractionService.SyncControlsFromLinkedCells(sheet, wb);

        control.Value.Should().Be(7, "the spinner must mirror its linked cell's live spill-member value");
    }

    [Fact]
    public void SyncControlsFromLinkedCells_DropDown_ReflectsSpillMemberValue()
    {
        var (wb, sheet, _) = MakeDynamicSpillSheet(memberValue: 2);
        var control = new FormControlModel
        {
            Kind = FormControlKind.DropDown,
            SelectedIndex = 0,
            LinkedCell = "A2",
        };
        sheet.FormControls.Add(control);

        FormControlInteractionService.SyncControlsFromLinkedCells(sheet, wb);

        control.SelectedIndex.Should().Be(2, "the dropdown must mirror its linked cell's live spill-member value");
    }

    [Fact]
    public void SyncControlsFromLinkedCells_OptionButtonGroup_ReflectsSpillMemberValue()
    {
        var (wb, sheet, _) = MakeDynamicSpillSheet(memberValue: 2);
        var first = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = true, LinkedCell = "A2" };
        var second = new FormControlModel { Kind = FormControlKind.OptionButton, IsChecked = false, LinkedCell = "A2" };
        sheet.FormControls.Add(first);
        sheet.FormControls.Add(second);

        FormControlInteractionService.SyncControlsFromLinkedCells(sheet, wb);

        first.IsChecked.Should().BeFalse("group index 1 no longer matches the spill member's value of 2");
        second.IsChecked.Should().BeTrue("the 2nd button in the group must be checked to match the spill member's value of 2");
    }

    // ── Sibling no-regression: an ordinary (non-spill) physical cell must still sync ──────────────

    [Fact]
    public void SyncControlsFromLinkedCells_CheckBox_StillReflectsOrdinaryPhysicalCell_NoRegression()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var addr = Addr(sheet, "B1");
        sheet.SetCell(addr, Cell.FromValue(new BoolValue(true)));

        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            IsChecked = false,
            LinkedCell = "B1", // ordinary physical cell, not a spill member
        };
        sheet.FormControls.Add(control);

        FormControlInteractionService.SyncControlsFromLinkedCells(sheet, wb);

        control.IsChecked.Should().BeTrue(
            "an ordinary physical cell's value must still sync correctly (GetValue must not regress GetCell's coverage)");
    }
}
