using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for round-156 finding sweep94 F1: "GroupedEditCellsCommand skips the 'cannot
/// change part of an array' guard that every other cell-edit command enforces". Every other member
/// of the <see cref="CommandGuards.RejectIfSplitsArray"/>-covered family -- most directly
/// <see cref="EditCellsCommand.Apply"/> (Commands.cs line 96) -- rejects an edit that lands on a
/// single non-anchor member of a legacy Ctrl+Shift+Enter (CSE) array without the whole declared
/// range being part of the edit. <see cref="GroupedEditCellsCommand"/> -- substituted for
/// EditCellsCommand the moment two or more sheet tabs are grouped -- only checked sheet protection
/// and wrote straight through <c>sheet.SetCell</c>, so the exact same gesture (typing into a covered
/// array member) was silently allowed the moment a second sheet happened to be grouped.
/// </summary>
public sealed class R156_GroupedEditCellsArraySplitGuardTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    /// <summary>
    /// Sets up a legacy CSE array anchored at A1, spilling into A1:A3 (3 rows x 1 col), on
    /// <paramref name="sheet"/> -- the same construction R126_FormControlLinkedCellArraySplitGuardTests
    /// and the product's own EditCellsCommand.Apply test suite use to exercise this exact guard.
    /// </summary>
    private static (CellAddress Anchor, CellAddress Member) MakeLegacyCseArray(Sheet sheet)
    {
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
        return (anchor, member);
    }

    // ── The bug: a grouped edit into a legacy CSE array member must be rejected, matching the
    // ── ungrouped EditCellsCommand path exactly. Before the fix this committed silently.

    [Fact]
    public void Apply_EditIntoLegacyCseArrayMember_IsRejected_MatchingEditCellsCommand()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var (_, member) = MakeLegacyCseArray(sheet1);
        // Sheet2 has no array at that address -- the guard must still fire because the source
        // sheet (Sheet1) has one; the whole grouped write is one unit.
        var ctx = new TestCommandContext(wb);

        var command = new GroupedEditCellsCommand(
            [sheet1.Id, sheet2.Id],
            sheet1.Id,
            [(member, Cell.FromValue(new TextValue("HACKED")))]);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse("A2 is a non-anchor member of an existing legacy CSE array; " +
            "EditCellsCommand.Apply rejects this write independent of sheet protection, and " +
            "GroupedEditCellsCommand must match it");
        outcome.ErrorMessage.Should().Be("You cannot change part of an array.");
        sheet1.GetValue(member).Should().Be(new NumberValue(2), "the rejected write must leave the array member untouched");
    }

    /// <summary>
    /// Adjacent case (rule 10 / the round-156 scope directive's explicit warning): a grouped edit
    /// legitimately writes the same value to every grouped sheet, so the guard must reject the
    /// whole command -- not silently apply on sheets where it would be allowed while skipping the
    /// one sheet with the array. A partial application across grouped sheets (Sheet2 mutated, Sheet1
    /// rejected) would desynchronize the group, which is worse than the original bug.
    /// </summary>
    [Fact]
    public void Apply_RejectedArraySplit_LeavesEVERYGroupedSheetUntouched_NoPartialApplication()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var (_, member) = MakeLegacyCseArray(sheet1);
        // Sheet2's corresponding address (A2) has no array -- if the check ran per-sheet and
        // applied independently, Sheet2 would get mutated even though Sheet1 must reject.
        var sheet2Target = Addr(sheet2, "A2");
        sheet2.SetCell(sheet2Target, Cell.FromValue(new TextValue("untouched2")));
        var ctx = new TestCommandContext(wb);

        var command = new GroupedEditCellsCommand(
            [sheet1.Id, sheet2.Id],
            sheet1.Id,
            [(member, Cell.FromValue(new TextValue("HACKED")))]);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet1.GetValue(member).Should().Be(new NumberValue(2), "Sheet1's array member must be untouched");
        sheet2.GetValue(sheet2Target).Should().Be(new TextValue("untouched2"),
            "Sheet2 must NOT be mutated either -- the guard is a whole-command validation pass that runs " +
            "before any sheet.SetCell, so rejecting for one grouped sheet rejects the edit for all of them");
    }

    // ── No-regression siblings ────────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_OrdinaryGroupedEdit_StillSucceeds_NoRegression()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);
        var a1 = new CellAddress(sheet1.Id, 1, 1);
        sheet1.SetCell(a1, Cell.FromValue(new TextValue("old1")));

        var command = new GroupedEditCellsCommand(
            [sheet1.Id, sheet2.Id],
            sheet1.Id,
            [(a1, Cell.FromValue(new TextValue("new")))]);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet1.GetValue(new CellAddress(sheet1.Id, 1, 1)).Should().Be(new TextValue("new"));
        sheet2.GetValue(new CellAddress(sheet2.Id, 1, 1)).Should().Be(new TextValue("new"));
    }

    /// <summary>
    /// R123-dynamic-spill-member-write: only a legacy CSE array keeps the whole-range restriction;
    /// a modern dynamic array's spill member is a normal, individually-writable cell in real Excel.
    /// GroupedEditCellsCommand must call RejectIfSplitsArray with allowDynamicSpillMemberWrite: true
    /// (matching EditCellsCommand.Apply's own call) so this case is not regressed by the fix above.
    /// </summary>
    [Fact]
    public void Apply_EditIntoDynamicSpillMember_StillSucceeds_NoRegression()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var anchor = Addr(sheet1, "A1");
        sheet1.SetCell(anchor, Cell.FromFormula("SEQUENCE(3)"));
        var spillValues = new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) },
        };
        sheet1.SetSpillRange(anchor, new RangeValue(spillValues)); // spills to A1:A3, no LegacyArrayRows
        var member = Addr(sheet1, "A2");
        var ctx = new TestCommandContext(wb);

        var command = new GroupedEditCellsCommand(
            [sheet1.Id, sheet2.Id],
            sheet1.Id,
            [(member, Cell.FromValue(new NumberValue(42)))]);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet1.GetValue(member).Should().Be(new NumberValue(42));
        sheet2.GetValue(new CellAddress(sheet2.Id, member.Row, member.Col)).Should().Be(new NumberValue(42));
    }
}
