using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class FilterCommandTests
{
    [Fact]
    public void FilterCommand_ReapplyingSameFilter_RevertKeepsCurrentRowsHidden()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Drop"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Keep"));
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));
        var ctx = new TestCommandContext(wb);

        var first = new FilterCommand(sheet.Id, range, 0, ["Keep"]);
        first.Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u]);

        var second = new FilterCommand(sheet.Id, range, 0, ["Keep"]);
        second.Apply(ctx).Success.Should().BeTrue();
        second.Revert(ctx);

        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u]);
    }

    [Fact]
    public void FilterCommand_RevertChangedFilterRestoresPreviousFilterRows()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Drop"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Keep"));
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));
        var ctx = new TestCommandContext(wb);

        new FilterCommand(sheet.Id, range, 0, ["Keep"]).Apply(ctx).Success.Should().BeTrue();
        var changed = new FilterCommand(sheet.Id, range, 0, ["Drop"]);
        changed.Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u]);

        changed.Revert(ctx);

        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u]);
    }

    [Fact]
    public void FilterCommand_FilteringMultipleColumns_AndsHiddenRowsAcrossColumns()
    {
        // Regression guard (verified NOT a bug): filtering column A must not un-hide rows that a
        // prior filter on column B already hid, and vice versa — Excel ANDs AutoFilter criteria
        // across columns. FilterCommand only adds/removes the rows implied by its OWN column's
        // criteria (never Clear()s the whole set), so applying a second column's filter must
        // preserve the first column's hidden rows.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        // Column A (offset 0): row 3 is "Drop", everything else "Keep".
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("ColA"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Drop"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Keep"));
        // Column B (offset 1): row 5 is "Drop", everything else "Keep".
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("ColB"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new TextValue("Drop"));
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 2));
        var ctx = new TestCommandContext(wb);

        // Filter column A first: hides row 3.
        new FilterCommand(sheet.Id, range, 0, ["Keep"]).Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u]);

        // Now filter column B: must hide row 5 WITHOUT un-hiding row 3.
        new FilterCommand(sheet.Id, range, 1, ["Keep"]).Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 5u]);
    }

    [Fact]
    public void TopBottomFilter_KeepsTopTiesByRowAndPreservesRowsOutsideRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Score"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(9));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("n/a"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new NumberValue(8));
        sheet.FilterHiddenRows.UnionWith([2u, 20u]);

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 6, 1));
        var command = new TopBottomFilterCommand(sheet.Id, range, 0, count: 2, top: true);

        var outcome = command.Apply(new TestCommandContext(wb));

        outcome.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([4u, 5u, 6u, 20u]);
    }

}
