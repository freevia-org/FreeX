using FreeX.Core.Model;
using FreeX.Core.Commands;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R47-meta-1 / R47-sibling-guard-asymmetry-sweep-2/-3/-5: R46 added a CSE-array/dynamic-spill split
/// guard (CommandGuards.RejectIfSplitsArray + InsertCellsCommand.ArrayMembersWithinShiftRegion) to
/// InsertRowsCommand only. This left DeleteRowsCommand, InsertColumnsCommand/DeleteColumnsCommand,
/// SortCommand, and RemoveDuplicateRowsCommand able to silently split a live legacy CSE array or
/// dynamic-array spill — shifting/clearing/rewriting some of its members while leaving others alone
/// — instead of refusing the way real Excel does with "You cannot change part of an array."
/// </summary>
public sealed class Round47SiblingGuardAsymmetrySweepTests
{
    private const string CannotChangePartOfArray = "You cannot change part of an array.";

    // ── R47-meta-1: DeleteRowsCommand ──────────────────────────────────────────────────────────

    [Fact]
    public void DeleteRows_ThroughMiddleOfDynamicArraySpill_IsRejected()
    {
        var (workbook, sheet, ctx) = TestWorkbookFixture.CreateContext();
        var anchor = new CellAddress(sheet.Id, 5, 2); // B5
        sheet.SetFormula(anchor, "SEQUENCE(3,1)");
        sheet.GetCell(anchor)!.Value = new NumberValue(1);
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) },
        })); // spills B5:B7 = 1,2,3

        // Deleting row 6 alone falls strictly inside the spill's own row extent (5..7): row 5 stays
        // put, row 6 is removed, row 7 shifts up to row 6 — splitting the array.
        var outcome = new DeleteRowsCommand(sheet.Id, startRow: 6, count: 1).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be(CannotChangePartOfArray);

        sheet.TryGetSpillExtent(anchor, out var rows, out var cols).Should().BeTrue();
        rows.Should().Be(3u);
        cols.Should().Be(1u);
        sheet.GetValue(5, 2).Should().Be(new NumberValue(1));
        sheet.GetValue(6, 2).Should().Be(new NumberValue(2));
        sheet.GetValue(7, 2).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void DeleteRows_AboveDynamicArraySpill_StillSucceedsAndShiftsSpillUp()
    {
        var (workbook, sheet, ctx) = TestWorkbookFixture.CreateContext();
        var anchor = new CellAddress(sheet.Id, 5, 2); // B5
        sheet.SetFormula(anchor, "SEQUENCE(3,1)");
        sheet.GetCell(anchor)!.Value = new NumberValue(1);
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) },
        })); // spills B5:B7 = 1,2,3

        // Deleting row 2 (entirely above and unrelated to the array) shifts the whole array up as
        // one unit — the already-correct case this new guard must not break.
        var outcome = new DeleteRowsCommand(sheet.Id, startRow: 2, count: 1).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var newAnchor = new CellAddress(sheet.Id, 4, 2); // B4
        sheet.TryGetSpillExtent(newAnchor, out var rows, out var cols).Should().BeTrue();
        rows.Should().Be(3u);
        cols.Should().Be(1u);
        sheet.GetValue(4, 2).Should().Be(new NumberValue(1));
        sheet.GetValue(5, 2).Should().Be(new NumberValue(2));
        sheet.GetValue(6, 2).Should().Be(new NumberValue(3));
    }

    // ── R47-sibling-guard-asymmetry-sweep-2: InsertColumnsCommand / DeleteColumnsCommand ───────

    [Fact]
    public void InsertColumns_ThroughMiddleOfDynamicArraySpill_IsRejected()
    {
        var (workbook, sheet, ctx) = TestWorkbookFixture.CreateContext();
        var anchor = new CellAddress(sheet.Id, 2, 2); // B2
        sheet.SetFormula(anchor, "SEQUENCE(1,4)");
        sheet.GetCell(anchor)!.Value = new NumberValue(1);
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[1, 4]
        {
            { new NumberValue(1), new NumberValue(2), new NumberValue(3), new NumberValue(4) },
        })); // spills B2:E2 (cols 2..5) = 1,2,3,4

        // Inserting a column before column D (col 4) falls strictly inside the spill's own column
        // extent (2..5): columns 2-3 stay put, columns 4-5 shift right — splitting the array.
        var outcome = new InsertColumnsCommand(sheet.Id, beforeCol: 4, count: 1).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be(CannotChangePartOfArray);

        sheet.TryGetSpillExtent(anchor, out var rows, out var cols).Should().BeTrue();
        rows.Should().Be(1u);
        cols.Should().Be(4u);
        sheet.GetValue(2, 2).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 3).Should().Be(new NumberValue(2));
        sheet.GetValue(2, 4).Should().Be(new NumberValue(3));
        sheet.GetValue(2, 5).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void InsertColumns_BeforeDynamicArraySpill_StillSucceedsAndShiftsSpillRight()
    {
        var (workbook, sheet, ctx) = TestWorkbookFixture.CreateContext();
        var anchor = new CellAddress(sheet.Id, 2, 2); // B2
        sheet.SetFormula(anchor, "SEQUENCE(1,4)");
        sheet.GetCell(anchor)!.Value = new NumberValue(1);
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[1, 4]
        {
            { new NumberValue(1), new NumberValue(2), new NumberValue(3), new NumberValue(4) },
        })); // spills B2:E2 (cols 2..5) = 1,2,3,4

        // Inserting before column A (col 1, entirely left of and unrelated to the array) shifts the
        // whole array right as one unit — the already-correct case this new guard must not break.
        var outcome = new InsertColumnsCommand(sheet.Id, beforeCol: 1, count: 1).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var newAnchor = new CellAddress(sheet.Id, 2, 3); // C2
        sheet.TryGetSpillExtent(newAnchor, out var rows, out var cols).Should().BeTrue();
        rows.Should().Be(1u);
        cols.Should().Be(4u);
        sheet.GetValue(2, 3).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 4).Should().Be(new NumberValue(2));
        sheet.GetValue(2, 5).Should().Be(new NumberValue(3));
        sheet.GetValue(2, 6).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void DeleteColumns_ThroughMiddleOfDynamicArraySpill_IsRejected()
    {
        var (workbook, sheet, ctx) = TestWorkbookFixture.CreateContext();
        var anchor = new CellAddress(sheet.Id, 2, 2); // B2
        sheet.SetFormula(anchor, "SEQUENCE(1,4)");
        sheet.GetCell(anchor)!.Value = new NumberValue(1);
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[1, 4]
        {
            { new NumberValue(1), new NumberValue(2), new NumberValue(3), new NumberValue(4) },
        })); // spills B2:E2 (cols 2..5) = 1,2,3,4

        // Deleting column D (col 4) alone falls strictly inside the spill's own column extent
        // (2..5): columns 2-3 stay put, column 5 shifts left into 4 — splitting the array.
        var outcome = new DeleteColumnsCommand(sheet.Id, startCol: 4, count: 1).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be(CannotChangePartOfArray);

        sheet.TryGetSpillExtent(anchor, out var rows, out var cols).Should().BeTrue();
        rows.Should().Be(1u);
        cols.Should().Be(4u);
        sheet.GetValue(2, 2).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 3).Should().Be(new NumberValue(2));
        sheet.GetValue(2, 4).Should().Be(new NumberValue(3));
        sheet.GetValue(2, 5).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void DeleteColumns_BeforeDynamicArraySpill_StillSucceedsAndShiftsSpillLeft()
    {
        var (workbook, sheet, ctx) = TestWorkbookFixture.CreateContext();
        var anchor = new CellAddress(sheet.Id, 2, 2); // B2
        sheet.SetFormula(anchor, "SEQUENCE(1,4)");
        sheet.GetCell(anchor)!.Value = new NumberValue(1);
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[1, 4]
        {
            { new NumberValue(1), new NumberValue(2), new NumberValue(3), new NumberValue(4) },
        })); // spills B2:E2 (cols 2..5) = 1,2,3,4

        // Deleting column A (col 1, entirely left of and unrelated to the array) shifts the whole
        // array left as one unit — the already-correct case this new guard must not break.
        var outcome = new DeleteColumnsCommand(sheet.Id, startCol: 1, count: 1).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var newAnchor = new CellAddress(sheet.Id, 2, 1); // A2
        sheet.TryGetSpillExtent(newAnchor, out var rows, out var cols).Should().BeTrue();
        rows.Should().Be(1u);
        cols.Should().Be(4u);
        sheet.GetValue(2, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 2).Should().Be(new NumberValue(2));
        sheet.GetValue(2, 3).Should().Be(new NumberValue(3));
        sheet.GetValue(2, 4).Should().Be(new NumberValue(4));
    }

    // ── R47-sibling-guard-asymmetry-sweep-3: SortCommand ────────────────────────────────────────

    [Fact]
    public void Sort_RangePartiallyCoveringDynamicArraySpill_IsRejected()
    {
        var (workbook, sheet, ctx) = TestWorkbookFixture.CreateContext();
        var anchor = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.SetFormula(anchor, "SEQUENCE(3,1)");
        sheet.GetCell(anchor)!.Value = new NumberValue(1);
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) },
        })); // spills A1:A3 = 1,2,3

        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(30))); // B1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new NumberValue(10))); // B2

        // Range A1:B2 covers only 2 of the array's 3 rows (A3 is left out) — sorting would move
        // row 1's content (including the spill anchor) independently of row 3, splitting the array.
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
        var outcome = new SortCommand(sheet.Id, range, sortByColOffset: 1, ascending: false).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be(CannotChangePartOfArray);

        sheet.TryGetSpillExtent(anchor, out var rows, out var cols).Should().BeTrue();
        rows.Should().Be(3u);
        cols.Should().Be(1u);
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Sort_NormalRangeWithNoArray_StillSucceeds()
    {
        var (workbook, sheet, ctx) = TestWorkbookFixture.CreateContext();
        // An unrelated live spill elsewhere on the sheet must not block sorting a range that never
        // touches it.
        var anchor = new CellAddress(sheet.Id, 10, 10);
        sheet.SetFormula(anchor, "SEQUENCE(2,1)");
        sheet.GetCell(anchor)!.Value = new NumberValue(1);
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[2, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
        }));

        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new NumberValue(2))); // C1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), Cell.FromValue(new NumberValue(1))); // C2

        var range = new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 2, 3));
        var outcome = new SortCommand(sheet.Id, range, sortByColOffset: 0, ascending: true).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(1, 3).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 3).Should().Be(new NumberValue(2));
    }

    // ── R47-sibling-guard-asymmetry-sweep-5: RemoveDuplicateRowsCommand ─────────────────────────

    [Fact]
    public void RemoveDuplicateRows_RangePartiallyCoveringDynamicArraySpill_IsRejected()
    {
        var (workbook, sheet, ctx) = TestWorkbookFixture.CreateContext();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1))); // A1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(1))); // A2 (duplicate of A1)
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(2))); // A3

        var anchor = new CellAddress(sheet.Id, 4, 1); // A4
        sheet.SetFormula(anchor, "SEQUENCE(3,1)");
        sheet.GetCell(anchor)!.Value = new NumberValue(9);
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[3, 1]
        {
            { new NumberValue(9) },
            { new NumberValue(8) },
            { new NumberValue(7) },
        })); // spills A4:A6 = 9,8,7

        // Range A1:A5 covers rows 1-3 plus only 2 of the array's 3 rows (A4, A5) — A6 is left out.
        // Removing duplicates clears/rewrites the whole range, which would split the array.
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));
        var outcome = new RemoveDuplicateRowsCommand(sheet.Id, range).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be(CannotChangePartOfArray);

        sheet.TryGetSpillExtent(anchor, out var rows, out var cols).Should().BeTrue();
        rows.Should().Be(3u);
        cols.Should().Be(1u);
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(4, 1).Should().Be(new NumberValue(9));
        sheet.GetValue(5, 1).Should().Be(new NumberValue(8));
        sheet.GetValue(6, 1).Should().Be(new NumberValue(7));
    }

    [Fact]
    public void RemoveDuplicateRows_NormalRangeWithNoArray_StillSucceeds()
    {
        var (workbook, sheet, ctx) = TestWorkbookFixture.CreateContext();
        // An unrelated live spill elsewhere on the sheet must not block deduping a range that never
        // touches it.
        var anchor = new CellAddress(sheet.Id, 10, 10);
        sheet.SetFormula(anchor, "SEQUENCE(2,1)");
        sheet.GetCell(anchor)!.Value = new NumberValue(1);
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[2, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
        }));

        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new NumberValue(5))); // C1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), Cell.FromValue(new NumberValue(5))); // C2 (duplicate)
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), Cell.FromValue(new NumberValue(6))); // C3

        var range = new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 3, 3));
        var outcome = new RemoveDuplicateRowsCommand(sheet.Id, range).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(1, 3).Should().Be(new NumberValue(5));
        sheet.GetValue(2, 3).Should().Be(new NumberValue(6));
    }
}
