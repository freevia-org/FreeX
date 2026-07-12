using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Covers R33-rendering-grid-overflow-display-3: turning on Wrap Text must auto-grow the row
/// height to fit the now-wrapped content (matching Excel), but must never shrink a row that is
/// already tall enough (e.g. because the user previously resized it by hand).
/// </summary>
public sealed class WorkbookSessionWrapTextAutoGrowTests
{
    [Fact]
    public void EnablingWrapText_OnContentNeedingFourLines_GrowsRowHeight()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.DefaultRowHeight = 20;
        sheet.DefaultColumnWidth = 8.43; // usable chars per line ≈ 6 at this width.
        // 24 chars at ~6 usable chars/line wraps to ceil(24/6) = 4 visual lines.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(new string('A', 24)));
        var session = CreateSession(workbook);
        session.SelectRange(Range(sheet.Id, 1, 1, 1, 1));

        session.SetSelectedRangeWrapText(true).Success.Should().BeTrue();

        sheet.RowHeights.Should().ContainKey(1u);
        sheet.RowHeights[1].Should().BeGreaterThan(sheet.DefaultRowHeight);
    }

    [Fact]
    public void EnablingWrapText_OnShortContent_DoesNotGrowRow()
    {
        // Sibling already-working case: toggling Wrap Text still flips the style bit even when
        // the content already fits on one line, and must not spuriously resize the row.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.DefaultRowHeight = 20;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("short"));
        var session = CreateSession(workbook);
        session.SelectRange(Range(sheet.Id, 1, 1, 1, 1));

        session.SetSelectedRangeWrapText(true).Success.Should().BeTrue();

        sheet.RowHeights.Should().NotContainKey(1u);
        var style = workbook.GetStyle(sheet.GetCell(1, 1)!.StyleId);
        style.WrapText.Should().BeTrue();
    }

    [Fact]
    public void EnablingWrapText_OnManuallyResizedTallerRow_DoesNotShrinkIt()
    {
        // Guards against over-correction: a row the user already made taller than what wrapping
        // needs must be left alone, matching Excel never shrinking a manually-set row height.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        sheet.DefaultRowHeight = 20;
        sheet.RowHeights[1] = 120;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(new string('A', 24)));
        var session = CreateSession(workbook);
        session.SelectRange(Range(sheet.Id, 1, 1, 1, 1));

        session.SetSelectedRangeWrapText(true).Success.Should().BeTrue();

        sheet.RowHeights[1].Should().Be(120);
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
