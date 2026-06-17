using System.Linq;

using FluentAssertions;

using FreeX.App.Avalonia;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for the UI-free <see cref="AutoFilterHeaderPlanner"/>: resolving the active AutoFilter range
/// (worksheet-level reference or a filtered structured table) and yielding the header cells that should show
/// a filter-dropdown button. No running shell required.
/// </summary>
public sealed class AutoFilterHeaderPlannerTests
{
    private static Sheet CreateSheet() => new Workbook("Book").AddSheet("Sheet1");

    [Fact]
    public void NoAutoFilter_YieldsNoButtons()
    {
        var sheet = CreateSheet();

        AutoFilterHeaderPlanner.TryGetAutoFilterRange(sheet).Should().BeNull();
        AutoFilterHeaderPlanner.GetHeaderButtonCells(sheet).Should().BeEmpty();
        AutoFilterHeaderPlanner.IsFilterButtonCell(sheet, 1, 1).Should().BeFalse();
    }

    [Fact]
    public void WorksheetAutoFilter_YieldsOneButtonPerHeaderColumn()
    {
        var sheet = CreateSheet();
        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:C4", null);

        var range = AutoFilterHeaderPlanner.TryGetAutoFilterRange(sheet);
        range.Should().NotBeNull();

        var cells = AutoFilterHeaderPlanner.GetHeaderButtonCells(sheet);
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
        var range = AutoFilterHeaderPlanner.TryGetAutoFilterRange(sheet)!.Value;
        var headerRow = range.Start.Row;

        AutoFilterHeaderPlanner.IsFilterButtonCell(sheet, headerRow, range.Start.Col).Should().BeTrue();
        AutoFilterHeaderPlanner.IsFilterButtonCell(sheet, headerRow, range.End.Col).Should().BeTrue();
        // A data row is not a header.
        AutoFilterHeaderPlanner.IsFilterButtonCell(sheet, headerRow + 1, range.Start.Col).Should().BeFalse();
        // A column outside the range is not a header button.
        AutoFilterHeaderPlanner.IsFilterButtonCell(sheet, headerRow, range.End.Col + 1).Should().BeFalse();
    }

    [Fact]
    public void InvalidReference_YieldsNoButtons()
    {
        var sheet = CreateSheet();
        sheet.AutoFilter = new WorksheetAutoFilterModel("not-a-range", null);

        AutoFilterHeaderPlanner.TryGetAutoFilterRange(sheet).Should().BeNull();
        AutoFilterHeaderPlanner.GetHeaderButtonCells(sheet).Should().BeEmpty();
    }
}
