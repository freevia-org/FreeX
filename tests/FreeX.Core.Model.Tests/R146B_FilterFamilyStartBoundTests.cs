using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// table-semantics-F1, third pass. The rule is that a filter's START bound must be as
/// header-count-aware as its END bound: a Structured Table loaded with <c>headerRowCount="0"</c> has
/// no header row, so <c>Range.Start.Row</c> is itself a data row.
/// </summary>
/// <remarks>
/// Two earlier passes fixed one member of this family each and each time a sibling was found still
/// hardcoding <c>Start.Row + 1</c> while already calling the header-aware
/// <c>GetFilterableLastRow</c> for its end. These tests pin the two members that survived both
/// passes, including the REVERSE-direction bug: <see cref="FilterHiddenRowUpdater.ClearRange"/> left
/// a headerless table's first row stuck permanently HIDDEN after Convert Table to Range, the mirror
/// image of leaving it permanently visible.
/// </remarks>
public sealed class R146B_FilterFamilyStartBoundTests
{
    private static StructuredTableModel AddTable(Sheet sheet, GridRange range, int? headerRowCount)
    {
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            Range = range,
            HeaderRowCount = headerRowCount,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        sheet.StructuredTables.Add(table);
        return table;
    }

    [Fact]
    public void ClearRange_HeaderlessTable_UnhidesTheFirstDataRow()
    {
        // Convert Table to Range must leave every row visible. With the naive start bound, row 1 of a
        // headerless table was never un-hidden -- and once the table is gone there is no filter UI
        // left to clear it, so it stayed hidden for the life of the document.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        AddTable(sheet, range, headerRowCount: 0);

        var hidden = new HashSet<uint> { 1, 2, 3 };
        var firstDataRow = FilterHiddenRowUpdater.GetFilterableFirstRow(sheet, range);

        FilterHiddenRowUpdater.ClearRange(hidden, range, firstDataRow);

        hidden.Should().BeEmpty(
            "Convert Table to Range clears the table's filter state, and a headerless table's first "
            + "row is a data row that must be un-hidden with the rest");
    }

    [Fact]
    public void ClearRange_TableWithHeader_StillSkipsTheHeaderRow()
    {
        // No-regression sibling: the default path must keep skipping a real header row, so a header
        // hidden for some unrelated reason is not silently revealed by clearing filters.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        AddTable(sheet, range, headerRowCount: 1);

        var hidden = new HashSet<uint> { 1, 2, 3 };
        var firstDataRow = FilterHiddenRowUpdater.GetFilterableFirstRow(sheet, range);

        FilterHiddenRowUpdater.ClearRange(hidden, range, firstDataRow);

        hidden.Should().BeEquivalentTo(new uint[] { 1 },
            "row 1 is the header row here, so clearing the table's data-row filter state leaves it alone");
    }

    [Fact]
    public void ClearRange_WithoutExplicitFirstRow_KeepsTheHeaderSkippingDefault()
    {
        // The new parameter is optional; omitting it must behave exactly as before this change.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));

        var hidden = new HashSet<uint> { 1, 2, 3 };
        FilterHiddenRowUpdater.ClearRange(hidden, range);

        hidden.Should().BeEquivalentTo(new uint[] { 1 });
    }

    [Fact]
    public void GetFilterableFirstRow_IsTheStartBoundCounterpartOfGetFilterableLastRow()
    {
        // The two bounds must agree about whether a header row exists. Every defect in this family
        // came from one of them being header-aware while the other was not.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));

        AddTable(sheet, range, headerRowCount: 0);
        FilterHiddenRowUpdater.GetFilterableFirstRow(sheet, range).Should().Be(1,
            "a headerless table's Range.Start.Row is a data row");

        sheet.StructuredTables.Clear();
        AddTable(sheet, range, headerRowCount: 1);
        FilterHiddenRowUpdater.GetFilterableFirstRow(sheet, range).Should().Be(2,
            "a table with a header row starts its data one row down");

        sheet.StructuredTables.Clear();
        FilterHiddenRowUpdater.GetFilterableFirstRow(sheet, range).Should().Be(2,
            "a plain worksheet AutoFilter range always treats its first row as the header");
    }
}
