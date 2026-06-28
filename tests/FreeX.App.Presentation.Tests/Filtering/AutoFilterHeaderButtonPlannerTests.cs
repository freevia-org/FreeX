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
}
