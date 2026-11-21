using FluentAssertions;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Excel's cell menu Filter ▸ "Filter by Selected Cell's Value": turns the AutoFilter on around the
/// active cell when needed and keeps only the rows matching the active cell in its column.
/// </summary>
public sealed class FilterBySelectedCellValueTests
{
    [Fact]
    public void WithoutAnAutoFilter_TurnsItOnAroundTheActiveCell_AndFiltersItsColumn()
    {
        var (session, sheet) = CreateSession();
        string[] fruit = ["Fruit", "Apple", "Pear", "Apple", "Plum"];
        for (uint row = 1; row <= fruit.Length; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(fruit[row - 1]));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), row == 1 ? new TextValue("Qty") : new NumberValue(row));
        }

        var apple = new CellAddress(sheet.Id, 2, 1);
        session.SelectCell(apple);
        FilterBySelectedCellValuePlanner.NeedsAutoFilter(sheet).Should().BeTrue();

        session.EnableAutoFilterAroundActiveCell().Success.Should().BeTrue();
        FilterBySelectedCellValuePlanner.NeedsAutoFilter(sheet).Should().BeFalse();

        FilterBySelectedCellValuePlanner.TryPlan(sheet, apple, out var range, out var columnOffset, out var values)
            .Should().BeTrue();
        range.Should().Be(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            "the AutoFilter covers the current region around the active cell");
        columnOffset.Should().Be(0);
        values.Should().Equal("Apple");
    }

    [Fact]
    public void ActiveCellOutsideTheAutoFilter_IsNotPlanned()
    {
        var (session, sheet) = CreateSession();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("H"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("x"));
        session.SelectCell(new CellAddress(sheet.Id, 2, 1));
        session.EnableAutoFilterAroundActiveCell().Success.Should().BeTrue();

        FilterBySelectedCellValuePlanner.TryPlan(sheet, new CellAddress(sheet.Id, 10, 5), out _, out _, out _)
            .Should().BeFalse();
    }

    private static (WorkbookSession Session, Sheet Sheet) CreateSession()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var sheet = workbook.Sheets.Single();
        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);
        return (session, sheet);
    }
}
