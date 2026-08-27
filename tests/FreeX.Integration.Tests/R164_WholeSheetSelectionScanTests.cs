using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// r164 remediation, dense whole-sheet enumeration. Ctrl+A (<c>SelectAllCells</c>) selects the whole
/// grid unclamped -- 1,048,576 x 16,384 = 17,179,869,184 cells -- and every command below walked
/// <see cref="GridRange.AllCells"/> over the selection, so the gesture hung the synchronous UI
/// thread. Each was measured before the fix: Clear Contents, Merge, Delete Cells, Sort, Remove
/// Duplicates, Clear Comments, the multi-cell Format Painter, Draw Border, the merge-warning scan,
/// the Sort dialog's colour swatches and Translate all ran past a 15-20s budget without returning
/// (Remove Duplicates took 46s), while a whole-COLUMN selection completed in ~100ms.
///
/// Two different remedies, deliberately: operations that only touch cells holding something are
/// narrowed to the populated range (a cell limit would newly reject select-all Delete, which works
/// today), while operations whose work is genuinely per destination cell -- Merge records every
/// covered cell's pre-merge style, Format Painter tiles a format onto each one -- get the same cell
/// cap the tiled paste/fill paths use.
///
/// Each test asserts completion, which is the whole point: before the fix these never returned.
/// The budget is generous so the suite does not turn timing-sensitive.
/// </summary>
public class R164_WholeSheetSelectionScanTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) Setup()
    {
        var workbook = new Workbook("R164WholeSheet");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("b"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("a"));
        return (workbook, sheet, new TestCommandContext(workbook));
    }

    private static GridRange WholeSheet(SheetId id) =>
        new(new CellAddress(id, 1, 1), new CellAddress(id, CellAddress.MaxRow, CellAddress.MaxCol));

    private static T Within<T>(Func<T> run)
    {
        var task = Task.Run(run);
        task.Wait(Budget).Should().BeTrue("the whole-sheet scan must not hang the UI thread");
        return task.Result;
    }

    private static void Within(Action run) => Within<bool>(() => { run(); return true; });

    [Fact]
    public void ClearContents_WholeSheetSelection_ClearsThePopulatedCellsInsteadOfHanging()
    {
        var (_, sheet, ctx) = Setup();

        var outcome = Within(() => new ClearContentsCommand(sheet.Id, WholeSheet(sheet.Id)).Apply(ctx));

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(BlankValue.Instance);
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void ClearContents_WholeSheetSelection_StillUndoesExactly()
    {
        var (_, sheet, ctx) = Setup();
        var command = new ClearContentsCommand(sheet.Id, WholeSheet(sheet.Id));

        Within(() => command.Apply(ctx));
        command.Revert(ctx);

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new NumberValue(2));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new TextValue("a"));
    }

    [Fact]
    public void ClearContents_BoundedSelection_StillClearsEveryCellInIt()
    {
        // Sibling/no-regression: narrowing must not change what a normal, bounded clear does.
        var (_, sheet, ctx) = Setup();
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));

        var outcome = new ClearContentsCommand(sheet.Id, range).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        outcome.AffectedCells.Should().HaveCount(4);
    }

    [Fact]
    public void ClearComments_WholeSheetSelection_ClearsTheNotesInsteadOfHanging()
    {
        var (_, sheet, ctx) = Setup();
        sheet.Comments[new CellAddress(sheet.Id, 1, 1)] = "note";

        var outcome = Within(() => new ClearCommentsCommand(sheet.Id, WholeSheet(sheet.Id)).Apply(ctx));

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.Comments.Should().BeEmpty();
    }

    [Fact]
    public void ClearComments_LeavesNotesOutsideTheSelectionAlone()
    {
        var (_, sheet, ctx) = Setup();
        sheet.Comments[new CellAddress(sheet.Id, 1, 1)] = "inside";
        sheet.Comments[new CellAddress(sheet.Id, 9, 9)] = "outside";
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));

        new ClearCommentsCommand(sheet.Id, range).Apply(ctx);

        sheet.Comments.Should().ContainKey(new CellAddress(sheet.Id, 9, 9));
        sheet.Comments.Should().NotContainKey(new CellAddress(sheet.Id, 1, 1));
    }

    [Fact]
    public void DeleteCells_WholeSheetSelection_CompletesInsteadOfHanging()
    {
        var (_, sheet, ctx) = Setup();

        var outcome = Within(() =>
            new DeleteCellsCommand(sheet.Id, WholeSheet(sheet.Id), DeleteCellsShiftDirection.Up).Apply(ctx));

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void Sort_WholeSheetSelection_SortsThePopulatedRowsInsteadOfHanging()
    {
        var (_, sheet, ctx) = Setup();

        var outcome = Within(() => new SortCommand(sheet.Id, WholeSheet(sheet.Id), 0, true).Apply(ctx));

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new NumberValue(1));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Sort_KeyColumnOffsetStillResolvesAgainstTheSelectionStart()
    {
        // The narrowing must trim only the END of the range: the sort key is an offset from
        // Start.Col, so moving the start would silently sort by a different column.
        var workbook = new Workbook("R164SortOffset");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        // Data sits in columns C/D, so a selection anchored at column A has empty leading columns.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("first"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new TextValue("second"));

        var outcome = Within(() => new SortCommand(sheet.Id, WholeSheet(sheet.Id), 2, true).Apply(ctx));

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        // Offset 2 from column A is column C -- the numeric column -- so the rows swap.
        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(new NumberValue(1));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 4)).Should().Be(new TextValue("second"));
    }

    [Fact]
    public void RemoveDuplicates_WholeSheetSelection_CompletesInsteadOfHanging()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("b"));

        var outcome = Within(() => new RemoveDuplicateRowsCommand(sheet.Id, WholeSheet(sheet.Id)).Apply(ctx));

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(new CellAddress(sheet.Id, 3, 1)).Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void Merge_WholeSheetSelection_IsRejectedWithACellLimit()
    {
        // Merge blanks every covered cell and records its pre-merge style for Unmerge, so unlike
        // Clear there is no populated-cells shortcut -- it takes the tiled-destination cap instead.
        var (_, sheet, ctx) = Setup();

        var outcome = Within(() => new MergeCellsCommand(sheet.Id, WholeSheet(sheet.Id)).Apply(ctx));

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("too large");
    }

    [Fact]
    public void Merge_AnOrdinarySelectionStillMerges()
    {
        var (_, sheet, ctx) = Setup();
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));

        var outcome = new MergeCellsCommand(sheet.Id, range).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.MergedRegions.Should().Contain(range);
    }

    [Fact]
    public void FormatPainter_MultiCellSourceOntoWholeSheet_IsRejectedWithACellLimit()
    {
        var (workbook, sheet, ctx) = Setup();
        var source = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));

        var outcome = Within(() =>
            FormatPainterCommandFactory.Create(workbook, sheet, source, WholeSheet(sheet.Id)).Apply(ctx));

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("too large");
    }

    [Fact]
    public void FormatPainter_AnOrdinaryPaintStillApplies()
    {
        var (workbook, sheet, ctx) = Setup();
        var source = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
        var target = new GridRange(new CellAddress(sheet.Id, 5, 5), new CellAddress(sheet.Id, 6, 6));

        var outcome = FormatPainterCommandFactory.Create(workbook, sheet, source, target).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
    }

    [Fact]
    public void MergeContentAnalysis_WholeSheetSelection_ReportsThePopulatedCellsInsteadOfHanging()
    {
        var (_, sheet, _) = Setup();

        var plan = Within(() => CellMergePlanner.AnalyzeContent(sheet, WholeSheet(sheet.Id), perRow: false));

        plan.WouldLoseContent.Should().BeTrue();
        plan.Entries.Should().HaveCount(4);
        // Row-major order is preserved, so the top-left cell is still reported first.
        plan.Entries[0].Address.Should().Be(new CellAddress(sheet.Id, 1, 1));
        plan.Entries[0].IsTopLeft.Should().BeTrue();
    }

    [Fact]
    public void SortDialogColourChoices_WholeSheetSelection_ReturnInsteadOfHanging()
    {
        var (workbook, sheet, _) = Setup();

        var choices = Within(() => SortDialogPlanner.BuildColorChoices(workbook, sheet, WholeSheet(sheet.Id)));

        choices.Should().NotBeEmpty();
    }

    [Fact]
    public void Translate_WholeSheetTarget_PlansOnlyTheCellsItWrites()
    {
        var (_, sheet, _) = Setup();

        var planned = Within(() => TranslateDialogPlanner.TryPlan(
            sheet.Id,
            new CellAddress(sheet.Id, 1, 1),
            "one\ntwo\nthree",
            "A1:XFD1048576",
            "en",
            "fr",
            out var plan,
            out _)
            ? plan
            : null);

        planned.Should().NotBeNull();
        planned!.Writes.Should().HaveCount(3);
        planned.Writes[0].Address.Should().Be(new CellAddress(sheet.Id, 1, 1));
    }

    [Fact]
    public void Translate_ASmallTargetStillOverflowsIntoItsLastCell()
    {
        // Sibling/no-regression: taking only as many addresses as there are lines must not change
        // the overflow rule, which is decided by the range's full capacity.
        var (_, sheet, _) = Setup();

        TranslateDialogPlanner.TryPlan(
            sheet.Id,
            new CellAddress(sheet.Id, 1, 1),
            "one\ntwo\nthree",
            "A1:A2",
            "en",
            "fr",
            out var plan,
            out _).Should().BeTrue();

        plan.Writes.Should().HaveCount(2);
        plan.Writes[1].Text.Should().Be("two\nthree");
    }
}
