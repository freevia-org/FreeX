using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for the review-wave findings G1/G2/G3/G6/G7: keeping
/// <see cref="Sheet.ActiveValueFilterColumns"/> (and its <see cref="Sheet.ValueFilterHiddenRows"/>
/// bookkeeping) consistent across column/row structural edits, AutoFilter toggling, and mixed filter
/// mechanisms. Native-JSON persistence round-tripping (finding G32) is covered in
/// FreeX.Core.IO.Tests/NativeJsonFilterStatePersistenceTests.cs, the test project that references the
/// IO adapter.
/// </summary>
public sealed class FilterStateIntegrityTests
{
    // ── G1: column insert/delete must shift/remove ActiveValueFilterColumns keys ──────────────

    [Fact]
    public void InsertColumns_ShiftsActiveValueFilterColumnsKeyAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        // Column C (3) holds a value filter.
        sheet.ActiveValueFilterColumns[3] = ["Keep"];
        sheet.ValueFilterHiddenRows.Add(5);
        sheet.FilterHiddenRows.Add(5);

        var command = new InsertColumnsCommand(sheet.Id, beforeCol: 2, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();

        // The filter that used to target column C (3) must now target column D (4) — not stay at 3.
        sheet.ActiveValueFilterColumns.Should().ContainKey(4);
        sheet.ActiveValueFilterColumns.Should().NotContainKey(3);
        sheet.ActiveValueFilterColumns[4].Should().BeEquivalentTo(["Keep"]);

        command.Revert(ctx);

        sheet.ActiveValueFilterColumns.Should().ContainKey(3);
        sheet.ActiveValueFilterColumns.Should().NotContainKey(4);
        sheet.ActiveValueFilterColumns[3].Should().BeEquivalentTo(["Keep"]);
    }

    [Fact]
    public void DeleteColumns_RemovesDeletedFilterColumnKeyAndShiftsSurvivorsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        // Column B (2) is deleted outright; column D (4) survives and must shift down to column C (3).
        sheet.ActiveValueFilterColumns[2] = ["DeletedColumnFilter"];
        sheet.ActiveValueFilterColumns[4] = ["SurvivingColumnFilter"];

        var command = new DeleteColumnsCommand(sheet.Id, startCol: 2, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.ActiveValueFilterColumns.Should().NotContainKey(2);
        sheet.ActiveValueFilterColumns.Should().NotContainKey(4);
        sheet.ActiveValueFilterColumns.Should().ContainKey(3);
        sheet.ActiveValueFilterColumns[3].Should().BeEquivalentTo(["SurvivingColumnFilter"]);

        command.Revert(ctx);

        sheet.ActiveValueFilterColumns.Should().ContainKey(2);
        sheet.ActiveValueFilterColumns[2].Should().BeEquivalentTo(["DeletedColumnFilter"]);
        sheet.ActiveValueFilterColumns.Should().ContainKey(4);
        sheet.ActiveValueFilterColumns[4].Should().BeEquivalentTo(["SurvivingColumnFilter"]);
        sheet.ActiveValueFilterColumns.Should().NotContainKey(3);
    }

    // ── G2: row insert/delete must keep ValueFilterHiddenRows shifted alongside FilterHiddenRows ──

    [Fact]
    public void InsertRows_ShiftsValueFilterHiddenRowsAlongsideFilterHiddenRowsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.ActiveValueFilterColumns[1] = ["Keep"];
        sheet.FilterHiddenRows.Add(10);
        sheet.ValueFilterHiddenRows.Add(10);

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 2);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([12u]);
        sheet.ValueFilterHiddenRows.Should().BeEquivalentTo([12u]);

        command.Revert(ctx);

        sheet.FilterHiddenRows.Should().BeEquivalentTo([10u]);
        sheet.ValueFilterHiddenRows.Should().BeEquivalentTo([10u]);
    }

    [Fact]
    public void DeleteRows_ShiftsValueFilterHiddenRowsAlongsideFilterHiddenRowsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.ActiveValueFilterColumns[1] = ["Keep"];
        sheet.FilterHiddenRows.Add(20);
        sheet.ValueFilterHiddenRows.Add(20);

        var command = new DeleteRowsCommand(sheet.Id, startRow: 5, count: 2);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([18u]);
        sheet.ValueFilterHiddenRows.Should().BeEquivalentTo([18u]);

        command.Revert(ctx);

        sheet.FilterHiddenRows.Should().BeEquivalentTo([20u]);
        sheet.ValueFilterHiddenRows.Should().BeEquivalentTo([20u]);
    }

    // ── G3: band-scoped Insert/Delete Cells must not silently corrupt AutoFilter state ─────────

    [Fact]
    public void InsertCellsShiftDown_RejectsWhenBandOverlapsAutoFilterRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:C20", null);
        sheet.FilterHiddenRows.Add(15);
        // Selecting B10:C20 is inside the AutoFilter's column span and row span.
        var range = new GridRange(new CellAddress(sheet.Id, 10, 2), new CellAddress(sheet.Id, 20, 3));

        var outcome = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Down).Apply(ctx);

        outcome.Success.Should().BeFalse();
        // AutoFilter reference and FilterHiddenRows must be untouched — not silently left stale.
        sheet.AutoFilter!.Reference.Should().Be("A1:C20");
        sheet.FilterHiddenRows.Should().BeEquivalentTo([15u]);
    }

    [Fact]
    public void DeleteCellsShiftLeft_RejectsWhenBandOverlapsAutoFilterRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:C20", null);
        var range = new GridRange(new CellAddress(sheet.Id, 10, 2), new CellAddress(sheet.Id, 15, 2));

        var outcome = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Left).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.AutoFilter!.Reference.Should().Be("A1:C20");
    }

    [Fact]
    public void InsertCellsShiftRight_AllowedWhenBandDoesNotOverlapAutoFilterRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:C5", null);
        sheet.SetCell(new CellAddress(sheet.Id, 50, 2), new TextValue("Unrelated"));
        // Far below the AutoFilter's row span — must not be blocked by the guard.
        var range = new GridRange(new CellAddress(sheet.Id, 50, 2), new CellAddress(sheet.Id, 50, 2));

        var outcome = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.AutoFilter!.Reference.Should().Be("A1:C5");
    }

    // ── G6: turning AutoFilter off must clear ActiveValueFilterColumns (with undo restore) ──────

    [Fact]
    public void ToggleAutoFilterOff_ClearsActiveValueFilterColumnsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 2));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        sheet.ActiveValueFilterColumns[1] = ["X"];
        sheet.ValueFilterHiddenRows.Add(4);
        sheet.FilterHiddenRows.Add(4);

        var command = new ToggleWorksheetAutoFilterCommand(sheet.Id, range);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.AutoFilter.Should().BeNull();
        sheet.ActiveValueFilterColumns.Should().BeEmpty();
        sheet.ValueFilterHiddenRows.Should().BeEmpty();

        command.Revert(ctx);

        sheet.AutoFilter.Should().NotBeNull();
        sheet.ActiveValueFilterColumns.Should().ContainKey(1);
        sheet.ActiveValueFilterColumns[1].Should().BeEquivalentTo(["X"]);
        sheet.ValueFilterHiddenRows.Should().BeEquivalentTo([4u]);
    }

    [Fact]
    public void ToggleAutoFilterOffThenOn_DoesNotResurrectStaleColumnFilterOnUnrelatedColumn()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("ColA"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("ColB"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("X"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Y"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("NotX"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Y"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));

        new ToggleWorksheetAutoFilterCommand(sheet.Id, range).Apply(ctx).Success.Should().BeTrue();
        new FilterCommand(sheet.Id, range, filterColOffset: 0, ["X"]).Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u]);

        // Turn the filter off, then back on, and filter only column B this time.
        new ToggleWorksheetAutoFilterCommand(sheet.Id, range).Apply(ctx).Success.Should().BeTrue();
        new ToggleWorksheetAutoFilterCommand(sheet.Id, range).Apply(ctx).Success.Should().BeTrue();
        new FilterCommand(sheet.Id, range, filterColOffset: 1, ["Y"]).Apply(ctx).Success.Should().BeTrue();

        // Column A's stale "X" filter must NOT still be applied — both rows pass column B's "Y" filter.
        sheet.FilterHiddenRows.Should().BeEmpty();
    }

    // ── G7: value-list filter recompute must not discard other filter kinds' hidden rows ────────

    [Fact]
    public void ValueFilter_DoesNotUnhideRowsHiddenByAnotherFilterMechanismOnAnotherColumn()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Score"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new TextValue("East"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));

        // Top-1 filter on column A (Score) hides everything except the top-scoring row (row 2).
        new TopBottomFilterCommand(sheet.Id, range, filterColOffset: 0, count: 1, top: true)
            .Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u]);

        // Now apply a value-list filter on column B (Region): every row passes ("East").
        new FilterCommand(sheet.Id, range, filterColOffset: 1, ["East"]).Apply(ctx).Success.Should().BeTrue();

        // Rows 3 and 4 must remain hidden — Top-1's exclusion on column A must survive.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u]);
    }

    [Fact]
    public void ValueFilter_StillHidesAndUnhidesItsOwnRowsAcrossReapplication()
    {
        // Regression guard against over-correcting G7: FilterCommand must still behave normally
        // when it is the only active filter mechanism.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Drop"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Keep"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));

        new FilterCommand(sheet.Id, range, 0, ["Keep"]).Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u]);

        // Clearing the filter must fully unhide (no other mechanism is active).
        new FilterCommand(sheet.Id, range, 0, []).Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEmpty();
    }
}
