using FluentAssertions;
using FreeX.Core.Model;
using Free.Shared.Pdf;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R87-services-print-headerfooter-5-1/5-2/5-3: the Avalonia/Skia PDF export path
/// (<see cref="WorkbookPdfContentBuilder.BuildWithPageSetup"/>) must render header/footer
/// <c>&amp;</c>-format codes (bold/italic/size/color) per run instead of stripping them to a single
/// plain 8pt run, must actually center/right-align the center/right sections using measured text
/// width instead of drawing flush-left at a fixed section-third boundary, and must draw a
/// header/footer picture (<c>&amp;G</c>) via a <see cref="PdfImage"/> op instead of silently
/// dropping it -- matching the WPF <c>PrintRenderer.HeaderFooterDrawing</c>/<c>HeaderFooterPictures</c>
/// path for the identical header/footer model.
/// </summary>
public sealed class R87_HeaderFooterPdfTests
{
    [Fact]
    public void BuildWithPageSetup_BoldSizedHeaderCode_RendersBoldAtRequestedSize_NotPlainRegular8pt()
    {
        var page = BuildPageWithHeader(center: "&B&16Confidential&B");

        var run = page.Ops.OfType<PdfText>().Single(t => t.Text == "Confidential");

        run.Face.Should().Be(PdfFontFace.Bold,
            "&B...&B must render the run bold instead of being stripped to Regular");
        run.FontSize.Should().Be(16,
            "&16 must set the run's font size instead of being stripped to the fixed 8pt default");
    }

    [Fact]
    public void BuildWithPageSetup_PlainHeaderText_StillRendersRegularAtDefaultSize()
    {
        // No-regression sibling: a header with no format codes at all must keep rendering exactly
        // as before (Regular face, the caller's default 8pt size).
        var page = BuildPageWithHeader(center: "Confidential");

        var run = page.Ops.OfType<PdfText>().Single(t => t.Text == "Confidential");

        run.Face.Should().Be(PdfFontFace.Regular);
        run.FontSize.Should().Be(8);
    }

    [Fact]
    public void BuildWithPageSetup_ColorCode_TintsTheRunInsteadOfTheFixedHeaderColor()
    {
        var page = BuildPageWithHeader(center: "&KFF0000Alert");

        var run = page.Ops.OfType<PdfText>().Single(t => t.Text == "Alert");

        run.Color.Should().Be(new PdfColor(0xFF, 0x00, 0x00),
            "&KRRGGBB must tint the run red instead of always using the fixed header text color");
    }

    [Fact]
    public void BuildWithPageSetup_RightSection_TextRightEdgeLandsAtPageRightMargin()
    {
        var (page, pageW, mR, _) = BuildPageWithFooterGeometry(right: "Page &P of &N");

        var run = page.Ops.OfType<PdfText>().Single(t => t.Text == "Page 1 of 1");

        // Mirrors PortablePdfTextMeasurer's approximate width model (length * fontSize * 0.54 for a
        // non-bold run) so this test independently re-derives the expected right edge rather than
        // hardcoding a magic X value.
        var measuredWidth = run.Text.Length * run.FontSize * 0.54;
        var rightEdge = run.X + measuredWidth;

        rightEdge.Should().BeApproximately(pageW - mR, 1.0,
            "the right section's text must be measured and right-aligned so its right edge sits at " +
            "the page's right margin, matching the WPF path's DrawHeaderFooterFormattedRuns, instead " +
            "of drawing flush-left at the section-third boundary");
    }

    [Fact]
    public void BuildWithPageSetup_LeftSection_StillStartsFlushAtTheLeftMargin()
    {
        // No-regression sibling: the left section's alignment behavior (flush-left at the left
        // margin) must be unaffected by the center/right measurement fix.
        var (page, _, _, mL) = BuildPageWithFooterGeometry(left: "Left");

        var run = page.Ops.OfType<PdfText>().Single(t => t.Text == "Left");

        run.X.Should().BeApproximately(mL, 0.01);
    }

    [Fact]
    public void BuildWithPageSetup_HeaderPictureToken_DrawsPdfImage()
    {
        var imageBytes = new byte[] { 1, 2, 3, 4 };
        var workbook = new Workbook("Logo");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PageHeader = new WorksheetHeaderFooter("&G", "", "");
        sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(
            Left: new WorksheetHeaderFooterPicture(imageBytes, "image/png", Width: 96, Height: 48),
            Center: null,
            Right: null);

        var page = BuildPage(workbook);

        page.Ops.OfType<PdfImage>().Should().ContainSingle(img =>
                img.ContentType == "image/png" && img.ImageBytes.SequenceEqual(imageBytes),
            "&G with a configured header picture must draw a PdfImage op instead of being silently dropped");
    }

    [Fact]
    public void BuildWithPageSetup_NoPictureTokenOrNoConfiguredPicture_DrawsNoPdfImage()
    {
        // No-regression sibling: a picture assigned to the section but no &G token in the text (and,
        // separately, an &G token with no configured picture -- the default empty picture set below)
        // must not draw anything.
        var workbook = new Workbook("NoLogo");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PageHeader = new WorksheetHeaderFooter("Plain text, no picture token", "", "");
        sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(
            Left: new WorksheetHeaderFooterPicture(new byte[] { 9, 9 }, "image/png"),
            Center: null,
            Right: null);

        var page = BuildPage(workbook);

        page.Ops.OfType<PdfImage>().Should().BeEmpty(
            "no &G token in the section text means no image should be drawn even if a picture is configured");
    }

    private static PdfContentPage BuildPageWithHeader(string center)
    {
        var workbook = new Workbook("HeaderFormat");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PageHeader = new WorksheetHeaderFooter("", center, "");
        return BuildPage(workbook);
    }

    private static (PdfContentPage Page, double PageWidthPt, double MarginRightPt, double MarginLeftPt)
        BuildPageWithFooterGeometry(string left = "", string right = "")
    {
        var workbook = new Workbook("FooterAlign");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PageFooter = new WorksheetHeaderFooter(left, "", right);

        var (pageW, _, mL, mR, _, _, _, _) = SheetPdfPageSetupResolver.ComputePdfGeometry(sheet);
        return (BuildPage(workbook), pageW, mR, mL);
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
