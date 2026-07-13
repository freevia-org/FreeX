using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for two round-38 band-scoped Insert/Delete Cells findings:
///
/// R38-commands-insert-delete-shift-2-1: a Conditional-Format, Data-Validation, or named-range
/// range that STRADDLES a band-scoped Insert/Delete Cells shift boundary must GROW (on insert) or
/// SHRINK (on delete) to track the surviving/inserted cells, matching Excel's own
/// reference-adjustment behavior and FreeX's own whole-row/whole-column shift helpers, instead of
/// being left stale (the previous "leave unchanged on partial overlap" behavior).
///
/// R38-commands-insert-delete-shift-2-2: the Insert Cells edge-of-sheet overflow guard must also
/// consider a merged region (even a completely blank one, with no occupied Cell entries) that would
/// be relocated past the last column/row, instead of only inspecting value-bearing cells and
/// silently letting AdjustMergesShiftRight/Down truncate the merge.
/// </summary>
public sealed class R38_InsertDeleteShiftStraddleTests
{
    // ── R38-commands-insert-delete-shift-2-1: CF/DV straddle grow (insert) ──────

    [Fact]
    public void InsertCellsShiftRight_DvRuleStraddlesInsertBoundary_GrowsRange_AndUndoRestores()
    {
        // DV rule B2:D2 (cols 2..4). Insert one cell at C2 (col 3) shift-right: the rule
        // straddles the insert point (Start.Col=2 < insertBeforeCol=3 <= End.Col=4), so it must
        // GROW to B2:E2 (cols 2..5) — the cell that used to be D2 (now E2) stays validated.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var dvRule = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 4)),
            Type = DvType.List,
            Formula1 = "Red,Green,Blue"
        };
        sheet.DataValidations.Add(dvRule);

        var insertRange = new GridRange(new CellAddress(sheet.Id, 2, 3), new CellAddress(sheet.Id, 2, 3));
        var cmd = new InsertCellsCommand(sheet.Id, insertRange, InsertCellsShiftDirection.Right);
        cmd.Apply(ctx).Success.Should().BeTrue();

        dvRule.AppliesTo.Start.Col.Should().Be(2, "Start.Col stays put — only the far edge grows");
        dvRule.AppliesTo.End.Col.Should().Be(5, "End.Col must grow by the inserted width, not stay stale at 4");
        dvRule.AppliesTo.Start.Row.Should().Be(2);
        dvRule.AppliesTo.End.Row.Should().Be(2);

        DataValidationService.GetApplicable(sheet, new CellAddress(sheet.Id, 2, 5))
            .Should().ContainSingle("the former D2 (now E2) must still be validated");

        cmd.Revert(ctx);

        dvRule.AppliesTo.Start.Col.Should().Be(2, "undo restores original AppliesTo");
        dvRule.AppliesTo.End.Col.Should().Be(4);
    }

    [Fact]
    public void InsertCellsShiftDown_CfRuleStraddlesInsertBoundary_GrowsRange_AndUndoRestores()
    {
        // CF rule A3:A6 (rows 3..6). Insert one cell at A4 (row 4) shift-down: the rule straddles
        // the insert point (Start.Row=3 < insertBeforeRow=4 <= End.Row=6), so it must GROW to
        // A3:A7 (rows 3..7) rather than being left stale at A3:A6.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var cfRule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 6, 1)),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0"
        };
        sheet.ConditionalFormats.Add(cfRule);

        var insertRange = new GridRange(new CellAddress(sheet.Id, 4, 1), new CellAddress(sheet.Id, 4, 1));
        var cmd = new InsertCellsCommand(sheet.Id, insertRange, InsertCellsShiftDirection.Down);
        cmd.Apply(ctx).Success.Should().BeTrue();

        cfRule.AppliesTo.Start.Row.Should().Be(3, "Start.Row stays put — only the far edge grows");
        cfRule.AppliesTo.End.Row.Should().Be(7, "End.Row must grow by the inserted height, not stay stale at 6");

        cmd.Revert(ctx);

        cfRule.AppliesTo.Start.Row.Should().Be(3, "undo restores original AppliesTo");
        cfRule.AppliesTo.End.Row.Should().Be(6);
    }

    [Fact]
    public void InsertCellsShiftRight_DvRuleEntirelyLeftOfInsertPoint_Unchanged()
    {
        // Sibling no-regression: a rule entirely LEFT of the insert point (End.Col < insertBeforeCol)
        // must remain completely untouched, not grown.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var dvRule = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1)),
            Type = DvType.List,
            Formula1 = "X,Y"
        };
        sheet.DataValidations.Add(dvRule);

        var insertRange = new GridRange(new CellAddress(sheet.Id, 2, 3), new CellAddress(sheet.Id, 2, 3));
        var cmd = new InsertCellsCommand(sheet.Id, insertRange, InsertCellsShiftDirection.Right);
        cmd.Apply(ctx).Success.Should().BeTrue();

        dvRule.AppliesTo.Start.Col.Should().Be(1, "rule entirely left of the insert point is unaffected");
        dvRule.AppliesTo.End.Col.Should().Be(1);
    }

    // ── R38-commands-insert-delete-shift-2-1: CF/DV straddle shrink (delete) ────

    [Fact]
    public void DeleteCellsShiftLeft_DvRuleStraddlesDeleteBoundary_ShrinksRange_AndUndoRestores()
    {
        // DV rule B2:D2 (cols 2..4). Delete C2 (col 3) shift-left: the rule straddles the deleted
        // column (Start.Col=2 < deletedStartCol=3 <= deletedEndCol=3 < End.Col=4), so it must
        // SHRINK to B2:C2 (cols 2..3 — B stays, D shifts left into C) rather than being left stale
        // referencing the now-vacated D2.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var dvRule = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 4)),
            Type = DvType.List,
            Formula1 = "Red,Green,Blue"
        };
        sheet.DataValidations.Add(dvRule);

        var deleteRange = new GridRange(new CellAddress(sheet.Id, 2, 3), new CellAddress(sheet.Id, 2, 3));
        var cmd = new DeleteCellsCommand(sheet.Id, deleteRange, DeleteCellsShiftDirection.Left);
        cmd.Apply(ctx).Success.Should().BeTrue();

        dvRule.AppliesTo.Start.Col.Should().Be(2, "surviving Start.Col (B) stays put");
        dvRule.AppliesTo.End.Col.Should().Be(3, "End.Col shrinks: former D2 shifted left into C2");

        cmd.Revert(ctx);

        dvRule.AppliesTo.Start.Col.Should().Be(2, "undo restores original AppliesTo");
        dvRule.AppliesTo.End.Col.Should().Be(4);
    }

    [Fact]
    public void DeleteCellsShiftUp_CfRuleStraddlesDeleteBoundary_ShrinksRange_AndUndoRestores()
    {
        // CF rule A2:A4 (rows 2..4). Delete A3 (row 3) shift-up: straddles the deleted row, so it
        // must shrink to A2:A3 (row 2 stays, row 4 shifts up into row 3).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var cfRule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 4, 1)),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0"
        };
        sheet.ConditionalFormats.Add(cfRule);

        var deleteRange = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 3, 1));
        var cmd = new DeleteCellsCommand(sheet.Id, deleteRange, DeleteCellsShiftDirection.Up);
        cmd.Apply(ctx).Success.Should().BeTrue();

        cfRule.AppliesTo.Start.Row.Should().Be(2, "surviving Start.Row stays put");
        cfRule.AppliesTo.End.Row.Should().Be(3, "End.Row shrinks: former row 4 shifted up into row 3");

        cmd.Revert(ctx);

        cfRule.AppliesTo.Start.Row.Should().Be(2, "undo restores original AppliesTo");
        cfRule.AppliesTo.End.Row.Should().Be(4);
    }

    [Fact]
    public void DeleteCellsShiftLeft_DvRuleEntirelyWithinDeletedRange_StillRemoved()
    {
        // Sibling no-regression: a rule entirely inside the deleted columns must still be REMOVED
        // (not grown/shrunk into a degenerate/incorrect range).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var dvRule = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 3)),
            Type = DvType.List,
            Formula1 = "X,Y"
        };
        sheet.DataValidations.Add(dvRule);

        var deleteRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 3));
        var cmd = new DeleteCellsCommand(sheet.Id, deleteRange, DeleteCellsShiftDirection.Left);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.DataValidations.Should().BeEmpty("rule was entirely within the deleted columns");

        cmd.Revert(ctx);

        sheet.DataValidations.Should().ContainSingle("rule restored on undo");
    }

    // ── R38-commands-insert-delete-shift-2-1: named-range straddle (insert + delete) ──

    [Fact]
    public void InsertCellsShiftRight_NamedRangeStraddlesInsertBoundary_GrowsRange_AndUndoRestores()
    {
        // Named range "Data" = B2:D2. Insert one cell at C2 shift-right: straddles the insert
        // point, so it must grow to B2:E2 like the CF/DV case above.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var original = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 4));
        wb.DefineNamedRange("Data", original);
        var ctx = new TestCommandContext(wb);

        var insertRange = new GridRange(new CellAddress(sheet.Id, 2, 3), new CellAddress(sheet.Id, 2, 3));
        var cmd = new InsertCellsCommand(sheet.Id, insertRange, InsertCellsShiftDirection.Right);
        cmd.Apply(ctx).Success.Should().BeTrue();

        wb.TryGetNamedRange("Data", out var shifted).Should().BeTrue();
        shifted.Should().Be(new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 5)),
            "the straddling named range must grow to track the inserted cell, matching Excel");

        cmd.Revert(ctx);

        wb.TryGetNamedRange("Data", out var restored).Should().BeTrue();
        restored.Should().Be(original);
    }

    [Fact]
    public void DeleteCellsShiftLeft_NamedRangeStraddlesDeleteBoundary_ShrinksRange_AndUndoRestores()
    {
        // Named range "Data" = B2:D2. Delete C2 shift-left: straddles the deleted column, so it
        // must shrink to B2:C2.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var original = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 4));
        wb.DefineNamedRange("Data", original);
        var ctx = new TestCommandContext(wb);

        var deleteRange = new GridRange(new CellAddress(sheet.Id, 2, 3), new CellAddress(sheet.Id, 2, 3));
        var cmd = new DeleteCellsCommand(sheet.Id, deleteRange, DeleteCellsShiftDirection.Left);
        cmd.Apply(ctx).Success.Should().BeTrue();

        wb.TryGetNamedRange("Data", out var shrunk).Should().BeTrue();
        shrunk.Should().Be(new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 3)),
            "the straddling named range must shrink to the surviving portion, matching Excel");

        cmd.Revert(ctx);

        wb.TryGetNamedRange("Data", out var restored).Should().BeTrue();
        restored.Should().Be(original);
    }

    // ── R38-commands-insert-delete-shift-2-2: blank-merge edge guard ────────────

    [Fact]
    public void InsertCellsShiftRight_BlankMergedRegionAtLastColumn_BlocksInsert()
    {
        // Merge the last two columns of row 1 while both cells are blank (an ordinary
        // "merge first, type later" layout merge — no Cell entries are created for either column).
        // Selecting A1 and inserting one cell shift-right would push the merge's right edge past
        // the last column; Excel blocks this the same way it blocks pushing any value past the
        // sheet edge, instead of silently truncating the merge to one column.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var lastTwoCols = new GridRange(
            new CellAddress(sheet.Id, 1, CellAddress.MaxCol - 1),
            new CellAddress(sheet.Id, 1, CellAddress.MaxCol));
        sheet.AddMergedRegion(lastTwoCols);
        sheet.GetCell(1, CellAddress.MaxCol - 1).Should().BeNull("the merge is entirely blank — no Cell entries");
        sheet.GetCell(1, CellAddress.MaxCol).Should().BeNull();

        var insertRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var outcome = new InsertCellsCommand(sheet.Id, insertRange, InsertCellsShiftDirection.Right).Apply(ctx);

        outcome.Success.Should().BeFalse("inserting would push the blank merge past the last column");
        outcome.ErrorMessage.Should().Contain("last column");
        sheet.MergedRegions.Should().ContainSingle();
        sheet.MergedRegions[0].Start.Col.Should().Be(CellAddress.MaxCol - 1, "the merge must be untouched, not truncated");
        sheet.MergedRegions[0].End.Col.Should().Be(CellAddress.MaxCol);
    }

    [Fact]
    public void InsertCellsShiftDown_BlankMergedRegionAtLastRow_BlocksInsert()
    {
        // Row analogue: merge the last two rows of column A while both cells are blank.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var lastTwoRows = new GridRange(
            new CellAddress(sheet.Id, CellAddress.MaxRow - 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));
        sheet.AddMergedRegion(lastTwoRows);

        var insertRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var outcome = new InsertCellsCommand(sheet.Id, insertRange, InsertCellsShiftDirection.Down).Apply(ctx);

        outcome.Success.Should().BeFalse("inserting would push the blank merge past the last row");
        outcome.ErrorMessage.Should().Contain("last row");
        sheet.MergedRegions.Should().ContainSingle();
        sheet.MergedRegions[0].Start.Row.Should().Be(CellAddress.MaxRow - 1, "the merge must be untouched, not truncated");
        sheet.MergedRegions[0].End.Row.Should().Be(CellAddress.MaxRow);
    }

    [Fact]
    public void InsertCellsShiftRight_MergeFullyInsideBandFarFromEdge_StillSucceeds()
    {
        // Sibling no-regression: a merge that is fully inside the shifted band but nowhere near the
        // sheet edge must still shift normally (the new edge guard must not over-reject).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 2, 4)));

        var insertRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 2));
        var outcome = new InsertCellsCommand(sheet.Id, insertRange, InsertCellsShiftDirection.Right).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.MergedRegions.Should().ContainSingle();
        sheet.MergedRegions[0].Start.Col.Should().Be(4, "merge shifted right by 1");
        sheet.MergedRegions[0].End.Col.Should().Be(5);
    }
}
