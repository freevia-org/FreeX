using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests ensuring sheet.RichTextRuns is correctly shifted/moved/cleared by
/// structural-edit commands and restored on undo — mirroring the Hyperlinks pattern.
/// </summary>
public sealed class RichTextRunsCommandTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<CellTextRun> MakeRuns(string text) =>
        [new CellTextRun(text, Bold: null, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null)];

    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    // ── EE2 Insert/Delete Rows ────────────────────────────────────────────────

    [Fact]
    public void InsertRow_AboveRichCell_RunsShiftToNewAddress()
    {
        var (wb, sheet, ctx) = Setup();
        var addrA5 = new CellAddress(sheet.Id, 5, 1);
        sheet.SetCell(addrA5, Cell.FromValue(new TextValue("rich")));
        sheet.RichTextRuns[addrA5] = MakeRuns("rich");

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        var addrA6 = new CellAddress(sheet.Id, 6, 1);
        sheet.RichTextRuns.Should().ContainKey(addrA6);
        sheet.RichTextRuns[addrA6][0].Text.Should().Be("rich");
        sheet.RichTextRuns.Should().NotContainKey(addrA5, "stale runs must not remain at old address");
    }

    [Fact]
    public void InsertRow_AboveRichCell_UndoRestoresOriginalAddress()
    {
        var (wb, sheet, ctx) = Setup();
        var addrA5 = new CellAddress(sheet.Id, 5, 1);
        sheet.SetCell(addrA5, Cell.FromValue(new TextValue("rich")));
        sheet.RichTextRuns[addrA5] = MakeRuns("rich");

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 1);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.RichTextRuns.Should().ContainKey(addrA5);
        sheet.RichTextRuns[addrA5][0].Text.Should().Be("rich");
        sheet.RichTextRuns.Should().NotContainKey(new CellAddress(sheet.Id, 6, 1));
    }

    [Fact]
    public void DeleteRow_WithRichCell_RunsShiftUpAndDeletedRunsAreRemoved()
    {
        var (wb, sheet, ctx) = Setup();
        var addrA2 = new CellAddress(sheet.Id, 2, 1);
        var addrA3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(addrA2, Cell.FromValue(new TextValue("deleted")));
        sheet.RichTextRuns[addrA2] = MakeRuns("deleted");
        sheet.SetCell(addrA3, Cell.FromValue(new TextValue("surviving")));
        sheet.RichTextRuns[addrA3] = MakeRuns("surviving");

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 2, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        // surviving row shifted up to row 2 — key (row=2, col=1) now has "surviving" runs
        var newAddr = new CellAddress(sheet.Id, 2, 1);
        sheet.RichTextRuns.Should().ContainKey(newAddr);
        sheet.RichTextRuns[newAddr][0].Text.Should().Be("surviving");
        // old address for the surviving row (row=3) must be gone
        sheet.RichTextRuns.Should().NotContainKey(addrA3, "old A3 address must be gone after shift");
    }

    [Fact]
    public void DeleteRow_WithRichCell_UndoRestoresBothRows()
    {
        var (wb, sheet, ctx) = Setup();
        var addrA2 = new CellAddress(sheet.Id, 2, 1);
        var addrA3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(addrA2, Cell.FromValue(new TextValue("deleted")));
        sheet.RichTextRuns[addrA2] = MakeRuns("deleted");
        sheet.SetCell(addrA3, Cell.FromValue(new TextValue("surviving")));
        sheet.RichTextRuns[addrA3] = MakeRuns("surviving");

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 2, count: 1);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.RichTextRuns.Should().ContainKey(addrA2);
        sheet.RichTextRuns[addrA2][0].Text.Should().Be("deleted");
        sheet.RichTextRuns.Should().ContainKey(addrA3);
        sheet.RichTextRuns[addrA3][0].Text.Should().Be("surviving");
    }

    // ── EE2 Insert/Delete Columns ─────────────────────────────────────────────

    [Fact]
    public void InsertColumn_LeftOfRichCell_RunsShiftRight()
    {
        var (wb, sheet, ctx) = Setup();
        var addrB1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(addrB1, Cell.FromValue(new TextValue("rich")));
        sheet.RichTextRuns[addrB1] = MakeRuns("rich");

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 2, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        var addrC1 = new CellAddress(sheet.Id, 1, 3);
        sheet.RichTextRuns.Should().ContainKey(addrC1);
        sheet.RichTextRuns.Should().NotContainKey(addrB1);
    }

    [Fact]
    public void InsertColumn_LeftOfRichCell_UndoRestores()
    {
        var (wb, sheet, ctx) = Setup();
        var addrB1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(addrB1, Cell.FromValue(new TextValue("rich")));
        sheet.RichTextRuns[addrB1] = MakeRuns("rich");

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 2, count: 1);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.RichTextRuns.Should().ContainKey(addrB1);
        sheet.RichTextRuns.Should().NotContainKey(new CellAddress(sheet.Id, 1, 3));
    }

    [Fact]
    public void DeleteColumn_WithRichCell_DeletedRunsRemovedAndSurvivorsShifted()
    {
        var (wb, sheet, ctx) = Setup();
        var addrB1 = new CellAddress(sheet.Id, 1, 2);
        var addrC1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(addrB1, Cell.FromValue(new TextValue("deleted")));
        sheet.RichTextRuns[addrB1] = MakeRuns("deleted");
        sheet.SetCell(addrC1, Cell.FromValue(new TextValue("surviving")));
        sheet.RichTextRuns[addrC1] = MakeRuns("surviving");

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 2, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        // surviving data from col 3 shifts to col 2 — key (row=1, col=2) now has "surviving"
        var newAddr = new CellAddress(sheet.Id, 1, 2);
        sheet.RichTextRuns.Should().ContainKey(newAddr);
        sheet.RichTextRuns[newAddr][0].Text.Should().Be("surviving");
        // old address of surviving col (col=3) must be gone
        sheet.RichTextRuns.Should().NotContainKey(addrC1, "old C1 address must be gone after shift");
    }

    [Fact]
    public void DeleteColumn_WithRichCell_UndoRestores()
    {
        var (wb, sheet, ctx) = Setup();
        var addrB1 = new CellAddress(sheet.Id, 1, 2);
        var addrC1 = new CellAddress(sheet.Id, 1, 3);
        sheet.RichTextRuns[addrB1] = MakeRuns("deleted");
        sheet.RichTextRuns[addrC1] = MakeRuns("surviving");

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 2, count: 1);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.RichTextRuns.Should().ContainKey(addrB1);
        sheet.RichTextRuns.Should().ContainKey(addrC1);
    }

    // ── EE3 Move range ────────────────────────────────────────────────────────

    [Fact]
    public void MoveRange_RichCell_RunsMoveToDestination()
    {
        var (wb, sheet, ctx) = Setup();
        var addrA1 = new CellAddress(sheet.Id, 1, 1);
        var addrC1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(addrA1, Cell.FromValue(new TextValue("rich")));
        sheet.RichTextRuns[addrA1] = MakeRuns("rich");

        var cmd = new MoveRangeCommand(sheet.Id, new GridRange(addrA1, addrA1), addrC1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.RichTextRuns.Should().ContainKey(addrC1);
        sheet.RichTextRuns[addrC1][0].Text.Should().Be("rich");
        sheet.RichTextRuns.Should().NotContainKey(addrA1, "runs must not remain at source after move");
    }

    [Fact]
    public void MoveRange_RichCell_UndoRestoresSourceAndClearsDestination()
    {
        var (wb, sheet, ctx) = Setup();
        var addrA1 = new CellAddress(sheet.Id, 1, 1);
        var addrC1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(addrA1, Cell.FromValue(new TextValue("rich")));
        sheet.RichTextRuns[addrA1] = MakeRuns("rich");

        var cmd = new MoveRangeCommand(sheet.Id, new GridRange(addrA1, addrA1), addrC1);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.RichTextRuns.Should().ContainKey(addrA1);
        sheet.RichTextRuns[addrA1][0].Text.Should().Be("rich");
        sheet.RichTextRuns.Should().NotContainKey(addrC1);
    }

    // ── EE4 Sort ──────────────────────────────────────────────────────────────

    [Fact]
    public void Sort_RichCellFollowsItsRowAndUndoRestores()
    {
        var (wb, sheet, ctx) = Setup();
        // Row 1: "Z" (rich), Row 2: "A"
        var addrA1 = new CellAddress(sheet.Id, 1, 1);
        var addrA2 = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(addrA1, Cell.FromValue(new TextValue("Z")));
        sheet.RichTextRuns[addrA1] = MakeRuns("Z");
        sheet.SetCell(addrA2, Cell.FromValue(new TextValue("A")));

        var range = new GridRange(addrA1, addrA2);
        var cmd = new SortCommand(sheet.Id, range, sortByColOffset: 0, ascending: true);
        cmd.Apply(ctx).Success.Should().BeTrue();

        // After sort: row1=A, row2=Z
        sheet.GetValue(1, 1).Should().Be(new TextValue("A"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Z"));

        // Runs should have moved with their row: A has no runs, Z's runs are at row 2
        sheet.RichTextRuns.Should().NotContainKey(addrA1, "row 1 now has 'A' which had no rich runs");
        sheet.RichTextRuns.Should().ContainKey(addrA2, "row 2 now has 'Z' which had rich runs");
        sheet.RichTextRuns[addrA2][0].Text.Should().Be("Z");

        cmd.Revert(ctx);

        // Undo: row1=Z (rich runs restored), row2=A (no runs)
        sheet.GetValue(1, 1).Should().Be(new TextValue("Z"));
        sheet.RichTextRuns.Should().ContainKey(addrA1);
        sheet.RichTextRuns[addrA1][0].Text.Should().Be("Z");
        sheet.RichTextRuns.Should().NotContainKey(addrA2);
    }

    // ── EE5 Clear Contents ────────────────────────────────────────────────────

    [Fact]
    public void ClearContents_RemovesRichTextRunsAndUndoRestoresThem()
    {
        var (wb, sheet, ctx) = Setup();
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, Cell.FromValue(new TextValue("rich")));
        sheet.RichTextRuns[address] = MakeRuns("rich");

        var cmd = new ClearContentsCommand(sheet.Id, new GridRange(address, address));
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.RichTextRuns.Should().NotContainKey(address, "rich runs must be removed when contents are cleared");

        cmd.Revert(ctx);

        sheet.RichTextRuns.Should().ContainKey(address);
        sheet.RichTextRuns[address][0].Text.Should().Be("rich");
    }

    [Fact]
    public void ClearContents_RichCellDoesNotResurrectRunsOnSubsequentRead()
    {
        // Verify that after clear, there is no orphaned entry that could be read back.
        var (wb, sheet, ctx) = Setup();
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, Cell.FromValue(new TextValue("rich")));
        sheet.RichTextRuns[address] = MakeRuns("rich");

        var cmd = new ClearContentsCommand(sheet.Id, new GridRange(address, address));
        cmd.Apply(ctx);

        // Double-read: must still be gone
        sheet.RichTextRuns.ContainsKey(address).Should().BeFalse();
        sheet.RichTextRuns.TryGetValue(address, out _).Should().BeFalse();
    }

    // ── EE5 FillCells ────────────────────────────────────────────────────────

    [Fact]
    public void FillDown_CopiesRichTextRunsToTargetCells()
    {
        var (wb, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(source, Cell.FromValue(new TextValue("rich")));
        sheet.RichTextRuns[source] = MakeRuns("rich");

        var cmd = new FillCellsCommand(sheet.Id, new GridRange(source, target), FillCellsDirection.Down);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.RichTextRuns.Should().ContainKey(target);
        sheet.RichTextRuns[target][0].Text.Should().Be("rich");
    }

    [Fact]
    public void FillDown_UndoRestoresTargetRichTextRuns()
    {
        var (wb, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(source, Cell.FromValue(new TextValue("rich")));
        sheet.RichTextRuns[source] = MakeRuns("rich");
        // put old runs at target
        sheet.RichTextRuns[target] = MakeRuns("old-runs");

        var cmd = new FillCellsCommand(sheet.Id, new GridRange(source, target), FillCellsDirection.Down);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        // Target runs should be restored to "old-runs"
        sheet.RichTextRuns.Should().ContainKey(target);
        sheet.RichTextRuns[target][0].Text.Should().Be("old-runs");
    }

    // ── EE5 RemoveDuplicateRows ───────────────────────────────────────────────

    [Fact]
    public void RemoveDuplicateRows_RichRunsFollowSurvivingRowsAndDuplicatesAreRemoved()
    {
        var (wb, sheet, ctx) = Setup();
        var addrA1 = new CellAddress(sheet.Id, 1, 1);
        var addrA2 = new CellAddress(sheet.Id, 2, 1);
        var addrA3 = new CellAddress(sheet.Id, 3, 1);
        // row1 = "unique" (rich), row2 = "dup" (no rich), row3 = "dup" (rich) → dup removed
        sheet.SetCell(addrA1, Cell.FromValue(new TextValue("unique")));
        sheet.RichTextRuns[addrA1] = MakeRuns("unique");
        sheet.SetCell(addrA2, Cell.FromValue(new TextValue("dup")));
        sheet.SetCell(addrA3, Cell.FromValue(new TextValue("dup")));
        sheet.RichTextRuns[addrA3] = MakeRuns("dup-rich");

        var range = new GridRange(addrA1, addrA3);
        var cmd = new RemoveDuplicateRowsCommand(sheet.Id, range);
        cmd.Apply(ctx).Success.Should().BeTrue();

        // row1 = "unique" still, row2 = "dup" (first occurrence), row3 = cleared
        sheet.RichTextRuns.Should().ContainKey(addrA1);
        // row3's duplicate-rich-runs must be gone (cleared after compaction)
        sheet.RichTextRuns.Should().NotContainKey(addrA3);
    }

    [Fact]
    public void RemoveDuplicateRows_UndoRestoresAllRichRuns()
    {
        var (wb, sheet, ctx) = Setup();
        var addrA1 = new CellAddress(sheet.Id, 1, 1);
        var addrA2 = new CellAddress(sheet.Id, 2, 1);
        var addrA3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(addrA1, Cell.FromValue(new TextValue("unique")));
        sheet.RichTextRuns[addrA1] = MakeRuns("unique");
        sheet.SetCell(addrA2, Cell.FromValue(new TextValue("dup")));
        sheet.SetCell(addrA3, Cell.FromValue(new TextValue("dup")));
        sheet.RichTextRuns[addrA3] = MakeRuns("dup-rich");

        var range = new GridRange(addrA1, addrA3);
        var cmd = new RemoveDuplicateRowsCommand(sheet.Id, range);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.RichTextRuns.Should().ContainKey(addrA1);
        sheet.RichTextRuns[addrA1][0].Text.Should().Be("unique");
        sheet.RichTextRuns.Should().ContainKey(addrA3);
        sheet.RichTextRuns[addrA3][0].Text.Should().Be("dup-rich");
    }
}
