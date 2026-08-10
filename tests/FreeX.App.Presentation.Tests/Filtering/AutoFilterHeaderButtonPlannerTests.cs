using FluentAssertions;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Filtering;

public sealed class AutoFilterHeaderButtonPlannerTests
{
    private static Sheet CreateSheet() => new Workbook("Book").AddSheet("Sheet1");

    [Fact]
    public void NoAutoFilter_YieldsNoButtons()
    {
        var sheet = CreateSheet();

        AutoFilterHeaderButtonPlanner.TryGetAutoFilterRange(sheet).Should().BeNull();
        AutoFilterHeaderButtonPlanner.GetHeaderButtonCells(sheet).Should().BeEmpty();
        AutoFilterHeaderButtonPlanner.IsFilterButtonCell(sheet, 1, 1).Should().BeFalse();
    }

    [Fact]
    public void WorksheetAutoFilter_YieldsOneButtonPerHeaderColumn()
    {
        var sheet = CreateSheet();
        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:C4", null);

        var range = AutoFilterHeaderButtonPlanner.TryGetAutoFilterRange(sheet);
        range.Should().NotBeNull();

        var cells = AutoFilterHeaderButtonPlanner.GetHeaderButtonCells(sheet);
        cells.Should().HaveCount(3);
        cells.Select(c => c.Row).Should().OnlyContain(r => r == range!.Value.Start.Row);
        cells.Select(c => c.Col).Should().BeEquivalentTo(new[]
        {
            range!.Value.Start.Col,
            range.Value.Start.Col + 1,
            range.Value.Start.Col + 2,
        });
    }

    [Fact]
    public void IsFilterButtonCell_TrueOnHeaderRow_FalseOnDataRowsAndOutsideColumns()
    {
        var sheet = CreateSheet();
        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:B4", null);
        var range = AutoFilterHeaderButtonPlanner.TryGetAutoFilterRange(sheet)!.Value;
        var headerRow = range.Start.Row;

        AutoFilterHeaderButtonPlanner.IsFilterButtonCell(sheet, headerRow, range.Start.Col).Should().BeTrue();
        AutoFilterHeaderButtonPlanner.IsFilterButtonCell(sheet, headerRow, range.End.Col).Should().BeTrue();
        AutoFilterHeaderButtonPlanner.IsFilterButtonCell(sheet, headerRow + 1, range.Start.Col).Should().BeFalse();
        AutoFilterHeaderButtonPlanner.IsFilterButtonCell(sheet, headerRow, range.End.Col + 1).Should().BeFalse();
    }

    [Fact]
    public void InvalidReference_YieldsNoButtons()
    {
        var sheet = CreateSheet();
        sheet.AutoFilter = new WorksheetAutoFilterModel("not-a-range", null);

        AutoFilterHeaderButtonPlanner.TryGetAutoFilterRange(sheet).Should().BeNull();
        AutoFilterHeaderButtonPlanner.GetHeaderButtonCells(sheet).Should().BeEmpty();
    }

    [Fact]
    public void ActiveColumns_PreferWorksheetFilterColumns()
    {
        var sheet = CreateSheet();
        var range = Range(sheet, 1, 1, 4, 3);
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(1, ["Open"]));
        var table = CreateTable(range);
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(2, ["High"]));
        sheet.StructuredTables.Add(table);

        AutoFilterHeaderButtonPlanner.GetActiveColumnOffsets(sheet, range)
            .Should()
            .BeEquivalentTo([1u]);
        AutoFilterHeaderButtonPlanner.IsColumnActive(sheet, range, range.Start.Col + 1).Should().BeTrue();
        AutoFilterHeaderButtonPlanner.IsColumnActive(sheet, range, range.Start.Col + 2).Should().BeFalse();
    }

    [Fact]
    public void ActiveColumns_FallBackToMatchingStructuredTable()
    {
        var sheet = CreateSheet();
        var range = Range(sheet, 2, 3, 8, 5);
        var table = CreateTable(range);
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(0, ["West"]));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(2, ["Open"]));
        sheet.StructuredTables.Add(table);

        AutoFilterHeaderButtonPlanner.GetActiveColumnOffsets(sheet, range)
            .Should()
            .BeEquivalentTo([0u, 2u]);
        AutoFilterHeaderButtonPlanner.IsColumnActive(sheet, range, range.Start.Col).Should().BeTrue();
        AutoFilterHeaderButtonPlanner.IsColumnActive(sheet, range, range.End.Col + 1).Should().BeFalse();
    }

    private static StructuredTableModel CreateTable(GridRange range) => new()
    {
        Id = 1,
        Name = "Table1",
        DisplayName = "Table1",
        Range = range,
        HasAutoFilter = true,
    };

    private static GridRange Range(Sheet sheet, uint startRow, uint startColumn, uint endRow, uint endColumn) =>
        new(
            new CellAddress(sheet.Id, startRow, startColumn),
            new CellAddress(sheet.Id, endRow, endColumn));
}
