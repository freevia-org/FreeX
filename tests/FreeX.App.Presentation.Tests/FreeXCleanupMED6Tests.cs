using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// Regression test for FreeX cleanup batch MED6 (P100).
/// </summary>
public sealed class FreeXCleanupMED6Tests
{
    private static readonly FakeTextMeasurer Measurer = new();

    private static (Workbook Workbook, Sheet Sheet) CreateBook()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }

    // ── P100: print preview pagination must exclude hidden rows/columns, matching the actual
    //          print/PDF job (WorkbookExportPrintPlanner), instead of paginating the full,
    //          unfiltered extent and reporting a higher page count than what actually prints. ──

    [Fact]
    public void TryCreate_HiddenRowsInMiddleOfRange_ExcludedFromPaginationLikeThePrintJob()
    {
        var (workbook, sheet) = CreateBook();

        // Populate rows 1..90 in column A. At the default 20px row height, a Letter-portrait page
        // (11in tall minus 1in top/bottom margins = 9in = 864px usable) fits ~43 rows/page, so 90
        // rows makes 2 pages. Hiding a contiguous block of 45 rows in the middle collapses the
        // visible extent down to ~45 rows — one page — exactly matching what the real print/PDF
        // job (which already filters via Sheet.IsRowEffectivelyHidden) would produce.
        for (uint r = 1; r <= 90; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));

        for (uint r = 20; r <= 64; r++)
            sheet.HiddenRows.Add(r);

        PrintPreviewPaginationContext.TryCreate(workbook, sheet, Measurer, out var context)
            .Should().BeTrue();

        context.PageCount.Should().Be(1,
            "hidden rows must be excluded from the preview's pagination just like the actual print job excludes them");
    }
}
