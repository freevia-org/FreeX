using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression tests for R23-array-formula-legacy-cse-1: InsertCellsCommand.Apply and
/// DeleteCellsCommand.Apply (the band-scoped "Insert Cells"/"Delete Cells" shift, as opposed to
/// whole-row/whole-column insert/delete) never called CommandGuards.RejectIfSplitsArray, so a
/// shift whose selected range covered only PART of a legacy CSE array (or dynamic-array spill)
/// block silently tore the array apart instead of refusing with Excel's "You cannot change part
/// of an array." Every sibling command (ClearContents, Copy, Autofill, Fill, MoveRange,
/// PasteCells, PasteSpecial, EditCells) already calls this guard.
///
/// The fix must reject a shift/delete that would carry SOME members of an array along while
/// leaving OTHERS behind, but must still ALLOW a shift whose selected band fully contains the
/// array's entire extent (the whole thing moves/deletes together as one atomic unit) — this is
/// exactly the scenario the round-21 "InsertCellsShiftDown_RelocatesSpillingArrayAndQueuesNewAnchorForRecalc"
/// test already exercises and must continue to pass.
/// </summary>
public class R23_InsertDeleteCellsArraySplitGuardTests
{
    private const string CannotChangePartOfArrayMessage = "You cannot change part of an array.";

    // Builds a legacy-CSE-style array anchored at B2, spilling to B2:B4 (3 rows x 1 col), with
    // every member cell (including non-anchor members) present as an occupied provisional cell —
    // mirroring the shape produced by XlsxFileAdapter for a multi-cell "t=array" formula loaded
    // from an XLSX before the first recalculation (see ArrayFormulaGuardTests' provisional-cell
    // scenario), which is exactly what the finding's failure scenario describes.
    private static (Workbook Workbook, Sheet Sheet, ICommandContext Ctx) MakeLegacyArraySetup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 2, 2); // B2
        sheet.SetCell(anchor, Cell.FromFormula("TRANSPOSE(D1:D3)"));
        sheet.GetCell(anchor)!.Value = new NumberValue(1);
        sheet.SetProvisionalSpillCell(anchor, 3, 2, Cell.FromValue(new NumberValue(2))); // B3
        sheet.SetProvisionalSpillCell(anchor, 4, 2, Cell.FromValue(new NumberValue(3))); // B4
        return (wb, sheet, new TestCommandContext(wb));
    }

    [Fact]
    public void InsertCellsShiftDown_OnMiddleMemberOfLegacyArrayBlock_IsRejected()
    {
        var (_, sheet, ctx) = MakeLegacyArraySetup();
        var member = new CellAddress(sheet.Id, 3, 2); // B3 - a member, not the whole block
        var insertRange = new GridRange(member, member);

        var outcome = new InsertCellsCommand(sheet.Id, insertRange, InsertCellsShiftDirection.Down).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be(CannotChangePartOfArrayMessage);
        // The array must be untouched - no silent corruption.
        sheet.GetValue(2, 2).Should().Be(new NumberValue(1)); // B2
        sheet.GetValue(3, 2).Should().Be(new NumberValue(2)); // B3
        sheet.GetValue(4, 2).Should().Be(new NumberValue(3)); // B4
    }

    [Fact]
    public void DeleteCellsShiftUp_OnMiddleMemberOfLegacyArrayBlock_IsRejected()
    {
        var (_, sheet, ctx) = MakeLegacyArraySetup();
        var member = new CellAddress(sheet.Id, 3, 2); // B3 - a member, not the whole block
        var deleteRange = new GridRange(member, member);

        var outcome = new DeleteCellsCommand(sheet.Id, deleteRange, DeleteCellsShiftDirection.Up).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be(CannotChangePartOfArrayMessage);
        sheet.GetValue(2, 2).Should().Be(new NumberValue(1)); // B2
        sheet.GetValue(3, 2).Should().Be(new NumberValue(2)); // B3
        sheet.GetValue(4, 2).Should().Be(new NumberValue(3)); // B4
    }

    [Fact]
    public void InsertCellsShiftDown_OnAnchorAloneOfLegacyArrayBlock_IsAllowed()
    {
        // Unlike a fixed-selection edit (EditCellsCommand), a Down-direction band shift selecting
        // just the anchor (B2) still carries every row below it - including the array's own
        // members (B3, B4) - down by the same amount, so the array survives intact as one atomic
        // unit. This is the same shape of scenario the round-21
        // InsertCellsShiftDown_RelocatesSpillingArrayAndQueuesNewAnchorForRecalc test already
        // covers for a live dynamic-array spill, and must remain allowed here too.
        var (_, sheet, ctx) = MakeLegacyArraySetup();
        var anchor = new CellAddress(sheet.Id, 2, 2); // B2
        var insertRange = new GridRange(anchor, anchor);

        var outcome = new InsertCellsCommand(sheet.Id, insertRange, InsertCellsShiftDirection.Down).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
    }

    [Fact]
    public void InsertCellsShiftDown_OnWholeLegacyArrayRangeAsUnit_IsAllowed()
    {
        // Selecting the array's whole declared range (not just a member) must still be allowed -
        // the whole block moves down together as one atomic unit, same as ClearContents/Copy/etc.
        var (_, sheet, ctx) = MakeLegacyArraySetup();
        var wholeRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 4, 2)); // B2:B4

        var outcome = new InsertCellsCommand(sheet.Id, wholeRange, InsertCellsShiftDirection.Down).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
    }

    [Fact]
    public void InsertCellsShiftDown_OnUnrelatedCell_StillWorks()
    {
        // Sanity check: the guard must not false-positive on ordinary cells when the sheet
        // does contain an unrelated legacy array elsewhere.
        var (_, sheet, ctx) = MakeLegacyArraySetup();
        var unrelated = new CellAddress(sheet.Id, 20, 20);
        sheet.SetCell(unrelated, Cell.FromValue(new TextValue("x")));
        var insertRange = new GridRange(unrelated, unrelated);

        var outcome = new InsertCellsCommand(sheet.Id, insertRange, InsertCellsShiftDirection.Down).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
    }
}
