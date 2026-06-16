using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionMultiRangeCopyTests
{
    [Fact]
    public void TryCopySelectedRangeText_SameRowAreas_ProducesSideBySideBlock()
    {
        var (session, sheet) = CreateSeededSession();
        // A1:A2 and C1:C2 — same rows, gap at column B.
        var a = new GridRange(Cell(sheet, 1, 1), Cell(sheet, 2, 1));
        var c = new GridRange(Cell(sheet, 1, 3), Cell(sheet, 2, 3));
        session.SelectRanges(a, new[] { a, c });

        var result = session.TryCopySelectedRangeText();

        result.Success.Should().BeTrue();
        result.Text.Should().Be("a1\tc1\r\na2\tc2");
    }

    [Fact]
    public void TryCopySelectedRangeText_SameColumnAreas_ProducesStackedBlock()
    {
        var (session, sheet) = CreateSeededSession();
        // A1:B1 and A3:B3 — same columns, gap at row 2.
        var top = new GridRange(Cell(sheet, 1, 1), Cell(sheet, 1, 2));
        var bottom = new GridRange(Cell(sheet, 3, 1), Cell(sheet, 3, 2));
        session.SelectRanges(top, new[] { top, bottom });

        var result = session.TryCopySelectedRangeText();

        result.Success.Should().BeTrue();
        result.Text.Should().Be("a1\tb1\r\na3\tb3");
    }

    [Fact]
    public void TryCopySelectedRangeText_NonCongruentAreas_Fails()
    {
        var (session, sheet) = CreateSeededSession();
        var a = new GridRange(Cell(sheet, 1, 1), Cell(sheet, 2, 1));
        var b = new GridRange(Cell(sheet, 3, 2), Cell(sheet, 4, 2));
        session.SelectRanges(a, new[] { a, b });

        var result = session.TryCopySelectedRangeText();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryCutSelectedRangeText_MultipleAreas_Fails()
    {
        var (session, sheet) = CreateSeededSession();
        var a = new GridRange(Cell(sheet, 1, 1), Cell(sheet, 2, 1));
        var c = new GridRange(Cell(sheet, 1, 3), Cell(sheet, 2, 3));
        session.SelectRanges(a, new[] { a, c });

        var result = session.TryCutSelectedRangeText();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    private static CellAddress Cell(Sheet sheet, uint row, uint col) => new(sheet.Id, row, col);

    private static (WorkbookSession Session, Sheet Sheet) CreateSeededSession()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var sheet = workbook.Sheets.Single();

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("a1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("a2"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("b1"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("a3"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("b3"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("c1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("c2"));

        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);
        return (session, sheet);
    }
}
