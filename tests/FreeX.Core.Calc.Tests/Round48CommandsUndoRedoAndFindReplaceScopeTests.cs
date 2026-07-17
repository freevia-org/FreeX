using FreeX.Core.Model;
using FreeX.Core.Commands;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R48-commands-undo-redo-inverse-3-1: Undo of a NO-OP Remove Duplicates (0 rows removed) used to
/// wipe every merged region on the sheet, because the no-op branch of
/// <see cref="RemoveDuplicateRowsCommand.Apply"/> took an empty merge snapshot (<c>[]</c>, not
/// <c>null</c>), and Revert unconditionally calls
/// <c>sheet.ReplaceMergedRegions(_mergeSnapshot)</c> whenever the snapshot is non-null — replacing
/// the sheet's entire merge list with that empty list. The fix records no merge snapshot at all
/// (null) when nothing was removed, so Revert's "restore merges" step is skipped entirely and every
/// merge on the sheet — in-range or not — survives undo untouched.
///
/// R48-commands-find-replace-3-1: FindReplaceService had no way to scope a search/replace to an
/// active multi-cell selection (Excel restricts Replace All to the selection whenever more than one
/// cell is selected). The fix adds an optional <c>FindOptions.SelectionScope</c> (a set of ranges —
/// Excel's "sqref" — defaulting to null so every existing caller is unaffected) that both
/// <see cref="FindReplaceService.Find"/> and <see cref="FindReplaceService.TryReplaceAll"/> (which is
/// built on top of Find) honor.
/// </summary>
public sealed class Round48CommandsUndoRedoAndFindReplaceScopeTests
{
    // ── R48-commands-undo-redo-inverse-3-1 ──────────────────────────────────────────────────────

    [Fact]
    public void RemoveDuplicateRowsCommand_UndoAfterNoOpApply_LeavesAllMergesIntact()
    {
        var (_, sheet, ctx) = TestWorkbookFixture.CreateContext();

        // A merge entirely outside the operated range — Remove Duplicates over A1:A3 must never
        // touch it, whether it removes rows or not.
        var outsideMerge = new GridRange(
            new CellAddress(sheet.Id, 10, 5),
            new CellAddress(sheet.Id, 11, 6));
        sheet.AddMergedRegion(outsideMerge);

        // Three distinct rows in the operated range — Remove Duplicates removes nothing.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Alpha"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Beta"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Gamma"));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));
        var command = new RemoveDuplicateRowsCommand(sheet.Id, range);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();
        command.RemovedRowCount.Should().Be(0, "all three rows are distinct");
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(outsideMerge);

        // Undo of the no-op must leave every merge on the sheet exactly as it was.
        command.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle(
                "undo of a 0-removed Remove Duplicates run must not wipe merges elsewhere on the sheet")
            .Which.Should().Be(outsideMerge);
    }

    [Fact]
    public void RemoveDuplicateRowsCommand_UndoAfterRealRemoval_RestoresExactPriorMerges()
    {
        // Sibling no-regression case: a genuine removal (RemovedRowCount > 0) must still restore
        // every prior merge — in-range or not — exactly as before, the same guarantee the no-op
        // case above now provides.
        var (_, sheet, ctx) = TestWorkbookFixture.CreateContext();

        var outsideMerge = new GridRange(
            new CellAddress(sheet.Id, 10, 5),
            new CellAddress(sheet.Id, 11, 6));
        sheet.AddMergedRegion(outsideMerge);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Dup"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Dup")); // duplicate of row 1
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Unique"));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));
        var command = new RemoveDuplicateRowsCommand(sheet.Id, range);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();
        command.RemovedRowCount.Should().Be(1, "row 2 duplicates row 1");

        command.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(outsideMerge);
        sheet.GetValue(1, 1).Should().Be(new TextValue("Dup"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Dup"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Unique"));
    }

    // ── R48-commands-find-replace-3-1 ───────────────────────────────────────────────────────────

    [Fact]
    public void FindReplaceService_ReplaceAll_WithSelectionScope_OnlyTouchesCellsInsideIt()
    {
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();
        var bus = new CommandBus(_ => new TestCommandContext(workbook));

        // "Total" appears both inside the user's intended selection (B2:B3) and outside it
        // (C5) — matching the finding's concrete scenario.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Total")); // B2 — in scope
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Total")); // B3 — in scope
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new TextValue("Total")); // C5 — out of scope

        var selection = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 2));
        var options = new FindOptions(
            Within: FindWithin.Sheet,
            CurrentSheetId: sheet.Id,
            SelectionScope: [selection]);

        var result = FindReplaceService.TryReplaceAll(
            workbook, bus, "Total", "Sum", options);

        result.Failure.Should().BeNull();
        result.ReplacedCount.Should().Be(2, "only the two in-selection matches should be replaced");
        sheet.GetValue(2, 2).Should().Be(new TextValue("Sum"));
        sheet.GetValue(3, 2).Should().Be(new TextValue("Sum"));
        sheet.GetValue(5, 3).Should().Be(new TextValue("Total"), "C5 is outside the selection scope and must be left untouched");
    }

    [Fact]
    public void FindReplaceService_ReplaceAll_WithoutSelectionScope_StillReplacesWholeSheet()
    {
        // Sibling no-regression case: every existing caller (no SelectionScope supplied) must keep
        // replacing across the whole Within-scoped sheet, exactly as before this option existed.
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();
        var bus = new CommandBus(_ => new TestCommandContext(workbook));

        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new TextValue("Total"));

        var replacedCount = FindReplaceService.ReplaceAll(workbook, bus, "Total", "Sum");

        replacedCount.Should().Be(3);
        sheet.GetValue(2, 2).Should().Be(new TextValue("Sum"));
        sheet.GetValue(3, 2).Should().Be(new TextValue("Sum"));
        sheet.GetValue(5, 3).Should().Be(new TextValue("Sum"));
    }
}
