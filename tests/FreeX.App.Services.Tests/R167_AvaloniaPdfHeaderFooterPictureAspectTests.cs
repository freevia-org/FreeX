using FluentAssertions;
using FreeX.Core.Model;
using Free.Shared.Pdf;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R167-services-avalonia-headerfooter-picture-aspect-1 / R167-services-avalonia-headerfooter-
/// picture-band-1: rounds 166/167 fixed a header/footer picture's aspect ratio and bounded/grew the
/// band ONLY in the WPF-shared <c>WorksheetPrintHeaderFooterGeometryPlanner</c>
/// (ResolvePictureBounds/BuildBand), which serves WPF Print, WPF Print Preview, and WPF's own PDF
/// export (which rasterizes the print output). The Avalonia/Skia PDF export path
/// (<see cref="WorkbookPdfContentBuilder.BuildWithPageSetup"/>, reached via
/// <c>SkiaPdfDocumentExporter</c> on Linux/macOS) is a wholly separate implementation that neither
/// round touched: it clamped width and height with two independent <c>Math.Min</c> calls -- the
/// exact non-uniform-scale distortion round 167 removed elsewhere -- and never grew the header/
/// footer band to fit a picture at all. These tests exercise THIS path directly (not the shared
/// planner) and assert the emitted <see cref="PdfImage"/> geometry a tall, narrow header/footer
/// picture actually produces in the exported PDF.
/// </summary>
public sealed class R167_AvaloniaPdfHeaderFooterPictureAspectTests
{
    // Fixed, explicit page geometry (rather than the sheet's A4/Normal-margin defaults) so every
    // expected number below is independently derivable arithmetic, not a value read back from the
    // implementation under test.
    private const double PageWidthPt = 612.0;   // Letter, portrait: 8.5in * 72
    private const double PageHeightPt = 792.0;  // Letter, portrait: 11in * 72
    private const double MarginPt = 72.0;       // 1in margins on all sides
    private const double PtPerPx = 72.0 / 96.0;
    private const double MaxBandHeightFraction = 0.25; // mirrors MaxHeaderFooterBandHeightFraction

    [Fact]
    public void BuildWithPageSetup_TallNarrowHeaderPicture_PreservesAspectRatio_InsteadOfSquashing()
    {
        // A tall, narrow picture (aspect ratio 1:8) whose raw height (400px -> 300pt) is far larger
        // than the base text-only band height would ever be, and far larger than
        // MaxBandHeightFraction * pageHeight (198pt) too -- so the band must grow to fit it (up to
        // that 25% cap) and the picture must then be scaled UNIFORMLY into that band, not squashed
        // onto whichever axis it overflows more.
        var picture = new WorksheetHeaderFooterPicture(new byte[] { 1, 2, 3, 4 }, "image/png", Width: 50, Height: 400);
        var page = BuildPageWithHeaderPicture(picture);

        var img = page.Ops.OfType<PdfImage>().Should().ContainSingle().Subject;

        // The band grows to fit the picture but is capped at 25% of the page height (792 * 0.25 =
        // 198pt); the raw picture height (300pt) exceeds that cap, so the cap -- not the raw height
        // -- is what the picture is scaled into on its binding axis.
        var expectedBandHeightPt = PageHeightPt * MaxBandHeightFraction; // 198.0
        var rawWidthPt = picture.Width * PtPerPx;   // 37.5
        var rawHeightPt = picture.Height * PtPerPx; // 300.0
        var expectedScale = expectedBandHeightPt / rawHeightPt; // 0.66 -- height is the binding axis
        var expectedWidthPt = rawWidthPt * expectedScale;
        var expectedHeightPt = rawHeightPt * expectedScale; // == expectedBandHeightPt

        img.Height.Should().BeApproximately(expectedHeightPt, 0.5,
            "the band must grow to accommodate the picture (bounded at 25% of the page height), " +
            "not stay pinned to the ~1-line text-only band height the prior code used as the height clamp");
        img.Width.Should().BeApproximately(expectedWidthPt, 0.5,
            "once the height axis binds, the width must shrink by the SAME uniform scale factor, " +
            "not stay at its full raw width while only the height gets clamped");

        // The defect this round fixes, stated directly: the emitted image's aspect ratio must match
        // the source picture's aspect ratio. Before the fix, width stayed at its full raw 37.5pt while
        // height was clamped independently to a small text-band height (~tens of points), so the
        // emitted image came out far WIDER than tall despite the source picture being 8x taller than
        // wide -- an inverted, badly distorted aspect ratio.
        var expectedAspect = picture.Width / picture.Height; // 0.125
        var actualAspect = img.Width / img.Height;
        actualAspect.Should().BeApproximately(expectedAspect, 0.01,
            "the exported picture must keep the source picture's aspect ratio instead of being " +
            "squashed onto independent width/height clamps");
    }

    [Fact]
    public void BuildWithPageSetup_PictureThatAlreadyFitsBothAxes_IsDrawnAtItsFullRawSize_Unchanged()
    {
        // No-regression sibling: a picture that already fits comfortably within both the section
        // width and the (grown-if-needed) band height must be drawn unscaled, exactly like before
        // this fix -- the uniform-scale rule must not shrink a picture that never needed shrinking.
        var picture = new WorksheetHeaderFooterPicture(new byte[] { 5, 6, 7, 8 }, "image/png", Width: 40, Height: 20);
        var page = BuildPageWithHeaderPicture(picture);

        var img = page.Ops.OfType<PdfImage>().Should().ContainSingle().Subject;

        var expectedWidthPt = picture.Width * PtPerPx;   // 30.0
        var expectedHeightPt = picture.Height * PtPerPx; // 15.0

        img.Width.Should().BeApproximately(expectedWidthPt, 0.5);
        img.Height.Should().BeApproximately(expectedHeightPt, 0.5);
    }

    [Fact]
    public void BuildWithPageSetup_TallNarrowFooterPicture_AlsoPreservesAspectRatio()
    {
        // Sibling: the footer half of RenderHeaderFooterBand shares the exact same
        // RenderHeaderFooterSection/GrowHeaderFooterBandHeightForPictures code the header half calls,
        // so the fix (and this proof) must hold symmetrically for the footer band too.
        var picture = new WorksheetHeaderFooterPicture(new byte[] { 9, 9, 9, 9 }, "image/png", Width: 50, Height: 400);

        var workbook = new Workbook("FooterPicture");
        var sheet = workbook.AddSheet("Sheet1");
        ApplyFixedGeometry(sheet);
        sheet.PageFooter = new WorksheetHeaderFooter("", "&G", "");
        sheet.PageFooterPictures = new WorksheetHeaderFooterPictureSet(Left: null, Center: picture, Right: null);

        var page = BuildPage(workbook);
        var img = page.Ops.OfType<PdfImage>().Should().ContainSingle().Subject;

        var expectedAspect = picture.Width / picture.Height; // 0.125
        var actualAspect = img.Width / img.Height;
        actualAspect.Should().BeApproximately(expectedAspect, 0.01,
            "the footer band must preserve picture aspect ratio exactly like the header band does");
    }

    private static void ApplyFixedGeometry(Sheet sheet)
    {
        sheet.PaperSize = WorksheetPaperSize.Letter;
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;
        sheet.PageMargins = new WorksheetPageMargins(Left: 1.0, Right: 1.0, Top: 1.0, Bottom: 1.0);
    }

    private static PdfContentPage BuildPageWithHeaderPicture(WorksheetHeaderFooterPicture picture)
    {
        var workbook = new Workbook("HeaderPicture");
        var sheet = workbook.AddSheet("Sheet1");
        ApplyFixedGeometry(sheet);
        sheet.PageHeader = new WorksheetHeaderFooter("", "&G", "");
        sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(Left: null, Center: picture, Right: null);

        return BuildPage(workbook);
    }

    private static PdfContentPage BuildPage(Workbook workbook)
    {
        var cell = Cell.FromValue(new TextValue("Hi"));
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 1, 1), cell);

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
        return doc.Pages[0];
    }
}
