using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// R123-dynamic-spill-member-write: CommandGuards.RejectIfSplitsArray applied Excel's legacy-CSE
/// "You cannot change part of an array" rule to modern dynamic-array spill members too, so typing,
/// pasting, or clearing a single non-anchor spill member cell was wrongly rejected outright instead
/// of letting the write proceed and the owning formula collapse to #SPILL! on its next
/// recalculation (matching real Excel). Fixed by adding an opt-in
/// <c>allowDynamicSpillMemberWrite</c> parameter to RejectIfSplitsArray, set true only by the
/// content-write command family (EditCellsCommand, the paste family, ClearContentsCommand, the
/// fill family) -- see CommandGuards.cs for the full family list and rationale. Structural/
/// relocation commands (Insert/Delete Rows/Columns/Cells, Sort, Move Range, Remove Duplicates) do
/// NOT opt in and keep rejecting ANY split of a dynamic-array spill's footprint, because those
/// commands physically shift cell positions rather than writing content into a user-selected cell
/// -- a different Excel rule this fix does not touch (see Round47SiblingGuardAsymmetrySweepTests
/// and R23_InsertDeleteCellsArraySplitGuardTests for that still-enforced behavior).
/// </summary>
public sealed class R123_DynamicSpillMemberContentWriteTests
{
    private const string CannotChangePartOfArrayMessage = "You cannot change part of an array.";

    private static (Workbook Workbook, Sheet Sheet, CellAddress Anchor, ICommandContext Ctx) MakeLiveDynamicSpillSetup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.SetCell(anchor, Cell.FromFormula("SEQUENCE(3,1)"));
        var cells = new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) },
        };
        sheet.SetSpillRange(anchor, new RangeValue(cells)); // spills to A1:A3
        return (wb, sheet, anchor, new TestCommandContext(wb));
    }

    [Fact]
    public void R123_EditCellsCommand_OnNonAnchorDynamicSpillMember_IsAllowed()
    {
        var (_, sheet, _, ctx) = MakeLiveDynamicSpillSetup();
        var member = new CellAddress(sheet.Id, 2, 1); // A2 - covered, non-anchor

        var outcome = EditCellsCommand.ForValue(sheet.Id, member, new NumberValue(999)).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(member).Should().Be(new NumberValue(999));
    }

    [Fact]
    public void R123_ClearContentsCommand_OnNonAnchorDynamicSpillMember_IsAllowed()
    {
        var (_, sheet, _, ctx) = MakeLiveDynamicSpillSetup();
        var member = new CellAddress(sheet.Id, 3, 1); // A3 - covered, non-anchor

        var outcome = new ClearContentsCommand(sheet.Id, new GridRange(member, member)).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
    }

    [Fact]
    public void R123_PasteCellsCommand_OnNonAnchorDynamicSpillMember_IsAllowed()
    {
        var (_, sheet, _, ctx) = MakeLiveDynamicSpillSetup();
        var member = new CellAddress(sheet.Id, 2, 1); // A2 - covered, non-anchor

        var command = new PasteCellsCommand(sheet.Id, [(member, Cell.FromValue(new NumberValue(777)))]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(member).Should().Be(new NumberValue(777));
    }

    [Fact]
    public void R123_LegacyCseArrayMember_IsStillBlocked_NoRegression()
    {
        // A legacy Ctrl+Shift+Enter (CSE) array (Cell.LegacyArrayRows/LegacyArrayCols populated)
        // keeps the full "You cannot change part of an array" restriction -- only a live DYNAMIC
        // array's spill was relaxed by this fix.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1); // A1
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
        sheet.SetSpillRange(anchor, new RangeValue(cells));
        var ctx = new TestCommandContext(wb);
        var member = new CellAddress(sheet.Id, 2, 1); // A2 - covered, non-anchor

        var outcome = EditCellsCommand.ForValue(sheet.Id, member, new NumberValue(999)).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be(CannotChangePartOfArrayMessage);
        sheet.GetValue(member).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void R123_StructuralDeleteRowsCommand_StillBlocksDynamicSpillSplit_NoRegression()
    {
        // Sibling no-regression check: structural/relocation commands were deliberately NOT opted
        // into allowDynamicSpillMemberWrite -- they physically shift cell positions rather than
        // writing content into a user-selected cell, a different Excel rule this fix leaves alone.
        var (_, sheet, anchor, ctx) = MakeLiveDynamicSpillSetup();

        // Deleting row 2 alone falls strictly inside the spill's own row extent (1..3): row 1
        // (anchor) stays put, row 2 is removed, row 3 shifts up to row 2 -- splitting the array.
        var outcome = new DeleteRowsCommand(sheet.Id, startRow: 2, count: 1).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be(CannotChangePartOfArrayMessage);
        sheet.TryGetSpillExtent(anchor, out var rows, out var cols).Should().BeTrue();
        rows.Should().Be(3u);
        cols.Should().Be(1u);
    }
}
