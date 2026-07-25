using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// Regression tests for R86-meta-2: PrintPreviewPaginationContext.BuildPage paginated every one
/// of a sheet's configured print areas independently but reset a LOCAL `remaining` page index to
/// 0 at the start of each area's plan and passed that local index straight into
/// PageContentRenderModelBuilder.Build, which derives the on-screen &amp;P/&amp;N header/footer
/// numbers from it (pageNumber = FirstPageNumber + local index, totalPages = that one area's own
/// PageCount). So the first page of a sheet's SECOND (or later) print area always displayed
/// "page 1 of {that area's own page count}" instead of continuing the running count across all of
/// the sheet's areas -- diverging from the real print/PDF export's continuous per-sheet counter
/// (WorkbookPdfContentBuilder.ResolveEffectiveSheetPageNumber/-TotalPages).
/// </summary>
public sealed class R86_PrintPreviewPaginationContextMultiAreaPageNumberTests
{
    private static readonly FakeTextMeasurer Measurer = new();

    private static (Workbook Workbook, Sheet Sheet) CreateBook()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }

    [Fact]
    public void BuildPage_SecondPrintAreasFirstPage_ContinuesPageNumberFromFirstArea()
    {
        var (workbook, sheet) = CreateBook();

        // Area 1: a single populated cell -- guaranteed exactly one printable page (mirrors
        // TryCreate_SingleCellSheetHasOnePage's own single-cell-sheet assumption).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Area1"));
        var area1 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));

        // Area 2: a wide/tall populated range, guaranteed multiple pages (mirrors
        // TryCreate_WideTallRangeProducesMultiplePages).
        for (uint r = 1; r <= 200; r += 50)
            sheet.SetCell(new CellAddress(sheet.Id, r, 10), new NumberValue(r));
        var area2 = new GridRange(new CellAddress(sheet.Id, 1, 10), new CellAddress(sheet.Id, 400, 70));

        sheet.SetPrintAreas([area1, area2]);
        sheet.PageFooter = new WorksheetHeaderFooter("", "Page &P of &N", "");

        PrintPreviewPaginationContext.TryCreate(workbook, sheet, Measurer, out var context).Should().BeTrue();

        // Area 1 alone is exactly 1 page; area 2 alone is 2+ pages (asserted independently so
        // this test's own expectations don't depend on FitScalarLine/pagination internals).
        var area1Plan = PagePaginationPlanner.Paginate(
            area1, sheet.ScaleToFit, sheet.PrintTitleRows, sheet.PrintTitleColumns, sheet.PaperSize,
            sheet.PageOrientation, sheet.PageMargins, sheet.RowHeights, sheet.DefaultRowHeight,
            sheet.ColumnWidths, sheet.DefaultColumnWidth, sheet.HeaderMargin, sheet.FooterMargin,
            sheet.RowPageBreaks, sheet.ColumnPageBreaks, sheet.IsRowEffectivelyHidden, sheet.IsColEffectivelyHidden);
        area1Plan.PageCount.Should().Be(1);
        context.PageCount.Should().BeGreaterThan(2);

        // Global preview page index 1 (0-based) is the first physical page of area 2. Its
        // on-screen page number must continue the running count from area 1 (page 2), not reset
        // to area 2's own local page 1.
        var area2FirstPage = context.BuildPage(1);
        area2FirstPage.Should().NotBeNull();
        area2FirstPage!.PageNumber.Should().Be(2);

        // The &P/&N footer text must agree: "Page 2 of {aggregate total}", not "Page 1 of
        // {area 2's own page count}".
        var footerText = string.Concat(area2FirstPage.FooterRuns.Select(r => r.Text));
        footerText.Should().Be($"Page 2 of {context.PageCount}");
    }

    /// <summary>Sibling no-regression: a single-print-area sheet's page numbering is unaffected.</summary>
    [Fact]
    public void BuildPage_SingleAreaSheet_PageNumbersStillStartAtFirstPageNumber()
    {
        var (workbook, sheet) = CreateBook();
        for (uint r = 1; r <= 200; r += 50)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));
        sheet.PrintArea = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 400, 60));
        sheet.PageFooter = new WorksheetHeaderFooter("", "Page &P of &N", "");

        PrintPreviewPaginationContext.TryCreate(workbook, sheet, Measurer, out var context).Should().BeTrue();
        context.PageCount.Should().BeGreaterThan(1);

        var firstPage = context.BuildPage(0);
        firstPage.Should().NotBeNull();
        firstPage!.PageNumber.Should().Be(1);

        var footerText = string.Concat(firstPage.FooterRuns.Select(r => r.Text));
        footerText.Should().Be($"Page 1 of {context.PageCount}");
    }
}
