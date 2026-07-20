using FluentAssertions;
using FreeX.Core.Model;
using Free.Shared.Pdf;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R50-render-print-preview-pagination-3-1: a fit-to-N-pages PDF export shrinks the cell GRID
/// geometry (via <see cref="WorkbookPdfContentBuilder"/>'s defensive overflow correction) but must
/// apply that SAME shrink ratio to rendered TEXT font sizes too -- otherwise the grid is scaled down
/// while text stays at its unscaled (clamped) size, overflowing the now-tiny cells.
/// </summary>
public sealed class R50_pagination_fit_scale_Tests
{
    [Fact]
    public void BuildWithPageSetup_FitToOnePage_ScalesFontSizeWithTheShrunkGrid()
    {
        var workbook = new Workbook("FitScale");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PaperSize = WorksheetPaperSize.A4;
        sheet.PageMargins = WorksheetPageMargins.Narrow;
        sheet.ScaleToFit = WorksheetScaleToFit.Default; // 100%, no fit-to-pages constraint yet.

        // Discover the sheet's natural (unscaled) per-page capacity using a large probe range, the
        // same approach R20_PortablePrintScaleTests uses.
        var probeRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 200, 60));
        var natural = SheetPdfPageSetupResolver.ResolveCapacity(sheet, probeRange);
        natural.RowsPerPage.Should().BeGreaterThan(0u);
        natural.ColumnsPerPage.Should().BeGreaterThan(0u);

        // Body content that naturally needs 3 column-pages x 3 row-pages at 100% scale.
        var bodyCols = natural.ColumnsPerPage * 3;
        var bodyRows = natural.RowsPerPage * 3;

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hello"));
        sheet.SetCell(new CellAddress(sheet.Id, bodyRows, bodyCols), new TextValue("Corner"));

        // "Fit to 1 page(s) wide by 1 page(s) tall" -- both axes explicitly constrained to a single
        // page, so ResolveCapacity resolves each axis independently to fit the entire body onto page
        // 1 (matching R20's "both axes constrained" case), even though the columns'/rows' ACTUAL point
        // widths/heights still total roughly 3x what fits on one physical page.
        sheet.ScaleToFit = new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: 1, FitToPagesTall: 1);

        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);
        var printPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent);
        printPlan.IsReady.Should().BeTrue(printPlan.StatusText);

        var pdfPlan = PortablePdfExportPlanner.CreatePlan(printPlan);
        pdfPlan.IsReady.Should().BeTrue(pdfPlan.StatusText);

        var document = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, pdfPlan);
        document.Pages.Should().NotBeEmpty();

        // The "Hello" cell's font size is the sheet's default (11pt, clamped to 10pt) times the
        // ratio actually used to shrink the grid. Since the resolved page count already equals the
        // fit-to target (1x1), CalculateEffectiveScalePercent alone reports ~100% -- pre-fix, the
        // rendered font size stayed at the full clamped 10pt even though the grid geometry (and thus
        // the cell this text sits in) was defensively shrunk to roughly a third of that. Post-fix,
        // the same defensive shrink ratio must also reduce the font size.
        var helloFontSizes = document.Pages[0].Ops
            .OfType<PdfText>()
            .Where(t => t.Text == "Hello")
            .Select(t => t.FontSize)
            .ToList();

        helloFontSizes.Should().NotBeEmpty("the 'Hello' cell must be rendered on the first page");
        helloFontSizes.Should().OnlyContain(size => size < 9.5,
            "text must shrink along with the grid geometry when the defensive fit-to-page " +
            "correction kicks in, not stay at the unscaled clamped font size (10pt)");
    }
}
