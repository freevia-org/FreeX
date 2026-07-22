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
    public void AutoFitRowHeight_StackedVerticalText_GrowsRowFromRotation()
    {
        // R69-commands-autofit-6-1: AutoFit Row Height must thread the cell's TextRotation into
        // the shared estimate, not just its WrapText flag -- otherwise a stacked/vertical-text
        // cell (Orientation 255) never reaches AutoFitSizingService.EstimateRotatedHeightUnits
        // and is measured as an ordinary single unrotated line.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.DefaultRowHeight = 20;
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue(new string('A', 20)));
        sheet.GetCell(address)!.StyleId = workbook.RegisterStyle(new CellStyle { TextRotation = 255 });
        var session = CreateSession(workbook);

        session.SelectRange(Range(sheet.Id, 1, 1, 1, 1));
        session.AutoFitSelectedRowHeight().Success.Should().BeTrue();

        sheet.RowHeights.Should().ContainKey(1u);
        // Stacked text needs one line-height per character (20 chars), clamped to the service's
        // ceiling -- far taller than the unrotated single-line height would ever grow to.
        sheet.RowHeights[1].Should().BeGreaterThan(sheet.DefaultRowHeight * 2);
    }

    [Fact]
    public void AutoFitRowHeight_HorizontalSingleLineText_StaysAtDefaultHeight()
    {
        // Sibling no-regression: a normal (unrotated) single-line cell must still autofit to the
        // default row height, exactly as before the TextRotation wiring fix.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.DefaultRowHeight = 20;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(new string('A', 20)));
        var session = CreateSession(workbook);

        session.SelectRange(Range(sheet.Id, 1, 1, 1, 1));
        session.AutoFitSelectedRowHeight().Success.Should().BeTrue();

        sheet.RowHeights.Should().ContainKey(1u);
        sheet.RowHeights[1].Should().Be(sheet.DefaultRowHeight);
    }

    [Fact]
    public void AutoFitColumnWidth_StackedVerticalText_NarrowsInsteadOfWideningToFullTextLength()
    {
        // R69-commands-autofit-6-2: AutoFit Column Width must narrow for stacked/vertical text
        // instead of measuring the unrotated string length -- Excel only needs ~1 glyph's width
        // per column for stacked text, not the full 16-character run.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.DefaultColumnWidth = 3.0;
        var address = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(address, new TextValue("PRODUCT CATEGORY"));
        sheet.GetCell(address)!.StyleId = workbook.RegisterStyle(new CellStyle { TextRotation = 255 });
        var session = CreateSession(workbook);

        session.SelectRange(Range(sheet.Id, 1, 2, 1, 2));
        session.AutoFitSelectedColumnWidth().Success.Should().BeTrue();

        sheet.ColumnWidths.Should().ContainKey(2u);
        sheet.ColumnWidths[2].Should().BeApproximately(3.0, 0.01);
    }

    [Fact]
    public void AutoFitColumnWidth_HorizontalText_StillWidensToFullTextLength()
    {
        // Sibling no-regression: an unrotated cell with the same text must still widen the column
        // to its full character length, exactly as before the rotation-aware estimate was added.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.DefaultColumnWidth = 3.0;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("PRODUCT CATEGORY"));
        var session = CreateSession(workbook);

        session.SelectRange(Range(sheet.Id, 1, 2, 1, 2));
        session.AutoFitSelectedColumnWidth().Success.Should().BeTrue();

        sheet.ColumnWidths.Should().ContainKey(2u);
        sheet.ColumnWidths[2].Should().BeApproximately(18.0, 0.01); // 16 chars + 2.0 padding
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
