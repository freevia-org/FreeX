using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for group N-subtotal-outline:
/// J22 — Data &gt; Subtotal must build a row outline (detail rows at level 1) so the outline
/// pane's collapse/expand controls work, like Excel.
/// J42 — Unhide Rows/Columns must also clear outline-collapse hidden state for the unhidden
/// range, matching Excel's "Unhide reveals everything" behavior regardless of hide mechanism.
/// </summary>
public sealed class SubtotalOutlineAndUnhideGroupCommandTests
{
    [Fact]
    public void SubtotalCommand_SummaryBelowData_MarksDetailRowsAtOutlineLevel1AndLeavesTotalsAtLevel0()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(25));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2));

        var command = new SubtotalCommand(sheet.Id, range, groupByColumnOffset: 0, subtotalColumnOffset: 1);
        var outcome = command.Apply(context);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // Final layout: 1=header, 2-3=East detail, 4=East Total, 5-6=West detail, 7=West Total, 8=Grand Total.
        sheet.GetValue(4, 1).Should().Be(new TextValue("East Total"));
        sheet.GetValue(7, 1).Should().Be(new TextValue("West Total"));
        sheet.GetValue(8, 1).Should().Be(new TextValue("Grand Total"));

        // Detail rows for each group get outline level 1, so the outline pane can collapse to
        // just the subtotal/grand-total rows, exactly like Excel.
        sheet.RowOutlineLevels.GetValueOrDefault(2u).Should().Be(1);
        sheet.RowOutlineLevels.GetValueOrDefault(3u).Should().Be(1);
        sheet.RowOutlineLevels.GetValueOrDefault(5u).Should().Be(1);
        sheet.RowOutlineLevels.GetValueOrDefault(6u).Should().Be(1);

        // Header, subtotal rows, and grand total row stay at level 0 (not part of the outline).
        sheet.RowOutlineLevels.GetValueOrDefault(1u).Should().Be(0);
        sheet.RowOutlineLevels.GetValueOrDefault(4u).Should().Be(0);
        sheet.RowOutlineLevels.GetValueOrDefault(7u).Should().Be(0);
        sheet.RowOutlineLevels.GetValueOrDefault(8u).Should().Be(0);

        // Collapsing level 1 should hide exactly the detail rows, driving the same
        // GroupHiddenRows mechanism as manual Group Rows.
        new CollapseRowGroupCommand(sheet.Id, 1).Apply(context).Success.Should().BeTrue();
        sheet.IsRowEffectivelyHidden(2).Should().BeTrue();
        sheet.IsRowEffectivelyHidden(3).Should().BeTrue();
        sheet.IsRowEffectivelyHidden(5).Should().BeTrue();
        sheet.IsRowEffectivelyHidden(6).Should().BeTrue();
        sheet.IsRowEffectivelyHidden(4).Should().BeFalse();
        sheet.IsRowEffectivelyHidden(7).Should().BeFalse();
        sheet.IsRowEffectivelyHidden(8).Should().BeFalse();
    }

    [Fact]
    public void SubtotalCommand_SummaryAboveData_MarksDetailRowsAtOutlineLevel1()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(25));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2));

        var command = new SubtotalCommand(
            sheet.Id,
            range,
            groupByColumnOffset: 0,
            subtotalColumnOffset: 1,
            summaryBelowData: false);
        var outcome = command.Apply(context);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // Final layout: 1=header, 2=Grand Total, 3=East Total, 4-5=East detail, 6=West Total, 7-8=West detail.
        sheet.GetValue(2, 1).Should().Be(new TextValue("Grand Total"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("East Total"));
        sheet.GetValue(6, 1).Should().Be(new TextValue("West Total"));

        sheet.RowOutlineLevels.GetValueOrDefault(4u).Should().Be(1);
        sheet.RowOutlineLevels.GetValueOrDefault(5u).Should().Be(1);
        sheet.RowOutlineLevels.GetValueOrDefault(7u).Should().Be(1);
        sheet.RowOutlineLevels.GetValueOrDefault(8u).Should().Be(1);

        sheet.RowOutlineLevels.GetValueOrDefault(2u).Should().Be(0);
        sheet.RowOutlineLevels.GetValueOrDefault(3u).Should().Be(0);
        sheet.RowOutlineLevels.GetValueOrDefault(6u).Should().Be(0);
    }

    [Fact]
    public void SubtotalCommand_Revert_RemovesOutlineLevelsItAdded()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));

        var command = new SubtotalCommand(sheet.Id, range, groupByColumnOffset: 0, subtotalColumnOffset: 1);
        command.Apply(context).Success.Should().BeTrue();
        sheet.RowOutlineLevels.Should().NotBeEmpty();

        command.Revert(context);

        sheet.RowOutlineLevels.Should().BeEmpty();
        sheet.GroupHiddenRows.Should().BeEmpty();
    }

    [Fact]
    public void SubtotalCommand_Revert_PreservesOutlineLevelsThatExistedBeforeSubtotal()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        // Pre-existing, unrelated outline grouping on the header row (row 1), before Subtotal runs.
        sheet.RowOutlineLevels[1] = 2;
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));

        var command = new SubtotalCommand(sheet.Id, range, groupByColumnOffset: 0, subtotalColumnOffset: 1);
        command.Apply(context).Success.Should().BeTrue();

        command.Revert(context);

        sheet.RowOutlineLevels.GetValueOrDefault(1u).Should().Be(2);
        sheet.RowOutlineLevels.Should().HaveCount(1);
    }

    [Fact]
    public void SetRowsHiddenCommand_Unhide_ClearsGroupCollapsedRowsWithinRange()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);

        // Group rows 5-10 (outline level 1) and collapse — GroupHiddenRows gains 5-10.
        new GroupRowsCommand(sheet.Id, 5, 10, level: 1).Apply(context).Success.Should().BeTrue();
        new CollapseRowGroupCommand(sheet.Id, 1).Apply(context).Success.Should().BeTrue();
        sheet.GroupHiddenRows.Should().BeEquivalentTo([5u, 6u, 7u, 8u, 9u, 10u]);

        // Manually hide row 3 too, via the plain Hide Rows mechanism.
        new SetRowsHiddenCommand(sheet.Id, 3, 3, hidden: true).Apply(context).Success.Should().BeTrue();
        sheet.HiddenRows.Should().Contain(3u);

        for (uint row = 5; row <= 10; row++)
            sheet.IsRowEffectivelyHidden(row).Should().BeTrue();

        // Unhide Rows over 1-12 (e.g. Ctrl+Shift+9 / header context menu / Format > Unhide Rows)
        // must reveal everything, including the rows hidden by the collapsed outline group.
        var unhide = new SetRowsHiddenCommand(sheet.Id, 1, 12, hidden: false);
        unhide.Apply(context).Success.Should().BeTrue();

        sheet.HiddenRows.Should().BeEmpty();
        sheet.GroupHiddenRows.Should().BeEmpty();
        for (uint row = 1; row <= 12; row++)
            sheet.IsRowEffectivelyHidden(row).Should().BeFalse();

        // The outline grouping itself (RowOutlineLevels) is unaffected — only the collapsed/hidden
        // state is cleared, matching Excel (the +/- outline control still exists, just expanded).
        for (uint row = 5; row <= 10; row++)
            sheet.RowOutlineLevels.GetValueOrDefault(row).Should().Be(1);
    }

    [Fact]
    public void SetRowsHiddenCommand_Unhide_UndoRestoresGroupCollapsedRows()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);
        new GroupRowsCommand(sheet.Id, 5, 10, level: 1).Apply(context).Success.Should().BeTrue();
        new CollapseRowGroupCommand(sheet.Id, 1).Apply(context).Success.Should().BeTrue();

        var unhide = new SetRowsHiddenCommand(sheet.Id, 1, 12, hidden: false);
        unhide.Apply(context).Success.Should().BeTrue();
        sheet.GroupHiddenRows.Should().BeEmpty();

        unhide.Revert(context);

        sheet.GroupHiddenRows.Should().BeEquivalentTo([5u, 6u, 7u, 8u, 9u, 10u]);
    }

    [Fact]
    public void SetColumnsHiddenCommand_Unhide_ClearsGroupCollapsedColumnsWithinRange()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);

        new GroupColumnsCommand(sheet.Id, 5, 10, level: 1).Apply(context).Success.Should().BeTrue();
        new CollapseColGroupCommand(sheet.Id, 1).Apply(context).Success.Should().BeTrue();
        sheet.GroupHiddenCols.Should().BeEquivalentTo([5u, 6u, 7u, 8u, 9u, 10u]);

        new SetColumnsHiddenCommand(sheet.Id, 3, 3, hidden: true).Apply(context).Success.Should().BeTrue();

        var unhide = new SetColumnsHiddenCommand(sheet.Id, 1, 12, hidden: false);
        unhide.Apply(context).Success.Should().BeTrue();

        sheet.HiddenCols.Should().BeEmpty();
        sheet.GroupHiddenCols.Should().BeEmpty();
        for (uint col = 1; col <= 12; col++)
            sheet.IsColEffectivelyHidden(col).Should().BeFalse();
    }

    [Fact]
    public void SetRowsHiddenCommand_Hide_DoesNotTouchGroupHiddenRows()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);
        new GroupRowsCommand(sheet.Id, 5, 10, level: 1).Apply(context).Success.Should().BeTrue();
        new CollapseRowGroupCommand(sheet.Id, 1).Apply(context).Success.Should().BeTrue();
        var snapshotBeforeHide = sheet.GroupHiddenRows.ToArray();

        // A plain Hide Rows (not Unhide) over an overlapping range must not disturb the
        // independent group-collapse hidden set.
        new SetRowsHiddenCommand(sheet.Id, 8, 20, hidden: true).Apply(context).Success.Should().BeTrue();

        sheet.GroupHiddenRows.Should().BeEquivalentTo(snapshotBeforeHide);
    }
}
