using FluentAssertions;

using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// R84-services-print-pagination-5-1: <see cref="PrintPreviewPaginationContext"/> is the Print Preview
/// window's data source (see MainWindow.PrintPreview.cs). It must paginate EVERY configured print area
/// (Sheet.PrintAreas — Excel's multi-area <c>_xlnm.Print_Area</c>), not only the first, so the preview's
/// page count and page content match the real print/PDF export (WorkbookExportPrintPlanner /
/// WorksheetPrintRenderPlanner, which both iterate the full list). Before the fix, TryCreate resolved
/// only <c>PageBreakPreviewInstructionBuilder.TryResolvePrintRange</c> (singular, first area only), so a
/// second print-area region silently vanished from the preview while still printing/exporting correctly.
/// </summary>
public sealed class R84_print_preview_multiarea_Tests
{
    private static readonly FakeTextMeasurer Measurer = new();

    private static (Workbook Workbook, Sheet Sheet) CreateBook()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }

    [Fact]
    public void TryCreate_MultiAreaPrintRange_PageCountCoversBothAreas()
    {
        var (workbook, sheet) = CreateBook();
        for (uint r = 1; r <= 10; r++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));
            sheet.SetCell(new CellAddress(sheet.Id, r, 6), new NumberValue(r + 100));
        }

        var area1 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 4));
        var area2 = new GridRange(new CellAddress(sheet.Id, 1, 6), new CellAddress(sheet.Id, 10, 9));
        sheet.SetPrintAreas([area1, area2]);

        PrintPreviewPaginationContext.TryCreate(workbook, sheet, Measurer, out var combinedContext)
            .Should().BeTrue();

        var sheetWithOnlyFirstArea = workbook.AddSheet("Sheet1Only");
        for (uint r = 1; r <= 10; r++)
            sheetWithOnlyFirstArea.SetCell(new CellAddress(sheetWithOnlyFirstArea.Id, r, 1), new NumberValue(r));
        sheetWithOnlyFirstArea.SetPrintAreas([
            new GridRange(
                new CellAddress(sheetWithOnlyFirstArea.Id, 1, 1),
                new CellAddress(sheetWithOnlyFirstArea.Id, 10, 4))
        ]);
        PrintPreviewPaginationContext.TryCreate(workbook, sheetWithOnlyFirstArea, Measurer, out var firstAreaOnlyContext)
            .Should().BeTrue();

        // The combined (two-area) context must report MORE pages than a sheet with only the first area
        // configured — i.e. the second print-area region must contribute pages of its own, not be
        // dropped by the preview.
        combinedContext.PageCount.Should().BeGreaterThan(firstAreaOnlyContext.PageCount);
        combinedContext.PageCount.Should().Be(firstAreaOnlyContext.PageCount * 2);
    }

    [Fact]
    public void BuildPage_MultiAreaPrintRange_SecondAreaPagesAreReachableAndDistinctFromFirst()
    {
        var (workbook, sheet) = CreateBook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("FirstAreaCell"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 6), new TextValue("SecondAreaCell"));

        var area1 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 4));
        var area2 = new GridRange(new CellAddress(sheet.Id, 1, 6), new CellAddress(sheet.Id, 5, 9));
        sheet.SetPrintAreas([area1, area2]);

        PrintPreviewPaginationContext.TryCreate(workbook, sheet, Measurer, out var context).Should().BeTrue();
        context.PageCount.Should().Be(2);

        var firstPage = context.BuildPage(0);
        var secondPage = context.BuildPage(1);

        firstPage.Should().NotBeNull();
        secondPage.Should().NotBeNull();

        var firstText = PrintPreviewInstructionBuilder.Build(firstPage!).Instructions;
        var secondText = PrintPreviewInstructionBuilder.Build(secondPage!).Instructions;

        firstText.Should().Contain(i => i.Kind == PrintPreviewPaintKind.Text && i.Text == "FirstAreaCell");
        secondText.Should().Contain(i => i.Kind == PrintPreviewPaintKind.Text && i.Text == "SecondAreaCell");

        // Out-of-range across BOTH areas must still return null (no regression on the bounds check).
        context.BuildPage(context.PageCount).Should().BeNull();
        context.BuildPage(-1).Should().BeNull();
    }

    [Fact]
    public void TryCreate_SingleAreaPrintRange_StillProducesOnePageNoRegression()
    {
        // No-regression sibling: a single-area (or no explicit print area / used-range fallback) sheet
        // must keep behaving exactly as before — one plan, page count unaffected by the multi-area path.
        var (workbook, sheet) = CreateBook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("hello"));

        PrintPreviewPaginationContext.TryCreate(workbook, sheet, Measurer, out var context).Should().BeTrue();

        context.PageCount.Should().Be(1);
        var page = context.BuildPage(0);
        page.Should().NotBeNull();
        PrintPreviewInstructionBuilder.Build(page!).Instructions
            .Should().Contain(i => i.Kind == PrintPreviewPaintKind.Text && i.Text == "hello");

        context.BuildPage(1).Should().BeNull();
        context.BuildPage(-1).Should().BeNull();
    }
}
