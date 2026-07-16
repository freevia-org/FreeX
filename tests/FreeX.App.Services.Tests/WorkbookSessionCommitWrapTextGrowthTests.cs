using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Covers R44-render-text-wrap-shrink-3-2: a row height must keep auto-growing to fit newly typed
/// content in a cell that already has Wrap Text on -- not just on the WrapText style flag's
/// off-to-on transition (that one-time grow is covered separately by
/// WorkbookSessionWrapTextAutoGrowTests). Matches Excel's "row grows unless manually pinned taller"
/// behavior on every commit, and never shrinks a row.
/// </summary>
public sealed class WorkbookSessionCommitWrapTextGrowthTests
{
    [Fact]
    public void CommitCellText_TypingLongerValueIntoAlreadyWrappedCell_GrowsRowHeight()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.DefaultRowHeight = 20;
        sheet.DefaultColumnWidth = 8.43; // usable chars per line ~= 6 at this width.
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("Hi"));
        var session = CreateSession(workbook);
        session.SelectRange(Range(sheet.Id, 1, 1, 1, 1));
        session.SetSelectedRangeWrapText(true).Success.Should().BeTrue();

        // 24 chars at ~6 usable chars/line wraps to ceil(24/6) = 4 visual lines -- taller than the
        // short "Hi" value's row height computed when wrap was first toggled on.
        var result = session.CommitCellText(new string('A', 24));

        result.Success.Should().BeTrue();
        sheet.RowHeights.Should().ContainKey(1u);
        sheet.RowHeights[1].Should().BeGreaterThan(sheet.DefaultRowHeight);
    }

    [Fact]
    public void CommitCellText_TypingShortValueIntoAlreadyWrappedCell_DoesNotShrinkRow()
    {
        // Sibling no-regression case: an edit that needs LESS height than the row already has must
        // leave the row alone (matching Excel/the wrap-toggle-on path never shrinking a row).
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.DefaultRowHeight = 20;
        sheet.DefaultColumnWidth = 8.43;
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue(new string('A', 24)));
        var session = CreateSession(workbook);
        session.SelectRange(Range(sheet.Id, 1, 1, 1, 1));
        session.SetSelectedRangeWrapText(true).Success.Should().BeTrue();
        var grownHeight = sheet.RowHeights[1];
        grownHeight.Should().BeGreaterThan(sheet.DefaultRowHeight);

        var result = session.CommitCellText("short");

        result.Success.Should().BeTrue();
        sheet.RowHeights[1].Should().Be(grownHeight);
    }

    [Fact]
    public void CommitCellText_TypingLongerValueIntoNonWrappedCell_DoesNotGrowRow()
    {
        // Guards the gate itself: without WrapText, Excel never auto-grows the row from overflowing
        // (non-wrapped) content, so CommitCellText must not touch row height at all.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.DefaultRowHeight = 20;
        var session = CreateSession(workbook);
        session.SelectRange(Range(sheet.Id, 1, 1, 1, 1));

        var result = session.CommitCellText(new string('A', 60));

        result.Success.Should().BeTrue();
        sheet.RowHeights.Should().NotContainKey(1u);
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
