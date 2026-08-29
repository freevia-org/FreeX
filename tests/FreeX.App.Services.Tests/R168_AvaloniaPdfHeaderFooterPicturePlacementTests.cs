using FluentAssertions;
using Free.Shared.Pdf;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R168-services-avalonia-headerfooter-picture-page-1: the Avalonia/Skia PDF export path
/// (<see cref="WorkbookPdfContentBuilder.BuildWithPageSetup"/>, reached via
/// <c>SkiaPdfDocumentExporter</c> on Linux/macOS) anchors a header/footer picture by CENTERING it on
/// the section's text baseline. That baseline sits only a few points inside the page edge, so once
/// round 167 let the band grow to fit a picture (up to a quarter of the page height), half of a tall
/// picture was centred straight past the edge of the sheet: a header picture ran off the top of the
/// page and a footer picture off the bottom, in both cases silently clipped by the PDF viewer.
///
/// Sibling of the WPF-side <c>BuildBand</c> footer clamp
/// (R168-presentation-headerfooter-footer-band-page-1) -- the same "the band grew but nothing held it
/// on the paper" defect, in the second of the two independent geometry implementations.
/// </summary>
public sealed class R168_AvaloniaPdfHeaderFooterPicturePlacementTests
{
    private const double PageWidthPt = 612.0;   // Letter, portrait: 8.5in * 72
    private const double PageHeightPt = 792.0;  // Letter, portrait: 11in * 72

    [Fact]
    public void BuildWithPageSetup_TallHeaderPicture_IsDrawnEntirelyOnThePage()
    {
        var img = RenderSinglePicture(isFooter: false);

        img.Y.Should().BeGreaterThanOrEqualTo(0, "the picture must not hang below the page's bottom edge");
        (img.Y + img.Height).Should().BeLessThanOrEqualTo(PageHeightPt + 0.001,
            "the picture must not run off the top of the page -- it used to be centred on a text " +
            "baseline a few points inside the top edge, putting half of a tall picture past it");
    }

    [Fact]
    public void BuildWithPageSetup_TallFooterPicture_IsDrawnEntirelyOnThePage()
    {
        var img = RenderSinglePicture(isFooter: true);

        img.Y.Should().BeGreaterThanOrEqualTo(0,
            "the footer half is the mirror image: centring a tall picture on the footer baseline " +
            "pushed its bottom half off the bottom edge of the page");
        (img.Y + img.Height).Should().BeLessThanOrEqualTo(PageHeightPt + 0.001);
    }

    [Fact]
    public void BuildWithPageSetup_TallHeaderPicture_KeepsItsFullBandHeightAndAspectRatio()
    {
        // Staying on the page must not come at the cost of the R167 fixes: the picture still fills
        // the grown band's height (the 25% cap = 198pt) and still keeps its 1:8 source aspect ratio.
        var img = RenderSinglePicture(isFooter: false);

        img.Height.Should().BeApproximately(198.0, 0.5);
        (img.Width / img.Height).Should().BeApproximately(50.0 / 400.0, 0.01);
    }

    [Fact]
    public void BuildWithPageSetup_ModestHeaderPicture_KeepsItsBaselineCentredPlacement()
    {
        // No-regression sibling: an ordinary small picture is nowhere near either page edge, so the
        // new clamp must leave its baseline-centred anchor exactly where it was.
        var picture = new WorksheetHeaderFooterPicture([5, 6, 7, 8], "image/png", Width: 40, Height: 20);
        var img = RenderSinglePicture(isFooter: false, picture);

        // 1in top margin, 0.3in header margin: the header baseline sits at 792 - 21.6 - 8 = 762.4,
        // and a 15pt-tall picture is centred on it against the 8pt base font.
        img.Y.Should().BeApproximately(762.4 - (15.0 / 2.0) + (8.0 / 2.0), 0.5);
        img.Height.Should().BeApproximately(15.0, 0.5);
    }

    [Fact]
    public void BuildWithPageSetup_MultiLineHeaderWithAPicture_CentresThePictureOnTheWholeBand()
    {
        // R168-services-avalonia-headerfooter-picture-band-centre-1: the picture was anchored to the
        // single baselineY the band renderer is handed, which for a header is the LAST line's
        // baseline (lines stack upward from it) and for a footer the FIRST line's. With one line
        // that is the band's centre; with three it is the band's edge, so the picture sat a full line
        // below the text it belongs beside and hung out of its own band into the grid. The WPF-shared
        // planner centres a picture in the band it was given (ResolvePictureBounds), so this is what
        // the two paths must agree on.
        var picture = new WorksheetHeaderFooterPicture([5, 6, 7, 8], "image/png", Width: 40, Height: 20);

        var oneLine = RenderPictureWithHeaderText("&G", picture);
        var threeLines = RenderPictureWithHeaderText("Line one\nLine two\nLine three&G", picture);

        var oneLineCentre = oneLine.Y + (oneLine.Height / 2.0);
        var threeLineCentre = threeLines.Y + (threeLines.Height / 2.0);

        // Three lines stack upward from the same baseline at the band's 10pt per-line height, putting
        // the band's centre one full line height above the single-line band's -- and the picture,
        // which follows that centre, with it.
        threeLineCentre.Should().BeApproximately(oneLineCentre + 10.0, 0.5,
            "the picture follows the centre of the band its text occupies, not the band's bottom edge");
    }

    private static PdfImage RenderPictureWithHeaderText(string centerText, WorksheetHeaderFooterPicture picture)
    {
        var workbook = new Workbook("MultiLineHeader");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PaperSize = WorksheetPaperSize.Letter;
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;
        sheet.PageMargins = new WorksheetPageMargins(Left: 1.0, Right: 1.0, Top: 1.0, Bottom: 1.0);
        sheet.PageHeader = new WorksheetHeaderFooter("", centerText, "");
        sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(Left: null, Center: picture, Right: null);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("Hi")));

        return RenderFirstPageImage(workbook);
    }

    private static PdfImage RenderSinglePicture(
        bool isFooter,
        WorksheetHeaderFooterPicture? picture = null)
    {
        picture ??= new WorksheetHeaderFooterPicture([1, 2, 3, 4], "image/png", Width: 50, Height: 400);

        var workbook = new Workbook(isFooter ? "FooterPicture" : "HeaderPicture");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PaperSize = WorksheetPaperSize.Letter;
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;
        sheet.PageMargins = new WorksheetPageMargins(Left: 1.0, Right: 1.0, Top: 1.0, Bottom: 1.0);

        var pictureSet = new WorksheetHeaderFooterPictureSet(Left: null, Center: picture, Right: null);
        if (isFooter)
        {
            sheet.PageFooter = new WorksheetHeaderFooter("", "&G", "");
            sheet.PageFooterPictures = pictureSet;
        }
        else
        {
            sheet.PageHeader = new WorksheetHeaderFooter("", "&G", "");
            sheet.PageHeaderPictures = pictureSet;
        }

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("Hi")));

        return RenderFirstPageImage(workbook);
    }

    private static PdfImage RenderFirstPageImage(Workbook workbook)
    {
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
        doc.Pages[0].WidthPoints.Should().BeApproximately(PageWidthPt, 0.001);
        doc.Pages[0].HeightPoints.Should().BeApproximately(PageHeightPt, 0.001);

        return doc.Pages[0].Ops.OfType<PdfImage>().Should().ContainSingle().Subject;
    }
}
