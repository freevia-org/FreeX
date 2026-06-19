using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionRowColumnSizingTests
{
    [Fact]
    public void GetDialogValues_UseExplicitDimensionThenSheetDefault()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.DefaultRowHeight = 18;
        sheet.DefaultColumnWidth = 9.25;
        sheet.RowHeights[3] = 24;
        sheet.ColumnWidths[4] = 14.5;
        var session = CreateSession(workbook);

        session.SelectRange(Range(sheet.Id, 3, 4, 5, 6));
        session.GetSelectedRowHeight().Should().Be(24);
        session.GetSelectedColumnWidth().Should().Be(14.5);

        session.SelectRange(Range(sheet.Id, 7, 8, 7, 8));
        session.GetSelectedRowHeight().Should().Be(18);
        session.GetSelectedColumnWidth().Should().Be(9.25);
    }

    [Fact]
    public void SetSelectedRowsHeightAndColumnsWidth_ApplyAcrossSelectionSpans()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var session = CreateSession(workbook);

        session.SelectRange(Range(sheet.Id, 2, 3, 4, 5));
        session.SetSelectedRowsHeight(30).Success.Should().BeTrue();
        session.SetSelectedColumnsWidth(12).Success.Should().BeTrue();

        sheet.RowHeights.Should().ContainKeys(2u, 3u, 4u);
        sheet.RowHeights.Values.Should().OnlyContain(h => h == 30);
        sheet.ColumnWidths.Should().ContainKeys(3u, 4u, 5u);
        sheet.ColumnWidths.Values.Should().OnlyContain(w => w == 12);
    }

    [Fact]
    public void AutoFitColumnWidth_WidensToLongestCellContent()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.DefaultColumnWidth = 8.43;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("a long-ish label"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("x"));
        var session = CreateSession(workbook);

        session.SelectRange(Range(sheet.Id, 1, 2, 2, 2));
        session.AutoFitSelectedColumnWidth().Success.Should().BeTrue();

        // "a long-ish label" is 16 chars; the shared estimate adds padding and exceeds the default.
        sheet.ColumnWidths.Should().ContainKey(2u);
        sheet.ColumnWidths[2].Should().BeGreaterThan(sheet.DefaultColumnWidth);
    }

    [Fact]
    public void AutoFitRowHeight_GrowsForMultiLineContent()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.DefaultRowHeight = 20;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("line one\nline two\nline three"));
        var session = CreateSession(workbook);

        session.SelectRange(Range(sheet.Id, 1, 1, 1, 1));
        session.AutoFitSelectedRowHeight().Success.Should().BeTrue();

        sheet.RowHeights.Should().ContainKey(1u);
        sheet.RowHeights[1].Should().BeGreaterThan(sheet.DefaultRowHeight);
    }

    [Fact]
    public void AutoFit_OnEmptySelectionWithoutContent_IsSuccessfulNoOp()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var session = CreateSession(workbook);

        session.SelectRange(Range(sheet.Id, 5, 5, 5, 5));
        session.AutoFitSelectedColumnWidth().Success.Should().BeTrue();
        session.AutoFitSelectedRowHeight().Success.Should().BeTrue();
    }

    private static GridRange Range(SheetId sheetId, uint row1, uint col1, uint row2, uint col2) =>
        new(new CellAddress(sheetId, row1, col1), new CellAddress(sheetId, row2, col2));

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
