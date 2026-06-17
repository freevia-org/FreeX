using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class AutoFilterDropdownPlannerTests
{
    // A structured Excel table can carry its AutoFilter purely inside the table definition
    // (<table><autoFilter ref="B2:G12"/></table>) with NO worksheet-level <autoFilter> element.
    // The contextures "expiry dates" workbook is exactly this shape.  The dropdown planner must surface
    // the table's filter range so the GridView draws filter-arrow buttons on the table's header row,
    // matching Excel (which always shows them on a filtered table).
    [Fact]
    public void TryGetAutoFilterRange_UsesStructuredTableAutoFilterWhenNoWorksheetAutoFilter()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("LicenceData");
        sheet.AutoFilter.Should().BeNull("the table carries the AutoFilter, not the worksheet");

        var range = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 12, 7));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "tblLic",
            DisplayName = "tblLic",
            Range = range,
            HasAutoFilter = true,
            HeaderRowCount = 1
        });

        AutoFilterDropdownPlanner.TryGetAutoFilterRange(sheet, out var resolved).Should().BeTrue();
        resolved.Should().Be(range);
    }

    [Fact]
    public void TryGetAutoFilterRange_IgnoresStructuredTableWithoutAutoFilter()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Data");
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "tbl",
            DisplayName = "tbl",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)),
            HasAutoFilter = false,
            HeaderRowCount = 1
        });

        AutoFilterDropdownPlanner.TryGetAutoFilterRange(sheet, out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetAutoFilterRange_PrefersWorksheetAutoFilterOverTable()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Data");
        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:C9", null);
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "tbl",
            DisplayName = "tbl",
            Range = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 12, 7)),
            HasAutoFilter = true,
            HeaderRowCount = 1
        });

        AutoFilterDropdownPlanner.TryGetAutoFilterRange(sheet, out var resolved).Should().BeTrue();
        resolved.Should().Be(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 9, 3)));
    }
}
