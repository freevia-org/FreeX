using System.Windows;
using FluentAssertions;
using Free.Shared.Pdf;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Text;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R131-app-host-headerfooter-center-asymmetric-margin-1: Excel centers a header/footer's CENTER
/// section on the PRINTABLE width -- the page width minus the left and right margins/insets -- not on
/// the raw page width. The two positions only coincide when the left and right insets happen to be
/// equal. Two of this app's three header/footer render paths centered on the full page width
/// unconditionally:
///   1. <see cref="PrintRenderer.HeaderFooterDrawing"/> (WPF native print / print-preview / physical
///      print) -- fixed by <see cref="PrintRenderer.ResolveHeaderFooterSectionRects"/>.
///   2. <see cref="PageContentRenderModelBuilder"/> (the shared, portable Page-Layout/Print-Preview
///      SCREEN model consumed by BOTH the WPF host's <c>PrintPreviewPaginationContext</c> and the
///      Avalonia host's <c>AvaloniaPrintPreviewPaginationContext</c> -- fixing this one file fixes both
///      shells' on-screen preview at once).
/// The third path, <see cref="FreeX.App.Services.WorkbookPdfContentBuilder"/> (the shared PDF export
/// path both WPF's "Export to PDF" and the Avalonia/Skia exporter draw from), already centered the
/// section correctly (<c>mL + sectionWidth</c>) -- these tests pin that path's already-correct behavior
/// too, and prove all three now agree on the same physical page position for an asymmetric-margin page.
/// </summary>
public sealed class R131_HeaderFooterCenterSectionAsymmetricMarginTests
{
    private const double Dpi = 96.0;
    private const double Pts = 72.0;

    // Deliberately asymmetric: 0.2in left vs 1.5in right. Any left/right mismatch reproduces the bug;
    // this gap (1.3in) is large enough that the old page-centered formula and the correct
    // printable-area-centered formula land ~0.65in (~62px) apart -- far outside any measurement noise.
    private const double MarginLeftIn = 0.2;
    private const double MarginRightIn = 1.5;
    private const double PageWidthIn = 8.5; // Letter portrait

    private static double ExpectedCenterLeftIn(double marginLeftIn, double marginRightIn, double pageWidthIn) =>
        marginLeftIn + ((pageWidthIn - marginLeftIn - marginRightIn) / 3.0);

    // ------------------------------------------------------------------
    // Path 1: WPF print / print-preview rendering (PrintRenderer.HeaderFooterDrawing)
    // ------------------------------------------------------------------

    [Fact]
    public void ResolveHeaderFooterSectionRects_AsymmetricMargins_CentersOnPrintableAreaNotFullPage()
    {
        var pageW = PageWidthIn * Dpi;
        var leftInset = MarginLeftIn * Dpi;
        var rightInset = MarginRightIn * Dpi;

        var (_, center, _) = PrintRenderer.ResolveHeaderFooterSectionRects(pageW, leftInset, rightInset, y: 10, lineHeight: 14);

        var expectedCenterLeft = ExpectedCenterLeftIn(MarginLeftIn, MarginRightIn, PageWidthIn) * Dpi;

        center.Left.Should().BeApproximately(expectedCenterLeft, 0.01,
            "Excel centers the center header/footer section on the PRINTABLE width between the " +
            "margins, not on the raw page width -- with asymmetric margins these are different points");

        // Directly rule out the old bug: the page-centered formula would place the band ~62px away.
        var sectionWidth = (pageW - leftInset - rightInset) / 3.0;
        var buggyFullPageCenteredLeft = (pageW - sectionWidth) / 2.0;
        center.Left.Should().NotBeApproximately(buggyFullPageCenteredLeft, 1.0,
            "the old formula centered on the full page width -- it must no longer match");
    }

    [Fact]
    public void ResolveHeaderFooterSectionRects_SymmetricMargins_StillMatchesFullPageCenter()
    {
        // Sibling/no-regression: with symmetric margins (the overwhelmingly common case), centering on
        // the printable area and centering on the full page coincide exactly -- the fix must not move
        // the band in this case.
        var pageW = PageWidthIn * Dpi;
        var inset = 0.75 * Dpi;

        var (_, center, _) = PrintRenderer.ResolveHeaderFooterSectionRects(pageW, inset, inset, y: 10, lineHeight: 14);

        var sectionWidth = (pageW - (2 * inset)) / 3.0;
        var fullPageCenteredLeft = (pageW - sectionWidth) / 2.0;

        center.Left.Should().BeApproximately(fullPageCenteredLeft, 0.01);
    }

    // ------------------------------------------------------------------
    // Path 2: shared Page-Layout/Print-Preview screen model (PageContentRenderModelBuilder), used by
    // BOTH the WPF and Avalonia hosts' Print Preview.
    // ------------------------------------------------------------------

    [Fact]
    public void Build_AsymmetricMargins_CentersHeaderOnPrintableAreaNotFullPage()
    {
        var (workbook, sheet) = CreateSheet(MarginLeftIn, MarginRightIn);
        sheet.PageHeader = new WorksheetHeaderFooter("", "CTR", "");

        var layout = BuildFirstPage(workbook, sheet)!;
        var centerRun = layout.HeaderRuns.Should().ContainSingle().Subject;

        var expectedCenterLeft = ExpectedCenterLeftIn(MarginLeftIn, MarginRightIn, PageWidthIn) * Dpi;

        centerRun.Bounds.Left.Should().BeApproximately(expectedCenterLeft, 0.01,
            "the shared print-preview screen model must center the header's center section on the " +
            "printable width, matching the WPF print path and the PDF export path");
    }

    [Fact]
    public void Build_SymmetricMargins_StillMatchesFullPageCenter()
    {
        // Sibling/no-regression for the Presentation-layer fix.
        var (workbook, sheet) = CreateSheet(0.75, 0.75);
        sheet.PageHeader = new WorksheetHeaderFooter("", "CTR", "");

        var layout = BuildFirstPage(workbook, sheet)!;
        var centerRun = layout.HeaderRuns.Should().ContainSingle().Subject;

        var pageW = PageWidthIn * Dpi;
        var inset = 0.75 * Dpi;
        var sectionWidth = (pageW - (2 * inset)) / 3.0;
        var fullPageCenteredLeft = (pageW - sectionWidth) / 2.0;

        centerRun.Bounds.Left.Should().BeApproximately(fullPageCenteredLeft, 0.01);
    }

    // ------------------------------------------------------------------
    // Path 3: Avalonia/Skia PDF export path (WorkbookPdfContentBuilder), shared by both shells' "Export
    // to PDF" -- already correct; pinned here as the reference and to rule out regression.
    // ------------------------------------------------------------------

    [Fact]
    public void BuildWithPageSetup_AsymmetricMargins_CentersHeaderPictureOnPrintableArea()
    {
        var centerLeftPt = BuildPdfCenterPictureX(MarginLeftIn, MarginRightIn);
        var expectedCenterLeftPt = ExpectedCenterLeftIn(MarginLeftIn, MarginRightIn, PageWidthIn) * Pts;

        centerLeftPt.Should().BeApproximately(expectedCenterLeftPt, 0.5,
            "the PDF export path already centers the center section on the printable width -- pinning " +
            "this so a future change can't silently regress it back to page-width centering");
    }

    // ------------------------------------------------------------------
    // Cross-path parity: the whole point of the finding -- all three paths must agree on the same
    // physical page position for the center section, given the same asymmetric margins.
    // ------------------------------------------------------------------

    [Fact]
    public void CenterSectionXOrigin_AsymmetricMargins_IdenticalAcrossWpfPreviewAndPdfExportPaths()
    {
        // Path 1: WPF print/print-preview.
        var pageWpx = PageWidthIn * Dpi;
        var (_, wpfCenter, _) = PrintRenderer.ResolveHeaderFooterSectionRects(
            pageWpx, MarginLeftIn * Dpi, MarginRightIn * Dpi, y: 0, lineHeight: 14);
        var wpfCenterLeftIn = wpfCenter.Left / Dpi;

        // Path 2: shared print-preview screen model (both shells).
        var (workbook, sheet) = CreateSheet(MarginLeftIn, MarginRightIn);
        sheet.PageHeader = new WorksheetHeaderFooter("", "CTR", "");
        var layout = BuildFirstPage(workbook, sheet)!;
        var presentationCenterLeftIn = layout.HeaderRuns.Single().Bounds.Left / Dpi;

        // Path 3: PDF export (both shells).
        var pdfCenterLeftIn = BuildPdfCenterPictureX(MarginLeftIn, MarginRightIn) / Pts;

        wpfCenterLeftIn.Should().BeApproximately(presentationCenterLeftIn, 0.02,
            "the WPF print path and the shared print-preview screen model must place the header/footer " +
            "center section at the same physical position on the page");
        wpfCenterLeftIn.Should().BeApproximately(pdfCenterLeftIn, 0.02,
            "the WPF print path and the PDF export path must place the header/footer center section at " +
            "the same physical position on the page, matching Excel's centering-on-printable-width " +
            "behavior regardless of output route");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static (Workbook Workbook, Sheet Sheet) CreateSheet(double marginLeftIn, double marginRightIn)
    {
        var workbook = new Workbook("HeaderFooterCenter.xlsx");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PaperSize = WorksheetPaperSize.Letter;
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;
        sheet.PageMargins = new WorksheetPageMargins(Left: marginLeftIn, Right: marginRightIn, Top: 0.75, Bottom: 0.75);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Data"));
        return (workbook, sheet);
    }

    private static PageContentLayout? BuildFirstPage(Workbook workbook, Sheet sheet)
    {
        var printRange = sheet.GetUsedRange() ?? new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var pagePlan = PagePaginationPlanner.Paginate(
            printRange,
            sheet.ScaleToFit,
            sheet.PrintTitleRows,
            sheet.PrintTitleColumns,
            sheet.PaperSize,
            sheet.PageOrientation,
            sheet.PageMargins);

        return PageContentRenderModelBuilder.Build(workbook, sheet, pagePlan, 0, new StubTextMeasurer(), new DateTime(2026, 1, 1));
    }

    /// <summary>
    /// Builds a PDF page whose center header section holds a single <c>&amp;G</c> picture sized to
    /// exactly fill the computed section width. Because <c>WorkbookPdfContentBuilder</c> centers a
    /// picture within its section via <c>sectionLeft + (sectionWidth - imageWidth) / 2</c>, an image
    /// exactly as wide as the section reports an X offset of zero -- so the resulting
    /// <see cref="PdfImage"/>'s X is the section's raw left edge itself (in PDF points), unshifted by
    /// any text-measurement centering. Mirrors the same technique <c>PrintRenderer.HeaderFooterPictures
    /// .CalculateHeaderFooterPictureRect</c> uses for the WPF path.
    /// </summary>
    private static double BuildPdfCenterPictureX(double marginLeftIn, double marginRightIn)
    {
        var (workbook, sheet) = CreateSheet(marginLeftIn, marginRightIn);
        sheet.PageHeader = new WorksheetHeaderFooter("", "&G", "");

        var sectionWidthPx = ((PageWidthIn * Dpi) - (marginLeftIn * Dpi) - (marginRightIn * Dpi)) / 3.0;
        sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(
            Left: null,
            Center: new WorksheetHeaderFooterPicture(new byte[] { 1, 2, 3, 4 }, "image/png", Width: sectionWidthPx, Height: 20),
            Right: null);

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

        var image = doc.Pages[0].Ops.OfType<PdfImage>().Should().ContainSingle().Subject;
        return image.X;
    }

    private sealed class StubTextMeasurer : ITextMeasurer
    {
        public TextSize Measure(string? text, string? fontFamily, double fontSize, bool bold, bool italic) =>
            new(string.IsNullOrEmpty(text) ? 0 : text.Length * fontSize * 0.5, fontSize * 1.2);
    }
}
