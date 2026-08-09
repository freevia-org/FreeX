using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

// R126-cellscmds-multiarea-rowheight-2: Avalonia counterpart of the WPF host's R124 fix
// (FreeX.App.Host.Tests.R124_MultiAreaHeaderRowColumnSizingTests). A Ctrl+click multi-area
// row/column-header selection is exactly what WorkbookSession.SelectRanges (via
// MainWindow.RowColumnVisibility.cs's AddAdditionalRowSelection/AddAdditionalColumnSelection on the
// Avalonia shell) produces: SelectedRanges holds every disjoint whole-row/column area while
// SelectedRange is only the last-clicked (active) one. SetSelectedRowsHeight, SetSelectedColumnsWidth,
// AutoFitSelectedRowHeight and AutoFitSelectedColumnWidth used to read only the active SelectedRange,
// so with rows 2 and 5 Ctrl+click selected, only row 5 was resized/AutoFit and row 2 was silently left
// untouched -- unlike real Excel (and unlike the WPF host as of R124), which applies the change to
// every disjoint area of a multi-area selection.
public sealed class R126_MultiAreaRowColumnSizingTests
{
    private const double PixelsPerPoint = 96.0 / 72.0;

    [Fact]
    public void SetSelectedRowsHeight_MultiAreaRowSelection_ResizesEveryDisjointRow()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var session = CreateSession(workbook);

        // Ctrl+click rows 2 and 5 (disjoint): SelectedRange is the active/last-clicked area (row 5),
        // SelectedRanges holds both -- exactly what AddAdditionalRowSelection produces.
        var row2 = WholeRow(sheet.Id, 2);
        var row5 = WholeRow(sheet.Id, 5);
        session.SelectRanges(row5, [row2, row5]);

        var result = session.SetSelectedRowsHeight(30);

        result.Success.Should().BeTrue();
        var expectedHeightPixels = 30.0 * PixelsPerPoint;
        // Before the fix, only row 5 (the active area) got the new height; row 2 was silently left
        // at its unset default.
        sheet.RowHeights.Should().ContainKey(2u, "row 2's disjoint area must also be resized");
        sheet.RowHeights[2].Should().BeApproximately(expectedHeightPixels, 0.001);
        sheet.RowHeights.Should().ContainKey(5u, "row 5 (the active area) must be resized");
        sheet.RowHeights[5].Should().BeApproximately(expectedHeightPixels, 0.001);
        sheet.RowHeights.Should().NotContainKey(1u);
        sheet.RowHeights.Should().NotContainKey(3u);
    }

    [Fact]
    public void SetSelectedColumnsWidth_MultiAreaColumnSelection_ResizesEveryDisjointColumn()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var session = CreateSession(workbook);

        var col2 = WholeColumn(sheet.Id, 2);
        var col5 = WholeColumn(sheet.Id, 5);
        session.SelectRanges(col5, [col2, col5]);

        var result = session.SetSelectedColumnsWidth(30);

        result.Success.Should().BeTrue();
        sheet.ColumnWidths.Should().ContainKey(2u, "column 2's disjoint area must also be resized");
        sheet.ColumnWidths[2].Should().BeApproximately(30.0, 0.001);
        sheet.ColumnWidths.Should().ContainKey(5u, "column 5 (the active area) must be resized");
        sheet.ColumnWidths[5].Should().BeApproximately(30.0, 0.001);
        sheet.ColumnWidths.Should().NotContainKey(1u);
        sheet.ColumnWidths.Should().NotContainKey(3u);
    }

    [Fact]
    public void AutoFitSelectedRowHeight_MultiAreaRowSelection_SizesEveryDisjointRow()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.DefaultRowHeight = 20;
        // AutoFit's measurement bounds for a whole-row selection fall back to the used range
        // (RowColumnSizingPlanner.GetMeasurementBounds), so seed one long cell per target row.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("line one\nline two\nline three"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("line one\nline two\nline three"));
        var session = CreateSession(workbook);

        var row2 = WholeRow(sheet.Id, 2);
        var row5 = WholeRow(sheet.Id, 5);
        session.SelectRanges(row5, [row2, row5]);

        var result = session.AutoFitSelectedRowHeight();

        result.Success.Should().BeTrue();
        sheet.RowHeights.Should().ContainKey(2u, "row 2's disjoint area must also be AutoFit");
        sheet.RowHeights[2].Should().BeGreaterThan(sheet.DefaultRowHeight);
        sheet.RowHeights.Should().ContainKey(5u, "row 5 (the active area) must be AutoFit");
        sheet.RowHeights[5].Should().BeGreaterThan(sheet.DefaultRowHeight);
    }

    [Fact]
    public void AutoFitSelectedColumnWidth_MultiAreaColumnSelection_SizesEveryDisjointColumn()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.DefaultColumnWidth = 8.43;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("a long-ish label"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new TextValue("a long-ish label"));
        var session = CreateSession(workbook);

        var col2 = WholeColumn(sheet.Id, 2);
        var col5 = WholeColumn(sheet.Id, 5);
        session.SelectRanges(col5, [col2, col5]);

        var result = session.AutoFitSelectedColumnWidth();

        result.Success.Should().BeTrue();
        sheet.ColumnWidths.Should().ContainKey(2u, "column 2's disjoint area must also be AutoFit");
        sheet.ColumnWidths[2].Should().BeGreaterThan(sheet.DefaultColumnWidth);
        sheet.ColumnWidths.Should().ContainKey(5u, "column 5 (the active area) must be AutoFit");
        sheet.ColumnWidths[5].Should().BeGreaterThan(sheet.DefaultColumnWidth);
    }

    // No-regression sibling: a plain single active-range Row Height (no Ctrl+click multi-area
    // selection) must keep resizing exactly that one row, unaffected by routing the command
    // construction through the ranges-aware plumbing.
    [Fact]
    public void SetSelectedRowsHeight_SingleActiveRange_StillResizesOnlyThatRow_NoRegression()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var session = CreateSession(workbook);

        session.SelectRange(WholeRow(sheet.Id, 3));
        session.SelectedRanges.Count.Should().BeLessThanOrEqualTo(1);

        var result = session.SetSelectedRowsHeight(40);

        result.Success.Should().BeTrue();
        sheet.RowHeights.Should().ContainSingle();
        sheet.RowHeights.Should().ContainKey(3u);
        sheet.RowHeights[3].Should().BeApproximately(40.0 * PixelsPerPoint, 0.001);
    }

    private static GridRange WholeRow(SheetId sheetId, uint row) =>
        new(new CellAddress(sheetId, row, 1), new CellAddress(sheetId, row, CellAddress.MaxCol));

    private static GridRange WholeColumn(SheetId sheetId, uint col) =>
        new(new CellAddress(sheetId, 1, col), new CellAddress(sheetId, CellAddress.MaxRow, col));

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
