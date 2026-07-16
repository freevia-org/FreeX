using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R45-commands-sort-filter-interaction-3-1: SortCommand.Apply built its row worklist from every
/// physical row in the range unconditionally, sorting filter-hidden rows together with visible
/// ones and merely carrying each row's FilterHiddenRows/ValueFilterHiddenRows flag along with its
/// data to the new position. Real Excel's own Sort documentation states hidden rows in a filtered
/// range are NOT sorted — a row the active AutoFilter (or a Top-N/Average/condition/color column
/// filter) is hiding must stay at its own physical row, completely untouched, while only the rows
/// that are actually visible get reordered among the visible row slots.
/// </summary>
public sealed class R45_SortFilterHiddenRowsTests
{
    [Fact]
    public void Apply_PinsFilterHiddenRowInPlace_AndOnlyReordersVisibleRows()
    {
        // Exact failureScenario from the finding: A1:B5 header at row 1, AutoFilter active on
        // column B, currently showing only Region="West". row2=Charlie/West(visible),
        // row3=Alice/East(hidden by filter), row4=Bob/West(visible), row5=Dave/East(hidden).
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sid, 1, 2), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Charlie"));
        sheet.SetCell(new CellAddress(sid, 2, 2), new TextValue("West"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Alice"));
        sheet.SetCell(new CellAddress(sid, 3, 2), new TextValue("East"));
        sheet.SetCell(new CellAddress(sid, 4, 1), new TextValue("Bob"));
        sheet.SetCell(new CellAddress(sid, 4, 2), new TextValue("West"));
        sheet.SetCell(new CellAddress(sid, 5, 1), new TextValue("Dave"));
        sheet.SetCell(new CellAddress(sid, 5, 2), new TextValue("East"));

        // AutoFilter on column B keeps "West" and hides the "East" rows (3 and 5).
        var filterRange = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 5, 2));
        new FilterCommand(sid, filterRange, 1, ["West"]).Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 5u]);

        // User selects A2:B5 and sorts ascending by column A (Name).
        var sortRange = new GridRange(new CellAddress(sid, 2, 1), new CellAddress(sid, 5, 2));
        var sortCommand = new SortCommand(sid, sortRange, sortByColOffset: 0, ascending: true);
        sortCommand.Apply(ctx).Success.Should().BeTrue();

        // Expected result per the finding: row2=Bob/West, row3=Alice/East (untouched),
        // row4=Charlie/West, row5=Dave/East (untouched). Only the two VISIBLE rows (Charlie,
        // Bob) are reordered between their own two slots (rows 2 and 4); the hidden rows never
        // move and never participate in the comparison.
        sheet.GetValue(2, 1).Should().Be(new TextValue("Bob"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("West"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Alice"));
        sheet.GetValue(3, 2).Should().Be(new TextValue("East"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Charlie"));
        sheet.GetValue(4, 2).Should().Be(new TextValue("West"));
        sheet.GetValue(5, 1).Should().Be(new TextValue("Dave"));
        sheet.GetValue(5, 2).Should().Be(new TextValue("East"));

        // The filter-hidden set must still name rows 3 and 5 — they were pinned in place, not
        // permuted to new positions.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 5u]);
    }

    [Fact]
    public void Revert_RestoresOriginalRowsIncludingTheHiddenOnes()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sid, 1, 2), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Charlie"));
        sheet.SetCell(new CellAddress(sid, 2, 2), new TextValue("West"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Alice"));
        sheet.SetCell(new CellAddress(sid, 3, 2), new TextValue("East"));
        sheet.SetCell(new CellAddress(sid, 4, 1), new TextValue("Bob"));
        sheet.SetCell(new CellAddress(sid, 4, 2), new TextValue("West"));
        sheet.SetCell(new CellAddress(sid, 5, 1), new TextValue("Dave"));
        sheet.SetCell(new CellAddress(sid, 5, 2), new TextValue("East"));

        var filterRange = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 5, 2));
        new FilterCommand(sid, filterRange, 1, ["West"]).Apply(ctx).Success.Should().BeTrue();

        var sortRange = new GridRange(new CellAddress(sid, 2, 1), new CellAddress(sid, 5, 2));
        var sortCommand = new SortCommand(sid, sortRange, sortByColOffset: 0, ascending: true);
        sortCommand.Apply(ctx).Success.Should().BeTrue();

        sortCommand.Revert(ctx);

        sheet.GetValue(2, 1).Should().Be(new TextValue("Charlie"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Alice"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Bob"));
        sheet.GetValue(5, 1).Should().Be(new TextValue("Dave"));
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 5u]);
    }

    /// <summary>
    /// Sibling no-regression case: with NO active filter (FilterHiddenRows/ValueFilterHiddenRows
    /// both empty), every row in the range is "visible" and must still sort exactly as before —
    /// the partition-by-visibility logic must be a complete no-op when nothing is filter-hidden.
    /// </summary>
    [Fact]
    public void Apply_WithNoActiveFilter_SortsAllRowsNormally()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Charlie"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Alice"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Bob"));
        sheet.SetCell(new CellAddress(sid, 4, 1), new TextValue("Dave"));

        sheet.FilterHiddenRows.Should().BeEmpty();
        sheet.ValueFilterHiddenRows.Should().BeEmpty();

        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));
        var sortCommand = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);
        sortCommand.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new TextValue("Alice"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Bob"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Charlie"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Dave"));
    }
}
