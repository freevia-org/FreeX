using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for round-13 bucket S5 finding R13-meta-1: <see cref="Sheet.ColumnFilterOwnedRows"/>
/// (a column-keyed dictionary whose HashSet values are 1-based row indices owned by that column's
/// condition/color/Top-Bottom/Average AutoFilter) must shift the same way <see cref="Sheet.FilterHiddenRows"/>
/// and <see cref="Sheet.ValueFilterHiddenRows"/> already do on row insert/delete. Left unshifted, the
/// dictionary's row indices go stale the moment rows move: the next clear/recompute of that column's
/// filter consults the wrong (pre-shift) row, fails to find it hidden by anything else, and silently
/// forgets about the row that is ACTUALLY hidden — orphaning it as permanently hidden with no active
/// filter and no UI way to reveal it.
/// </summary>
public sealed class FreeXR13S5Tests
{
    // ── R13-meta-1: InsertRowsCommand must shift ColumnFilterOwnedRows' row values ────────────────

    [Fact]
    public void InsertRows_ShiftsColumnFilterOwnedRowsAndClearingTheColumnFilterUnhidesTheShiftedRow()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // AutoFilter over A1:B10; a condition/color/Top-Bottom/Average filter on column B (2) hides
        // row 5 — recorded both in the union (FilterHiddenRows) and in the column's own ownership set.
        sheet.FilterHiddenRows.Add(5);
        sheet.ColumnFilterOwnedRows[2] = [5];

        // Insert a row above row 5: it (and everything the filter owns) must become row 6.
        var insert = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 1);
        insert.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([6u]);
        // The bug: pre-fix, ColumnFilterOwnedRows[2] is left stale at {5} instead of shifting to {6}.
        sheet.ColumnFilterOwnedRows[2].Should().BeEquivalentTo([6u]);

        // Now clear column B's filter through the real public command path (count:0 == "no criteria").
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 2));
        var clear = new TopBottomFilterCommand(sheet.Id, range, filterColOffset: 1, count: 0, top: true);
        clear.Apply(ctx).Success.Should().BeTrue();

        // Excel behavior: clearing the only active filter on column B must fully reveal row 6 — no
        // orphaned permanently-hidden row. Pre-fix this stays hidden because the clear looked for
        // (and only relinquished) the stale row 5, which was never actually in FilterHiddenRows.
        sheet.FilterHiddenRows.Should().BeEmpty();
        sheet.ColumnFilterOwnedRows[2].Should().BeEmpty();
    }

    [Fact]
    public void DeleteRows_ShiftsColumnFilterOwnedRowsAndUndoRestoresOriginalOwnership()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Column B (2) owns row 20 via a non-value-list filter mechanism; row 5 is unrelated data.
        sheet.FilterHiddenRows.Add(20);
        sheet.ColumnFilterOwnedRows[2] = [20];

        var delete = new DeleteRowsCommand(sheet.Id, startRow: 5, count: 2);
        delete.Apply(ctx).Success.Should().BeTrue();

        // Two rows removed above row 20 -> it must shift down to row 18, exactly like FilterHiddenRows.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([18u]);
        sheet.ColumnFilterOwnedRows[2].Should().BeEquivalentTo([18u]);

        delete.Revert(ctx);

        sheet.FilterHiddenRows.Should().BeEquivalentTo([20u]);
        sheet.ColumnFilterOwnedRows[2].Should().BeEquivalentTo([20u]);
    }
}
