using FluentAssertions;
using FreeX.Core.Model;
using Free.Shared.Pdf;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R99-services-header-band-2: Excel's page-setup model places the header/footer TEXT within the
/// top/bottom margin band -- the printed cell grid's own edge sits at
/// <c>max(topMargin, headerMargin)</c> (and <c>max(bottomMargin, footerMargin)</c>), not the plain
/// margin. <see cref="SheetPdfPageSetupResolver.ResolveCapacityDetail"/> (R96) already derives its
/// row/column page CAPACITY this way, but <see cref="WorkbookPdfContentBuilder.BuildPageWithPageSetup"/>
/// insetted the actual drawn content rect by the plain margin only and explicitly discarded the
/// resolver's header/footer band values (<c>_ = headerBandPt; _ = footerBandPt;</c>) -- so whenever a
/// sheet's Header/Footer margin exceeded its Top/Bottom margin, the PDF content renderer's real grid
/// draw ops (gridlines, cell fills, cell text) started above the header text's own band and visually
/// collided with it, even though the page's row capacity (and the WPF print path, fixed separately
/// this round in PrintRenderer.HeaderFooter.cs) already agreed on where the grid should start.
/// </summary>
public sealed class R99_PdfHeaderFooterMarginOverlapTests
{
    private const double Pts = SheetPdfPageSetupResolver.PdfPointsPerInch; // 72

    [Fact]
    public void BuildWithPageSetup_HeaderMarginExceedsTopMargin_GridTopStartsAtHeaderMargin_NotPlainTopMargin()
    {
        // Header margin (1.5in) deliberately larger than the top margin (0.2in) -- the bug case.
        var (page, pageH, mT, headerEdgePt) = BuildPageWithMargins(topMarginIn: 0.2, headerMarginIn: 1.5);

        var topGridLineY = page.Ops.OfType<PdfLine>()
            .Where(l => l.Y1 == l.Y2)
            .Select(l => l.Y1)
            .Max(); // In PDF y-up, the topmost horizontal gridline has the largest Y.

        var expectedGridTopY = pageH - Math.Max(mT, headerEdgePt);

        topGridLineY.Should().BeApproximately(expectedGridTopY, 0.5,
            "the grid's top edge must sit at max(topMargin, headerMargin) from the page top, matching " +
            "SheetPdfPageSetupResolver's page-capacity math, not the plain (smaller) top margin");

        // No-overlap assertion on real geometry: the header text baseline must sit strictly above
        // (higher Y, in y-up space) the grid's top edge, never inside/below it.
        var headerRun = page.Ops.OfType<PdfText>().Single(t => t.Text == "HDR");
        headerRun.Y.Should().BeGreaterThanOrEqualTo(topGridLineY,
            "the header text baseline must sit at or above the grid's top edge, never inside/below it " +
            "(touching -- Y exactly equal -- is the expected outcome once headerMargin >= topMargin, " +
            "matching the WPF path's equivalent clamp)");
    }

    [Fact]
    public void BuildWithPageSetup_TopMarginExceedsHeaderMargin_GridTopStartsAtPlainTopMargin()
    {
        // Top margin (1.0in) larger than the header margin (0.3in) -- Excel's common/default case.
        var (page, pageH, mT, headerEdgePt) = BuildPageWithMargins(topMarginIn: 1.0, headerMarginIn: 0.3);

        var topGridLineY = page.Ops.OfType<PdfLine>()
            .Where(l => l.Y1 == l.Y2)
            .Select(l => l.Y1)
            .Max();

        var expectedGridTopY = pageH - Math.Max(mT, headerEdgePt);
        expectedGridTopY.Should().BeApproximately(pageH - mT, 0.01, "sanity: top margin should win here");

        topGridLineY.Should().BeApproximately(expectedGridTopY, 0.5,
            "when the top margin is already the larger of the two, the grid must still start at the " +
            "plain top margin (no regression from the max() fix)");
    }

    [Fact]
    public void BuildWithPageSetup_FooterMarginExceedsBottomMargin_GridBottomRespectsFooterMargin()
    {
        var (page, _, _, _) = BuildPageWithMargins(
            topMarginIn: 0.75, headerMarginIn: 0.3, bottomMarginIn: 0.2, footerMarginIn: 1.5);

        var (_, _, _, _, _, mB, _, _) = SheetPdfPageSetupResolver.ComputePdfGeometry(LastBuiltSheet!);
        var footerEdgePt = LastBuiltSheet!.FooterMargin * Pts;
        var expectedGridBottomY = Math.Max(mB, footerEdgePt);

        var bottomGridLineY = page.Ops.OfType<PdfLine>()
            .Where(l => l.Y1 == l.Y2)
            .Select(l => l.Y1)
            .Min();

        bottomGridLineY.Should().BeGreaterThanOrEqualTo(expectedGridBottomY - 0.5,
            "the grid's bottom edge must not draw below max(bottomMargin, footerMargin), matching the " +
            "page-capacity math and leaving room for the footer band");
    }

    private static Sheet? LastBuiltSheet;

    private static (PdfContentPage Page, double PageHeightPt, double MarginTopPt, double HeaderEdgePt) BuildPageWithMargins(
        double topMarginIn,
        double headerMarginIn,
        double bottomMarginIn = 0.75,
        double footerMarginIn = 0.3)
    {
        var workbook = new Workbook("HeaderBand");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PageMargins = new WorksheetPageMargins(0.7, 0.7, topMarginIn, bottomMarginIn);
        sheet.HeaderMargin = headerMarginIn;
        sheet.FooterMargin = footerMarginIn;
        sheet.PrintGridlines = true;
        sheet.PageHeader = new WorksheetHeaderFooter("", "HDR", "");
        var cell = Cell.FromValue(new TextValue("Hi"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), cell);
        LastBuiltSheet = sheet;

        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);

        var exportPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent);
        exportPlan.IsReady.Should().BeTrue(exportPlan.StatusText);

        var pdfPlan = PortablePdfExportPlanner.CreatePlan(exportPlan);
        pdfPlan.IsReady.Should().BeTrue(pdfPlan.StatusText);

        var doc = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, pdfPlan);
        doc.Pages.Should().NotBeEmpty();

        var (pageW, pageHeightPt, mL, mR, marginTopPt, mB, _, _) = SheetPdfPageSetupResolver.ComputePdfGeometry(sheet);
        var headerEdgePt = sheet.HeaderMargin * Pts;
        return (doc.Pages[0], pageHeightPt, marginTopPt, headerEdgePt);
    }
}
